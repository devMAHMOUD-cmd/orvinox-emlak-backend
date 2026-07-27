using CraftoraApi.DTOs.Auth;
using FluentValidation;

namespace CraftoraApi.Validators;

public sealed class RegisterDtoValidator : AbstractValidator<RegisterDto>
{
    public RegisterDtoValidator()
    {
        RuleFor(request => request.FullName)
            .NotEmpty()
            .Length(3, 100);

        RuleFor(request => request.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(255);

        RuleFor(request => request.Password)
            .NotEmpty()
            .Length(8, 128);

        RuleFor(request => request.PasswordConfirm)
            .NotEmpty()
            .Length(8, 128)
            .Equal(request => request.Password);
    }
}

public sealed class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(request => request.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(255);

        RuleFor(request => request.Password)
            .NotEmpty()
            .MaximumLength(128);
    }
}
