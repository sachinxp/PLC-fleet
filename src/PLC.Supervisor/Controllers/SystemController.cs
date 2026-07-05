using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    [HttpGet("elevation")]
    public ActionResult GetElevation()
    {
        return Ok(new
        {
            isElevated = false,
            message = "Running in loopback mode (no elevation required for development)"
        });
    }

    [HttpGet("info")]
    public ActionResult GetInfo()
    {
        return Ok(new
        {
            version = "0.1.0",
            dotnetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription
        });
    }
}
