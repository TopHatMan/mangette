using API.Controllers.DTOs;
using API.Schema.MangaContext;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class DiscoverController(MangaContext context) : ControllerBase
{
    /// <summary>
    /// Trending, popular, new, and recently updated manga from AniList (no API key).
    /// Same idea as Radarr Discover. Cached for 30 minutes.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<DiscoverPage>(Status200OK, "application/json")]
    public async Task<Ok<DiscoverPage>> Get([FromQuery] bool refresh = false)
    {
        DiscoverPage page = await DiscoverCatalog.Load(context, HttpContext.RequestAborted, refresh);
        return TypedResults.Ok(page);
    }
}
