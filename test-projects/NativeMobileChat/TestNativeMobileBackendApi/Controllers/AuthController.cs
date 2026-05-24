using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TestNativeMobileBackendApi.Configuration;
using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models.Auth;
using TestNativeMobileBackendApi.Services;

namespace TestNativeMobileBackendApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            return Ok(_authService.Register(request));
        }
        catch (InvalidOperationException ex) when (ex.Message is "UserNameInUse" or "EmailInUse")
        {
            return Conflict(ex.Message);
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var response = _authService.Login(request);
        return response is null ? Unauthorized() : Ok(response);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("refresh")]
    public IActionResult Refresh([FromBody] RefreshTokenRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var response = _authService.Refresh(request);
        return response is null ? Unauthorized() : Ok(response);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("revoke")]
    public IActionResult Revoke([FromBody] RefreshTokenRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return _authService.RevokeRefreshToken(request.RefreshToken) ? Ok() : NotFound();
    }

    [Authorize(Policy = AuthorizationPolicies.ChatUser)]
    [HttpPost("revoke-all")]
    public IActionResult RevokeAll()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null || !Guid.TryParse(userId, out var id))
        {
            return Unauthorized();
        }

        _authService.RevokeAllForUser(id);
        return Ok();
    }

    [Authorize(Policy = AuthorizationPolicies.ChatUser)]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null || !Guid.TryParse(userId, out var id))
        {
            return Unauthorized();
        }

        var users = HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var user = users.FindById(id);
        return user is null ? NotFound() : Ok(TokenService.ToProfile(user));
    }
}
