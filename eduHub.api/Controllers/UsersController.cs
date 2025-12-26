using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using eduHub.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/org/users")]
[Authorize(Policy = AuthorizationConstants.Policies.OrgUser)]
public class UsersController : ApiControllerBase
{
    private const long MaxAvatarBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAvatarTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpg",
        "image/jpeg",
        "image/png"
    };
    private static readonly string[] AllowedAvatarExtensions = { ".jpg", ".png" };

    private readonly IUserService _userService;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public UsersController(
        IUserService userService,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _userService = userService;
        _environment = environment;
        _configuration = configuration;
    }

    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponseDto>> GetMe()
    {
        if (!TryGetUserId(out var userId))
            return UnauthorizedProblem("Invalid token.");

        if (!TryGetOrganizationId(out var organizationId))
            return UnauthorizedProblem("Invalid tenant context.");

        var user = await _userService.GetByIdInOrgAsync(userId, organizationId);
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

        var normalizedContentType = NormalizeContentType(avatar.ContentType);
        if (!string.IsNullOrWhiteSpace(normalizedContentType) &&
            !AllowedAvatarTypes.Contains(normalizedContentType))
            return BadRequestProblem("Only JPG or PNG images are allowed.");

        if (!TryGetImageExtension(avatar, out var extension))
            return BadRequestProblem("Only JPG or PNG images are allowed.");

        if (!string.IsNullOrWhiteSpace(normalizedContentType))
        {
            var expectedType = extension == ".png" ? "image/png" : "image/jpeg";
            if (!string.Equals(normalizedContentType, expectedType, StringComparison.OrdinalIgnoreCase))
                return BadRequestProblem("Avatar file content does not match the declared type.");
        }

        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var avatarDirectory = Path.Combine(webRoot, "uploads", "avatars");
        Directory.CreateDirectory(avatarDirectory);

        var fileName = $"user-{userId}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(avatarDirectory, fileName);

        foreach (var allowedExtension in AllowedAvatarExtensions)
        {
            var existingPath = Path.Combine(avatarDirectory, $"user-{userId}{allowedExtension}");
            if (!string.Equals(existingPath, filePath, StringComparison.OrdinalIgnoreCase) &&
                System.IO.File.Exists(existingPath))
            {
                System.IO.File.Delete(existingPath);
            }
        }

        await using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await avatar.CopyToAsync(stream);
        }

        var relativePath = $"/uploads/avatars/{fileName}";
        var avatarUrl = BuildAvatarUrl(relativePath);

        var updatedUrl = await _userService.UpdateAvatarAsync(userId, avatarUrl);
        return Ok(new UserAvatarResponseDto { AvatarUrl = updatedUrl });
    }

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrWhiteSpace(userIdValue) && int.TryParse(userIdValue, out userId);
    }

    private bool TryGetOrganizationId(out Guid organizationId)
    {
        organizationId = Guid.Empty;
        var orgIdValue = User.FindFirstValue(TenantClaimTypes.OrganizationId);
        return !string.IsNullOrWhiteSpace(orgIdValue) && Guid.TryParse(orgIdValue, out organizationId);
    }

    private string BuildAvatarUrl(string relativePath)
    {
        var baseUrl = _configuration.GetValue<string>("PublicBaseUrl");
        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return new Uri(baseUri, relativePath).ToString();
        }

        if (_environment.IsDevelopment() && Request.Host.HasValue)
            return $"{Request.Scheme}://{Request.Host}{relativePath}";

        return relativePath;
    }

    private static string NormalizeContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        return string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : contentType;
    }

    private static bool TryGetImageExtension(IFormFile file, out string extension)
    {
        extension = string.Empty;
        Span<byte> header = stackalloc byte[8];
        using var stream = file.OpenReadStream();
        if (!TryReadHeader(stream, header))
            return false;

        if (IsPng(header))
        {
            extension = ".png";
            return true;
        }

        if (IsJpeg(header))
        {
            extension = ".jpg";
            return true;
        }

        return false;
    }

    private static bool TryReadHeader(Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer.Slice(totalRead));
            if (read == 0)
                break;
            totalRead += read;
        }

        return totalRead >= buffer.Length;
    }

    private static bool IsPng(ReadOnlySpan<byte> header)
    {
        return header.Length >= 8 &&
               header[0] == 0x89 &&
               header[1] == 0x50 &&
               header[2] == 0x4E &&
               header[3] == 0x47 &&
               header[4] == 0x0D &&
               header[5] == 0x0A &&
               header[6] == 0x1A &&
               header[7] == 0x0A;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> header)
    {
        return header.Length >= 3 &&
               header[0] == 0xFF &&
               header[1] == 0xD8 &&
               header[2] == 0xFF;
    }
}
