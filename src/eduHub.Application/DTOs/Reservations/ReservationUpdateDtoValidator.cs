using FluentValidation;

namespace eduHub.Application.DTOs.Reservations;

public class ReservationUpdateDtoValidator : AbstractValidator<ReservationUpdateDto>
{
    public ReservationUpdateDtoValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0);

        RuleFor(x => x.Purpose)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.StartTimeUtc)
            .LessThan(x => x.EndTimeUtc);
    }
}
