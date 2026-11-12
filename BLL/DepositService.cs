using TatumWelcome2.Models;

namespace TatumWelcome2.BLL;

public static class DepositService
{
    public static bool Deposit(Account account, decimal amount)
    {
        return account.Deposit(amount, "deposit");
    }
}