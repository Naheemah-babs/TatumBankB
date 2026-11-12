using TatumWelcome2.DTOs;
using TatumWelcome2.Models;

namespace TatumWelcome2.BLL;

public sealed class DataService
{
    private readonly BillPurchaseService _billPurchaseService;

    public DataService(BillPurchaseService billPurchaseService)
    {
        _billPurchaseService = billPurchaseService;
    }

    public async Task<BillPurchaseResult> PurchaseAsync(
        Account account,
        string network,
        string phone,
        DataPlan plan)
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

        if (plan == null ||
            !DataPlans.DataPlanItems.Any(p =>
                p.Name == plan.Name &&
                p.Price == plan.Price))
        {
            return new BillPurchaseResult(
                TransactionResult.InvalidPlan, false, "");
        }

        string reference = Guid.NewGuid().ToString("N");

        BillPaymentRequestDto request = new()
        {
            Reference = reference,
            BankId = "BankB",
            AccountNumber = account.AccountNumber,
            PaymentType = "Data",
            Network = network,
            PhoneNumber = phone,
            Amount = plan.Price,
            DataPlanName = plan.Name
        };

        return await _billPurchaseService.PurchaseAsync(
            account,
            request,
            $"Data {network} {plan.Name} {phone} Ref: {reference}");
    }
}