# eduHub API

eduHub API is an ASP.NET Core Web API for managing buildings, rooms, reservations, and users. The service uses PostgreSQL (EF Core) and JWT bearer authentication, and follows a layered architecture (Domain/Application/Infrastructure).

## Solution layout
- eduHub.api/ - HTTP host, controllers, middleware, configuration.
- src/eduHub.Domain/ - entities and enums.
- src/eduHub.Application/ - DTOs, validators, interfaces, security constants.
- src/eduHub.Infrastructure/ - persistence, EF Core, services, migrations.
- tests/ - integration tests.

## Prerequisites
- .NET SDK 10 (TargetFramework `net10.0`, preview).
- PostgreSQL instance reachable via `ConnectionStrings:DefaultConnection`.
- Optional: `dotnet-ef` tool for running migrations.

## Configuration
Local development:
- Copy `eduHub.api/appsettings.Development.example.json` to `eduHub.api/appsettings.Development.json` (gitignored).
- Or use user-secrets / environment variables (use `__` for nesting).

Required settings:
- `Jwt:Key` (>= 32 bytes).
- `ConnectionStrings:DefaultConnection`.

Common optional settings:
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes`, `Jwt:RefreshTokenDays`.
- `Cors:AllowedOrigins` (required in production; empty fails fast).
- `ForwardedHeaders:KnownProxies`, `ForwardedHeaders:KnownNetworks`, `ForwardedHeaders:ForwardLimit`.
- `Startup:AutoMigrate` and `Startup:AllowDangerousOperationsInProduction`.
- `Seed:Enabled`, `Seed:Admin:Enabled`, `Seed:Admin:UserName`, `Seed:Admin:Email`, `Seed:Admin:Password`, `Seed:SampleData:Enabled`.

Example (user-secrets):
- `dotnet user-secrets set "Jwt:Key" "<generate-a-strong-secret>" --project eduHub.api`
- `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Database=...;Username=...;Password=...;Ssl Mode=Require" --project eduHub.api`

## Database migrations
Migrations live under `src/eduHub.Infrastructure/Persistence/Migrations`.

Apply migrations:
- `dotnet ef database update --project src/eduHub.Infrastructure --startup-project eduHub.api`

Startup migrations and seeding are opt-in via `Startup:AutoMigrate` and `Seed:*`.

## Running
- `dotnet restore`
- `dotnet run --project eduHub.api`

Swagger UI is enabled in Development. JWT authentication requires HTTPS outside Development.

## API surface
- Controllers: `Auth`, `Buildings`, `Rooms`, `Reservations`, `WeatherForecast`.
- Health check: `GET /health` verifies database connectivity.

## Tests
- `dotnet test`