
namespace TatumWelcome2.DTOs;

public class InterbankTransferRequestDto
{
    public string Reference { get; set; } = string.Empty;

    public string SenderAccountNumber { get; set; } = string.Empty;

    public string RecipientAccountNumber { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}
