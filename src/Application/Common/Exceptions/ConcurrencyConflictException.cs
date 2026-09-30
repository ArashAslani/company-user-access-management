namespace CompanyAccessManagement.Application.Common.Exceptions;

/// <summary>Another request changed the same data after it was read; the caller should reload and retry.</summary>
public class ConcurrencyConflictException : Exception
{
    public const string Code = "CONCURRENCY_CONFLICT";

    public ConcurrencyConflictException(Exception innerException)
        : base("The data was changed by another request. Reload and try again.", innerException)
    {
    }
}