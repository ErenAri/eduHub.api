using FluentValidation;
using eduHub.Application.DTOs.Users;

namespace eduHub.Application.Validators.Users;

public class OrganizationUserCreateDtoValidator : AbstractValidator<OrganizationUserCreateDto>
{
    public OrganizationUserCreateDtoValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(200);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12);
    }
}
