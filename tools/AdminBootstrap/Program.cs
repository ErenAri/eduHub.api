using Npgsql;

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

int? existingId = null;
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
        existingId = reader.GetInt32(0);
        existingUserName = reader.GetString(1);
        existingEmail = reader.IsDBNull(2) ? null : reader.GetString(2);
    }
}

var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);
if (existingId.HasValue)
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
    update.Parameters.AddWithValue("id", existingId.Value);
    await update.ExecuteNonQueryAsync();
    Console.WriteLine($"Updated admin user '{existingUserName ?? adminUserName}' to Admin.");
    return 0;
}

await using (var insert = conn.CreateCommand())
{
    insert.CommandText = @"
        INSERT INTO users (""UserName"", ""Email"", ""PasswordHash"", ""Role"", ""CreatedAtUtc"")
        VALUES (@user, @email, @hash, 1, @created);";
    insert.Parameters.AddWithValue("user", adminUserName);
    insert.Parameters.AddWithValue("email", adminEmail);
    insert.Parameters.AddWithValue("hash", passwordHash);
    insert.Parameters.AddWithValue("created", DateTime.UtcNow);
    await insert.ExecuteNonQueryAsync();
}

Console.WriteLine($"Created admin user '{adminUserName}'.");
return 0;
