using FluentValidation;
using eduHub.Application.DTOs.Users;

namespace eduHub.Application.Validators.Users;

public class OrganizationUserUpdateDtoValidator : AbstractValidator<OrganizationUserUpdateDto>
{
    public OrganizationUserUpdateDtoValidator()
    {
        RuleFor(x => x)
            .Must(dto =>
                dto.Role.HasValue ||
                dto.Status.HasValue ||
                !string.IsNullOrWhiteSpace(dto.UserName) ||
                !string.IsNullOrWhiteSpace(dto.Email))
            .WithMessage("Provide at least one field to update.");

        When(x => !string.IsNullOrWhiteSpace(x.UserName), () =>
        {
            RuleFor(x => x.UserName!)
                .MinimumLength(3)
                .MaximumLength(100);
        });

        When(x => !string.IsNullOrWhiteSpace(x.Email), () =>
        {
            RuleFor(x => x.Email!)
                .EmailAddress()
                .MaximumLength(200);
        });
    }
}
