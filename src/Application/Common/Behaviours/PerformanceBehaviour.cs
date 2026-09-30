using System.Diagnostics;
using CompanyAccessManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace CompanyAccessManagement.Application.Common.Behaviours;

public class PerformanceBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TRequest> _logger;
    private readonly IUser _user;

    public PerformanceBehaviour(ILogger<TRequest> logger, IUser user)
    {
        _logger = logger;
        _user = user;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();

        var response = await next();

        timer.Stop();

        if (timer.ElapsedMilliseconds > 500)
        {
            _logger.LogWarning("Long running request: {Name} ({ElapsedMilliseconds} milliseconds) {UserId}",
                typeof(TRequest).Name, timer.ElapsedMilliseconds, _user.Id?.ToString() ?? string.Empty);
        }

        return response;
    }
}
