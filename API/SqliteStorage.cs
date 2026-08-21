using API.Schema.ActionsContext;
using API.Schema.LibraryContext;
using API.Schema.MangaContext;
using API.Schema.NotificationsContext;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace API;

internal static class SqliteStorage
{
    internal const string MangaHistoryTable = "__EFMigrationsHistory_Manga";
    internal const string LibraryHistoryTable = "__EFMigrationsHistory_Library";
    internal const string NotificationsHistoryTable = "__EFMigrationsHistory_Notifications";
    internal const string ActionsHistoryTable = "__EFMigrationsHistory_Actions";

    internal static string ConnectionString
    {
        get
        {
            Directory.CreateDirectory(MangetteSettings.DataDirectory);
            return new SqliteConnectionStringBuilder
            {
                DataSource = MangetteSettings.DatabasePath,
                Cache = SqliteCacheMode.Shared,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                DefaultTimeout = 30
            }.ToString();
        }
    }

    internal static void Configure(DbContextOptionsBuilder options, string historyTable)
    {
        options.UseSqlite(ConnectionString, sqlite =>
        {
            sqlite.MigrationsHistoryTable(historyTable);
            sqlite.CommandTimeout(60);
        });
    }

    internal static void ApplyPragmas(DbContext context)
    {
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        context.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
        context.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
    }
}

internal sealed class MangaContextFactory : IDesignTimeDbContextFactory<MangaContext>
{
    public MangaContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<MangaContext> options = new();
        SqliteStorage.Configure(options, SqliteStorage.MangaHistoryTable);
        return new MangaContext(options.Options);
    }
}

internal sealed class LibraryContextFactory : IDesignTimeDbContextFactory<LibraryContext>
{
    public LibraryContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<LibraryContext> options = new();
        SqliteStorage.Configure(options, SqliteStorage.LibraryHistoryTable);
        return new LibraryContext(options.Options);
    }
}

internal sealed class NotificationsContextFactory : IDesignTimeDbContextFactory<NotificationsContext>
{
    public NotificationsContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<NotificationsContext> options = new();
        SqliteStorage.Configure(options, SqliteStorage.NotificationsHistoryTable);
        return new NotificationsContext(options.Options);
    }
}

internal sealed class ActionsContextFactory : IDesignTimeDbContextFactory<ActionsContext>
{
    public ActionsContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<ActionsContext> options = new();
        SqliteStorage.Configure(options, SqliteStorage.ActionsHistoryTable);
        return new ActionsContext(options.Options);
    }
}
