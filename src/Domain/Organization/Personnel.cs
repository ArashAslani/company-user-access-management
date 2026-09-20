using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Organization.Events;

namespace CleanArchitecture.Domain.Organization;

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
        Status = PersonnelStatus.Employed;
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

    public void AssignPosition(Guid positionId, bool isPrimary, DateTime assignedAt)
    {
        if (_positions.Any(p => p.PositionId == positionId && p.Status == PersonnelPositionStatus.Active))
            throw new InvalidOperationException("Personnel already assigned to this position.");

        var assignment = new PersonnelPosition(Id, positionId, isPrimary, assignedAt);
        _positions.Add(assignment);

        if (isPrimary)
        {
            foreach (var other in _positions.Where(p => p.PositionId != positionId && p.IsPrimary))
                other.SetPrimary(false);
        }

        AddDomainEvent(new PersonnelPositionAssignedEvent(Id, positionId, isPrimary));
    }

    public void RemovePosition(Guid positionId, DateTime endedAt)
    {
        var assignment = _positions.FirstOrDefault(p => p.PositionId == positionId && p.Status == PersonnelPositionStatus.Active);
        if (assignment is null)
            throw new InvalidOperationException("Active position assignment not found.");

        assignment.End(endedAt);
        AddDomainEvent(new PersonnelPositionRemovedEvent(Id, positionId));
    }

    public void SetPrimaryPosition(Guid positionId)
    {
        var target = _positions.FirstOrDefault(p => p.PositionId == positionId && p.Status == PersonnelPositionStatus.Active);
        if (target is null)
            throw new InvalidOperationException("Position assignment not found or inactive.");

        foreach (var p in _positions.Where(p => p.IsPrimary && p.PositionId != positionId))
            p.SetPrimary(false);

        target.SetPrimary(true);
        AddDomainEvent(new PrimaryPositionChangedEvent(Id, positionId));
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
}

public enum Gender { Male, Female }

public enum PersonnelStatus { Employed, Inactive }