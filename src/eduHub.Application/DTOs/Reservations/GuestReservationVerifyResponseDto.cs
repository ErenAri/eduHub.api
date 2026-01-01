namespace eduHub.Application.DTOs.Reservations;

public class GuestReservationVerifyResponseDto
{
    public bool VerificationSent { get; set; }
    public string? DebugToken { get; set; }
}
