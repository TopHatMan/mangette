using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record AuthStatus(bool Enabled, bool Authenticated, string? Username);

    [HttpGet("Status")]
    [ProducesResponseType<AuthStatus>(Status200OK, "application/json")]
    public Ok<AuthStatus> Status()
    {
        bool enabled = Mangette.Settings.AuthenticationEnabled;
        bool authed = User.Identity?.IsAuthenticated == true;
        return TypedResults.Ok(new AuthStatus(enabled, authed, authed ? User.Identity?.Name : null));
    }

    [HttpPost("Login")]
    [ProducesResponseType<AuthStatus>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status401Unauthorized, "text/plain")]
    public async Task<Results<Ok<AuthStatus>, BadRequest<string>, UnauthorizedHttpResult>> Login([FromBody] LoginRequest request)
    {
        if (!Mangette.Settings.AuthenticationEnabled)
            return TypedResults.Ok(new AuthStatus(false, true, null));
        if (string.IsNullOrWhiteSpace(request.Username) || request.Password is null)
            return TypedResults.BadRequest("Username and password are required.");
        if (!string.Equals(request.Username.Trim(), Mangette.Settings.AuthUsername, StringComparison.Ordinal) ||
            !AuthCrypto.VerifyPassword(request.Password, Mangette.Settings.AuthPasswordHash))
            return TypedResults.Unauthorized();

        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, Mangette.Settings.AuthUsername)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });
        return TypedResults.Ok(new AuthStatus(true, true, Mangette.Settings.AuthUsername));
    }

    [HttpPost("Logout")]
    [ProducesResponseType(Status200OK)]
    public async Task<Ok> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.Ok();
    }
}
