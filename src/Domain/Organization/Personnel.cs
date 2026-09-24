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

    public void AssignPosition(Guid positionId, bool isPrimary, DateTime effectiveFrom, DateTime? effectiveTo = null)
    {
        var now = DateTime.UtcNow;

        if (_positions.Any(p => p.PositionId == positionId && p.IsCurrentlyEffective()))
            throw new InvalidOperationException("Personnel already has an effective assignment to this position.");

        if (_positions.Any(p => p.PositionId == positionId && p.HasOverlap(effectiveFrom, effectiveTo)))
            throw new InvalidOperationException("Effective window overlaps with existing assignment for this position.");

        if (isPrimary)
        {
            if (_positions.Any(p => p.IsPrimary && p.Position?.CompanyId != null && p.IsCurrentlyEffective() && p.HasOverlap(effectiveFrom, effectiveTo)))
                throw new InvalidOperationException("Primary overlap conflict: another primary assignment is effective in the same window.");
        }

        var assignment = new PersonnelPosition(Id, positionId, isPrimary, effectiveFrom, effectiveTo);
        _positions.Add(assignment);

        if (isPrimary)
        {
            foreach (var other in _positions.Where(p => p.PositionId != positionId && p.IsPrimary && p.HasOverlap(effectiveFrom, effectiveTo)))
                other.SetPrimary(false);
        }

        if (Status == PersonnelStatus.Draft && _positions.Any(p => p.IsCurrentlyEffective()))
            Status = PersonnelStatus.Employed;

        AddDomainEvent(new PersonnelPositionAssignedEvent(Id, positionId, isPrimary));
    }

    public void RemovePosition(Guid positionId, DateTime endedAt)
    {
        var assignment = _positions.FirstOrDefault(p => p.PositionId == positionId && p.IsCurrentlyEffective());
        if (assignment is null)
            throw new InvalidOperationException("Active position assignment not found.");

        assignment.SetStatus(PersonnelPositionStatus.Inactive, endedAt);
        AddDomainEvent(new PersonnelPositionRemovedEvent(Id, positionId));
    }

    public void SetPrimaryPosition(Guid positionId, DateTime? effectiveFrom = null, DateTime? effectiveTo = null)
    {
        var target = _positions.FirstOrDefault(p => p.PositionId == positionId);
        if (target is null)
            throw new InvalidOperationException("Position assignment not found.");

        var now = DateTime.UtcNow;
        var from = effectiveFrom ?? target.EffectiveFrom;
        var to = effectiveTo ?? target.EffectiveTo;

        if (_positions.Any(p => p.PositionId != positionId && p.IsPrimary && p.Position?.CompanyId != null && p.IsCurrentlyEffective() && p.HasOverlap(from, to)))
            throw new InvalidOperationException("Primary overlap conflict: another primary assignment is effective in the same window.");

        foreach (var p in _positions.Where(p => p.PositionId != positionId && p.IsPrimary && p.Position?.CompanyId != null && p.HasOverlap(from, to)))
            p.SetPrimary(false);

        target.SetPrimary(true);
        AddDomainEvent(new PrimaryPositionChangedEvent(Id, positionId));
    }

    public void UpdatePositionEffectiveWindow(Guid positionId, DateTime effectiveFrom, DateTime? effectiveTo)
    {
        var target = _positions.FirstOrDefault(p => p.PositionId == positionId);
        if (target is null)
            throw new InvalidOperationException("Position assignment not found.");

        if (_positions.Any(p => p.PositionId != positionId && p.HasOverlap(effectiveFrom, effectiveTo)))
            throw new InvalidOperationException("Effective window overlaps with another assignment for this position.");

        if (target.IsPrimary)
        {
            if (_positions.Any(p => p.PositionId != positionId && p.IsPrimary && p.Position?.CompanyId != null && p.HasOverlap(effectiveFrom, effectiveTo)))
                throw new InvalidOperationException("Primary overlap conflict: another primary assignment is effective in the same window.");
        }

        target.UpdateEffectiveWindow(effectiveFrom, effectiveTo);
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
