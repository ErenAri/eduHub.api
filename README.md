## eduHub API

### Configuration
- Set configuration via user-secrets or environment variables:
  - `ConnectionStrings:DefaultConnection` (Postgres connection string)
  - `Jwt:Key` (strong signing key)
  - `Seed:Enabled` to `true` only when you want automatic seeding.
  - `Seed:Admin:Enabled` plus `Seed:Admin:Password` when you explicitly want an admin user created.
  - `Seed:SampleData:Enabled` controls loading the sample buildings/rooms.

### Running
- Restore, migrate, and run:
  - `dotnet restore`
  - `dotnet ef database update` (from `eduHub.api` or via the `AppDbContext` project)
  - `dotnet run --project eduHub.api`

JWT authentication requires HTTPS in non-development environments. Configure a valid `Jwt:Key` before running.
