using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ApiControllerBase
{
    private readonly IUserService _userService;

    public AuthController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Registers a new user.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserResponseDto>> Register([FromBody] UserRegisterDto dto)
    {
        try
        {
            var user = await _userService.RegisterAsync(dto);
            return Ok(user);
        }
        catch (InvalidOperationException)
        {
            return BadRequestProblem("Unable to register.", "RegistrationFailed");
        }
    }

    /// <summary>
    /// Logs in and returns JWT + refresh token.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] UserLoginDto dto)
    {
        var auth = await _userService.LoginAsync(dto);
        if (auth == null)
            return BadRequestProblem("Invalid credentials.", "InvalidCredentials");

        return Ok(auth);
    }

    /// <summary>
    /// Returns the authenticated user profile.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponseDto>> Me()
    {
        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdValue) || !int.TryParse(userIdValue, out var userId))
            return UnauthorizedProblem("Invalid token.");

        var user = await _userService.GetByIdAsync(userId);
        if (user == null)
            return NotFoundProblem("User not found.");

        return Ok(user);
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token.
    /// </summary>
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

        var auth = await _userService.RefreshAsync(dto);
        if (auth == null)
            return UnauthorizedProblem("Invalid refresh token.");

        return Ok(auth);
    }

    /// <summary>
    /// Revokes the current access token immediately.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expClaim = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(jti) || string.IsNullOrWhiteSpace(userId) || !int.TryParse(userId, out var parsedUserId))
            return BadRequestProblem("Invalid token.", "InvalidToken");

        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);
        if (long.TryParse(expClaim, out var expSeconds))
            expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expSeconds);

        await _userService.RevokeTokenAsync(jti, expiresAtUtc, parsedUserId);
        await _userService.RevokeRefreshTokensAsync(parsedUserId);
        return NoContent();
    }
}
