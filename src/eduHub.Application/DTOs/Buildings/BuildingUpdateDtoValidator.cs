using FluentValidation;

namespace eduHub.Application.DTOs.Buildings;

public class BuildingUpdateDtoValidator : AbstractValidator<BuildingUpdateDto>
{
    public BuildingUpdateDtoValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0);

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);
    }
}
