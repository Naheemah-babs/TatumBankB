namespace TatumWelcome2.Domain.Models;

public static class OutgoingBillPaymentStatus
{
    public const string Pending = "Pending";
    public const string Success = "Success";
    public const string Failed = "Failed";
    public const string ReferenceConflict = "ReferenceConflict";
}

/// <summary>One row of OutgoingBillPayments (B's record of a payment sent to A).</summary>
public sealed class OutgoingBillPaymentRecord
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
    public string Status { get; set; } = OutgoingBillPaymentStatus.Pending;
    public DateTime CreatedAt { get; set; }
}

public enum OutgoingReservationStatus
{
    /// <summary>Debit made and a Pending row created. Safe to call A.</summary>
    Reserved,

    /// <summary>The reference was already known. See Existing.</summary>
    Existing,

    /// <summary>Refused before any debit (PIN, frozen, funds, account).</summary>
    Rejected
}

public sealed record OutgoingReservation(
    OutgoingReservationStatus Status,
    TransactionResult? RejectionReason = null,
    OutgoingBillPaymentRecord? Existing = null);