using DevPath.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Persistence;

/// <summary>
/// Sobe um PostgreSQL 16 descartável (Testcontainers) e aplica as migrations do zero.
/// Precisa do Docker rodando na máquina.
/// </summary>
public sealed class MigrationsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build(); // mesma imagem do docker-compose

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MigrateAsync_EmBancoLimpo_CriaTodasAsTabelasSemPendencias()
    {
        await using var db = CreateDbContext();

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_InitialCreate"))
            .And.Contain(m => m.EndsWith("_AddIdentity"));

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        tables.Should().Contain(
        [
            "CareerPaths", "Modules", "Lessons", "Exercises",
            "UserProgress", "UserPathEnrollments",
            "Achievements", "UserAchievements", "CodeSubmissions",
            "AspNetUsers", "AspNetRoles", "AspNetUserRoles"
        ]);
    }

    [Fact]
    public async Task Slug_Duplicado_ViolaIndiceUnico()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        db.CareerPaths.Add(Domain.CareerPaths.CareerPath.Create("Backend .NET"));
        db.CareerPaths.Add(Domain.CareerPaths.CareerPath.Create("Backend .NET"));
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}
