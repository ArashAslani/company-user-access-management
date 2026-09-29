using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Infrastructure.Authorization;

internal sealed record RoleNode(Guid Id, Guid? ParentRoleId, RoleKind Kind, RoleStatus Status, DateTime? ValidUntil);

/// <summary>
/// In-memory role hierarchy of one company and application. All traversals tolerate cycles in persisted data.
/// </summary>
internal sealed class RoleGraph
{
    private readonly Dictionary<Guid, RoleNode> _roles;
    private readonly ILookup<Guid, Guid> _children;

    public RoleGraph(IEnumerable<RoleNode> roles)
    {
        _roles = roles.ToDictionary(r => r.Id);
        _children = _roles.Values
            .Where(r => r.ParentRoleId.HasValue)
            .ToLookup(r => r.ParentRoleId!.Value, r => r.Id);
    }

    public bool TryGet(Guid roleId, out RoleNode role) => _roles.TryGetValue(roleId, out role!);

    /// <summary>The role itself followed by every role beneath it.</summary>
    public IEnumerable<Guid> SelfAndDescendants(Guid roleId)
    {
        var visited = new HashSet<Guid> { roleId };
        var queue = new Queue<Guid>();
        queue.Enqueue(roleId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;

            foreach (var child in _children[current])
            {
                if (visited.Add(child))
                    queue.Enqueue(child);
            }
        }
    }

    /// <summary>Every role above <paramref name="roleId"/>, nearest first, excluding the role itself.</summary>
    public IReadOnlyList<Guid> Ancestors(Guid roleId)
    {
        var ancestors = new List<Guid>();
        var visited = new HashSet<Guid> { roleId };
        var current = _roles.GetValueOrDefault(roleId)?.ParentRoleId;

        while (current.HasValue && visited.Add(current.Value) && _roles.TryGetValue(current.Value, out var parent))
        {
            ancestors.Add(parent.Id);
            current = parent.ParentRoleId;
        }

        return ancestors;
    }

    /// <summary>Roles from <paramref name="originRoleId"/> up to and including <paramref name="branchRoleId"/>.</summary>
    public IReadOnlyList<Guid> PathUpTo(Guid originRoleId, Guid branchRoleId)
    {
        var path = new List<Guid> { originRoleId };
        if (originRoleId == branchRoleId)
            return path;

        foreach (var ancestor in Ancestors(originRoleId))
        {
            path.Add(ancestor);
            if (ancestor == branchRoleId)
                return path;
        }

        throw new InvalidOperationException($"Role {branchRoleId} is not an ancestor of role {originRoleId}.");
    }
}
