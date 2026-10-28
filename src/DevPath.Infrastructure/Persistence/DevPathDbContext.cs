using DevPath.Domain.Achievements;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Progress;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.Persistence;

/// <summary>
/// Ponto de entrada do EF Core para o banco do DevPath (PostgreSQL).
/// </summary>
/// <remarks>
/// O mapeamento de cada entidade fica em <c>Persistence/Configurations</c>
/// (uma classe <see cref="IEntityTypeConfiguration{TEntity}"/> por entidade), assim
/// o Domain continua sem nenhuma dependência do EF Core.
/// O schema é criado só por migrations (<c>dotnet ef database update</c>), nunca por EnsureCreated.
/// </remarks>
public sealed class DevPathDbContext(DbContextOptions<DevPathDbContext> options) : DbContext(options)
{
    public DbSet<CareerPath> CareerPaths => Set<CareerPath>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<UserProgress> UserProgress => Set<UserProgress>();
    public DbSet<UserPathEnrollment> UserPathEnrollments => Set<UserPathEnrollment>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<UserAchievement> UserAchievements => Set<UserAchievement>();
    public DbSet<CodeSubmission> CodeSubmissions => Set<CodeSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Aplica todas as classes IEntityTypeConfiguration<T> deste assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DevPathDbContext).Assembly);
    }
}
