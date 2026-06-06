using Microsoft.Data.Sqlite;

namespace Gpt2Image.Core.Storage;

public sealed class SqliteDatabase
{
    private static int _sqliteInitialized;
    private readonly AppPaths _paths;

    public SqliteDatabase(AppPaths paths)
    {
        _paths = paths;
    }

    public string DatabasePath => _paths.DatabasePath;

    public SqliteConnection OpenConnection()
    {
        EnsureSqliteInitialized();
        _paths.EnsureDirectories();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 30,
            ForeignKeys = true
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void EnsureSqliteInitialized()
    {
        if (Interlocked.Exchange(ref _sqliteInitialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }
}
