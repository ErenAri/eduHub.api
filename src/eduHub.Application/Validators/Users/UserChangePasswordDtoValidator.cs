using FluentValidation;
using eduHub.Application.DTOs.Users;

namespace eduHub.Application.Validators.Users;

public class UserChangePasswordDtoValidator : AbstractValidator<UserChangePasswordDto>
{
    public UserChangePasswordDtoValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty();

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(12);
    }
}
