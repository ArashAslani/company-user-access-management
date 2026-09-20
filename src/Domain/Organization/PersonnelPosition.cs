using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.Organization;

public sealed class PersonnelPosition : BaseEntity
{
    public Guid PersonnelId { get; private set; }
    public Guid PositionId { get; private set; }
    public bool IsPrimary { get; private set; }
    public PersonnelPositionStatus Status { get; private set; }
    public DateTime AssignedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }

    public Personnel? Personnel { get; private set; }
    public Position? Position { get; private set; }

    private PersonnelPosition() { }

    public PersonnelPosition(Guid personnelId, Guid positionId, bool isPrimary, DateTime assignedAt)
    {
        PersonnelId = personnelId;
        PositionId = positionId;
        IsPrimary = isPrimary;
        Status = PersonnelPositionStatus.Active;
        AssignedAt = assignedAt;
    }

    public void SetPrimary(bool isPrimary)
    {
        IsPrimary = isPrimary;
    }

    public void End(DateTime endedAt)
    {
        Status = PersonnelPositionStatus.Inactive;
        EndedAt = endedAt;
        IsPrimary = false;
    }

    public void Reactivate(DateTime assignedAt)
    {
        Status = PersonnelPositionStatus.Active;
        EndedAt = null;
        AssignedAt = assignedAt;
    }
}

public enum PersonnelPositionStatus { Active, Inactive }