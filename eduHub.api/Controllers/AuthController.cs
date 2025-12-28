using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
            return ValidationProblem(conflictErrors);

        try
        {
            var user = await _userService.RegisterAsync(dto);
            return Ok(user);
        }
        catch (InvalidOperationException)
        {
            conflictErrors = await GetRegistrationConflictsAsync(dto);
            if (conflictErrors.Count > 0)
                return ValidationProblem(conflictErrors);

            return BadRequestProblem("Unable to register.");
        }
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
