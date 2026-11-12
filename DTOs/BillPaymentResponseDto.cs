
namespace TatumWelcome2.DTOs;

public class BillPaymentResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}
