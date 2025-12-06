using FluentValidation;

namespace eduHub.Application.DTOs.Rooms;

public class RoomUpdateDtoValidator : AbstractValidator<RoomUpdateDto>
{
    public RoomUpdateDtoValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0);

        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Capacity)
            .GreaterThan(0);
    }
}