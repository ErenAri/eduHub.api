using FluentValidation;
using System;

namespace eduHub.Application.DTOs.Buildings;

public class BuildingCreateDtoValidator : AbstractValidator<BuildingCreateDto>
{
    public BuildingCreateDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);
    }
}

