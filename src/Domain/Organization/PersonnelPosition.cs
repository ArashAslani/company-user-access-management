using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.Organization;

public sealed class PersonnelPosition : BaseEntity
{
    public Guid PersonnelId { get; private set; }
    public Guid PositionId { get; private set; }
    public bool IsPrimary { get; private set; }
    public PersonnelPositionStatus Status { get; private set; }
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? DeactivatedAt { get; private set; }

    public Personnel? Personnel { get; private set; }
    public Position? Position { get; private set; }

    private PersonnelPosition() { }

    internal PersonnelPosition(Guid personnelId, Guid positionId, bool isPrimary, DateTime effectiveFrom, DateTime? effectiveTo, DateTime createdAt)
    {
        EnsureValidWindow(effectiveFrom, effectiveTo);

        Id = Guid.NewGuid();
        PersonnelId = personnelId;
        PositionId = positionId;
        IsPrimary = isPrimary;
        Status = PersonnelPositionStatus.Active;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        CreatedAt = createdAt;
    }

    public bool IsActive => Status == PersonnelPositionStatus.Active;

    /// <summary>An assignment whose window has already ended is historical and can no longer be changed.</summary>
    public bool IsSealed(DateTime now) => EffectiveTo.HasValue && now > EffectiveTo.Value;

    public bool HasStarted(DateTime now) => EffectiveFrom <= now;

    public bool IsCurrentlyEffective(DateTime? at = null)
    {
        var now = at ?? DateTime.UtcNow;
        return Status == PersonnelPositionStatus.Active
            && EffectiveFrom <= now
            && (EffectiveTo == null || EffectiveTo > now);
    }

    public bool HasOverlap(DateTime otherEffectiveFrom, DateTime? otherEffectiveTo)
    {
        var otherEnd = otherEffectiveTo ?? DateTime.MaxValue;
        var thisEnd = EffectiveTo ?? DateTime.MaxValue;

        return EffectiveFrom < otherEnd && otherEffectiveFrom < thisEnd;
    }

    internal void SetPrimary(bool isPrimary)
    {
        IsPrimary = isPrimary;
    }

    internal void Activate()
    {
        Status = PersonnelPositionStatus.Active;
        DeactivatedAt = null;
    }

    internal void Deactivate(DateTime deactivatedAt)
    {
        Status = PersonnelPositionStatus.Inactive;
        DeactivatedAt = deactivatedAt;
    }

    internal void UpdateEffectiveWindow(DateTime effectiveFrom, DateTime? effectiveTo)
    {
        EnsureValidWindow(effectiveFrom, effectiveTo);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    private static void EnsureValidWindow(DateTime effectiveFrom, DateTime? effectiveTo)
    {
        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom)
            throw new DomainRuleViolationException("INVALID_EFFECTIVE_WINDOW", "EffectiveTo must be later than EffectiveFrom.");
    }
}

public enum PersonnelPositionStatus { Active, Inactive }
