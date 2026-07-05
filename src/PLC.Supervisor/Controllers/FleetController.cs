using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CsvHelper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PLC.Shared.Models;
using PLC.Shared.Serialization;

[ApiController]
[Route("api/plcs")]
public class FleetController : ControllerBase
{
    private readonly FleetService _fleetService;
    private readonly IHubContext<FleetHub> _hubContext;
    private readonly TagTemplateEngine _templateEngine;

    public FleetController(FleetService fleetService, IHubContext<FleetHub> hubContext, TagTemplateEngine templateEngine)
    {
        _fleetService = fleetService;
        _hubContext = hubContext;
        _templateEngine = templateEngine;
    }

    [HttpGet]
    public ActionResult<IEnumerable<PlcInstance>> GetAll()
    {
        return Ok(_fleetService.GetAll());
    }

    [HttpGet("export/csv")]
    public IActionResult ExportCsv()
    {
        var plcs = _fleetService.GetAll();
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        writer.WriteLine("Name,Brand,Personality,State,TagName,TagAddress,TagDataType,TagAccess,TagEngUnit,TagEnabled");
        foreach (var plc in plcs)
        {
            var tags = plc.Tags.Count > 0 ? plc.Tags : new List<TagDefinition> { new() };
            foreach (var tag in tags)
            {
                writer.WriteLine($"\"{plc.Name}\",{plc.Brand},{plc.Personality},{plc.State},\"{tag.Name}\",\"{tag.Address}\",{tag.DataType},{tag.Access},\"{tag.EngUnit}\",{tag.Enabled}");
            }
        }
        var csv = writer.ToString();
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "plc-fleet-export.csv");
    }

    [HttpGet("{id}")]
    public ActionResult<PlcInstance> Get(string id)
    {
        var plc = _fleetService.Get(id);
        if (plc == null) return NotFound();
        return Ok(plc);
    }

    [HttpPost]
    public async Task<ActionResult<PlcInstance>> Create([FromBody] CreatePlcRequest request)
    {
        var plc = new PlcInstance
        {
            Name = request.Name,
            Brand = request.Brand,
            Personality = request.Personality,
            Description = request.Description,
            Network = new NetworkConfig
            {
                IpAddress = request.IpAddress ?? _fleetService.SuggestNextIp(),
                Port = request.Brand.DefaultPort(),
                MaxConnections = request.MaxConnections,
                UseLoopback = string.IsNullOrEmpty(request.IpAddress) || request.IpAddress.StartsWith("127."),
                NicName = request.NicName ?? ""
            },
            Tags = _templateEngine.LoadTemplate(request.Brand, request.Personality),
            OrderCode = $"{request.Brand}-{request.Personality}-{Guid.NewGuid().ToString("N")[..6]}",
            SerialNumber = $"SN-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}",
            FirmwareVersion = request.Brand switch
            {
                Brand.Siemens => "V4.5.0",
                Brand.Rockwell => "V33.01",
                Brand.Modbus => "V1.0",
                Brand.Mitsubishi => "1.100",
                Brand.Beckhoff => "3.1.4024.56",
                Brand.OpcUa => "1.04.08",
                _ => "1.0"
            }
        };

        var created = _fleetService.Create(plc);
        await _hubContext.Clients.All.SendAsync("PlcCreated", created);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public ActionResult Update(string id, [FromBody] PlcInstance plc)
    {
        if (id != plc.Id) return BadRequest();
        _fleetService.Update(plc);
        return Ok(plc);
    }

    [HttpGet("export/xlsx")]
    public IActionResult ExportXlsx()
    {
        var plcs = _fleetService.GetAll();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("PLC Fleet");

        ws.Cell(1, 1).Value = "Name";
        ws.Cell(1, 2).Value = "Brand";
        ws.Cell(1, 3).Value = "Personality";
        ws.Cell(1, 4).Value = "State";
        ws.Cell(1, 5).Value = "IP Address";
        ws.Cell(1, 6).Value = "Port";
        ws.Cell(1, 7).Value = "TagName";
        ws.Cell(1, 8).Value = "TagAddress";
        ws.Cell(1, 9).Value = "TagDataType";
        ws.Cell(1, 10).Value = "TagAccess";
        ws.Cell(1, 11).Value = "TagEngUnit";
        ws.Cell(1, 12).Value = "TagEnabled";
        ws.Cell(1, 13).Value = "OrderCode";
        ws.Cell(1, 14).Value = "SerialNumber";
        ws.Cell(1, 15).Value = "FirmwareVersion";

        var header = ws.Range(1, 1, 1, 15);
        header.Style.Fill.BackgroundColor = XLColor.Gray;
        header.Style.Font.Bold = true;

        int row = 2;
        foreach (var plc in plcs)
        {
            var tags = plc.Tags.Count > 0 ? plc.Tags : new List<TagDefinition> { new() };
            foreach (var tag in tags)
            {
                ws.Cell(row, 1).Value = plc.Name;
                ws.Cell(row, 2).Value = plc.Brand.ToString();
                ws.Cell(row, 3).Value = plc.Personality;
                ws.Cell(row, 4).Value = plc.State.ToString();
                ws.Cell(row, 5).Value = plc.Network.IpAddress;
                ws.Cell(row, 6).Value = plc.Network.Port;
                ws.Cell(row, 7).Value = tag.Name;
                ws.Cell(row, 8).Value = tag.Address;
                ws.Cell(row, 9).Value = tag.DataType;
                ws.Cell(row, 10).Value = tag.Access.ToString();
                ws.Cell(row, 11).Value = tag.EngUnit;
                ws.Cell(row, 12).Value = tag.Enabled;
                ws.Cell(row, 13).Value = plc.OrderCode;
                ws.Cell(row, 14).Value = plc.SerialNumber;
                ws.Cell(row, 15).Value = plc.FirmwareVersion;
                row++;
            }
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Seek(0, SeekOrigin.Begin);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "plc-fleet-export.xlsx");
    }

    [HttpGet("export/json")]
    public IActionResult ExportJson()
    {
        var plcs = _fleetService.GetAll();
        return Ok(plcs);
    }

    [HttpPost("import")]
    public async Task<ActionResult> Import([FromBody] List<PlcInstance> plcs)
    {
        var created = new List<PlcInstance>();
        foreach (var plc in plcs)
        {
            plc.Id = Guid.NewGuid().ToString("N");
            plc.OrderCode ??= $"{plc.Brand}-{plc.Personality}-{Guid.NewGuid().ToString("N")[..6]}";
            plc.SerialNumber ??= $"SN-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";
            var result = _fleetService.Create(plc);
            await _hubContext.Clients.All.SendAsync("PlcCreated", result);
            created.Add(result);
        }
        return Ok(created);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        _fleetService.Delete(id);
        await _hubContext.Clients.All.SendAsync("PlcDeleted", new { id });
        return NoContent();
    }

    [HttpPost("{id}/start")]
    public async Task<ActionResult> Start(string id)
    {
        var success = await _fleetService.StartAsync(id);
        if (!success) return NotFound();
        await _hubContext.Clients.All.SendAsync("PlcStateChanged", new { id, state = "Running" });
        return Ok();
    }

    [HttpPost("{id}/stop")]
    public async Task<ActionResult> Stop(string id)
    {
        var success = await _fleetService.StopAsync(id);
        if (!success) return NotFound();
        await _hubContext.Clients.All.SendAsync("PlcStateChanged", new { id, state = "Stopped" });
        return Ok();
    }
}

public class CreatePlcRequest
{
    public string Name { get; set; } = string.Empty;
    public Brand Brand { get; set; }
    public string Personality { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? NicName { get; set; }
    public int MaxConnections { get; set; } = 8;
}
