using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[Route("v1/[controller]")]
public class SystemController : ControllerBase
{
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            name = "Kreyora API",
            version = "0.1.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
        });
    }
}
