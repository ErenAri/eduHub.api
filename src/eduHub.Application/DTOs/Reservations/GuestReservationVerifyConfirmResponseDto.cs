namespace eduHub.Application.DTOs.Reservations;

public class GuestReservationVerifyConfirmResponseDto
{
    public bool Verified { get; set; }
    public string? GuestToken { get; set; }
}
