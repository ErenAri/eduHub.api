namespace eduHub.api.Models;

public class ErrorResponse
{
    public string Code { get; set; } = null!;
    public string Message { get; set; } = null!;
    public object? Details { get; set; }
}
