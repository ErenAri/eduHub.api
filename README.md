## eduHub API

### Configuration
- Copy `eduHub.api/appsettings.Local.example.json` to `eduHub.api/appsettings.Development.json` (or user-secrets/environment variables) and set:
  - `ConnectionStrings:DefaultConnection` to your Postgres connection string.
  - `Jwt:Key` to a strong, private signing key.
  - `Seed:Enabled` to `true` only when you want automatic seeding.
  - `Seed:Admin:Enabled` plus `Seed:Admin:Password` when you explicitly want an admin user created.
  - `Seed:SampleData:Enabled` controls loading the sample buildings/rooms.

### Running
- Restore, migrate, and run:
  - `dotnet restore`
  - `dotnet ef database update` (from `eduHub.api` or via the `AppDbContext` project)
  - `dotnet run --project eduHub.api`

JWT authentication requires HTTPS in non-development environments. Configure a valid `Jwt:Key` before running.
