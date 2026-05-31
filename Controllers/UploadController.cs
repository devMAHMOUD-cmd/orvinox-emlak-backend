using System.Security.Claims;
using CraftoraApi.DTOs;
using CraftoraApi.Messages;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class UploadController : ControllerBase
{
    private readonly IStorageService _storageService;
    private readonly ISendEndpointProvider _sendEndpointProvider;

    public UploadController(
        IStorageService storageService,
        ISendEndpointProvider sendEndpointProvider)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _sendEndpointProvider = sendEndpointProvider ?? throw new ArgumentNullException(nameof(sendEndpointProvider));
    }

    [HttpPost("public-url")]
    public IActionResult GeneratePublicUploadUrl([FromBody] GeneratePresignedUrlDto dto)
    {
        var objectKey = $"uploads/{Guid.NewGuid()}_{dto.FileName}";
        var uploadUrl = _storageService.GeneratePresignedUploadUrl(
            "public-assets",
            objectKey,
            dto.ContentType);

        return Ok(new
        {
            uploadUrl,
            objectKey
        });
    }

    [HttpPost("private-url")]
    public IActionResult GeneratePrivateUploadUrl([FromBody] GeneratePresignedUrlDto dto)
    {
        var objectKey = $"courses_or_products/{Guid.NewGuid()}_{dto.FileName}";
        var uploadUrl = _storageService.GeneratePresignedUploadUrl(
            "private-products",
            objectKey,
            dto.ContentType);

        return Ok(new
        {
            uploadUrl,
            objectKey
        });
    }

    [HttpPost("complete")]
    public async Task<IActionResult> CompleteUploadAsync([FromBody] UploadCompleteDto dto)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Geçersiz kullanıcı token'ı.");
        }

        var fileUploadedEvent = new FileUploadedEvent(
            UserId: userId,
            ObjectKey: dto.ObjectKey,
            EntityType: dto.EntityType,
            EntityId: dto.EntityId,
            UploadedAt: DateTime.UtcNow);

        var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:file_processing_queue"));
        await endpoint.Send(fileUploadedEvent);

        return Ok(new { message = "Dosya işleme kuyruğuna alındı." });
    }

    [HttpDelete("file")]
    public async Task<IActionResult> DeleteFileAsync(
        [FromQuery] string bucketName,
        [FromQuery] string objectKey)
    {
        await _storageService.DeleteFileAsync(bucketName, objectKey);
        return Ok(new { message = "Dosya başarıyla silindi." });
    }
}
