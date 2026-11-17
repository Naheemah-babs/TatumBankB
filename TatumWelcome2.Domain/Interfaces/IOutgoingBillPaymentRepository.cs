using TatumWelcome2.Domain.Models;

namespace TatumWelcome2.Domain.Interfaces;

public interface IOutgoingBillPaymentRepository
{
    /// <summary>
    /// One DB transaction, under the account lock: account exists, PIN correct,
    /// reference unused, not frozen, sufficient funds. Then debits and inserts
    /// a Pending row. Returns Existing (with the stored row) for a known reference.
    /// </summary>
    OutgoingReservation Reserve(
        BillPaymentRequest request,
        string pin,
        string description);

    /// <summary>
    /// Moves a Pending payment to Success (writes the ledger row) or Failed
    /// (refunds). Locks the row and checks its status, so it never completes
    /// twice. Returns true if the payment is now in the requested final state.
    /// </summary>
    bool Complete(string reference, bool success);

    /// <summary>Pending to ReferenceConflict. Moves no money.</summary>
    bool MarkReferenceConflict(string reference);

    OutgoingBillPaymentRecord? GetByReference(string reference);

    List<OutgoingBillPaymentRecord> GetPending();
}