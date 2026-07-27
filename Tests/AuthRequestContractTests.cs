using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Auth;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class AuthRequestContractTests
{
    [Fact]
    public void Register_rejects_oversized_email_and_password()
    {
        var request = new RegisterDto(
            FullName: "Craftora Test",
            Email: $"{new string('a', 250)}@example.com",
            Password: new string('x', 129),
            PasswordConfirm: new string('x', 129));

        var results = Validate(request);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(RegisterDto.Email)));
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(RegisterDto.Password)));
    }

    [Fact]
    public void Login_rejects_oversized_credentials()
    {
        var request = new LoginDto(
            Email: $"{new string('a', 250)}@example.com",
            Password: new string('x', 129));

        var results = Validate(request);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(LoginDto.Email)));
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(LoginDto.Password)));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            instance,
            new ValidationContext(instance),
            results,
            validateAllProperties: true);
        return results;
    }
}
