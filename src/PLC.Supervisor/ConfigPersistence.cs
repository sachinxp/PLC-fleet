using System.IO;
using PLC.Shared.Models;
using PLC.Shared.Serialization;

public class ConfigPersistence
{
    private static readonly string FleetDir = Path.Combine("data", "fleet");

    public ConfigPersistence()
    {
        if (!Directory.Exists(FleetDir)) Directory.CreateDirectory(FleetDir);
    }

    public void SavePlc(PlcInstance plc)
    {
        var path = Path.Combine(FleetDir, $"{plc.Id}.json");
        ConfigSerializer.SaveToFile(path, plc);
    }

    public PlcInstance? LoadPlc(string id)
    {
        var path = Path.Combine(FleetDir, $"{id}.json");
        return ConfigSerializer.LoadFromFile<PlcInstance>(path);
    }

    public void DeletePlc(string id)
    {
        var path = Path.Combine(FleetDir, $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
    }
}
