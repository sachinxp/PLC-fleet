using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

public class PortConflictChecker
{
    public record PortConflict(int Port, string ProcessName);

    public List<PortConflict> CheckConflicts(IEnumerable<int> portsToCheck)
    {
        var conflicts = new List<PortConflict>();
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();

        foreach (var port in portsToCheck)
        {
            if (listeners.Any(l => l.Port == port))
            {
                conflicts.Add(new PortConflict(port, "Unknown"));
            }
        }

        return conflicts;
    }

    public bool IsPortExcludedByWindows(int port)
    {
        // Check Windows excluded port range
        // In v1, we just return false; will implement netsh query later
        return false;
    }
}
