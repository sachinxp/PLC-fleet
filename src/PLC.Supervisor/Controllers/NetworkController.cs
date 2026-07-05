using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/network")]
public class NetworkController : ControllerBase
{
    private readonly NetworkService _networkService;
    private readonly PortConflictChecker _portChecker;

    public NetworkController(NetworkService networkService, PortConflictChecker portChecker)
    {
        _networkService = networkService;
        _portChecker = portChecker;
    }

    [HttpGet("nics")]
    public ActionResult GetNics()
    {
        return Ok(_networkService.GetAvailableNics());
    }

    [HttpGet("port-check/{port}")]
    public async Task<ActionResult> CheckPort(int port)
    {
        var available = await _networkService.CheckPortConflictAsync(port);
        return Ok(new { port, available, conflicts = available ? null : _portChecker.CheckConflicts(new[] { port }) });
    }

    [HttpGet("status")]
    public ActionResult GetStatus()
    {
        return Ok(new
        {
            isElevated = _networkService.IsElevated,
            loopbackMode = true // simplified for dev
        });
    }
}
