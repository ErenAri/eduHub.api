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
public class AuthController : ControllerBase
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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserResponseDto>> Register([FromBody] UserRegisterDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var user = await _userService.RegisterAsync(dto);
            return Ok(user);
        }
        catch (InvalidOperationException)
        {
            return BadRequest("Unable to register.");
        }
    }

    /// <summary>
    /// Logs in and returns JWT + refresh token.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] UserLoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var auth = await _userService.LoginAsync(dto);
        if (auth == null)
            return BadRequest("Invalid credentials.");

        return Ok(auth);
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponseDto>> Refresh([FromBody] RefreshRequestDto dto)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(dto.RefreshToken))
            return BadRequest("Refresh token is required.");

        var auth = await _userService.RefreshAsync(dto);
        if (auth == null)
            return Unauthorized();

        return Ok(auth);
    }

    /// <summary>
    /// Revokes the current access token immediately.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expClaim = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(jti) || string.IsNullOrWhiteSpace(userId) || !int.TryParse(userId, out var parsedUserId))
            return BadRequest("Invalid token.");

        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);
        if (long.TryParse(expClaim, out var expSeconds))
            expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expSeconds);

        await _userService.RevokeTokenAsync(jti, expiresAtUtc, parsedUserId);
        await _userService.RevokeRefreshTokensAsync(parsedUserId);
        return NoContent();
    }
}
