using TatumWelcome2.Models;

namespace TatumWelcome2.Repositories;

public static class AccountData
{
    public static List<Account> GetAccounts()
    {
        Account nah = new Account
        {
            Name = "Naheemot B",
            Branch = "Lagos",
            AccountNumber = "0012345678",
            Pin = "1234"
        };

        Account test = new Account
        {
            Name = "Test User B",
            Branch = "Lagos",
            AccountNumber = "0087654321",
            Pin = "4321"
        };


        Account ada = new Account
        {
            Name = "Ada Smith B",
            Branch = "Lagos",
            AccountNumber = "0011111111",
            Pin = "1111"
        };

        Account ola = new Account
        {
            Name = "Ola Wale B",
            Branch = "Lagos",
            AccountNumber = "0022222222",
            Pin = "2222"
        };

        Account jide = new Account
        {
            Name = "Baba Jide B",
            Branch = "Lagos",
            AccountNumber = "0033333333",
            Pin = "3333"
        };

        Account tunde = new Account
        {
            Name = "Baba Tunde B",
            Branch = "Lagos",
            AccountNumber = "0044444444",
            Pin = "4444"
        };

       
        nah.Deposit(50000, "Opening balance");
        test.Deposit(10000, "Opening balance");
        ada.Deposit(25000, "Opening balance");
        ola.Deposit(15000, "Opening balance");
        jide.Deposit(8000, "Opening balance");
        tunde.Deposit(30000, "Opening balance");

        return new List<Account> { nah, test, ada, ola, jide, tunde };
    }
}