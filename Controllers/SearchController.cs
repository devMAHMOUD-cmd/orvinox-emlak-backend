using CraftoraApi.DTOs.Search;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/search")]
[EnableRateLimiting("search")]
public sealed class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    [HttpGet]
    public async Task<IActionResult> SearchProductsAsync([FromQuery] SearchRequestDto request)
    {
        var result = await _searchService.SearchProductsAsync(request);
        return Ok(result);
    }
}
