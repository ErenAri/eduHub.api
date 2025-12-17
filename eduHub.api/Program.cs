using eduHub.api.Middleware;
using eduHub.Application.Validators.Users;
using eduHub.Infrastructure;
using eduHub.Infrastructure.Persistence;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// =======================================
// Controllers + FluentValidation
// =======================================

builder.Services.AddControllers();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<UserRegisterDtoValidator>();

builder.Services.AddEndpointsApiExplorer();

// =======================================
// Swagger + JWT Security
// =======================================

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "eduHub API",
        Version = "v1"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter: Bearer {your JWT token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new List<string>()
        }
    });
});

// =======================================
// Infrastructure (DbContext + Services)
// =======================================

builder.Services.AddInfrastructure(builder.Configuration);

// =======================================
// Authentication / JWT
// =======================================

var jwtSection = builder.Configuration.GetSection("Jwt");
var key = jwtSection["Key"];
if (string.IsNullOrWhiteSpace(key))
    throw new Exception("Jwt:Key is missing");
if (Encoding.UTF8.GetByteCount(key) < 32)
    throw new Exception("Jwt:Key must be at least 32 bytes (256-bit).");

var issuer = jwtSection["Issuer"];
if (string.IsNullOrWhiteSpace(issuer))
    issuer = "eduHub";

var audience = jwtSection["Audience"];
if (string.IsNullOrWhiteSpace(audience))
    audience = "eduHub";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ClockSkew = TimeSpan.Zero
        };
    });

// =======================================
// Authorization
// =======================================

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();

    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);

    options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 2;

    foreach (var child in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren())
    {
        if (IPAddress.TryParse(child.Value, out var proxyIp))
            options.KnownProxies.Add(proxyIp);
    }

    foreach (var child in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").GetChildren())
    {
        var cidr = child.Value;
        if (string.IsNullOrWhiteSpace(cidr))
            continue;

        var parts = cidr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            continue;

        if (!IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var prefixLength))
            continue;

        options.KnownNetworks.Add(new IPNetwork(prefix, prefixLength));
    }
});

var trustedForwardingConfigured =
    builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren().Any() ||
    builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").GetChildren().Any();
if (builder.Environment.IsProduction() && !trustedForwardingConfigured)
    throw new InvalidOperationException("Configure ForwardedHeaders:KnownProxies or KnownNetworks for production behind a proxy.");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext =>
    {
        var clientIp = GetClientIp(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 5,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
});

static string GetClientIp(HttpContext context)
{
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
var app = builder.Build();

// =======================================
// Database Migration + Seeding (OPT-IN)
// =======================================

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    var configuration = services.GetRequiredService<IConfiguration>();
    var env = services.GetRequiredService<IHostEnvironment>();

    var isProduction = env.IsProduction();
    var allowDangerousOps =
        configuration.GetValue("Startup:AllowDangerousOperationsInProduction", false);

    if (isProduction && !allowDangerousOps)
    {
        // skip migrations/seeding unless explicitly allowed
    }
    else
    {
        var autoMigrate = configuration.GetValue("Startup:AutoMigrate", env.IsDevelopment());
        if (autoMigrate)
        {
            await db.Database.MigrateAsync();
        }

        var seedEnabled = configuration.GetValue("Seed:Enabled", env.IsDevelopment());
        if (seedEnabled)
        {
            await DbInitializer.SeedAsync(db, configuration, env);
        }
    }
}

// =======================================
// Middleware Pipeline
// =======================================

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok("ok"))
   .RequireAuthorization();

app.Run();
