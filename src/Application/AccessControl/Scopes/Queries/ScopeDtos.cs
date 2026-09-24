namespace CompanyAccessManagement.Application.AccessControl.Scopes.Queries;

public record WorkshopDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string Code { get; init; } = null!;
    public Guid? RelatedSiteId { get; init; }
}

public record ScopeResourceTreeDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
    public List<ScopeResourceTreeDto> Children { get; init; } = new();
    public List<ResourceActionDto> Actions { get; init; } = new();
}

public record ResourceActionDto
{
    public string ActionCode { get; init; } = null!;
    public string Name { get; init; } = null!;
}
