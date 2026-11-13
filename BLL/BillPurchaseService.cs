
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TatumWelcome2.DTOs;
using TatumWelcome2.Interfaces;
using TatumWelcome2.Models;
using TatumWelcome2.Repositories;

namespace TatumWelcome2.BLL;

public sealed class BillPurchaseService
{
    private readonly BillPaymentRepository _repository;
    private readonly IBankApiClient _bankApiClient;
    private readonly IConfiguration _configuration;

    // One lock per payment reference.
    private readonly ConcurrentDictionary<string, SemaphoreSlim>
        _paymentLocks = new();

    public BillPurchaseService(
        BillPaymentRepository repository,
        IBankApiClient bankApiClient,
        IConfiguration configuration)
    {
        _repository = repository;
        _bankApiClient = bankApiClient;
        _configuration = configuration;
    }

    public async Task<BillPurchaseResult> PurchaseAsync(
        Account account,
        BillPaymentRequestDto request,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (account == null ||
            request == null ||
            request.BankId != "BankB" ||
            request.AccountNumber != account.AccountNumber ||
            string.IsNullOrWhiteSpace(request.Reference) ||
            request.Amount <= 0)
        {
            return new BillPurchaseResult(
                TransactionResult.PaymentFailed,
                false,
                request?.Reference ?? "");
        }

        TransactionResult reservation =
            _repository.ReservePayment(
                request,
                description,
                out bool alreadyCompleted);

        if (reservation != TransactionResult.Success)
        {
            return new BillPurchaseResult(
                reservation,
                false,
                request.Reference);
        }

        if (alreadyCompleted)
        {
            return new BillPurchaseResult(
                TransactionResult.Success,
                false,
                request.Reference);
        }

        return await ProcessPaymentAsync(
            request,
            cancellationToken);
    }

    public async Task ReconcilePendingAsync(
        CancellationToken cancellationToken)
    {
        List<BillPaymentRequestDto> pendingPayments =
            _repository.GetPendingPayments();

        foreach (BillPaymentRequestDto payment in pendingPayments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ProcessPaymentAsync(
                payment,
                cancellationToken);
        }
    }

    // Coordinates concurrent requests for the same reference.
    private async Task<BillPurchaseResult> ProcessPaymentAsync(
        BillPaymentRequestDto request,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim paymentLock = _paymentLocks.GetOrAdd(
            request.Reference,
            _ => new SemaphoreSlim(1, 1));

        await paymentLock.WaitAsync(cancellationToken);

        try
        {
            return await ProcessPaymentCoreAsync(
                request,
                cancellationToken);
        }
        finally
        {
            paymentLock.Release();
        }
    }

    // Processes the payment while its reference is locked.
    private async Task<BillPurchaseResult> ProcessPaymentCoreAsync(
        BillPaymentRequestDto request,
        CancellationToken cancellationToken)
    {
        string reference = request.Reference;

        string? status =
            _repository.GetPaymentStatus(reference);

        // Already successful: never send or deduct again.
        if (status == "Success")
        {
            return new BillPurchaseResult(
                TransactionResult.Success,
                false,
                reference);
        }

        // Already failed and refunded: do not retry.
        if (status == "Failed")
        {
            return new BillPurchaseResult(
                TransactionResult.PaymentFailed,
                false,
                reference);
        }

        // NEW: A reference conflict must not be retried.
        if (status == "ReferenceConflict")
        {
            return new BillPurchaseResult(
                TransactionResult.PaymentFailed,
                false,
                reference);
        }

        if (status != "Pending")
        {
            return new BillPurchaseResult(
                TransactionResult.PaymentFailed,
                false,
                reference);
        }

        string bankAUrl =
            _configuration["Banks:BankA:Url"]
            ?? throw new InvalidOperationException(
                "Bank A URL is missing.");

        try
        {
            // Bank A determines success or failure.
            using HttpResponseMessage httpResponse =
                await _bankApiClient.SendBillPaymentAsync(
                    bankAUrl,
                    request);

            // NEW: Handle a payment reference conflict.
            if (httpResponse.StatusCode == HttpStatusCode.Conflict)
            {
                BillPaymentResponseDto? conflictResponse =
                    await httpResponse.Content
                        .ReadFromJsonAsync<BillPaymentResponseDto>(
                            cancellationToken: cancellationToken);

                if (conflictResponse?.Reference == reference &&
                    conflictResponse.Message ==
                        "Payment reference already used for another request.")
                {
                    bool marked =
                        _repository.MarkReferenceConflict(reference);

                    if (marked)
                    {
                        return new BillPurchaseResult(
                            TransactionResult.PaymentFailed,
                            false,
                            reference);
                    }
                }

                // Unrecognized conflict: retain Pending.
                return Pending(reference);
            }

            // Other uncertain responses remain pending.
            if (!httpResponse.IsSuccessStatusCode)
            {
                return Pending(reference);
            }

            BillPaymentResponseDto? response =
                await httpResponse.Content
                    .ReadFromJsonAsync<BillPaymentResponseDto>(
                        cancellationToken: cancellationToken);

            if (response == null ||
                response.Reference != reference)
            {
                return Pending(reference);
            }

            // Success: finalize reserved deduction.
            // Failure: refund the reserved amount.
            bool completed =
                _repository.CompletePayment(
                    reference,
                    response.Success);

            if (!completed)
            {
                return Pending(reference);
            }

            return new BillPurchaseResult(
                response.Success
                    ? TransactionResult.Success
                    : TransactionResult.PaymentFailed,
                false,
                reference);
        }
        catch (HttpRequestException)
        {
            return Pending(reference);
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Pending(reference);
        }
        catch (JsonException)
        {
            return Pending(reference);
        }
        catch (NotSupportedException)
        {
            return Pending(reference);
        }
    }

    private static BillPurchaseResult Pending(
        string reference)
    {
        return new BillPurchaseResult(
            TransactionResult.PaymentFailed,
            true,
            reference);
    }
}
