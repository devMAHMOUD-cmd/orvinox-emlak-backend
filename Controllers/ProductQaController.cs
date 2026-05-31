using System.Security.Claims;
using CraftoraApi.DTOs.Interaction;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/product-qa")]
public sealed class ProductQaController : ControllerBase
{
    private readonly IProductQaService _productQaService;

    public ProductQaController(IProductQaService productQaService)
    {
        _productQaService = productQaService ?? throw new ArgumentNullException(nameof(productQaService));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> AskQuestionAsync([FromBody] CreateQuestionDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _productQaService.AskQuestionAsync(userId, dto);

        return Ok(result);
    }

    [Authorize]
    [HttpPost("{id:guid}/answer")]
    public async Task<IActionResult> AnswerQuestionAsync(
        [FromRoute] Guid id,
        [FromBody] AnswerQuestionDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _productQaService.AnswerQuestionAsync(id, userId, dto);

        return Ok(result);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteQuestionAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _productQaService.DeleteQuestionAsync(id, userId);

        return NoContent();
    }

    [HttpGet("product/{productId:guid}")]
    public async Task<IActionResult> GetProductQuestionsAsync([FromRoute] Guid productId)
    {
        var result = await _productQaService.GetProductQuestionsAsync(productId);
        return Ok(result);
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Gecersiz kullanici token'i.");
        }

        return userId;
    }
}
