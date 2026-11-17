using Microsoft.AspNetCore.Mvc;
using TatumWelcome2.Domain.Models;

namespace TatumWelcome2.API.Controllers;

[ApiController]
[Route("bills")]
[Tags("Bill Payments")]
public class BillPaymentsController : ControllerBase
{
    [HttpGet("networks")]
    public IActionResult GetNetworks()
    {
        return Ok(DataPlans.Networks);
    }

    [HttpGet("plans")]
    public IActionResult GetPlans()
    {
        return Ok(DataPlans.DataPlanItems);
    }
}