using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.Organization;

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

    public PersonnelPosition(Guid personnelId, Guid positionId, bool isPrimary, DateTime effectiveFrom, DateTime? effectiveTo = null)
    {
        PersonnelId = personnelId;
        PositionId = positionId;
        IsPrimary = isPrimary;
        Status = PersonnelPositionStatus.Active;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        CreatedAt = DateTime.UtcNow;
    }

    public void SetPrimary(bool isPrimary)
    {
        IsPrimary = isPrimary;
    }

    public void SetStatus(PersonnelPositionStatus status, DateTime? deactivatedAt = null)
    {
        Status = status;
        if (status == PersonnelPositionStatus.Inactive && deactivatedAt.HasValue)
        {
            DeactivatedAt = deactivatedAt;
        }
    }

    public void UpdateEffectiveWindow(DateTime effectiveFrom, DateTime? effectiveTo)
    {
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

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
}

public enum PersonnelPositionStatus { Active, Inactive }