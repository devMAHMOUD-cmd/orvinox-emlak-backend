using CraftoraApi.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
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

    [Fact]
    public void Subscription_controller_exposes_atomic_shop_start_route()
    {
        var method = typeof(SubscriptionController)
            .GetMethod(nameof(SubscriptionController.StartShopSubscriptionAsync));
        var route = method?
            .GetCustomAttributes(typeof(HttpPostAttribute), inherit: true)
            .Cast<HttpPostAttribute>()
            .SingleOrDefault();

        Assert.NotNull(method);
        Assert.Equal("start-with-shop", route?.Template);
    }
}
