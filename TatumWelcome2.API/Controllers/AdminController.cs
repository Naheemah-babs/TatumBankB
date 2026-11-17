using Microsoft.AspNetCore.Mvc;
using TatumWelcome2.API.Mapping;
using TatumWelcome2.API.Security;
using TatumWelcome2.BLL.IServices;

namespace TatumWelcome2.API.Controllers;

[ApiController]
[Route("admin")]
[Tags("Admin")]
[AdminApiKey]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet("accounts/{accountNumber}")]
    public IActionResult GetAccount(string accountNumber)
    {
        return AdminResponses.AccountSummary(
            _adminService.GetAccountSummary(accountNumber));
    }

    [HttpPut("accounts/{accountNumber}/freeze")]
    public IActionResult FreezeAccount(string accountNumber)
    {
        return AdminResponses.StatusChange(
            _adminService.Freeze(accountNumber),
            accountNumber,
            freeze: true);
    }

    [HttpPut("accounts/{accountNumber}/unfreeze")]
    public IActionResult UnfreezeAccount(string accountNumber)
    {
        return AdminResponses.StatusChange(
            _adminService.Unfreeze(accountNumber),
            accountNumber,
            freeze: false);
    }
}