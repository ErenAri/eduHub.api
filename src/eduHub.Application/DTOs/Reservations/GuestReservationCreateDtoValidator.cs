using FluentValidation;

namespace eduHub.Application.DTOs.Reservations;

public class GuestReservationCreateDtoValidator : AbstractValidator<GuestReservationCreateDto>
{
    public GuestReservationCreateDtoValidator()
    {
        RuleFor(x => x.RoomId)
            .GreaterThan(0);

        RuleFor(x => x.Purpose)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.GuestToken)
            .NotEmpty();

        RuleFor(x => x.StartTimeUtc)
            .LessThan(x => x.EndTimeUtc);
    }
}
