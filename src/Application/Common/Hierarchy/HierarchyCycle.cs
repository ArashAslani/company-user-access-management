using CompanyAccessManagement.Domain.Common;

namespace CompanyAccessManagement.Application.Common.Hierarchy;

/// <summary>
/// Walks a proposed parent's ancestor chain and rejects when the node being attached appears on that path.
/// </summary>
public static class HierarchyCycle
{
    public static async Task EnsureAcyclicAsync(
        Guid nodeId,
        Guid? proposedParentId,
        Func<Guid, CancellationToken, Task<Guid?>> loadParentId,
        CancellationToken cancellationToken,
        string entityName)
    {
        if (proposedParentId is null)
            return;

        if (proposedParentId.Value == nodeId)
            throw new DomainRuleViolationException(
                "HIERARCHY_CYCLE",
                $"{entityName} cannot be its own parent.");

        var visited = new HashSet<Guid> { nodeId };
        var current = proposedParentId;

        while (current.HasValue)
        {
            if (!visited.Add(current.Value))
                throw new DomainRuleViolationException(
                    "HIERARCHY_CYCLE",
                    $"{entityName} hierarchy cycle detected.");

            current = await loadParentId(current.Value, cancellationToken);
        }
    }
}
