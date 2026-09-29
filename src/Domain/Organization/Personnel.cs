using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization.Events;

namespace CompanyAccessManagement.Domain.Organization;

public sealed class Personnel : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    public string NationalCode { get; private set; } = null!;
    public string? PersonnelCode { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string? PhoneNumber { get; private set; }
    public Gender Gender { get; private set; }
    public PersonnelStatus Status { get; private set; }

    private readonly List<PersonnelPosition> _positions = [];
    public IReadOnlyCollection<PersonnelPosition> Positions => _positions.AsReadOnly();

    private readonly List<PersonnelSignature> _signatures = [];
    public IReadOnlyCollection<PersonnelSignature> Signatures => _signatures.AsReadOnly();

    private Personnel() { }

    public Personnel(string nationalCode, string firstName, string lastName, Gender gender, string? personnelCode = null, string? phoneNumber = null)
    {
        Id = Guid.NewGuid();
        NationalCode = nationalCode;
        FirstName = firstName;
        LastName = lastName;
        Gender = gender;
        PersonnelCode = personnelCode;
        PhoneNumber = phoneNumber;
        Status = PersonnelStatus.Draft;
    }

    public void UpdateDetails(string firstName, string lastName, string? phoneNumber, Gender gender)
    {
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
        Gender = gender;
    }

    public void SetStatus(PersonnelStatus status)
    {
        Status = status;
    }

    public PersonnelPosition AssignPosition(Guid positionId, bool isPrimary, DateTime effectiveFrom, DateTime? effectiveTo, DateTime now)
    {
        EnsureNoOverlapOnSamePosition(positionId, effectiveFrom, effectiveTo, excludeAssignmentId: null);

        var assignment = new PersonnelPosition(Id, positionId, isPrimary, effectiveFrom, effectiveTo, now);
        _positions.Add(assignment);

        if (Status == PersonnelStatus.Draft && _positions.Any(p => p.IsCurrentlyEffective(now)))
            Status = PersonnelStatus.Employed;

        AddDomainEvent(new PersonnelPositionAssignedEvent(Id, positionId, isPrimary));
        return assignment;
    }

    public void UpdatePositionAssignment(Guid assignmentId, bool isPrimary, DateTime effectiveFrom, DateTime? effectiveTo, PersonnelPositionStatus status, DateTime now)
    {
        var assignment = GetAssignment(assignmentId);
        EnsureNotSealed(assignment, now);

        if (assignment.HasStarted(now) && effectiveFrom != assignment.EffectiveFrom)
            throw new DomainRuleViolationException("EFFECTIVE_FROM_LOCKED", "EffectiveFrom cannot be changed after the assignment has started.");

        if (status == PersonnelPositionStatus.Active)
            EnsureNoOverlapOnSamePosition(assignment.PositionId, effectiveFrom, effectiveTo, excludeAssignmentId: assignment.Id);

        var primaryChanged = assignment.IsPrimary != isPrimary;

        assignment.UpdateEffectiveWindow(effectiveFrom, effectiveTo);
        assignment.SetPrimary(isPrimary);

        if (status == PersonnelPositionStatus.Active)
            assignment.Activate();
        else if (assignment.IsActive)
            assignment.Deactivate(now);

        if (primaryChanged)
            AddDomainEvent(new PrimaryPositionChangedEvent(Id, assignment.PositionId));
    }

    public void RemovePositionAssignment(Guid assignmentId, DateTime now)
    {
        var assignment = GetAssignment(assignmentId);
        EnsureNotSealed(assignment, now);

        if (!assignment.IsActive)
            throw new DomainRuleViolationException("ASSIGNMENT_INACTIVE", "The position assignment is already inactive.");

        assignment.Deactivate(now);

        if (Status == PersonnelStatus.Employed && !_positions.Any(p => p.IsCurrentlyEffective(now)))
            Status = PersonnelStatus.Draft;

        AddDomainEvent(new PersonnelPositionRemovedEvent(Id, assignment.PositionId));
    }

    public PersonnelPosition? FindAssignment(Guid assignmentId) => _positions.FirstOrDefault(p => p.Id == assignmentId);

    private PersonnelPosition GetAssignment(Guid assignmentId)
    {
        return FindAssignment(assignmentId)
            ?? throw new DomainRuleViolationException("ASSIGNMENT_NOT_FOUND", "Position assignment not found for this personnel.");
    }

    private static void EnsureNotSealed(PersonnelPosition assignment, DateTime now)
    {
        if (assignment.IsSealed(now))
            throw new DomainRuleViolationException("SEALED_RECORD", "Ended position assignments are historical records and cannot be changed.");
    }

    private void EnsureNoOverlapOnSamePosition(Guid positionId, DateTime effectiveFrom, DateTime? effectiveTo, Guid? excludeAssignmentId)
    {
        if (_positions.Any(p => p.PositionId == positionId
                && p.IsActive
                && p.Id != excludeAssignmentId
                && p.HasOverlap(effectiveFrom, effectiveTo)))
        {
            throw new DomainRuleViolationException("ASSIGNMENT_OVERLAP", "The effective window overlaps an active assignment to the same position.");
        }
    }

    public PersonnelSignature UploadSignature(byte[] content, string mimeType, string contentHash, Guid uploadedByUserId)
    {
        if (content.Length > 8 * 1024 * 1024)
            throw new InvalidOperationException("Signature file exceeds 8MB limit.");

        var allowedMimeTypes = new[] { "image/png", "image/jpeg" };
        if (!allowedMimeTypes.Contains(mimeType.ToLowerInvariant()))
            throw new InvalidOperationException("Only PNG/JPEG signatures are allowed.");

        var nextVersion = _signatures.Any() ? _signatures.Max(s => s.Version) + 1 : 1;

        foreach (var s in _signatures.Where(s => s.IsCurrent))
            s.SetNotCurrent();

        var signature = new PersonnelSignature(Id, nextVersion, mimeType, content.Length, contentHash, content, uploadedByUserId);
        _signatures.Add(signature);

        AddDomainEvent(new PersonnelSignatureReplacedEvent(Id, signature.Id, nextVersion));
        return signature;
    }

    public PersonnelSignature? GetCurrentSignature() => _signatures.FirstOrDefault(s => s.IsCurrent);

    public bool CanDelete()
    {
        return !_positions.Any(p => p.IsCurrentlyEffective());
    }

    public void ConfirmEmployment()
    {
        if (Status != PersonnelStatus.Draft)
            throw new InvalidOperationException("Only Draft personnel can confirm employment.");

        if (!_positions.Any(p => p.IsCurrentlyEffective()))
            throw new InvalidOperationException("Cannot confirm employment: no effective position assigned.");

        Status = PersonnelStatus.Employed;
    }

    public void RevertToDraft()
    {
        if (Status != PersonnelStatus.Employed)
            throw new InvalidOperationException("Only Employed personnel can revert to draft.");

        if (_positions.Any(p => p.IsCurrentlyEffective()))
            throw new InvalidOperationException("Cannot revert to draft: has effective position assignments.");

        Status = PersonnelStatus.Draft;
    }
}

public enum Gender { Male, Female }

public enum PersonnelStatus { Draft, Employed, Inactive }
