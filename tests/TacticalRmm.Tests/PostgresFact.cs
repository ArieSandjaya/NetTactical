using Microsoft.EntityFrameworkCore;
using TacticalRmm.Infrastructure.Persistence;
using Xunit;

namespace TacticalRmm.Tests;

/// <summary>Runs only when NT_TEST_PG holds a connection string to a throwaway PostgreSQL (CI sets it); otherwise skipped.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NT_TEST_PG") is null) Skip = "NT_TEST_PG not set (needs a real PostgreSQL)";
    }
}

public static class PgDb
{
    /// <summary>A brand new database with its own name, built the same way as at startup.</summary>
    public static async Task<AppDbContext> CreateAsync()
    {
        var baseCs = Environment.GetEnvironmentVariable("NT_TEST_PG")!;
        var name = "nt_" + Guid.NewGuid().ToString("N")[..12];
        var b = new Npgsql.NpgsqlConnectionStringBuilder(baseCs) { Database = name };
        var o = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(b.ConnectionString).Options;
        var db = new AppDbContext(o);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
