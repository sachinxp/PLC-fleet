using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PLC.Shared.Models;

namespace PLC.Protocols.OpcUa;

public class UaSession : IDisposable
{
    private readonly TcpClient _client;
    private readonly UaServer _server;
    private readonly uint _sessionId;
    private readonly NetworkStream _stream;
    private readonly List<TagDefinition> _tags;
    private readonly Dictionary<string, object?> _tagValues;
    private readonly string _serverUri;
    private readonly string _manufacturer;
    private uint _requestHandle;
    private uint _channelId;

    public UaSession(TcpClient client, UaServer server, uint sessionId, List<TagDefinition> tags,
        Dictionary<string, object?> tagValues, string serverUri, string manufacturer)
    {
        _client = client;
        _server = server;
        _sessionId = sessionId;
        _tags = tags;
        _tagValues = tagValues;
        _serverUri = serverUri;
        _manufacturer = manufacturer;
        _stream = client.GetStream();
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var buffer = new byte[8192];
            while (!ct.IsCancellationRequested && _client.Connected)
            {
                var read = await _stream.ReadAsync(buffer, 0, 8, ct);
                if (read < 8) break;

                var msgType = ((uint)buffer[0] << 24) | ((uint)buffer[1] << 16) | ((uint)buffer[2] << 8) | buffer[3];
                var totalLen = (int)BitConverter.ToUInt32(buffer, 4);

                var remaining = totalLen - 8;
                var totalRead = 0;
                while (totalRead < remaining)
                {
                    read = await _stream.ReadAsync(buffer, totalRead, remaining - totalRead, ct);
                    if (read <= 0) break;
                    totalRead += read;
                }
                if (totalRead < remaining) break;

                var body = buffer[..totalRead].ToArray();

                byte[]? response = null;

                if (msgType == UaBinaryProtocol.MessageHello)
                {
                    response = UaBinaryProtocol.EncodeAcknowledge();
                }
                else if (msgType == UaBinaryProtocol.MessageOpenSecureChannel)
                {
                    _channelId = (uint)new Random().Next(1, 100000);
                    response = UaBinaryProtocol.EncodeOpenSecureChannelResponse(ParseRequestHandle(body), _channelId);
                }
                else if (msgType == UaBinaryProtocol.MessageCloseSecureChannel)
                {
                    break;
                }
                else if (msgType == UaBinaryProtocol.MessageMessage)
                {
                    response = HandleServiceRequest(body);
                }

                if (response != null)
                {
                    var latency = _server.GetLatencyMs();
                    if (latency > 0) await Task.Delay(latency, ct);
                    await _stream.WriteAsync(response, ct);
                }
            }
        }
        catch { }
        finally { _server.RemoveSession(this); Dispose(); }
    }

    private byte[]? HandleServiceRequest(byte[] body)
    {
        if (body.Length < 4) return null;
        var serviceId = BitConverter.ToUInt32(body, 0);

        _requestHandle = BitConverter.ToUInt32(body, 4);

        switch (serviceId)
        {
            case UaBinaryProtocol.GetEndpointsRequestId:
                return UaBinaryProtocol.EncodeGetEndpointsResponse(_requestHandle, _serverUri);

            case UaBinaryProtocol.CreateSessionRequestId:
                var authToken = BitConverter.GetBytes(_sessionId);
                return UaBinaryProtocol.EncodeSessionResponse(_requestHandle, _sessionId, _serverUri, authToken);

            case UaBinaryProtocol.ActivateSessionRequestId:
                return CreateGenericResponse(serviceId);

            case UaBinaryProtocol.BrowseRequestId:
                return HandleBrowse(body);

            case UaBinaryProtocol.ReadRequestId:
                return HandleRead(body);

            case UaBinaryProtocol.WriteRequestId:
                return HandleWrite(body);

            case UaBinaryProtocol.CreateSubscriptionRequestId:
                return CreateGenericResponse(serviceId);

            case UaBinaryProtocol.CreateMonitoredItemsRequestId:
                return CreateGenericResponse(serviceId);

            default:
                return null;
        }
    }

    private byte[]? HandleBrowse(byte[] body)
    {
        // Parse browse request to determine starting node
        var offset = 8; // skip serviceId + requestHandle

        // NodeId to browse
        if (offset >= body.Length - 1) return null;
        var nodeId = UaBinaryProtocol.ParseNumericNodeId(body, offset, out var consumed);
        offset += consumed;

        // For now, always return tag references
        var references = new List<UAReferenceDescription>();

        if (nodeId == UaBinaryProtocol.ObjectsFolderId || nodeId == 0)
        {
            // Return tag folders grouped by type
            var typeGroups = _tags.Where(t => t.Enabled)
                .GroupBy(t => t.DataType)
                .ToList();

            foreach (var group in typeGroups)
            {
                references.Add(new UAReferenceDescription
                {
                    ReferenceTypeId = 33, // Organizes
                    IsForward = true,
                    NodeId = new UANodeId { NamespaceIndex = 2, Identifier = (uint)(1000 + group.Key.GetHashCode() % 100) },
                    BrowseName = group.Key + "Tags",
                    DisplayName = group.Key + " Tags",
                    NodeClass = 1 // Object
                });
            }
        }
        else
        {
            // Return tag items
            foreach (var tag in _tags.Where(t => t.Enabled))
            {
                references.Add(new UAReferenceDescription
                {
                    ReferenceTypeId = 33,
                    IsForward = true,
                    NodeId = new UANodeId { NamespaceIndex = 2, Identifier = (uint)tag.Name.GetHashCode() },
                    BrowseName = tag.Name,
                    DisplayName = tag.Name,
                    NodeClass = 2 // Variable
                });
            }
        }

        return UaBinaryProtocol.EncodeBrowseResponse(_requestHandle, references);
    }

    private byte[]? HandleRead(byte[] body)
    {
        var values = new List<DataValue>();
        // Read multiple nodes
        if (body.Length > 12)
        {
            var numNodes = BitConverter.ToUInt32(body, body.Length - 8);
            // Simplified: read first requested node
            foreach (var tag in _tags.Where(t => t.Enabled))
            {
                object? val = null;
                lock (_tagValues) { _tagValues.TryGetValue(tag.Name, out val); }
                values.Add(new DataValue { Value = val ?? tag.Simulation.Value });
                if (values.Count >= 10) break;
            }
        }

        if (values.Count == 0)
            values.Add(new DataValue { Value = 0 });

        return UaBinaryProtocol.EncodeReadResponse(_requestHandle, values);
    }

    private byte[]? HandleWrite(byte[] body)
    {
        return CreateGenericResponse(UaBinaryProtocol.WriteRequestId);
    }

    private byte[]? CreateGenericResponse(uint serviceId)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(DateTime.UtcNow.Ticks));
        data.AddRange(BitConverter.GetBytes(_requestHandle));
        data.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // Good
        if (serviceId == UaBinaryProtocol.CreateSubscriptionRequestId)
        {
            // SubscriptionId
            data.AddRange(BitConverter.GetBytes(1u));
            // RevisedPublishingInterval
            data.AddRange(BitConverter.GetBytes(500.0));
            // RevisedLifetimeCount
            data.AddRange(BitConverter.GetBytes(30000u));
            // RevisedMaxKeepAliveCount
            data.AddRange(BitConverter.GetBytes(10000u));
        }
        if (serviceId == UaBinaryProtocol.CreateMonitoredItemsRequestId)
        {
            data.AddRange(BitConverter.GetBytes(0u)); // Results count
            data.AddRange(BitConverter.GetBytes(0u)); // Diagnostic count
        }
        return UaBinaryProtocol.BuildServiceResponse(serviceId, _requestHandle, data.ToArray());
    }

    private static uint ParseRequestHandle(byte[] body)
    {
        if (body.Length < 4) return 0;
        return BitConverter.ToUInt32(body, 0);
    }

    public void Dispose() { _stream?.Dispose(); _client?.Dispose(); }
}
