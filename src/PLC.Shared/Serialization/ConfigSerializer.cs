using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PLC.Shared.Models;

namespace PLC.Shared.Serialization;

public class ConfigSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize<T>(T obj) =>
        JsonSerializer.Serialize(obj, JsonOptions);

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions);

    public static void SaveToFile<T>(string path, T obj)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, Serialize(obj));
    }

    public static T? LoadFromFile<T>(string path)
    {
        if (!File.Exists(path)) return default;
        return Deserialize<T>(File.ReadAllText(path));
    }
}
