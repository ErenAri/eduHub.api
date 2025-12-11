using FluentValidation;

namespace eduHub.Application.DTOs.Users;

public class UserLoginDtoValidator : AbstractValidator<UserLoginDto>
{
    public UserLoginDtoValidator()
    {
        RuleFor(x => x.UserNameOrEmail)
            .NotEmpty();

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}
