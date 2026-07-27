using CraftoraApi.Infrastructure.Security;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class SupportAddressResolverTests
{
    [Fact]
    public void Exact_support_address_starts_a_new_ticket()
    {
        var result = SupportAddressResolver.Resolve(
            new[] { "Craftora <support@craftoramedya.com>" },
            "support@craftoramedya.com");

        Assert.NotNull(result);
        Assert.Null(result.TicketId);
        Assert.Equal("support@craftoramedya.com", result.Address);
    }

    [Fact]
    public void Plus_address_resolves_existing_ticket()
    {
        var ticketId = Guid.NewGuid();

        var result = SupportAddressResolver.Resolve(
            new[] { $"support+{ticketId:D}@craftoramedya.com" },
            "support@craftoramedya.com");

        Assert.NotNull(result);
        Assert.Equal(ticketId, result.TicketId);
    }

    [Theory]
    [InlineData("support+not-a-guid@craftoramedya.com")]
    [InlineData("support@example.com")]
    [InlineData("other@craftoramedya.com")]
    public void Unrelated_or_invalid_address_is_ignored(string recipient)
    {
        var result = SupportAddressResolver.Resolve(
            new[] { recipient },
            "support@craftoramedya.com");

        Assert.Null(result);
    }
}
