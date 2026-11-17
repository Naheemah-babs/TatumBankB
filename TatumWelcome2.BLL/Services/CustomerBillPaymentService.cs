using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TatumWelcome2.BLL.DTOs;
using TatumWelcome2.BLL.IServices;
using TatumWelcome2.BLL.Options;
using TatumWelcome2.Domain.Interfaces;
using TatumWelcome2.Domain.Models;

namespace TatumWelcome2.BLL.Services;

/// <summary>Caller side: B debits its customer, then asks Bank A to pay the bill.</summary>
public sealed class CustomerBillPaymentService : ICustomerBillPaymentService
{
    public const string OwnBankId = "BankB";

    private const string ReferenceConflictMessage =
        "Payment reference already used for another request.";

    private readonly IOutgoingBillPaymentRepository _repository;
    private readonly IBankApiClient _bankApiClient;
    private readonly InterBankOptions _interbankOptions;
    private readonly ILogger<CustomerBillPaymentService> _logger;

    public CustomerBillPaymentService(
        IOutgoingBillPaymentRepository repository,
        IBankApiClient bankApiClient,
        IOptions<InterBankOptions> interbankOptions,
        ILogger<CustomerBillPaymentService> logger)
    {
        _repository = repository;
        _bankApiClient = bankApiClient;
        _interbankOptions = interbankOptions.Value;
        _logger = logger;
    }

