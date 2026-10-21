namespace CompanyAccessManagement.Application.Common.Exceptions;

/// <summary>
/// The effective scope set is larger than the configured ceiling, or it is an unrestricted grant with
/// exceptions. Either shape is returned as 409 <see cref="Code"/> instead of a truncated list.
/// </summary>
public sealed class EffectiveScopesTooLargeException : Exception
{
    public const string Code = "EFFECTIVE_SCOPES_TOO_LARGE";

    public EffectiveScopesTooLargeException(int maxKeys)
        : base($"The effective scope set exceeds {maxKeys} keys or cannot be represented as a finite selection.")
    {
    }
}
