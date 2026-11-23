using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TatumWelcome2.API.Mapping;
using TatumWelcome2.API.Security;
using TatumWelcome2.BLL.DTOs;
using TatumWelcome2.BLL.IServices;
using TatumWelcome2.Domain.Models;

namespace TatumWelcome2.API.Controllers;

[ApiController]
[Authorize]
[Route("accounts/me")]
[Tags("Customer Transactions")]
public class TransactionsController : ControllerBase
{
    private readonly IAccountService _accountService;

    public TransactionsController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpPost("deposit")]
    public IActionResult Deposit(
        [FromBody] DepositRequestDto request)
    {
        string? accountNumber = User.GetAccountNumber();

        if (accountNumber is null)
            return Unauthorized();

        TransactionResult result =
            _accountService.Deposit(accountNumber, request.Amount);

        return TransactionResponses.Deposit(result, accountNumber, request.Amount);
    }

    [HttpPost("withdraw")]
    [EnableRateLimiting("pin-transactions")]
    public IActionResult Withdraw(
    [FromBody] WithdrawRequestDto request)
    {
        string? accountNumber = User.GetAccountNumber();

        if (accountNumber is null)
            return Unauthorized();

        TransactionResult result = _accountService.Withdraw(
            accountNumber,
            request.Amount,
            request.Pin);

        int lockSeconds = result == TransactionResult.AccountLocked
            ? _accountService.GetPinLockSecondsRemaining(accountNumber)
            : 0;

        return TransactionResponses.Withdraw(result, request.Amount, lockSeconds);
    }

    [HttpPost("transfers")]
    [EnableRateLimiting("pin-transactions")]
    public IActionResult Transfer(
        [FromBody] IntrabankTransferRequestDto request)
    {
        string? senderNumber = User.GetAccountNumber();

        if (senderNumber is null)
            return Unauthorized();

        TransactionResult result = _accountService.Transfer(
            senderNumber,
            request.RecipientAccountNumber,
            request.Amount,
            request.Pin);

        int lockSeconds = result == TransactionResult.AccountLocked
            ? _accountService.GetPinLockSecondsRemaining(senderNumber)
            : 0;

        return TransactionResponses.Transfer(
            result,
            request.RecipientAccountNumber,
            request.Amount,
            lockSeconds);
    }
};
