
using TatumWelcome2.Models;

namespace TatumWelcome2.BLL;

public static class TransferService
{
    public static Account? FindRecipient(
        List<Account> accounts,
        string accountNumber)
    {
        return AccountService.FindAccount(
            accounts,
            accountNumber);
    }

    public static TransactionResult Check(
        Account sender,
        decimal amount)
    {
        return sender.CheckWithdraw(amount);
    }

    public static TransactionResult Transfer(
        Account sender,
        Account recipient,
        decimal amount,
        string pin)
    {
        
        if (!AccountService.VerifyPin(sender, pin))
        {
            return TransactionResult.InvalidPin;
        }

        
        if (sender.AccountNumber == recipient.AccountNumber)
        {
            return TransactionResult.SameAccount;
        }

        
        TransactionResult check = sender.CheckWithdraw(amount);

        if (check != TransactionResult.Success)
        {
            return check;
        }

        
        return AccountService.Transfer(
            sender,
            recipient,
            amount);
    }
}
