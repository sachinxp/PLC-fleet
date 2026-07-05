using System.Collections.Generic;
using PLC.Shared.Models;

namespace PLC.Shared.Ipc;

public enum IpcMessageType
{
    // Supervisor → Worker
    StartWorker,
    StopWorker,
    ConfigSnapshot,
    UpdateTag,
    UpdateSimulation,
    Ping,
    
    // Worker → Supervisor
    WorkerStarted,
    WorkerStopped,
    TagValues,
    ConnectionStats,
    TrafficEvent,
    ErrorReport,
    Pong
}

public class IpcMessage
{
    public IpcMessageType Type { get; set; }
    public string Payload { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}

public class ConfigSnapshotPayload
{
    public string InstanceId { get; set; } = string.Empty;
    public PlcInstance Instance { get; set; } = new();
}

public class TagValuesPayload
{
    public string InstanceId { get; set; } = string.Empty;
    public List<TagValue> Values { get; set; } = new();
}

public class TagValue
{
    public string TagName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}

public class ConnectionStatsPayload
{
    public string InstanceId { get; set; } = string.Empty;
    public int ActiveConnections { get; set; }
    public long TotalRequests { get; set; }
    public int ErrorCount { get; set; }
    public List<ConnectionInfo> Connections { get; set; } = new();
}

public class ConnectionInfo
{
    public string ClientIp { get; set; } = string.Empty;
    public long ConnectedAt { get; set; }
    public long RequestCount { get; set; }
}

public class TrafficEventPayload
{
    public string InstanceId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public bool Success { get; set; }
    public long Timestamp { get; set; }
}
