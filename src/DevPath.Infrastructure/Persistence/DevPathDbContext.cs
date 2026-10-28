using DevPath.Domain.Achievements;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Datasets;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.Persistence;

/// <summary>
/// Ponto de entrada do EF Core para o banco do DevPath (PostgreSQL).
/// </summary>
/// <remarks>
/// O mapeamento de cada entidade fica em <c>Persistence/Configurations</c>
/// (uma classe <see cref="IEntityTypeConfiguration{TEntity}"/> por entidade), assim
/// o Domain continua sem nenhuma dependência do EF Core.
/// Herda de <see cref="IdentityDbContext{TUser, TRole, TKey}"/> para ganhar as tabelas AspNetUsers,
/// AspNetRoles etc. com chave <see cref="Guid"/>, a mesma usada nos <c>UserId</c> do Domain.
/// O schema é criado só por migrations (<c>dotnet ef database update</c>), nunca por EnsureCreated.
/// </remarks>
public sealed class DevPathDbContext(DbContextOptions<DevPathDbContext> options) :
    IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
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
    public DbSet<Dataset> Datasets => Set<Dataset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Primeiro o Identity mapeia as tabelas dele; depois as nossas configurações.
        base.OnModelCreating(modelBuilder);

        // Aplica todas as classes IEntityTypeConfiguration<T> deste assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DevPathDbContext).Assembly);
    }
}
