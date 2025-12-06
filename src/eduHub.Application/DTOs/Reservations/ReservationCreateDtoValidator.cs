using FluentValidation;

namespace eduHub.Application.DTOs.Reservations;

public class ReservationCreateDtoValidator : AbstractValidator<ReservationCreateDto>
{
    public ReservationCreateDtoValidator()
    {
        RuleFor(x => x.RoomId)
            .GreaterThan(0);

        RuleFor(x => x.Purpose)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.StartTimeUtc)
            .LessThan(x => x.EndTimeUtc);
    }
}
