
using TatumWelcome2.Models;

namespace TatumWelcome2.BLL;

public record BillPurchaseResult(
    TransactionResult Result,
    bool IsPending,
    string Reference);
