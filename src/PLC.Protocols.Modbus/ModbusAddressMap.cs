using System;
using System.Collections.Generic;
using System.Linq;
using PLC.Shared.Models;

namespace PLC.Protocols.Modbus;

public class ModbusAddressMap
{
    private readonly List<TagDefinition> _tags;
    private readonly Dictionary<int, TagDefinition> _coils = new();
    private readonly Dictionary<int, TagDefinition> _discreteInputs = new();
    private readonly Dictionary<int, TagDefinition> _inputRegisters = new();
    private readonly Dictionary<int, TagDefinition> _holdingRegisters = new();

    public ModbusAddressMap(List<TagDefinition> tags)
    {
        _tags = tags;
        IndexTags();
    }

    private void IndexTags()
    {
        foreach (var tag in _tags.Where(t => t.Enabled))
        {
            if (int.TryParse(tag.Address, out var addr))
            {
                // Convert application addresses to PDU-relative (0-based) addresses
                if (addr >= 40001) _holdingRegisters[addr - 40001] = tag;
                else if (addr >= 30001) _inputRegisters[addr - 30001] = tag;
                else if (addr >= 20001) _discreteInputs[addr - 20001] = tag;
                else if (addr >= 10001) _discreteInputs[addr - 10001] = tag;
                else _coils[addr] = tag;
            }
        }
    }

    public TagDefinition? GetCoil(int address) =>
        _coils.TryGetValue(address, out var tag) ? tag : null;

    public TagDefinition? GetDiscreteInput(int address) =>
        _discreteInputs.TryGetValue(address, out var tag) ? tag : null;

    public TagDefinition? GetInputRegister(int address) =>
        _inputRegisters.TryGetValue(address, out var tag) ? tag : null;

    public TagDefinition? GetHoldingRegister(int address) =>
        _holdingRegisters.TryGetValue(address, out var tag) ? tag : null;

    public int CoilCount => _coils.Count;
    public int DiscreteInputCount => _discreteInputs.Count;
    public int InputRegisterCount => _inputRegisters.Count;
    public int HoldingRegisterCount => _holdingRegisters.Count;

    public static int WordCountForType(string dataType) => dataType switch
    {
        "Int16" => 1,
        "UInt16" => 1,
        "Int32" => 2,
        "UInt32" => 2,
        "Float32" => 2,
        "Float64" => 4,
        _ => 1
    };
}
