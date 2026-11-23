namespace TatumWelcome2.Domain.Models;

public enum TransactionResult
{
    Success,
    InvalidAmount,
    InsufficientFunds,
    AccountNotFound,
    SameAccount,
    InvalidRecipient,

    InvalidPin,

    InvalidNetwork,

    InvalidPhone,

    InvalidPlan,
    AccountLocked,
    AccountFrozen,
    PaymentFailed,
    BalanceLimitExceeded,
    DuplicateReference,
    PinRequired,
    RecipientRequired,
    AmountNotWholeNumber,
    AmountOutOfRange
}