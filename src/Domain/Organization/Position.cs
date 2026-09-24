using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.Organization;

public sealed class Position : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public Guid CompanyId { get; private set; }
    public Guid? ParentPositionId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public PositionStatus Status { get; private set; }

    public Position? ParentPosition { get; private set; }
    private readonly List<Position> _children = [];
    public IReadOnlyCollection<Position> Children => _children.AsReadOnly();

    private readonly List<PersonnelPosition> _assignments = [];
    public IReadOnlyCollection<PersonnelPosition> Assignments => _assignments.AsReadOnly();

    private Position() { }

    public Position(Guid companyId, string code, string title, string? description = null, Guid? parentPositionId = null)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        Code = code;
        Title = title;
        Description = description;
        ParentPositionId = parentPositionId;
        Status = PositionStatus.Active;
    }

    public void UpdateDetails(string code, string title, string? description)
    {
        Code = code;
        Title = title;
        Description = description;
    }

    public void ChangeParent(Guid? newParentPositionId)
    {
        if (newParentPositionId == Id)
            throw new InvalidOperationException("Position cannot be its own parent.");

        ParentPositionId = newParentPositionId;
    }

    public void SetStatus(PositionStatus status)
    {
        if (status == PositionStatus.Inactive && _assignments.Any(a => a.Status == PersonnelPositionStatus.Active))
            throw new InvalidOperationException("Cannot deactivate position with active personnel assignments. Reassign or end assignments first.");

        Status = status;
    }
}

public enum PositionStatus { Active, Inactive }
