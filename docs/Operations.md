# Operations

## Configuration
- `Jwt:Key` is required and must be at least 32 bytes.
- `Cors:AllowedOrigins` must be set for production.
- `ForwardedHeaders:RequireKnownProxies=true` expects `ForwardedHeaders:KnownProxies` or `ForwardedHeaders:KnownNetworks` in production.

## Migrations
- Production does not auto-migrate; run manually during deploy.
- Example:
```
dotnet ef database update --project src/eduHub.Infrastructure --startup-project eduHub.api
```

## Seeding
- Admin and sample data seeding are development-only and blocked in production.

## Token cleanup
- A background service deletes expired refresh tokens and expired revoked tokens.
- Configure with `TokenCleanup:Enabled` and `TokenCleanup:IntervalMinutes`.

## Request logging
- Logs slow requests when they exceed `RequestLogging:SlowRequestThresholdMs`.
- Enable/disable via `RequestLogging:Enabled`.

## Health endpoints
- `/health/live` is liveness only (no DB).
- `/health/ready` includes DB checks.
- `/health` is an alias for ready.

## Rate limiting
- Global limit: 60 requests/minute.
- Auth limit (`/api/auth/*`): 5 requests/minute.
