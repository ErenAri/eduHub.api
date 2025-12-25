using FluentValidation;
using eduHub.Application.DTOs.Users;

namespace eduHub.Application.Validators.Users;

public class UserProfileUpdateDtoValidator : AbstractValidator<UserProfileUpdateDto>
{
    public UserProfileUpdateDtoValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(200);
    }
}
