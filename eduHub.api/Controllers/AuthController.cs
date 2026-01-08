using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private readonly IUserService _userService;
    private readonly AppDbContext _context;

    public AuthController(IUserService userService, AppDbContext context)
    {
        _userService = userService;
        _context = context;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] UserLoginDto dto)
    {
        var headerSlug = Request.Headers["x-tenant-slug"].FirstOrDefault()?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(headerSlug))
        {
            var organization = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Slug == headerSlug);
            if (organization == null || !organization.IsActive)
                return NotFoundProblem("Tenant not found.");

            var auth = await _userService.LoginAsync(dto, organization.Id);
            if (auth == null)
                return BadRequestProblem("Invalid credentials.", "InvalidCredentials");

            auth.OrganizationSlug = organization.Slug;
            return Ok(auth);
        }

        var authResponse = await _userService.LoginUserAsync(dto);
        if (authResponse == null)
            return BadRequestProblem("Invalid credentials.", "InvalidCredentials");

        return Ok(authResponse);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserResponseDto>> Register(
        [FromBody] UserRegisterDto dto)
    {
        var conflictErrors = await GetRegistrationConflictsAsync(dto);
        if (conflictErrors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(conflictErrors));

        try
        {
            var user = await _userService.RegisterAsync(dto);
            return Ok(user);
        }
        catch (InvalidOperationException)
        {
            conflictErrors = await GetRegistrationConflictsAsync(dto);
            if (conflictErrors.Count > 0)
                return ValidationProblem(new ValidationProblemDetails(conflictErrors));

            return BadRequestProblem("Unable to register.");
        }
    }

    [HttpGet("me")]
    [Authorize]
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

        var authResponse = await _userService.RefreshUserAsync(dto);
        if (authResponse == null)
            return UnauthorizedProblem("Invalid refresh token.");

        return Ok(authResponse);
    }

    [HttpPost("logout")]
    [Authorize]
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

    private async Task<Dictionary<string, string[]>> GetRegistrationConflictsAsync(
        UserRegisterDto dto)
    {
        var conflicts = await _context.Users
            .AsNoTracking()
            .Where(u => u.UserName == dto.UserName || u.Email == dto.Email)
            .Select(u => new { u.UserName, u.Email })
            .ToListAsync();

        var errors = new Dictionary<string, string[]>();
        if (conflicts.Any(u => u.UserName == dto.UserName))
            errors["UserName"] = new[] { "This username is already taken." };
        if (conflicts.Any(u => u.Email == dto.Email))
            errors["Email"] = new[] { "This email is already in use." };

        return errors;
    }
}
