using Npgsql;

const string DefaultOrgName = "Default Organization";
const string DefaultOrgSlug = "default";

var connectionString = Environment.GetEnvironmentVariable("EDUHUB_CONN");
var adminUserName = Environment.GetEnvironmentVariable("EDUHUB_ADMIN_USERNAME") ?? "admin1";
var adminPassword = Environment.GetEnvironmentVariable("EDUHUB_ADMIN_PASSWORD");
var adminEmail = Environment.GetEnvironmentVariable("EDUHUB_ADMIN_EMAIL") ?? $"{adminUserName}@eduhub.local";

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing EDUHUB_CONN.");
    return 1;
}

if (string.IsNullOrWhiteSpace(adminPassword))
{
    Console.Error.WriteLine("Missing EDUHUB_ADMIN_PASSWORD.");
    return 1;
}

await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync();

await using (var avatarCheck = conn.CreateCommand())
{
    avatarCheck.CommandText = @"
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'users'
          AND lower(column_name) = 'avatarurl'
        LIMIT 1;";
    var hasAvatar = await avatarCheck.ExecuteScalarAsync();
    if (hasAvatar == null)
    {
        Console.WriteLine("Warning: users.AvatarUrl column is missing. Apply migrations to align schema.");
    }
}

int? adminId = null;
string? existingUserName = null;
string? existingEmail = null;

await using (var select = conn.CreateCommand())
{
    select.CommandText = @"
        SELECT ""Id"", ""UserName"", ""Email""
        FROM users
        WHERE ""UserName"" = @user
           OR ""Email"" = @email
        LIMIT 1;";
    select.Parameters.AddWithValue("user", adminUserName);
    select.Parameters.AddWithValue("email", adminEmail);
    await using var reader = await select.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        adminId = reader.GetInt32(0);
        existingUserName = reader.GetString(1);
        existingEmail = reader.IsDBNull(2) ? null : reader.GetString(2);
    }
}

var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);
if (adminId.HasValue)
{
    await using var update = conn.CreateCommand();
    update.CommandText = @"
        UPDATE users
        SET ""Role"" = 1,
            ""PasswordHash"" = @hash,
            ""Email"" = COALESCE(""Email"", @email)
        WHERE ""Id"" = @id;";
    update.Parameters.AddWithValue("hash", passwordHash);
    update.Parameters.AddWithValue("email", adminEmail);
    update.Parameters.AddWithValue("id", adminId.Value);
    await update.ExecuteNonQueryAsync();
    Console.WriteLine($"Updated admin user '{existingUserName ?? adminUserName}' to Admin.");
}
else
{
    await using var insert = conn.CreateCommand();
    insert.CommandText = @"
        INSERT INTO users (""UserName"", ""Email"", ""PasswordHash"", ""Role"", ""CreatedAtUtc"")
        VALUES (@user, @email, @hash, 1, @created)
        RETURNING ""Id"";";
    insert.Parameters.AddWithValue("user", adminUserName);
    insert.Parameters.AddWithValue("email", adminEmail);
    insert.Parameters.AddWithValue("hash", passwordHash);
    insert.Parameters.AddWithValue("created", DateTime.UtcNow);
    adminId = (int) (await insert.ExecuteScalarAsync() ?? 0);
    Console.WriteLine($"Created admin user '{adminUserName}'.");
}

var orgsTableExists = await TableExistsAsync(conn, "organizations");
var membersTableExists = await TableExistsAsync(conn, "organization_members");

if (adminId.HasValue && orgsTableExists && membersTableExists)
{
    var orgId = await EnsureDefaultOrganizationAsync(conn);
    await EnsureOrgAdminMembershipAsync(conn, orgId, adminId.Value);
}
else if (!orgsTableExists || !membersTableExists)
{
    Console.WriteLine("Warning: organization tables are missing. Apply migrations before assigning memberships.");
}

return 0;

static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string tableName)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT 1
        FROM information_schema.tables
        WHERE table_name = @table
        LIMIT 1;";
    cmd.Parameters.AddWithValue("table", tableName);
    return await cmd.ExecuteScalarAsync() != null;
}

static async Task<Guid> EnsureDefaultOrganizationAsync(NpgsqlConnection conn)
{
    await using (var select = conn.CreateCommand())
    {
        select.CommandText = @"
            SELECT ""Id""
            FROM organizations
            WHERE ""Slug"" = @slug
            LIMIT 1;";
        select.Parameters.AddWithValue("slug", DefaultOrgSlug);
        var existing = await select.ExecuteScalarAsync();
        if (existing is Guid id)
            return id;
    }

    var newId = Guid.NewGuid();
    await using (var insert = conn.CreateCommand())
    {
        insert.CommandText = @"
            INSERT INTO organizations (""Id"", ""Name"", ""Slug"", ""IsActive"", ""CreatedAtUtc"")
            VALUES (@id, @name, @slug, TRUE, @created);";
        insert.Parameters.AddWithValue("id", newId);
        insert.Parameters.AddWithValue("name", DefaultOrgName);
        insert.Parameters.AddWithValue("slug", DefaultOrgSlug);
        insert.Parameters.AddWithValue("created", DateTimeOffset.UtcNow);
        await insert.ExecuteNonQueryAsync();
    }

    return newId;
}

static async Task EnsureOrgAdminMembershipAsync(NpgsqlConnection conn, Guid orgId, int userId)
{
    await using (var select = conn.CreateCommand())
    {
        select.CommandText = @"
            SELECT 1
            FROM organization_members
            WHERE ""OrganizationId"" = @orgId
              AND ""UserId"" = @userId
            LIMIT 1;";
        select.Parameters.AddWithValue("orgId", orgId);
        select.Parameters.AddWithValue("userId", userId);
        if (await select.ExecuteScalarAsync() != null)
            return;
    }

    await using (var insert = conn.CreateCommand())
    {
        insert.CommandText = @"
            INSERT INTO organization_members (""OrganizationId"", ""UserId"", ""Role"", ""Status"", ""JoinedAtUtc"")
            VALUES (@orgId, @userId, 2, 0, @joined);";
        insert.Parameters.AddWithValue("orgId", orgId);
        insert.Parameters.AddWithValue("userId", userId);
        insert.Parameters.AddWithValue("joined", DateTimeOffset.UtcNow);
        await insert.ExecuteNonQueryAsync();
    }
}