    public Task<BillPurchaseResult> BuyAirtimeAsync(
        string accountNumber,
        AirtimePurchaseRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Pin))
            return Task.FromResult(Invalid(request.Reference, "PIN is required.", request.Amount));

        if (!TryResolveReference(request.Reference, out string reference))
            return Task.FromResult(InvalidReference(request.Reference, request.Amount));

        BillPaymentRequest bill = new()
        {
            Reference = reference,
            BankId = OwnBankId,
            AccountNumber = accountNumber,
            PaymentType = BillPaymentTypes.Airtime,
            Network = request.Network,
            PhoneNumber = request.PhoneNumber,
            Amount = request.Amount
        };

        return PurchaseAsync(
            bill,
            request.Pin,
            TransactionDescriptions.Airtime(request.Network, request.PhoneNumber, reference));
    }

    public Task<BillPurchaseResult> BuyDataAsync(
        string accountNumber,
        DataPurchaseRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Pin))
            return Task.FromResult(Invalid(request.Reference, "PIN is required.", 0m));

        if (!TryResolveReference(request.Reference, out string reference))
            return Task.FromResult(InvalidReference(request.Reference, 0m));

        DataPlan? plan = DataPlans.DataPlanItems.FirstOrDefault(
            item => string.Equals(
                item.Name,
                request.PlanName?.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (plan == null)
            return Task.FromResult(Invalid(reference, "Invalid data plan.", 0m));

        BillPaymentRequest bill = new()
        {
            Reference = reference,
            BankId = OwnBankId,
            AccountNumber = accountNumber,
            PaymentType = BillPaymentTypes.Data,
            Network = request.Network,
            PhoneNumber = request.PhoneNumber,
            Amount = plan.Price,
            DataPlanName = plan.Name
        };

        return PurchaseAsync(
            bill,
            request.Pin,
            TransactionDescriptions.Data(request.Network, plan.Name, request.PhoneNumber, reference));
    }

    private async Task<BillPurchaseResult> PurchaseAsync(
        BillPaymentRequest bill,
        string pin,
        string description)
    {
        string? error = BillPaymentValidator.Validate(bill);

        if (error != null)
            return Invalid(bill.Reference, error, bill.Amount);

        // Checked before any debit, so a missing URL never takes the customer's money.
        if (string.IsNullOrWhiteSpace(_interbankOptions.Url))
        {
            throw new InvalidOperationException(
                $"{InterBankOptions.SectionName}:Url is not configured.");
        }

        // Debit and Pending row commit together. No lock is held during the call to A.
        OutgoingReservation reservation = _repository.Reserve(bill, pin, description);

        return reservation.Status switch
        {
            OutgoingReservationStatus.Reserved =>
                await SendToProviderAsync(bill),

            OutgoingReservationStatus.Rejected =>
                Rejected(bill, reservation.RejectionReason ?? TransactionResult.PaymentFailed),

            _ => Replay(bill, reservation.Existing)
        };
    }

    private async Task<BillPurchaseResult> SendToProviderAsync(BillPaymentRequest bill)
    {
        try
        {
            using HttpResponseMessage response =
                await _bankApiClient.SendBillPaymentAsync(_interbankOptions.Url, bill);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                BillPaymentResponseDto? conflict =
                    await response.Content.ReadFromJsonAsync<BillPaymentResponseDto>();

                if (conflict?.Reference == bill.Reference &&
                    conflict.Message == ReferenceConflictMessage)
                {
                    _repository.MarkReferenceConflict(bill.Reference);

                    return Result(BillPurchaseOutcome.ReferenceConflict, bill, ReferenceConflictMessage);
                }

                return Pending(bill);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Bank A returned HTTP {StatusCode} for bill payment {Reference}.",
                    response.StatusCode,
                    bill.Reference);

                return Pending(bill);
            }

            BillPaymentResponseDto? body =
                await response.Content.ReadFromJsonAsync<BillPaymentResponseDto>();

            if (body == null || body.Reference != bill.Reference)
                return Pending(bill);

            // A answers 200 for approved and declined, so success comes from the body.
            // Success writes the ledger row. A decline refunds.
            if (!_repository.Complete(bill.Reference, body.Success))
                return Pending(bill);

            return Result(
                body.Success
                    ? BillPurchaseOutcome.Successful
                    : BillPurchaseOutcome.Declined,
                bill,
                body.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error for bill payment {Reference}", bill.Reference);
            return Pending(bill);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timeout for bill payment {Reference}", bill.Reference);
            return Pending(bill);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unreadable reply for bill payment {Reference}", bill.Reference);
            return Pending(bill);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Unsupported reply for bill payment {Reference}", bill.Reference);
            return Pending(bill);
        }
    }

    public async Task ReconcilePendingAsync(CancellationToken cancellationToken)
    {
        List<OutgoingBillPaymentRecord> pending = _repository.GetPending();

        if (pending.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(_interbankOptions.Url))
        {
            throw new InvalidOperationException(
                $"{InterBankOptions.SectionName}:Url is not configured.");
        }

        foreach (OutgoingBillPaymentRecord payment in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                BillPaymentRequest bill = new()
                {
                    Reference = payment.Reference,
                    BankId = payment.BankId,
                    AccountNumber = payment.AccountNumber,
                    PaymentType = payment.PaymentType,
                    Network = payment.Network,
                    PhoneNumber = payment.PhoneNumber,
                    Amount = payment.Amount,
                    DataPlanName = payment.DataPlanName
                };

                // Same call and same reply mapping as a live purchase.
                BillPurchaseResult result = await SendToProviderAsync(bill);

                _logger.LogInformation(
                    "Bill payment {Reference} reconciled: {Outcome}",
                    payment.Reference,
                    result.Outcome);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Bill payment reconciliation failed: {Reference}",
                    payment.Reference);
            }
        }
    }

    /// <summary>Known reference: same details replay the stored outcome, no new debit.</summary>
    private static BillPurchaseResult Replay(
        BillPaymentRequest bill,
        OutgoingBillPaymentRecord? existing)
    {
        if (existing == null)
            return Invalid(bill.Reference, "Unable to retrieve previous payment.", bill.Amount);

        if (!BillPaymentValidator.IsSameRequest(existing, bill))
            return Result(BillPurchaseOutcome.ReferenceConflict, bill, ReferenceConflictMessage);

        return existing.Status switch
        {
            OutgoingBillPaymentStatus.Success =>
                Result(BillPurchaseOutcome.Successful, bill, "Payment successful.", replayed: true),

            OutgoingBillPaymentStatus.Failed =>
                Result(BillPurchaseOutcome.Declined, bill, "Payment declined.", replayed: true),

            OutgoingBillPaymentStatus.ReferenceConflict =>
                Result(BillPurchaseOutcome.ReferenceConflict, bill, ReferenceConflictMessage, replayed: true),

            _ => Result(
                BillPurchaseOutcome.Pending,
                bill,
                $"Payment pending confirmation. Reference: {bill.Reference}. No additional debit made.",
                replayed: true)
        };
    }

    private static BillPurchaseResult Pending(BillPaymentRequest bill) =>
        Result(
            BillPurchaseOutcome.Pending,
            bill,
            $"Payment pending confirmation. Reference: {bill.Reference}.");

    private static BillPurchaseResult Result(
        BillPurchaseOutcome outcome,
        BillPaymentRequest bill,
        string message,
        bool replayed = false) =>
        new(outcome, bill.Reference, message, bill.Amount, Replayed: replayed);

    private static BillPurchaseResult Rejected(
        BillPaymentRequest bill,
        TransactionResult reason)
    {
        string message = reason switch
        {
            TransactionResult.AccountNotFound => "Account not found.",
            TransactionResult.InvalidPin => "Incorrect PIN.",
            TransactionResult.AccountFrozen => "Account is frozen.",
            TransactionResult.InsufficientFunds => "Insufficient funds.",
            _ => "Purchase failed."
        };

        return new BillPurchaseResult(
            BillPurchaseOutcome.Rejected,
            bill.Reference,
            message,
            bill.Amount,
            reason);
    }

    /// <summary>Uses the caller's reference (GUID "N" format) or generates one.</summary>
    private static bool TryResolveReference(string? reference, out string resolved)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            resolved = Guid.NewGuid().ToString("N");
            return true;
        }

        resolved = reference.Trim();
        return Guid.TryParseExact(resolved, "N", out _);
    }

    private static BillPurchaseResult InvalidReference(string? reference, decimal amount) =>
        Invalid(
            reference,
            "Reference must be a 32-character GUID (\"N\" format) or omitted.",
            amount);

    private static BillPurchaseResult Invalid(string? reference, string message, decimal amount) =>
        new(BillPurchaseOutcome.InvalidRequest, reference ?? string.Empty, message, amount);
}