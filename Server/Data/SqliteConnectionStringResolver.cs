using Microsoft.Data.Sqlite;

namespace AuthWithAdmin.Server.Data;

/// <summary>
/// Turns the configured SQLite connection string into one whose <c>Data Source</c>
/// is an ABSOLUTE path anchored at the application's content root.
///
/// <para><b>The problem this removes.</b> The shipped connection string is
/// <c>Data Source=FinalProjectDB.db</c> — a RELATIVE path, which
/// Microsoft.Data.Sqlite resolves against <see cref="Environment.CurrentDirectory"/>,
/// i.e. the working directory of the process. Locally that happens to be the
/// Server project folder, so it finds the real database and everything looks
/// fine. In production the working directory is whatever the host chose: IIS
/// (in-process) uses the app directory, but a systemd unit, a Windows service,
/// a scheduled task or a plain <c>dotnet /path/App.dll</c> launched from
/// elsewhere does not. SQLite then does not fail — it CREATES a new, empty
/// database at the wrong location, the migrator builds the schema and seeds it,
/// and the application comes up working with none of the real data. That
/// failure is completely silent, which is what makes it dangerous.</para>
///
/// <para><b>Behaviour.</b>
/// <list type="bullet">
/// <item>An ABSOLUTE <c>Data Source</c> is honoured exactly as given — this is
/// how the faculty server can put the database outside the deployed
/// application directory (so a redeploy cannot overwrite it).</item>
/// <item>A RELATIVE <c>Data Source</c> is resolved against
/// <c>IHostEnvironment.ContentRootPath</c>, never the working directory.</item>
/// <item>A missing or blank connection string falls back to
/// <c>FinalProjectDB.db</c> under the content root, which is the file the
/// application has always used.</item>
/// <item>Non-file sources — <c>:memory:</c>, <c>Mode=Memory</c> and
/// <c>file:</c> URIs — are passed through untouched.</item>
/// <item>Every other connection-string keyword the operator supplied
/// (<c>Cache</c>, <c>Mode</c>, <c>Foreign Keys</c>, <c>Pooling</c>, …) is
/// preserved: the string is parsed and rebuilt, not string-replaced.</item>
/// </list></para>
///
/// <para>No path is hardcoded for any operating system, and nothing here reads
/// or writes the database — it only computes the address.</para>
/// </summary>
public static class SqliteConnectionStringResolver
{
    /// <summary>The one connection name the application uses. Named here so the
    /// resolver and its caller cannot disagree about the key.</summary>
    public const string ConnectionName = "DefaultConnection";

    /// <summary>Configuration path for <see cref="ConnectionName"/>, for callers
    /// that need to write the resolved value back.</summary>
    public const string ConfigurationKey = "ConnectionStrings:" + ConnectionName;

    /// <summary>The database the application has always shipped with. Used only
    /// when configuration supplies no data source at all.</summary>
    public const string DefaultDataSource = "FinalProjectDB.db";

    /// <param name="configured">The raw connection string from configuration.
    /// May be null or empty.</param>
    /// <param name="contentRootPath">
    /// <see cref="IHostEnvironment.ContentRootPath"/> — the deployed application
    /// directory. Deliberately a parameter rather than read from the environment,
    /// so this is a pure function and can be reasoned about (and tested) without
    /// a host.</param>
    /// <returns>A connection string whose Data Source is absolute, unless the
    /// source is not a file at all.</returns>
    public static string Resolve(string? configured, string contentRootPath)
    {
        var builder = string.IsNullOrWhiteSpace(configured)
            ? new SqliteConnectionStringBuilder { DataSource = DefaultDataSource }
            : new SqliteConnectionStringBuilder(configured);

        var dataSource = builder.DataSource;

        if (string.IsNullOrWhiteSpace(dataSource))
            dataSource = DefaultDataSource;

        // In-memory and URI sources are not filesystem paths; rooting them would
        // corrupt them.
        if (builder.Mode == SqliteOpenMode.Memory
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return builder.ConnectionString;
        }

        if (!Path.IsPathRooted(dataSource))
            dataSource = Path.Combine(contentRootPath, dataSource);

        // GetFullPath normalises "..", "." and separator style for the host OS.
        builder.DataSource = Path.GetFullPath(dataSource);

        return builder.ConnectionString;
    }

    /// <summary>
    /// In PRODUCTION ONLY, refuses to start when the configured database file
    /// does not exist.
    ///
    /// <para><b>Why this is a guard and not a convenience.</b> SQLite creates a
    /// missing database rather than failing, so a production deployment that
    /// forgot to copy the database does not stop — it comes up on a brand-new
    /// empty file. What the operator then sees is not "database missing" but
    /// whatever the migrator happens to hit while seeding a fresh schema
    /// (currently a foreign-key error deep inside the QA seed), which says
    /// nothing about the real cause. Worse is the case where seeding SUCCEEDS:
    /// the application is then serving demo data from an empty database that
    /// looks healthy, and the first person to notice is a user whose work is
    /// gone.</para>
    ///
    /// <para>The faculty deployment ships an EXISTING FinalProjectDB.db, so a
    /// missing file is always a deployment mistake and never a legitimate
    /// state. Failing immediately, with the resolved path in the message, turns
    /// a silent data-loss scenario into a thirty-second fix.</para>
    ///
    /// <para><b>Deliberately narrow.</b> It does nothing outside Production, so
    /// development and the first-run-creates-the-database behaviour every
    /// developer relies on are untouched. It skips non-file sources
    /// (<c>:memory:</c>, <c>Mode=Memory</c>, <c>file:</c> URIs), which have no
    /// path to test. It only ever READS directory metadata — it never creates,
    /// opens, or writes the database, and it does not change what the migrator
    /// does once a database is present.</para>
    /// </summary>
    /// <param name="connectionString">The RESOLVED connection string, i.e. the
    /// output of <see cref="Resolve"/>.</param>
    /// <param name="isProduction"><c>IHostEnvironment.IsProduction()</c>.
    /// Passed in rather than read here so this stays testable and has no
    /// hosting dependency.</param>
    /// <exception cref="InvalidOperationException">The database file is missing
    /// in Production.</exception>
    public static void EnsureDatabaseExistsInProduction(string connectionString, bool isProduction)
    {
        if (!isProduction) return;

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;

        if (string.IsNullOrWhiteSpace(dataSource)) return;

        // Same exclusions as Resolve: these are not filesystem paths.
        if (builder.Mode == SqliteOpenMode.Memory
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(dataSource)) return;

        throw new InvalidOperationException(
            $"""
             The production database was not found.

               Expected at: {dataSource}

             Motiva is deployed with an EXISTING database; it does not create one
             in Production. Starting on an empty file would either fail during
             seeding with an unrelated error, or silently serve demo data while
             the real records were never there.

             Fix this in one of two ways:

               1. Copy FinalProjectDB.db to the path above, and make sure the
                  account running the application can write to its DIRECTORY
                  (SQLite creates -wal / -shm siblings next to the file).

               2. Point ConnectionStrings:DefaultConnection at the existing
                  database instead — an absolute path is recommended, so that
                  redeploying the application cannot overwrite or orphan it:

                    ConnectionStrings__DefaultConnection="Data Source=/path/to/FinalProjectDB.db"

             A RELATIVE Data Source is resolved against the application's content
             root, which is the deployed application directory. See
             docs/DEPLOYMENT.md.
             """);
    }
}
