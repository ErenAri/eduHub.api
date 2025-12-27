using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using eduHub.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/platform/auth")]
public class PlatformAuthController : ApiControllerBase
{
    private readonly IUserService _userService;

    public PlatformAuthController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] UserLoginDto dto)
    {
        var auth = await _userService.LoginPlatformAsync(dto);
        if (auth == null)
            return BadRequestProblem("Invalid credentials.", "InvalidCredentials");

        return Ok(auth);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Refresh([FromBody] RefreshRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return BadRequestProblem("Refresh token is required.", "RefreshTokenRequired");

        var auth = await _userService.RefreshPlatformAsync(dto);
        if (auth == null)
            return UnauthorizedProblem("Invalid refresh token.");

        return Ok(auth);
    }

    [HttpGet("me")]
    [Authorize(Policy = AuthorizationConstants.Policies.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponseDto>> Me()
    {
        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdValue) || !int.TryParse(userIdValue, out var userId))
            return UnauthorizedProblem("Invalid token.");

        var user = await _userService.GetByIdAsync(userId);
        if (user == null)
            return UnauthorizedProblem("Invalid token.");

        return Ok(user);
    }

    [HttpPost("logout")]
    [Authorize(Policy = AuthorizationConstants.Policies.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expClaim = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(jti) || string.IsNullOrWhiteSpace(userId) ||
            !int.TryParse(userId, out var parsedUserId))
            return BadRequestProblem("Invalid token.", "InvalidToken");

        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);
        if (long.TryParse(expClaim, out var expSeconds))
            expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expSeconds);

        await _userService.RevokeTokenAsync(jti, expiresAtUtc, parsedUserId);
        await _userService.RevokeRefreshTokensAsync(parsedUserId);
        return NoContent();
    }
}
