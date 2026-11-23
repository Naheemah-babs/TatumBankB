using System;
using System.Collections.Generic;
using System.Text;
using TatumWelcome2.Domain.Models;

namespace TatumWelcome2.BLL.DTOs
{
    public enum BillPurchaseOutcome
    {
        /// <summary>Provider approved; the account was debited.</summary>
        Successful,

        /// <summary>Provider declined; the account was not debited.</summary>
        Declined,

        /// <summary>
        /// Refused before reaching the provider (PIN, frozen, funds, account);
        /// see <see cref="BillPurchaseResult.FailureReason"/>. The reference was
        /// not consumed and may be retried.
        /// </summary>
        Rejected,

        /// <summary>Reference already used for a different purchase.</summary>
        ReferenceConflict,

        InvalidRequest,

        /// <summary>
        /// Debited, but the outcome at the provider is unknown (timeout or network
        /// error). The Processor will settle it. No further debit on retry.
        /// </summary>
        Pending,

        /// <summary>
        /// Wrong-PIN lockout is active; nothing was debited.
        /// See <see cref="BillPurchaseResult.RetryAfterSeconds"/>.
        /// </summary>
        AccountLocked
    }

    /// <param name="Replayed">
    /// True when this reference was already processed: the stored outcome is
    /// returned and no further debit is made.
    /// </param>
    /// <param name="RetryAfterSeconds">Seconds until the lock ends (AccountLocked only).</param>
    public sealed record BillPurchaseResult(
        BillPurchaseOutcome Outcome,
        string Reference,
        string Message,
        decimal Amount,
        TransactionResult? FailureReason = null,
        bool Replayed = false,
        int? RetryAfterSeconds = null);
}