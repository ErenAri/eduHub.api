using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Users;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


namespace eduHub.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword("eduHub-dummy-password");

    public UserService(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<UserResponseDto> RegisterAsync(UserRegisterDto dto)
    {

        var exists = await _context.Users
            .AnyAsync(u => u.UserName == dto.UserName || u.Email == dto.Email);

        if (exists)
            throw new InvalidOperationException("Unable to register.");

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        var user = new User
        {
            UserName = dto.UserName,
            Email = dto.Email,
            PasswordHash = passwordHash,
            Role = UserRole.User,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Users.Add(user);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new InvalidOperationException("Unable to register.");
        }

        return new UserResponseDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            Role = user.Role.ToString()
        };
    }

    public async Task<AuthResponseDto?> LoginAsync(UserLoginDto dto)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.UserName == dto.UserNameOrEmail ||
                u.Email == dto.UserNameOrEmail);

        if (user == null)
        {
            _ = BCrypt.Net.BCrypt.Verify(dto.Password, DummyPasswordHash);
            return null;
        }

        var valid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        if (!valid)
            return null;

        var accessToken = GenerateJwtToken(user, out var expiresAtUtc);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            ExpiresAtUtc = expiresAtUtc,
            User = new UserResponseDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Role = user.Role.ToString()
            }
        };
    }

    private string GenerateJwtToken(User user, out DateTime expiresAtUtc)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var key = jwtSection["Key"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Jwt:Key is missing.");
        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes (256-bit).");

        var issuer = jwtSection["Issuer"];
        if (string.IsNullOrWhiteSpace(issuer))
            issuer = "eduHub";

        var audience = jwtSection["Audience"];
        if (string.IsNullOrWhiteSpace(audience))
            audience = "eduHub";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var minutes = _configuration.GetValue("Jwt:AccessTokenMinutes", 15);
        if (minutes < 5 || minutes > 60)
            throw new InvalidOperationException("Jwt:AccessTokenMinutes must be between 5 and 60.");
        expiresAtUtc = DateTime.UtcNow.AddMinutes(minutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
