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

    /// <summary>Parent existence, company and deeper cycles are checked by the application layer (<c>HierarchyCycle</c>).</summary>
    public void ChangeParent(Guid? newParentPositionId)
    {
        if (newParentPositionId == Id)
            throw new DomainRuleViolationException("HIERARCHY_CYCLE", "Position cannot be its own parent.");

        ParentPositionId = newParentPositionId;
    }

    /// <summary>Deactivation requires the assignments to be loaded; ended assignments are history and do not block it.</summary>
    public void SetStatus(PositionStatus status, DateTime now)
    {
        if (status == PositionStatus.Inactive && _assignments.Any(a => a.IsCurrentOrUpcoming(now)))
            throw new DomainRuleViolationException("POSITION_HAS_ACTIVE_ASSIGNMENTS", "Cannot deactivate a position with current or upcoming assignments. End or remove them first.");

        Status = status;
    }
}

public enum PositionStatus { Active, Inactive }