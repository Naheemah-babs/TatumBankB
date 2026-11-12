using TatumWelcome2.DTOs;
using TatumWelcome2.Models;

namespace TatumWelcome2.BLL;

public sealed class AirtimeService
{
    private readonly BillPurchaseService _billPurchaseService;

    public AirtimeService(BillPurchaseService billPurchaseService)
    {
        _billPurchaseService = billPurchaseService;
    }

    public async Task<BillPurchaseResult> PurchaseAsync(
        Account account,
        string network,
        string phone,
        decimal amount)
    {
        if (!DataPlans.Networks.Contains(network))
        {
            return new BillPurchaseResult(
                TransactionResult.InvalidNetwork, false, "");
        }

        if (string.IsNullOrWhiteSpace(phone) ||
            phone.Length != 11 ||
            !phone.All(char.IsDigit))
        {
            return new BillPurchaseResult(
                TransactionResult.InvalidPhone, false, "");
        }

        if (amount < 50)
        {
            return new BillPurchaseResult(
                TransactionResult.InvalidAmount, false, "");
        }

        string reference = Guid.NewGuid().ToString("N");

        BillPaymentRequestDto request = new()
        {
            Reference = reference,
            BankId = "BankB",
            AccountNumber = account.AccountNumber,
            PaymentType = "Airtime",
            Network = network,
            PhoneNumber = phone,
            Amount = amount
        };

        return await _billPurchaseService.PurchaseAsync(
            account,
            request,
            $"Airtime {network} {phone} Ref: {reference}");
    }
}