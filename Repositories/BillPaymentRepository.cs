
using Dapper;
using MySqlConnector;
using TatumWelcome2.Database;
using TatumWelcome2.DTOs;
using TatumWelcome2.Models;

namespace TatumWelcome2.Repositories;

public sealed class BillPaymentRepository
{
    private readonly DbConnectionFactory _factory;

    public BillPaymentRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public TransactionResult ReservePayment(
        BillPaymentRequestDto request,
        string description,
        out bool alreadyCompleted)
    {
        alreadyCompleted = false;

        if (request == null ||
            string.IsNullOrWhiteSpace(request.Reference) ||
            request.Amount <= 0)
        {
            return TransactionResult.PaymentFailed;
        }

        using var connection = _factory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            // Lock the account first. Requests from the same
            // account cannot reserve funds simultaneously.
            AccountBalanceRow? account =
                connection.QuerySingleOrDefault<AccountBalanceRow>(
                    """
                    SELECT Balance, IsFrozen
                    FROM Accounts
                    WHERE AccountNumber = @AccountNumber
                    FOR UPDATE;
                    """,
                    new { request.AccountNumber },
                    transaction);

            if (account == null)
            {
                transaction.Rollback();
                return TransactionResult.AccountNotFound;
            }

            // Check the reference while holding the account lock.
            PaymentRow? previous =
                connection.QuerySingleOrDefault<PaymentRow>(
                    """
                    SELECT *
                    FROM OutgoingBillPayments
                    WHERE Reference = @Reference
                    FOR UPDATE;
                    """,
                    new { request.Reference },
                    transaction);

            if (previous != null)
            {
                bool sameRequest =
                    previous.AccountNumber == request.AccountNumber &&
                    previous.BankId == request.BankId &&
                    previous.PaymentType == request.PaymentType &&
                    previous.Network == request.Network &&
                    previous.PhoneNumber == request.PhoneNumber &&
                    previous.Amount == request.Amount &&
                    previous.DataPlanName == request.DataPlanName;

                if (!sameRequest)
                {
                    transaction.Rollback();
                    return TransactionResult.PaymentFailed;
                }

                alreadyCompleted = previous.Status == "Success";

                transaction.Commit();

                return previous.Status == "Failed"
                    ? TransactionResult.PaymentFailed
                    : TransactionResult.Success;
            }

            if (account.IsFrozen)
            {
                transaction.Rollback();
                return TransactionResult.AccountFrozen;
            }

            if (account.Balance < request.Amount)
            {
                transaction.Rollback();
                return TransactionResult.InsufficientFunds;
            }

            // Reserve the reference before deducting money.
            connection.Execute(
                """
                INSERT INTO OutgoingBillPayments
                    (Reference, AccountNumber, BankId,
                     PaymentType, Network, PhoneNumber,
                     Amount, DataPlanName, Description, Status)
                VALUES
                    (@Reference, @AccountNumber, @BankId,
                     @PaymentType, @Network, @PhoneNumber,
                     @Amount, @DataPlanName, @Description, 'Pending');
                """,
                new
                {
                    request.Reference,
                    request.AccountNumber,
                    request.BankId,
                    request.PaymentType,
                    request.Network,
                    request.PhoneNumber,
                    request.Amount,
                    request.DataPlanName,
                    Description = description
                },
                transaction);

            connection.Execute(
                """
                UPDATE Accounts
                SET Balance = Balance - @Amount
                WHERE AccountNumber = @AccountNumber;
                """,
                new
                {
                    request.AccountNumber,
                    request.Amount
                },
                transaction);

            transaction.Commit();
            return TransactionResult.Success;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            // Another account may have used the same reference.
            // Roll back the entire reservation.
            transaction.Rollback();
            return TransactionResult.PaymentFailed;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public bool CompletePayment(string reference, bool success)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            PaymentRow? payment =
                connection.QuerySingleOrDefault<PaymentRow>(
                    """
                    SELECT *
                    FROM OutgoingBillPayments
                    WHERE Reference = @Reference
                    FOR UPDATE;
                    """,
                    new { Reference = reference },
                    transaction);

            if (payment == null)
            {
                transaction.Rollback();
                return false;
            }

            // Never apply a completed payment twice.
            if (payment.Status != "Pending")
            {
                transaction.Commit();

                return payment.Status ==
                    (success ? "Success" : "Failed");
            }

            if (success)
            {
                // Money was already reserved.
                // Only record the completed transaction.
                connection.Execute(
                    """
                    INSERT INTO Transactions
                        (AccountNumber, Type, Amount,
                         Date, Description, Reference)
                    VALUES
                        (@AccountNumber, @Type, @Amount,
                         @Date, @Description, @Reference);
                    """,
                    new
                    {
                        payment.AccountNumber,
                        Type = TransactionType.Withdrawal.ToString(),
                        payment.Amount,
                        Date = DateTime.Now,
                        payment.Description,
                        payment.Reference
                    },
                    transaction);
            }
            else
            {
                // Bank A rejected the payment.
                // Release the reserved money once.
                connection.Execute(
                    """
                    UPDATE Accounts
                    SET Balance = Balance + @Amount
                    WHERE AccountNumber = @AccountNumber;
                    """,
                    new
                    {
                        payment.AccountNumber,
                        payment.Amount
                    },
                    transaction);
            }

            connection.Execute(
                """
                UPDATE OutgoingBillPayments
                SET Status = @Status
                WHERE Reference = @Reference;
                """,
                new
                {
                    Reference = reference,
                    Status = success ? "Success" : "Failed"
                },
                transaction);

            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public string? GetPaymentStatus(string reference)
    {
        using var connection = _factory.CreateConnection();

        return connection.QuerySingleOrDefault<string>(
            """
            SELECT Status
            FROM OutgoingBillPayments
            WHERE Reference = @Reference;
            """,
            new { Reference = reference });
    }

    public List<BillPaymentRequestDto> GetPendingPayments()
    {
        using var connection = _factory.CreateConnection();

        return connection.Query<BillPaymentRequestDto>(
            """
            SELECT Reference, BankId, AccountNumber,
                   PaymentType, Network, PhoneNumber,
                   Amount, DataPlanName
            FROM OutgoingBillPayments
            WHERE Status = 'Pending'
            ORDER BY CreatedAt;
            """).ToList();
    }

    private sealed class AccountBalanceRow
    {
        public decimal Balance { get; set; }
        public bool IsFrozen { get; set; }
    }

    private sealed class PaymentRow
    {
        public string Reference { get; set; } = "";
        public string AccountNumber { get; set; } = "";
        public string BankId { get; set; } = "";
        public string PaymentType { get; set; } = "";
        public string Network { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public decimal Amount { get; set; }
        public string? DataPlanName { get; set; }
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public bool PaymentBelongsToAccount(
    string reference,
    string accountNumber)
    {
        using var connection = _factory.CreateConnection();

        return connection.ExecuteScalar<int>(
            """
        SELECT COUNT(*)
        FROM OutgoingBillPayments
        WHERE Reference = @Reference
          AND AccountNumber = @AccountNumber;
        """,
            new
            {
                Reference = reference,
                AccountNumber = accountNumber
            }) > 0;
    }
}
