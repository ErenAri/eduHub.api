## eduHub API

### Configuration
- Local settings file: copy `eduHub.api/appsettings.Development.example.json` to `eduHub.api/appsettings.Development.json` (this file is gitignored).
- Or set secrets via user-secrets / environment variables:
  - `Jwt:Key` / `Jwt__Key`
  - `ConnectionStrings:DefaultConnection` / `ConnectionStrings__DefaultConnection`

Example (user-secrets):
- `dotnet user-secrets set "Jwt:Key" "<generate-a-strong-secret>" --project eduHub.api`
- `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Database=...;Username=...;Password=...;Ssl Mode=Require" --project eduHub.api`

Seeding (optional):
- `Seed:Enabled` to `true` only when you want automatic seeding.
- `Seed:Admin:Enabled` plus `Seed:Admin:Password` when you explicitly want an admin user created.
- `Seed:SampleData:Enabled` controls loading the sample buildings/rooms.

### Running
- Restore, migrate, and run:
  - `dotnet restore`
  - `dotnet ef database update` (from `eduHub.api` or via the `AppDbContext` project)
  - `dotnet run --project eduHub.api`

JWT authentication requires HTTPS in non-development environments. Configure a valid `Jwt:Key` before running.
