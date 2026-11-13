
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TatumWelcome2.BLL;
using TatumWelcome2.DTOs;
using TatumWelcome2.Interfaces;
using TatumWelcome2.Models;
using TatumWelcome2.Repositories;

namespace TatumWelcome2.Controllers;

[ApiController]
[Route("bills")]
[Tags("Bill Payments")]
public class BillPaymentsController : ControllerBase
{
    private readonly BillPurchaseService _service;
    private readonly IAccountRepository _accounts;

    private readonly BillPaymentRepository _paymentRepository;

    public BillPaymentsController(
    BillPurchaseService service,
    IAccountRepository accounts,
    BillPaymentRepository paymentRepository)
    {
        _service = service;
        _accounts = accounts;
        _paymentRepository = paymentRepository;
    }

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

    [Authorize]
    [HttpPost("pay")]
    public async Task<IActionResult> Pay(
        [FromBody] BillPaymentRequestDto request,
        CancellationToken cancellationToken)
    {
        string? accountNumber =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(accountNumber))
            return Unauthorized();

        Account? account =
            _accounts.FindByAccountNumber(accountNumber);

        if (account == null)
            return NotFound("Account not found.");

        if (request.AccountNumber != accountNumber ||
            request.BankId != "BankB")
        {
            return BadRequest(
                "Payment must belong to the logged-in Bank B account.");
        }

        if (string.IsNullOrWhiteSpace(request.Reference))
            return BadRequest("Payment reference is required.");

        if (!DataPlans.Networks.Contains(request.Network))
            return BadRequest("Invalid network.");

        if (string.IsNullOrWhiteSpace(request.PhoneNumber) ||
            request.PhoneNumber.Length != 11 ||
            !request.PhoneNumber.All(char.IsDigit))
        {
            return BadRequest("Phone number must contain 11 digits.");
        }

        if (request.PaymentType == "Airtime")
        {
            if (request.Amount < 50)
                return BadRequest("Minimum airtime amount is 50.");

            if (!string.IsNullOrWhiteSpace(request.DataPlanName))
                return BadRequest(
                    "Airtime payments must not include a data plan.");
        }
        else if (request.PaymentType == "Data")
        {
            bool validPlan = DataPlans.DataPlanItems.Any(plan =>
                plan.Name == request.DataPlanName &&
                plan.Price == request.Amount);

            if (!validPlan)
                return BadRequest("Invalid data plan or price.");
        }
        else
        {
            return BadRequest(
                "Payment type must be Airtime or Data.");
        }

        string description = request.PaymentType == "Data"
            ? $"Data {request.Network} {request.DataPlanName} " +
              $"{request.PhoneNumber} Ref: {request.Reference}"
            : $"Airtime {request.Network} {request.PhoneNumber} " +
              $"Ref: {request.Reference}";

        BillPurchaseResult result =
            await _service.PurchaseAsync(
                account,
                request,
                description,
                cancellationToken);

        if (result.IsPending)
        {
            return Accepted(new
            {
                result.Reference,
                Status = "Pending",
                Message = "Payment is being reconciled."
            });
        }
        if (_paymentRepository.PaymentBelongsToAccount(
        result.Reference, accountNumber) &&
    _paymentRepository.GetPaymentStatus(result.Reference)
        == "ReferenceConflict")
        {
            return Conflict(new
            {
                result.Reference,
                Status = "ReferenceConflict",
                Message = "Payment reference conflicts with an existing transaction."
            });
        }  
        if (result.Result != TransactionResult.Success)
        {
            return BadRequest(new
            {
                result.Reference,
                Status = "Failed",
                Result = result.Result.ToString()
            });
        }

        return Ok(new
        {
            result.Reference,
            Status = "Success",
            Message = "Bill payment successful."
        });
    }

    [Authorize]
    [HttpGet("status/{reference}")]
    public IActionResult GetPaymentStatus(string reference)
    {
        string? accountNumber =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(accountNumber))
            return Unauthorized();

        if (!_paymentRepository.PaymentBelongsToAccount(
            reference, accountNumber))
        {
            return NotFound("Payment not found.");
        }

        string? status =
            _paymentRepository.GetPaymentStatus(reference);

        return Ok(new
        {
            Reference = reference,
            Status = status
        });
    }
}
