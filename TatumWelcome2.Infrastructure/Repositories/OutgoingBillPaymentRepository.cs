using Dapper;
using Microsoft.Data.SqlClient;
using TatumWelcome2.BLL.DTOs;
using TatumWelcome2.BLL.IServices;
using TatumWelcome2.Domain.Interfaces;
using TatumWelcome2.Domain.Models;
using TatumWelcome2.Infrastructure.Database;

namespace TatumWelcome2.Infrastructure.Repositories;

public sealed class OutgoingBillPaymentRepository : IOutgoingBillPaymentRepository
{
    private const string SelectPaymentSql = """
        SELECT Reference, AccountNumber, BankId, PaymentType, Network,
               PhoneNumber, Amount, DataPlanName, Description,
               Status, CreatedAt
        FROM OutgoingBillPayments
        WHERE Reference = @Reference
        """;

    private const string SelectPaymentForUpdateSql = """
        SELECT Reference, AccountNumber, BankId, PaymentType, Network,
               PhoneNumber, Amount, DataPlanName, Description,
               Status, CreatedAt
        FROM OutgoingBillPayments WITH (UPDLOCK, ROWLOCK)
        WHERE Reference = @Reference;
        """;

    private readonly IDbConnectionFactory _factory;
    private readonly IPinHasher _pinHasher;

    public OutgoingBillPaymentRepository(
        IDbConnectionFactory factory,
        IPinHasher pinHasher)
    {
        _factory = factory;
        _pinHasher = pinHasher;
    }

    public OutgoingReservation Reserve(
        BillPaymentRequest request,
        string pin,
        string description)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            AccountRow? account = connection.QuerySingleOrDefault<AccountRow>(
                """
                SELECT PinHash, Balance, IsFrozen
                FROM Accounts WITH (UPDLOCK, ROWLOCK)
                WHERE AccountNumber = @AccountNumber;
                """,
                new { request.AccountNumber },
                transaction);

            if (account == null)
                return Reject(transaction, TransactionResult.AccountNotFound);

            if (!_pinHasher.Verify(pin, account.PinHash))
                return Reject(transaction, TransactionResult.InvalidPin);

            OutgoingBillPaymentRecord? existing =
                connection.QuerySingleOrDefault<OutgoingBillPaymentRecord>(
                    SelectPaymentSql,
                    new { request.Reference },
                    transaction);

            if (existing != null)
            {
                transaction.Rollback();

                return new OutgoingReservation(
                    OutgoingReservationStatus.Existing,
                    Existing: existing);
            }

            if (account.IsFrozen)
                return Reject(transaction, TransactionResult.AccountFrozen);

            if (account.Balance < request.Amount)
                return Reject(transaction, TransactionResult.InsufficientFunds);

            connection.Execute(
                """
                UPDATE Accounts
                SET Balance = Balance - @Amount
                WHERE AccountNumber = @AccountNumber;
                """,
                new { request.Amount, request.AccountNumber },
                transaction);

            connection.Execute(
                """
                INSERT INTO OutgoingBillPayments
                    (Reference, AccountNumber, BankId, PaymentType,
                     Network, PhoneNumber, Amount, DataPlanName,
                     Description, Status)
                VALUES
                    (@Reference, @AccountNumber, @BankId, @PaymentType,
                     @Network, @PhoneNumber, @Amount, @DataPlanName,
                     @Description, 'Pending');
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

            transaction.Commit();
            return new OutgoingReservation(OutgoingReservationStatus.Reserved);
        }
        catch (SqlException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            // Same reference taken by another account at the same moment.
            transaction.Rollback();

            return new OutgoingReservation(
                OutgoingReservationStatus.Existing,
                Existing: GetByReference(request.Reference));
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public bool Complete(string reference, bool success)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            OutgoingBillPaymentRecord? payment =
                connection.QuerySingleOrDefault<OutgoingBillPaymentRecord>(
                    SelectPaymentForUpdateSql,
                    new { Reference = reference },
                    transaction);

            if (payment == null)
            {
                transaction.Rollback();
                return false;
            }

            // Never complete the same payment twice.
            if (payment.Status != OutgoingBillPaymentStatus.Pending)
            {
                transaction.Rollback();

                return payment.Status == (success
                    ? OutgoingBillPaymentStatus.Success
                    : OutgoingBillPaymentStatus.Failed);
            }

            if (success)
            {
                string type = payment.PaymentType == BillPaymentTypes.Data
                    ? TransactionType.DataPurchase.ToString()
                    : TransactionType.AirtimePurchase.ToString();

                connection.Execute(
                    """
                    INSERT INTO Transactions
                        (AccountNumber, Type, Amount, Date,
                         Description, Reference)
                    VALUES
                        (@AccountNumber, @Type, @Amount, @Date,
                         @Description, @Reference);
                    """,
                    new
                    {
                        payment.AccountNumber,
                        Type = type,
                        payment.Amount,
                        Date = DateTime.UtcNow,
                        payment.Description,
                        payment.Reference
                    },
                    transaction);
            }
            else
            {
                decimal balance = connection.QuerySingle<decimal>(
                    """
                    SELECT Balance
                    FROM Accounts WITH (UPDLOCK, ROWLOCK)
                    WHERE AccountNumber = @AccountNumber;
                    """,
                    new { payment.AccountNumber },
                    transaction);

                // A refund must not push the account over its balance cap.
                if (balance > AccountLimits.MaximumBalance - payment.Amount)
                {
                    transaction.Rollback();
                    return false;
                }

                connection.Execute(
                    """
                    UPDATE Accounts
                    SET Balance = Balance + @Amount
                    WHERE AccountNumber = @AccountNumber;
                    """,
                    new { payment.AccountNumber, payment.Amount },
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
                    Status = success
                        ? OutgoingBillPaymentStatus.Success
                        : OutgoingBillPaymentStatus.Failed
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

    public bool MarkReferenceConflict(string reference)
    {
        using var connection = _factory.CreateConnection();

        int updated = connection.Execute(
            """
            UPDATE OutgoingBillPayments
            SET Status = 'ReferenceConflict'
            WHERE Reference = @Reference
              AND Status = 'Pending';
            """,
            new { Reference = reference });

        return updated == 1;
    }

    public OutgoingBillPaymentRecord? GetByReference(string reference)
    {
        using var connection = _factory.CreateConnection();

        return connection.QuerySingleOrDefault<OutgoingBillPaymentRecord>(
            SelectPaymentSql + ";",
            new { Reference = reference });
    }

    public List<OutgoingBillPaymentRecord> GetPending()
    {
        using var connection = _factory.CreateConnection();

        return connection.Query<OutgoingBillPaymentRecord>(
            """
            SELECT Reference, AccountNumber, BankId, PaymentType, Network,
                   PhoneNumber, Amount, DataPlanName, Description,
                   Status, CreatedAt
            FROM OutgoingBillPayments
            WHERE Status = 'Pending'
            ORDER BY CreatedAt;
            """).ToList();
    }

    private static OutgoingReservation Reject(
        SqlTransaction transaction,
        TransactionResult reason)
    {
        transaction.Rollback();

        return new OutgoingReservation(
            OutgoingReservationStatus.Rejected,
            reason);
    }

    private sealed class AccountRow
    {
        public string PinHash { get; set; } = "";
        public decimal Balance { get; set; }
        public bool IsFrozen { get; set; }
    }
}