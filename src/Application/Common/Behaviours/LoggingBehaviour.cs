using CompanyAccessManagement.Application.Common.Interfaces;
using MediatR.Pipeline;
using Microsoft.Extensions.Logging;

namespace CompanyAccessManagement.Application.Common.Behaviours;

/// <summary>
/// Logs request metadata only. Payloads carry PII and binary content (e.g. signatures) and are never logged.
/// </summary>
public class LoggingBehaviour<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    private readonly ILogger _logger;
    private readonly IUser _user;

    public LoggingBehaviour(ILogger<TRequest> logger, IUser user)
    {
        _logger = logger;
        _user = user;
    }

    public Task Process(TRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Request: {Name} {UserId}", typeof(TRequest).Name, _user.Id?.ToString() ?? string.Empty);
        return Task.CompletedTask;
    }
}
