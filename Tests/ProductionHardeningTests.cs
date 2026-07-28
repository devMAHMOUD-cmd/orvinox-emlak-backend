using CraftoraApi.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class ProductionHardeningTests
{
    [Theory]
    [InlineData(typeof(OrderController))]
    [InlineData(typeof(CartController))]
    [InlineData(typeof(CouponController))]
    [InlineData(typeof(SubscriptionController))]
    public void Commerce_controllers_use_general_rate_limit(Type controllerType)
    {
        var attribute = controllerType
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal("general", attribute.PolicyName);
    }
}
