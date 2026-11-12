
using TatumWelcome2.Models;
using TatumWelcome2.Services;

namespace TatumWelcome2.BLL;

public static class WithdrawalService
{
    public static TransactionResult Check(
        Account account,
        decimal amount)
    {
        return account.CheckWithdraw(amount);
    }

    public static TransactionResult Withdraw(
    Account account,
    decimal amount,
    string pin)
{
    if (!AccountService.VerifyPin(account, pin))
        return TransactionResult.InvalidPin;

    return account.Withdraw(amount, "withdrawal");
}
}