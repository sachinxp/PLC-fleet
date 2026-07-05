using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

public class NetworkService
{
    private readonly ElevatorClient _elevator;
    private bool _useLoopback = true;

    public NetworkService(ElevatorClient elevator)
    {
        _elevator = elevator;
    }

    public bool IsElevated => _elevator.IsConnected;

    public async Task<bool> AddIpAddressAsync(string nicName, string ipAddress, string subnetMask)
    {
        if (_useLoopback) return true; // loopback doesn't need elevation
        return await _elevator.AddIpAddressAsync(nicName, ipAddress, subnetMask);
    }

    public async Task<bool> RemoveIpAddressAsync(string nicName, string ipAddress)
    {
        if (_useLoopback) return true;
        return await _elevator.RemoveIpAddressAsync(nicName, ipAddress);
    }

    public string[] GetAvailableNics()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.Name)
            .ToArray();
    }

    public bool IsPortAvailable(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return !listeners.Any(l => l.Port == port);
    }

    public Task<bool> CheckPortConflictAsync(int port)
    {
        return Task.FromResult(IsPortAvailable(port));
    }
}
