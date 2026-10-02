using CompanyAccessManagement.Domain.Common;
using CompanyAccessManagement.Domain.Organization.Events;

namespace CompanyAccessManagement.Domain.Organization;

public sealed class Personnel : BaseAuditableEntity<Guid>
{
    public override Guid Id { get; protected set; }
    /// <summary>The owning tenant. Immutable: a person never moves between companies.</summary>
    public Guid CompanyId { get; private set; }
    public string NationalCode { get; private set; } = null!;
    public string? PersonnelCode { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string? PhoneNumber { get; private set; }
    public Gender Gender { get; private set; }
    public PersonnelStatus Status { get; private set; }
    public string? ExternalSource { get; private set; }
    public string? ExternalId { get; private set; }
    /// <summary>Rotated by every mutation of the aggregate, so two writers holding the same stale state cannot both commit.</summary>
    public Guid ConcurrencyToken { get; private set; }

    private readonly List<PersonnelPosition> _positions = [];
    public IReadOnlyCollection<PersonnelPosition> Positions => _positions.AsReadOnly();

    private readonly List<PersonnelSignature> _signatures = [];
    public IReadOnlyCollection<PersonnelSignature> Signatures => _signatures.AsReadOnly();

    private Personnel() { }

    public Personnel(Guid companyId, string nationalCode, string firstName, string lastName, Gender gender, string? personnelCode = null, string? phoneNumber = null)
    {
        if (companyId == Guid.Empty)
            throw new DomainRuleViolationException("PERSONNEL_COMPANY_REQUIRED", "Personnel must belong to a company.");

        Id = Guid.NewGuid();
        CompanyId = companyId;
        NationalCode = nationalCode;
        FirstName = firstName;
        LastName = lastName;
        Gender = gender;
        PersonnelCode = personnelCode;
        PhoneNumber = phoneNumber;
        Status = PersonnelStatus.Draft;
        ConcurrencyToken = Guid.NewGuid();
    }

    private void Touch() => ConcurrencyToken = Guid.NewGuid();

    public void UpdateDetails(string firstName, string lastName, string? phoneNumber, Gender gender)
    {
        Touch();
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
        Gender = gender;
    }

    /// <summary>The national code is editable; uniqueness is enforced by the caller and the database.</summary>
    public void ChangeNationalCode(string nationalCode)
    {
        if (string.IsNullOrWhiteSpace(nationalCode))
            throw new DomainRuleViolationException("NATIONAL_CODE_REQUIRED", "National code is required.");

        Touch();
        NationalCode = nationalCode;
    }

    public void SetExternalIdentity(string? source, string? id)
    {
        (ExternalSource, ExternalId) = ExternalIdentity.Normalize(source, id);
        Touch();
    }

    /// <summary>
    /// Explicit status changes. Employment is never set directly: it follows an effective position assignment or
    /// <see cref="ConfirmEmployment"/>. Deactivation requires no effective position; reactivation returns to Draft or Employed.
    /// </summary>
    public void ChangeStatus(PersonnelStatus status, DateTime now)
    {
        if (status == Status)
            return;

        Touch();

        switch (status)
        {
            case PersonnelStatus.Inactive:
                Deactivate(now);
                break;
            case PersonnelStatus.Draft when Status == PersonnelStatus.Inactive:
                Status = HasEffectivePosition(now) ? PersonnelStatus.Employed : PersonnelStatus.Draft;
                break;
            case PersonnelStatus.Draft:
                RevertToDraft(now);
                break;
            default:
                throw TransitionInvalid("Employment starts only through an effective position assignment.");
        }
    }

    /// <summary>Soft delete: the person, their signatures and assignment history are kept.</summary>
    public void Deactivate(DateTime now)
    {
        if (!CanDelete(now))
            throw TransitionInvalid("Personnel with an effective position cannot be deactivated.");

        Touch();
        Status = PersonnelStatus.Inactive;
    }

    /// <param name="positionCompanies">Company of every position this personnel is (or will be) assigned to, including <paramref name="positionId"/>.</param>
    public PersonnelPosition AssignPosition(Guid positionId, bool isPrimary, DateTime effectiveFrom, DateTime? effectiveTo, DateTime now, IReadOnlyDictionary<Guid, Guid> positionCompanies)
    {
        if (Status == PersonnelStatus.Inactive)
            throw new DomainRuleViolationException("PERSONNEL_INACTIVE", "Inactive personnel cannot be assigned to a position.");

        if (CompanyOf(positionId, positionCompanies) != CompanyId)
            throw new DomainRuleViolationException("PERSONNEL_COMPANY_MISMATCH", "Personnel can only be assigned to positions of their own company.");

        EnsureNoOverlapOnSamePosition(positionId, effectiveFrom, effectiveTo, excludeAssignmentId: null);
        if (isPrimary)
            EnsureNoOverlappingPrimaryInSameCompany(positionId, effectiveFrom, effectiveTo, excludeAssignmentId: null, positionCompanies);

        var assignment = new PersonnelPosition(Id, positionId, isPrimary, effectiveFrom, effectiveTo, now);
        _positions.Add(assignment);
        Touch();

        if (Status == PersonnelStatus.Draft && HasEffectivePosition(now))
            Status = PersonnelStatus.Employed;

        AddDomainEvent(new PersonnelPositionAssignedEvent(Id, positionId, isPrimary));
        return assignment;
    }

    public void UpdatePositionAssignment(Guid assignmentId, bool isPrimary, DateTime effectiveFrom, DateTime? effectiveTo, PersonnelPositionStatus status, DateTime now, IReadOnlyDictionary<Guid, Guid> positionCompanies)
    {
        var assignment = GetAssignment(assignmentId);
        EnsureNotSealed(assignment, now);

        if (assignment.HasStarted(now) && effectiveFrom != assignment.EffectiveFrom)
            throw new DomainRuleViolationException("EFFECTIVE_FROM_LOCKED", "EffectiveFrom cannot be changed after the assignment has started.");

        if (status == PersonnelPositionStatus.Active)
        {
            EnsureNoOverlapOnSamePosition(assignment.PositionId, effectiveFrom, effectiveTo, excludeAssignmentId: assignment.Id);
            if (isPrimary)
                EnsureNoOverlappingPrimaryInSameCompany(assignment.PositionId, effectiveFrom, effectiveTo, excludeAssignmentId: assignment.Id, positionCompanies);
        }

        var primaryChanged = assignment.IsPrimary != isPrimary;
        Touch();

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
        Touch();

        if (Status == PersonnelStatus.Employed && !HasEffectivePosition(now))
            Status = PersonnelStatus.Draft;

        AddDomainEvent(new PersonnelPositionRemovedEvent(Id, assignment.PositionId));
    }

    /// <summary>
    /// Creates a new assignment row that corrects a sealed historical record. The sealed row is left unchanged.
    /// </summary>
    public PersonnelPosition CorrectPositionAssignment(
        Guid sealedAssignmentId,
        DateTime effectiveFrom,
        DateTime? effectiveTo,
        bool isPrimary,
        DateTime now,
        IReadOnlyDictionary<Guid, Guid> positionCompanies)
    {
        var sealedAssignment = GetAssignment(sealedAssignmentId);
        if (!sealedAssignment.IsSealed(now))
            throw new DomainRuleViolationException("ASSIGNMENT_NOT_SEALED", "Only sealed (ended) assignments can be corrected; use update for open windows.");
        if (IsSuperseded(sealedAssignment))
            throw new DomainRuleViolationException("ASSIGNMENT_ALREADY_CORRECTED", "This assignment was already corrected; correct its latest version instead.");

        EnsureNoOverlapOnSamePosition(sealedAssignment.PositionId, effectiveFrom, effectiveTo, excludeAssignmentId: sealedAssignment.Id);
        if (isPrimary)
            EnsureNoOverlappingPrimaryInSameCompany(sealedAssignment.PositionId, effectiveFrom, effectiveTo, excludeAssignmentId: sealedAssignment.Id, positionCompanies);

        var correction = new PersonnelPosition(Id, sealedAssignment.PositionId, isPrimary, effectiveFrom, effectiveTo, now, correctsAssignmentId: sealedAssignment.Id);
        _positions.Add(correction);
        Touch();

        if (Status == PersonnelStatus.Draft && HasEffectivePosition(now))
            Status = PersonnelStatus.Employed;

        AddDomainEvent(new PersonnelPositionCorrectedEvent(Id, sealedAssignment.Id, correction.Id, sealedAssignment.PositionId));
        return correction;
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
                && !IsSuperseded(p)
                && p.HasOverlap(effectiveFrom, effectiveTo)))
        {
            throw new DomainRuleViolationException("ASSIGNMENT_OVERLAP", "The effective window overlaps an active assignment to the same position.");
        }
    }

    private void EnsureNoOverlappingPrimaryInSameCompany(Guid positionId, DateTime effectiveFrom, DateTime? effectiveTo, Guid? excludeAssignmentId, IReadOnlyDictionary<Guid, Guid> positionCompanies)
    {
        var companyId = CompanyOf(positionId, positionCompanies);

        var conflict = _positions.Any(p => p.IsPrimary
            && p.IsActive
            && p.Id != excludeAssignmentId
            && !IsSuperseded(p)
            && p.HasOverlap(effectiveFrom, effectiveTo)
            && CompanyOf(p.PositionId, positionCompanies) == companyId);

        if (conflict)
            throw new DomainRuleViolationException("PRIMARY_OVERLAP_CONFLICT", "Another primary assignment in the same company overlaps this effective window.");
    }

    private bool IsSuperseded(PersonnelPosition assignment) => _positions.Any(p => p.CorrectsAssignmentId == assignment.Id);

    private static Guid CompanyOf(Guid positionId, IReadOnlyDictionary<Guid, Guid> positionCompanies)
    {
        return positionCompanies.TryGetValue(positionId, out var companyId)
            ? companyId
            : throw new InvalidOperationException($"Company of position {positionId} was not supplied; primary assignment rules cannot be evaluated.");
    }

    public const int MaxSignatureBytes = 8 * 1024 * 1024;

    /// <summary>Size, declared type and magic bytes. Callers decode the image only after these cheap checks pass.</summary>
    public static void EnsureSignatureContentAllowed(byte[] content, string mimeType)
    {
        if (content.Length == 0)
            throw new DomainRuleViolationException("SIGNATURE_EMPTY", "Signature file is empty.");

        if (content.Length > MaxSignatureBytes)
            throw new DomainRuleViolationException("SIGNATURE_TOO_LARGE", "Signature file exceeds 8MB limit.");

        var matchesDeclaredType = mimeType.ToLowerInvariant() switch
        {
            "image/png" => content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/jpeg" => content.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            _ => false
        };
        if (!matchesDeclaredType)
            throw new DomainRuleViolationException("SIGNATURE_TYPE_NOT_ALLOWED", "Only PNG/JPEG signatures whose content matches the declared type are allowed.");
    }

    public PersonnelSignature UploadSignature(byte[] content, string mimeType, string contentHash, Guid? uploadedByUserId)
    {
        EnsureSignatureContentAllowed(content, mimeType);
        mimeType = mimeType.ToLowerInvariant();

        var nextVersion = _signatures.Any() ? _signatures.Max(s => s.Version) + 1 : 1;

        foreach (var s in _signatures.Where(s => s.IsCurrent))
            s.SetNotCurrent();

        var signature = new PersonnelSignature(Id, nextVersion, mimeType, content.Length, contentHash, content, uploadedByUserId);
        _signatures.Add(signature);
        Touch();

        AddDomainEvent(new PersonnelSignatureReplacedEvent(Id, signature.Id, nextVersion));
        return signature;
    }

    public PersonnelSignature? GetCurrentSignature() => _signatures.FirstOrDefault(s => s.IsCurrent);

    public bool CanDelete(DateTime now) => !HasEffectivePosition(now);

    public void ConfirmEmployment(DateTime now)
    {
        if (Status != PersonnelStatus.Draft)
            throw TransitionInvalid("Only Draft personnel can confirm employment.");

        if (!HasEffectivePosition(now))
            throw TransitionInvalid("Cannot confirm employment: no effective position assigned.");

        Touch();
        Status = PersonnelStatus.Employed;
    }

    public void RevertToDraft(DateTime now)
    {
        if (Status != PersonnelStatus.Employed)
            throw TransitionInvalid("Only Employed personnel can revert to draft.");

        if (HasEffectivePosition(now))
            throw TransitionInvalid("Cannot revert to draft: has effective position assignments.");

        Touch();
        Status = PersonnelStatus.Draft;
    }

    private bool HasEffectivePosition(DateTime now) => _positions.Any(p => p.IsCurrentlyEffective(now));

    private static DomainRuleViolationException TransitionInvalid(string message)
        => new("PERSONNEL_STATUS_TRANSITION_INVALID", message);
}

public enum Gender { Male, Female }

public enum PersonnelStatus { Draft, Employed, Inactive }
