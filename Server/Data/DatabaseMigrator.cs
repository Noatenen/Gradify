using Microsoft.Data.Sqlite;

namespace AuthWithAdmin.Server.Data;

public static class DatabaseMigrator
{
    public static async Task MigrateAsync(IConfiguration config)
    {
        string connectionString = config.GetConnectionString("DefaultConnection")!;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var existingColumns = await GetColumnsAsync(connection, "users");

        if (!existingColumns.Contains("Phone"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE users ADD COLUMN Phone TEXT NOT NULL DEFAULT ''");

        if (!existingColumns.Contains("AcademicYear"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE users ADD COLUMN AcademicYear TEXT NOT NULL DEFAULT '2025-2026'");

        if (!existingColumns.Contains("IsActive"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE users ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1");
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1)); // column index 1 = name
        }

        return columns;
    }

    private static async Task ExecuteNonQueryAsync(this SqliteConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
