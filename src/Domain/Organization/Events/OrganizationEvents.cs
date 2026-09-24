using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Domain.Organization.Events;

public sealed class PersonnelCreatedEvent : BaseEvent
{
    public Guid PersonnelId { get; }
    public string NationalCode { get; }
    public string FirstName { get; }
    public string LastName { get; }

    public PersonnelCreatedEvent(Guid personnelId, string nationalCode, string firstName, string lastName)
    {
        PersonnelId = personnelId;
        NationalCode = nationalCode;
        FirstName = firstName;
        LastName = lastName;
    }
}

public sealed class PersonnelUpdatedEvent : BaseEvent
{
    public Guid PersonnelId { get; }
    public string FirstName { get; }
    public string LastName { get; }

    public PersonnelUpdatedEvent(Guid personnelId, string firstName, string lastName)
    {
        PersonnelId = personnelId;
        FirstName = firstName;
        LastName = lastName;
    }
}

public sealed class PersonnelStatusChangedEvent : BaseEvent
{
    public Guid PersonnelId { get; }
    public PersonnelStatus OldStatus { get; }
    public PersonnelStatus NewStatus { get; }

    public PersonnelStatusChangedEvent(Guid personnelId, PersonnelStatus oldStatus, PersonnelStatus newStatus)
    {
        PersonnelId = personnelId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }
}

public sealed class PersonnelPositionAssignedEvent : BaseEvent
{
    public Guid PersonnelId { get; }
    public Guid PositionId { get; }
    public bool IsPrimary { get; }

    public PersonnelPositionAssignedEvent(Guid personnelId, Guid positionId, bool isPrimary)
    {
        PersonnelId = personnelId;
        PositionId = positionId;
        IsPrimary = isPrimary;
    }
}

public sealed class PersonnelPositionRemovedEvent : BaseEvent
{
    public Guid PersonnelId { get; }
    public Guid PositionId { get; }

    public PersonnelPositionRemovedEvent(Guid personnelId, Guid positionId)
    {
        PersonnelId = personnelId;
        PositionId = positionId;
    }
}

public sealed class PrimaryPositionChangedEvent : BaseEvent
{
    public Guid PersonnelId { get; }
    public Guid NewPrimaryPositionId { get; }

    public PrimaryPositionChangedEvent(Guid personnelId, Guid newPrimaryPositionId)
    {
        PersonnelId = personnelId;
        NewPrimaryPositionId = newPrimaryPositionId;
    }
}

public sealed class PersonnelSignatureUploadedEvent : BaseEvent
{
    public Guid PersonnelId { get; }
    public Guid SignatureId { get; }
    public int Version { get; }

    public PersonnelSignatureUploadedEvent(Guid personnelId, Guid signatureId, int version)
    {
        PersonnelId = personnelId;
        SignatureId = signatureId;
        Version = version;
    }
}

public sealed class PersonnelSignatureReplacedEvent : BaseEvent
{
    public Guid PersonnelId { get; }
    public Guid NewSignatureId { get; }
    public int NewVersion { get; }

    public PersonnelSignatureReplacedEvent(Guid personnelId, Guid newSignatureId, int newVersion)
    {
        PersonnelId = personnelId;
        NewSignatureId = newSignatureId;
        NewVersion = newVersion;
    }
}
