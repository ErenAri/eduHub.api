using eduHub.api.Middleware;
using eduHub.Application.Validators.Users;
using eduHub.Infrastructure;
using eduHub.Infrastructure.Persistence;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// =======================================
// Controllers + FluentValidation
// =======================================

builder.Services
    .AddControllers();

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
string key = jwtSection["Key"] ?? throw new Exception("Jwt:Key is missing");

string issuer = jwtSection["Issuer"] ?? "eduHub";
string audience = jwtSection["Audience"] ?? "eduHub";

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
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
});

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
        // NO migrate, NO seed in production by default
    }
    else
    {
        var autoMigrate =
            configuration.GetValue("Startup:AutoMigrate", env.IsDevelopment());

        if (autoMigrate)
        {
            await db.Database.MigrateAsync();
        }

        var seedEnabled =
            configuration.GetValue("Seed:Enabled", env.IsDevelopment());

        if (seedEnabled)
        {
            await DbInitializer.SeedAsync(db, configuration, env);
        }
    }
}

// =======================================
// Middleware Pipeline
// =======================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
