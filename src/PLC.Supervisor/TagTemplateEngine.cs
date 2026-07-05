using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PLC.Shared.Models;
using PLC.Shared.Serialization;

public class TagTemplateEngine
{
    private static readonly string TemplatesDir = Path.Combine("templates");

    public TagTemplateEngine()
    {
        if (!Directory.Exists(TemplatesDir))
            Directory.CreateDirectory(TemplatesDir);
    }

    public List<TagDefinition> LoadTemplate(Brand brand, string personality)
    {
        var path = Path.Combine(TemplatesDir, $"{brand.ToProtocolId()}.json");
        if (File.Exists(path))
        {
            var template = ConfigSerializer.LoadFromFile<TemplateFile>(path);
            if (template?.Tags != null)
                return template.Tags;
        }

        // Fall back to generated defaults
        return GenerateDefaultTags(brand);
    }

    private List<TagDefinition> GenerateDefaultTags(Brand brand)
    {
        var tags = new List<TagDefinition>();
        var types = GetTypesForBrand(brand);

        foreach (var type in types)
        {
            tags.AddRange(GenerateFiveTags(brand, type));
        }

        return tags;
    }

    private string[] GetTypesForBrand(Brand brand) => brand switch
    {
        Brand.Siemens => new[] { "Bool", "Byte", "Int16", "UInt16", "Int32", "Float32", "String" },
        Brand.Rockwell => new[] { "Bool", "Int32", "Float32", "String" },
        Brand.Modbus => new[] { "Bool", "Int16", "Int32", "Float32" },
        Brand.Mitsubishi => new[] { "Bool", "Int16", "Int32", "Float32" },
        Brand.Beckhoff => new[] { "Bool", "Int32", "Float32", "String" },
        Brand.OpcUa => new[] { "Bool", "Int32", "Float32", "String", "DateTime" },
        _ => new[] { "Bool", "Int32" }
    };

    private List<TagDefinition> GenerateFiveTags(Brand brand, string dataType)
    {
        var tags = new List<TagDefinition>();
        var profiles = new[] { "Step", "Sine", "Random", "Static", "Echo" };
        
        for (int i = 0; i < 5; i++)
        {
            var idx = i + 1;
            tags.Add(new TagDefinition
            {
                Name = $"Sim_{dataType}_{idx:D2}",
                Address = GenerateAddress(brand, dataType, idx),
                DataType = dataType,
                Access = profiles[i] == "Echo" ? TagAccess.ReadWrite : TagAccess.ReadOnly,
                Description = $"{dataType} tag {idx} with {profiles[i]} profile",
                Simulation = new SimulationConfig
                {
                    Profile = profiles[i],
                    LowLimit = 0,
                    HighLimit = dataType == "Bool" ? 1 : 100,
                    PeriodMs = 10000,
                    UpdateMs = 1000,
                    Step = dataType == "Bool" ? 1 : 5,
                    AtLimit = "AutoReverse"
                }
            });
        }

        return tags;
    }

    private string GenerateAddress(Brand brand, string dataType, int index) => brand switch
    {
        // S7 covers all memory areas: Input (I), Output (Q), Memory (M), DB, Timer (T), Counter (C)
        Brand.Siemens => (dataType, index) switch
        {
            // Bool: cycle through I, Q, M, DB, T, C areas
            ("Bool", 1) => "I0.0",        // Input bit
            ("Bool", 2) => "Q0.0",        // Output bit
            ("Bool", 3) => "M0.0",        // Memory/Merker bit
            ("Bool", 4) => "DB1.DBX0.0",  // DB bit
            ("Bool", 5) => "M100.0",      // Merker bit (echo/write test)
            // Int16: cycle through IW, QW, MW, DBW, Timer
            ("Int16", 1) => "IW0",        // Input word
            ("Int16", 2) => "QW0",        // Output word
            ("Int16", 3) => "MW20",       // Memory word
            ("Int16", 4) => "DB1.DBW0",   // DB word
            ("Int16", 5) => "T0",         // Timer (echo/write test)
            // Int32: cycle through ID, QD, MD, DBD
            ("Int32", 1) => "ID0",
            ("Int32", 2) => "QD0",
            ("Int32", 3) => "MD40",
            ("Int32", 4) => "DB1.DBD0",
            ("Int32", 5) => "DB1.DBD100", // echo
            // Float32: DBD areas
            ("Float32", 1) => "DB1.DBD4",
            ("Float32", 2) => "DB1.DBD8",
            ("Float32", 3) => "DB1.DBD12",
            ("Float32", 4) => "DB1.DBD16",
            ("Float32", 5) => "DB1.DBD20", // echo
            // String: DB areas
            ("String", 1) => "DB2.DBB0",
            ("String", 2) => "DB2.DBB32",
            ("String", 3) => "DB2.DBB64",
            ("String", 4) => "DB2.DBB96",
            ("String", 5) => "DB2.DBB128", // echo
            // Counter (UInt16)
            ("UInt16", _) => $"C{index - 1}",
            // Byte
            ("Byte", 1) => "MB0",
            ("Byte", 2) => "IB0",
            ("Byte", 3) => "QB0",
            ("Byte", 4) => "DB1.DBB0",
            ("Byte", 5) => "DB1.DBB1",
            _ => $"DB1.DBW{index * 2}"
        },
        Brand.Modbus => dataType switch
        {
            "Bool" => $"{index}",
            "Int16" => $"{40000 + index}",
            "Int32" => $"{40000 + index * 2}",
            "Float32" => $"{41000 + index * 2}",
            _ => $"{40000 + index}"
        },
        Brand.Rockwell => $"Sim_{dataType}_{index:D2}",
        Brand.Mitsubishi => dataType switch
        {
            "Bool" => $"M{index - 1}",
            "Int16" => $"D{100 + index}",
            "Int32" => $"D{200 + index * 2}",
            "Float32" => $"D{300 + index * 2}",
            _ => $"D{100 + index}"
        },
        Brand.Beckhoff => dataType switch
        {
            "Bool" => $"MAIN.bTag{index}",
            "Int32" => $"MAIN.nTag{index}",
            "Float32" => $"MAIN.fTag{index}",
            "String" => $"MAIN.sTag{index}",
            _ => $"MAIN.nTag{index}"
        },
        Brand.OpcUa => $"ns=2;s=/Objects/Simulator/Tag_{dataType}_{index}",
        _ => $"Tag_{index}"
    };
}

public class TemplateFile
{
    public string Brand { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = "1.0";
    public List<TagDefinition> Tags { get; set; } = new();
}
