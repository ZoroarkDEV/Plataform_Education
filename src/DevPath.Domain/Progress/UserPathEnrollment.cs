namespace DevPath.Domain.Progress;

/// <summary>Matrícula de um aluno em uma trilha. Chave composta: (UserId, CareerPathId).</summary>
/// <remarks>UserId sem navegação até a issue #9 (ApplicationUser / Identity).</remarks>
public sealed class UserPathEnrollment
{
    public Guid UserId { get; private set; }
    public Guid CareerPathId { get; private set; }
    public DateTime EnrolledAt { get; private set; }

    /// <summary>De 0 a 100.</summary>
    public decimal ProgressPercentage { get; private set; }

    public Guid? CurrentModuleId { get; private set; }
    public bool IsActive { get; private set; }

    private UserPathEnrollment() { }

    public static UserPathEnrollment Enroll(Guid userId, Guid careerPathId) =>
        new()
        {
            UserId = userId,
            CareerPathId = careerPathId,
            EnrolledAt = DateTime.UtcNow,
            ProgressPercentage = 0,
            IsActive = true
        };

    public void UpdateProgress(decimal percentage, Guid? currentModuleId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percentage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentage, 100m);
        ProgressPercentage = percentage;
        CurrentModuleId = currentModuleId;
    }
}
