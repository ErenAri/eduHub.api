using eduHub.api.HostedServices;
using eduHub.api.Middleware;
using eduHub.api.Options;
using eduHub.Application.Validators.Users;
using eduHub.Infrastructure;
using eduHub.Infrastructure.Persistence;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Net;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Security.Claims;
using eduHub.Application.Security;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

var builder = WebApplication.CreateBuilder(args);

var corsPolicyName = "CorsPolicy";
var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

if (builder.Environment.IsProduction() && allowedCorsOrigins.Length == 0)
    throw new InvalidOperationException("Configure Cors:AllowedOrigins for production environments.");

builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        if (allowedCorsOrigins.Length == 0)
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedCorsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// =======================================
// Controllers + FluentValidation
// =======================================

builder.Services.AddControllers();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<UserRegisterDtoValidator>();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problemDetailsFactory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = problemDetailsFactory.CreateValidationProblemDetails(
            context.HttpContext,
            context.ModelState);
        problemDetails.Type = "https://httpstatuses.com/400";
        problemDetails.Extensions["code"] = "ValidationError";
        problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        return new BadRequestObjectResult(problemDetails)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});

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
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Key),
        "Jwt:Key is missing.")
    .Validate(options => Encoding.UTF8.GetByteCount(options.Key) >= 32,
        "Jwt:Key must be at least 32 bytes (256-bit).")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer),
        "Jwt:Issuer is missing.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Audience),
        "Jwt:Audience is missing.")
    .Validate(options => options.AccessTokenMinutes is >= 5 and <= 60,
        "Jwt:AccessTokenMinutes must be between 5 and 60.")
    .Validate(options => options.RefreshTokenDays is >= 1 and <= 90,
        "Jwt:RefreshTokenDays must be between 1 and 90.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;

        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();    
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters       
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var jti = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);

                if (string.IsNullOrWhiteSpace(userIdValue) || string.IsNullOrWhiteSpace(jti) || !int.TryParse(userIdValue, out var userId))
                {
                    context.Fail("Invalid token claims.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var userExists = await db.Users.AnyAsync(u => u.Id == userId);
                var isRevoked = await db.RevokedTokens.AnyAsync(t => t.Jti == jti);

                if (!userExists || isRevoked)
                    context.Fail("Token is no longer valid.");
            }
        };
    });

// =======================================
// Authorization
// =======================================

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationConstants.Policies.AdminOnly, policy => policy.RequireRole(AuthorizationConstants.Roles.Admin));
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var trustAllForwarders = builder.Configuration.GetValue("ForwardedHeaders:TrustAll", false);
var ingressLockedDown = builder.Configuration.GetValue("ForwardedHeaders:IngressLockedDown", false);
var trustedForwardingConfigured =
    builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren().Any() ||
    builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").GetChildren().Any();
var requireKnownProxies = builder.Configuration.GetValue("ForwardedHeaders:RequireKnownProxies", true);
if (trustAllForwarders && !ingressLockedDown)
    throw new InvalidOperationException("ForwardedHeaders:TrustAll requires ingress to be locked down.");
if (builder.Environment.IsProduction() && !trustAllForwarders && requireKnownProxies && !trustedForwardingConfigured)
    throw new InvalidOperationException("Configure ForwardedHeaders:KnownProxies or KnownNetworks for production behind a proxy.");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();

    options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 2;

    if (trustAllForwarders)
    {
        options.KnownNetworks.Add(new IPNetwork(IPAddress.Any, 0));
        options.KnownNetworks.Add(new IPNetwork(IPAddress.IPv6Any, 0));
        return;
    }

    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);

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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext =>
    {
        var partitionKey = GetRateLimitPartition(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 5,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var partitionKey = GetRateLimitPartition(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 60,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
});

// =======================================
// Observability (OpenTelemetry + Health)
// =======================================

var otelServiceName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceName")
    ?? builder.Environment.ApplicationName;
var otelServiceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
var otlpEndpoint = builder.Configuration.GetValue<string>("OpenTelemetry:Otlp:Endpoint");
var samplingRatio = builder.Configuration.GetValue("OpenTelemetry:SamplingRatio", 1.0);
samplingRatio = Math.Clamp(samplingRatio, 0.0, 1.0);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(otelServiceName, serviceVersion: otelServiceVersion)
        .AddAttributes(new[]
        {
            new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)
        }))
    .WithTracing(tracing =>
    {
        tracing
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
            .AddAspNetCoreInstrumentation(options => { options.RecordException = true; })
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http");

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
    });

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddDbContextCheck<AppDbContext>("db", tags: new[] { "ready" });

builder.Services.AddOptions<RequestLoggingOptions>()
    .Bind(builder.Configuration.GetSection(RequestLoggingOptions.SectionName))
    .Validate(options => options.SlowRequestThresholdMs >= 100,
        "RequestLogging:SlowRequestThresholdMs must be at least 100.")
    .ValidateOnStart();

builder.Services.AddOptions<TokenCleanupOptions>()
    .Bind(builder.Configuration.GetSection(TokenCleanupOptions.SectionName))
    .Validate(options => options.IntervalMinutes is >= 5 and <= 1440,
        "TokenCleanup:IntervalMinutes must be between 5 and 1440.")
    .ValidateOnStart();
builder.Services.AddHostedService<TokenCleanupService>();

static string GetClientIp(HttpContext context)
{
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

static string GetRateLimitPartition(HttpContext context)
{
    if (context.User?.Identity?.IsAuthenticated == true)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
            return $"user:{userId}";
    }

    return $"ip:{GetClientIp(context)}";
}
var app = builder.Build();

// =======================================
// Database Migration + Seeding (Development only)
// =======================================

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    var configuration = services.GetRequiredService<IConfiguration>();
    var env = services.GetRequiredService<IHostEnvironment>();

    var adminSeedingEnabled = configuration.GetValue("Seed:Admin:Enabled", false);
    if (adminSeedingEnabled && !env.IsDevelopment())
        throw new InvalidOperationException("Admin seeding is only supported in Development.");

    if (env.IsDevelopment())
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
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

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
app.UseCors(corsPolicyName);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description
        }),
        durationMs = report.TotalDuration.TotalMilliseconds
    };

    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponse
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
}).RequireAuthorization();

app.Run();
