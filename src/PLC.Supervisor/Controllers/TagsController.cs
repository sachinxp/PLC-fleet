using System.Linq;
using Microsoft.AspNetCore.Mvc;
using PLC.Shared.Models;

[ApiController]
[Route("api/plcs/{plcId}/tags")]
public class TagsController : ControllerBase
{
    private readonly FleetService _fleetService;

    public TagsController(FleetService fleetService)
    {
        _fleetService = fleetService;
    }

    [HttpGet]
    public ActionResult GetTags(string plcId)
    {
        var plc = _fleetService.Get(plcId);
        if (plc == null) return NotFound();
        return Ok(plc.Tags);
    }

    [HttpPost]
    public ActionResult AddTag(string plcId, [FromBody] TagDefinition tag)
    {
        var plc = _fleetService.Get(plcId);
        if (plc == null) return NotFound();
        plc.Tags.Add(tag);
        _fleetService.Update(plc);
        return Ok(tag);
    }

    [HttpPut("{tagName}")]
    public ActionResult UpdateTag(string plcId, string tagName, [FromBody] TagDefinition tag)
    {
        var plc = _fleetService.Get(plcId);
        if (plc == null) return NotFound();
        var existing = plc.Tags.FirstOrDefault(t => t.Name == tagName);
        if (existing == null) return NotFound();
        var idx = plc.Tags.IndexOf(existing);
        plc.Tags[idx] = tag;
        _fleetService.Update(plc);
        return Ok(tag);
    }

    [HttpPatch("{tagName}")]
    public ActionResult PatchTag(string plcId, string tagName, [FromBody] TagPatch patch)
    {
        var plc = _fleetService.Get(plcId);
        if (plc == null) return NotFound();
        var existing = plc.Tags.FirstOrDefault(t => t.Name == tagName);
        if (existing == null) return NotFound();

        if (patch.Name != null) existing.Name = patch.Name;
        if (patch.Address != null) existing.Address = patch.Address;
        if (patch.DataType != null) existing.DataType = patch.DataType;
        if (patch.Access.HasValue) existing.Access = patch.Access.Value;
        if (patch.Description != null) existing.Description = patch.Description;
        if (patch.EngUnit != null) existing.EngUnit = patch.EngUnit;
        if (patch.Enabled.HasValue) existing.Enabled = patch.Enabled.Value;

        _fleetService.Update(plc);
        return Ok(existing);
    }

    [HttpDelete("{tagName}")]
    public ActionResult DeleteTag(string plcId, string tagName)
    {
        var plc = _fleetService.Get(plcId);
        if (plc == null) return NotFound();
        plc.Tags.RemoveAll(t => t.Name == tagName);
        _fleetService.Update(plc);
        return NoContent();
    }
}

public class TagPatch
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? DataType { get; set; }
    public TagAccess? Access { get; set; }
    public string? Description { get; set; }
    public string? EngUnit { get; set; }
    public bool? Enabled { get; set; }
}
