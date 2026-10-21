using CompanyAccessManagement.Domain.AccessControl;

namespace CompanyAccessManagement.Infrastructure.Authorization;

/// <summary>
/// Keys of one scope type contributed by a rule, a candidate, or a union of them.
/// <see cref="Kind.AllExcept"/> is the remainder of an unrestricted grant after selected keys are removed;
/// the HTTP contract cannot return that shape.
/// </summary>
internal sealed class ScopeContribution
{
    private enum Kind { Empty, Finite, All, AllExcept }

    private readonly Kind _kind;
    private readonly HashSet<string> _keys;

    private ScopeContribution(Kind kind, IEnumerable<string>? keys)
    {
        _kind = kind;
        _keys = keys is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(keys, StringComparer.Ordinal);
    }

    public static ScopeContribution Empty { get; } = new(Kind.Empty, null);

    public static ScopeContribution All { get; } = new(Kind.All, null);

    public bool IsEmpty => _kind == Kind.Empty;

    public bool IsAll => _kind == Kind.All;

    /// <summary>Unrestricted grant with a finite set of keys removed. Not representable as All or Selected.</summary>
    public bool IsUnrepresentable => _kind == Kind.AllExcept;

    public int KeyCount => _kind == Kind.Finite ? _keys.Count : 0;

    public bool Contains(string key) => _kind switch
    {
        Kind.All => true,
        Kind.Finite => _keys.Contains(key),
        Kind.AllExcept => !_keys.Contains(key),
        _ => false
    };

    public static ScopeContribution FromRule(AccessRule rule, string scopeType) => rule.ScopeMode switch
    {
        ScopeMode.All => All,
        ScopeMode.Selected => Finite(rule.Scopes.Where(s => s.ScopeType == scopeType).Select(s => s.ScopeKey)),
        _ => Empty
    };

    public IReadOnlyList<string> OrderedKeys()
        => _kind == Kind.Finite
            ? _keys.OrderBy(k => k, StringComparer.Ordinal).ToArray()
            : [];

    public ScopeContribution Union(ScopeContribution other)
    {
        if (_kind == Kind.All || other._kind == Kind.All)
            return All;
        if (_kind == Kind.Empty)
            return other;
        if (other._kind == Kind.Empty)
            return this;
        if (_kind == Kind.Finite && other._kind == Kind.Finite)
            return Finite(_keys.Concat(other._keys));
        if (_kind == Kind.AllExcept && other._kind == Kind.AllExcept)
            return AllExcept(_keys.Intersect(other._keys, StringComparer.Ordinal));

        var finite = _kind == Kind.Finite ? _keys : other._keys;
        var excluded = _kind == Kind.AllExcept ? _keys : other._keys;
        return AllExcept(excluded.Where(k => !finite.Contains(k)));
    }

    public ScopeContribution Intersect(ScopeContribution other)
    {
        if (_kind == Kind.Empty || other._kind == Kind.Empty)
            return Empty;
        if (_kind == Kind.All)
            return other;
        if (other._kind == Kind.All)
            return this;
        if (_kind == Kind.Finite && other._kind == Kind.Finite)
            return Finite(_keys.Where(other._keys.Contains));
        if (_kind == Kind.AllExcept && other._kind == Kind.AllExcept)
            return AllExcept(_keys.Concat(other._keys));

        var finite = _kind == Kind.Finite ? _keys : other._keys;
        var excluded = _kind == Kind.AllExcept ? _keys : other._keys;
        return Finite(finite.Where(k => !excluded.Contains(k)));
    }

    /// <summary>Subtracts keys a DENY covers. An All DENY clears the contribution.</summary>
    public ScopeContribution Except(ScopeContribution denied)
    {
        if (_kind == Kind.Empty || denied._kind == Kind.Empty)
            return this;
        if (denied._kind == Kind.All)
            return Empty;
        if (_kind == Kind.All && denied._kind == Kind.Finite)
            return AllExcept(denied._keys);
        if (_kind == Kind.All && denied._kind == Kind.AllExcept)
            return Finite(denied._keys);
        if (_kind == Kind.Finite && denied._kind == Kind.Finite)
            return Finite(_keys.Where(k => !denied._keys.Contains(k)));
        if (_kind == Kind.Finite && denied._kind == Kind.AllExcept)
            return Finite(_keys.Where(denied._keys.Contains));
        if (_kind == Kind.AllExcept && denied._kind == Kind.Finite)
            return AllExcept(_keys.Concat(denied._keys));

        return Finite(denied._keys.Where(k => !_keys.Contains(k)));
    }

    private static ScopeContribution Finite(IEnumerable<string> keys)
    {
        var distinct = new HashSet<string>(keys, StringComparer.Ordinal);
        return distinct.Count == 0 ? Empty : new ScopeContribution(Kind.Finite, distinct);
    }

    private static ScopeContribution AllExcept(IEnumerable<string> excluded)
    {
        var distinct = new HashSet<string>(excluded, StringComparer.Ordinal);
        return distinct.Count == 0 ? All : new ScopeContribution(Kind.AllExcept, distinct);
    }
}
