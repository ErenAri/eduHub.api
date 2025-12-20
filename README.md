# eduHub API

eduHub API is an ASP.NET Core Web API for managing buildings, rooms, reservations, and users. It implements a Clean Architecture layout with EF Core (PostgreSQL) and JWT bearer authentication.

## Architecture
- eduHub.api/ - HTTP host, controllers, middleware, configuration.
- src/eduHub.Domain/ - core entities, enums.
- src/eduHub.Application/ - DTOs, validators, interfaces, security constants.
- src/eduHub.Infrastructure/ - persistence, EF Core, services, migrations.
- tests/ - integration tests.

## Security and policy
- JWT bearer validation enforces issuer, audience, lifetime, and key length; tokens are checked against user existence and revoked token state.
- Authorization fallback requires authenticated users; admin endpoints require the AdminOnly policy.
- CORS must be explicitly configured in Production or startup fails.
- Rate limiting partitions by authenticated user id, otherwise by client IP. Authentication runs before rate limiting.

## Forwarded headers
The service processes X-Forwarded-For and X-Forwarded-Proto.

Production requirements:
- Prefer explicit `ForwardedHeaders:KnownProxies` or `ForwardedHeaders:KnownNetworks`.
- Alternatively, set all of the following:
  - `ForwardedHeaders:TrustAll=true`
  - `ForwardedHeaders:IngressLockedDown=true`
  - `ForwardedHeaders:RequireKnownProxies=false`

Ingress must be restricted to trusted proxies or Front Door when TrustAll is enabled.

## Health endpoints
- `GET /health/live` - anonymous, no database access.
- `GET /health/ready` - requires authentication, includes database readiness checks.
- No `/health` alias.

## Data access and migrations
Migrations live under `src/eduHub.Infrastructure/Persistence/Migrations`.

Apply migrations:
- `dotnet ef database update --project src/eduHub.Infrastructure --startup-project eduHub.api`

Startup migrations are controlled by `Startup:AutoMigrate` (recommended false in Production). Seeding is controlled by `Seed:*`, and admin seeding throws outside Development.

## Configuration
Local development:
- Copy `eduHub.api/appsettings.Development.example.json` to `eduHub.api/appsettings.Development.json` (gitignored).
- Or use user-secrets or environment variables (use `__` for nesting).

Required settings:
- `Jwt:Key` (at least 32 bytes).
- `ConnectionStrings:DefaultConnection`.
- `Cors:AllowedOrigins` (required in Production).

Forwarded headers (choose one approach):
- `ForwardedHeaders:KnownProxies` or `ForwardedHeaders:KnownNetworks`.
- or `ForwardedHeaders:TrustAll=true` with `ForwardedHeaders:IngressLockedDown=true`.
- `ForwardedHeaders:ForwardLimit` must match your proxy hop count.

Optional settings:
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes`, `Jwt:RefreshTokenDays`.
- `OpenTelemetry:Otlp:Endpoint`, `OpenTelemetry:SamplingRatio`.
- `TokenCleanup:*`, `RequestLogging:*`.
- `Startup:AutoMigrate`, `Seed:*`.

Example environment variables:
```
Jwt__Key=...32+bytes...
ConnectionStrings__DefaultConnection=Host=...;Database=...;Username=...;Password=...;Ssl Mode=Require
Cors__AllowedOrigins__0=https://app.example.com
ForwardedHeaders__KnownNetworks__0=10.0.0.0/8
ForwardedHeaders__ForwardLimit=2
```

## Build and repository hygiene
- Deterministic builds are enabled in `Directory.Build.props`.
- Build artifacts (bin/, obj/, .vs/, .tmp/) are excluded from compilation and git.
- `appsettings.Development.json` is gitignored and should never be committed.

## Running locally
- `dotnet restore`
- `dotnet run --project eduHub.api`

Swagger UI is enabled in Development only. HSTS is enabled outside Development.

## Tests
- `dotnet test`
- Integration tests use Testcontainers and require Docker.
