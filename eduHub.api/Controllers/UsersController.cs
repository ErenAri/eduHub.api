using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ApiControllerBase
{
    private const long MaxAvatarBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAvatarTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png"
    };
    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png"
    };

    private readonly IUserService _userService;
    private readonly IWebHostEnvironment _environment;

    public UsersController(IUserService userService, IWebHostEnvironment environment)
    {
        _userService = userService;
        _environment = environment;
    }

    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponseDto>> GetMe()
    {
        if (!TryGetUserId(out var userId))
            return UnauthorizedProblem("Invalid token.");

        var user = await _userService.GetByIdAsync(userId);
        if (user == null)
            return NotFoundProblem("User not found.");

        return Ok(user);
    }

    [HttpPatch("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponseDto>> UpdateProfile([FromBody] UserProfileUpdateDto dto)
    {
        if (!TryGetUserId(out var userId))
            return UnauthorizedProblem("Invalid token.");

        var updated = await _userService.UpdateProfileAsync(userId, dto);
        return Ok(updated);
    }

    [HttpPost("me/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] UserChangePasswordDto dto)
    {
        if (!TryGetUserId(out var userId))
            return UnauthorizedProblem("Invalid token.");

        await _userService.ChangePasswordAsync(userId, dto);
        return NoContent();
    }

    [HttpPost("me/avatar")]
    [RequestSizeLimit(MaxAvatarBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserAvatarResponseDto>> UploadAvatar([FromForm] IFormFile avatar)
    {
        if (!TryGetUserId(out var userId))
            return UnauthorizedProblem("Invalid token.");

        if (avatar == null || avatar.Length == 0)
            return BadRequestProblem("Avatar file is required.");

        if (avatar.Length > MaxAvatarBytes)
            return BadRequestProblem("Avatar file must be 2MB or smaller.");

        if (!AllowedAvatarTypes.Contains(avatar.ContentType))
            return BadRequestProblem("Only JPG or PNG images are allowed.");

        var extension = Path.GetExtension(avatar.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedAvatarExtensions.Contains(extension))
            return BadRequestProblem("Only JPG or PNG images are allowed.");

        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var avatarDirectory = Path.Combine(webRoot, "uploads", "avatars");
        Directory.CreateDirectory(avatarDirectory);

        var fileName = $"user-{userId}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(avatarDirectory, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await avatar.CopyToAsync(stream);
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var avatarUrl = $"{baseUrl}/uploads/avatars/{fileName}";

        var updatedUrl = await _userService.UpdateAvatarAsync(userId, avatarUrl);
        return Ok(new UserAvatarResponseDto { AvatarUrl = updatedUrl });
    }

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrWhiteSpace(userIdValue) && int.TryParse(userIdValue, out userId);
    }
}
