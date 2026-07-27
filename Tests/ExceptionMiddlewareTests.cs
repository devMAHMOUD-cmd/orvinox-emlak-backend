using System.Text.Json;
using CraftoraApi.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class ExceptionMiddlewareTests
{
    [Fact]
    public async Task Oversized_request_returns_413_without_internal_error()
    {
        var middleware = new ExceptionMiddleware(
            _ => throw new BadHttpRequestException(
                "Request body too large.",
                StatusCodes.Status413PayloadTooLarge),
            NullLogger<ExceptionMiddleware>.Instance,
            new TestWebHostEnvironment());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        var error = response.RootElement.GetProperty("error");

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal("PAYLOAD_TOO_LARGE", error.GetProperty("code").GetString());
        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            error.GetProperty("statusCode").GetInt32());
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CraftoraApi.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
