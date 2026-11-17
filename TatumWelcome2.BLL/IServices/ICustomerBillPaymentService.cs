using TatumWelcome2.BLL.DTOs;

namespace TatumWelcome2.BLL.IServices;

public interface ICustomerBillPaymentService
{
    Task<BillPurchaseResult> BuyAirtimeAsync(
        string accountNumber,
        AirtimePurchaseRequestDto request);

    Task<BillPurchaseResult> BuyDataAsync(
        string accountNumber,
        DataPurchaseRequestDto request);

    /// <summary>Re-sends Pending payments to Bank A and settles them.</summary>
    Task ReconcilePendingAsync(CancellationToken cancellationToken);
}