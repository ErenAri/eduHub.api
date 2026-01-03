using eduHub.Application.DTOs.Public;
using FluentValidation;

namespace eduHub.Application.Validators.Public;

public class PublicContactRequestDtoValidator : AbstractValidator<PublicContactRequestDto>
{
    public PublicContactRequestDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(200);

        RuleFor(x => x.Organization)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(2000);
    }
}
