using CraftoraApi.DTOs.Auth;
using CraftoraApi.Validators;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class AuthRequestValidatorTests
{
    [Fact]
    public async Task Login_rejects_oversized_credentials()
    {
        var request = new LoginDto(
            Email: $"{new string('a', 250)}@example.com",
            Password: new string('x', 129));

        var result = await new LoginDtoValidator().ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginDto.Email));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginDto.Password));
    }

    [Fact]
    public async Task Register_rejects_oversized_fields()
    {
        var password = new string('x', 129);
        var request = new RegisterDto(
            FullName: new string('x', 101),
            Email: $"{new string('a', 250)}@example.com",
            Password: password,
            PasswordConfirm: password);

        var result = await new RegisterDtoValidator().ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterDto.FullName));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterDto.Email));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterDto.Password));
    }
}
