using FluentValidation;

namespace eduHub.Application.DTOs.Rooms;

public class RoomCreateDtoValidator : AbstractValidator<RoomCreateDto>
{
    public RoomCreateDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Capacity)
            .GreaterThan(0);

        RuleFor(x => x.BuildingId)
            .GreaterThan(0);
    }
}
