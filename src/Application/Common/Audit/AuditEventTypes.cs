namespace CompanyAccessManagement.Application.Common.Audit;

/// <summary>Event type codes written to <c>AuditLog.EventType</c> (design §§43–44).</summary>
public static class AuditEventTypes
{
    public const string PositionCreated = "POSITION_CREATED";
    public const string PositionUpdated = "POSITION_UPDATED";
    public const string PositionParentChanged = "POSITION_PARENT_CHANGED";
    public const string PositionActivated = "POSITION_ACTIVATED";
    public const string PositionDeactivated = "POSITION_DEACTIVATED";

    public const string PersonnelCreated = "PERSONNEL_CREATED";
    public const string PersonnelUpdated = "PERSONNEL_UPDATED";
    public const string PersonnelStatusChanged = "PERSONNEL_STATUS_CHANGED";

    public const string PersonnelPositionAssigned = "PERSONNEL_POSITION_ASSIGNED";
    public const string PersonnelPositionRemoved = "PERSONNEL_POSITION_REMOVED";
    public const string PrimaryPositionChanged = "PRIMARY_POSITION_CHANGED";
    public const string PersonnelPositionCorrected = "PERSONNEL_POSITION_CORRECTED";

    public const string SignatureUploaded = "SIGNATURE_UPLOADED";
    public const string SignatureReplaced = "SIGNATURE_REPLACED";

    public const string RoleAssigned = "ROLE_ASSIGNED";
    public const string RoleRemoved = "ROLE_REMOVED";
    public const string RoleParentChanged = "ROLE_PARENT_CHANGED";

    public const string PermissionGranted = "PERMISSION_GRANTED";
    public const string PermissionRevoked = "PERMISSION_REVOKED";
    public const string ScopeChanged = "SCOPE_CHANGED";
    public const string PermissionCopied = "PERMISSION_COPIED";

    public const string DelegationCreated = "DELEGATION_CREATED";
    public const string DelegationRevoked = "DELEGATION_REVOKED";

    public const string AttachmentUploaded = "ATTACHMENT_UPLOADED";
    public const string AttachmentDeleted = "ATTACHMENT_DELETED";
}
