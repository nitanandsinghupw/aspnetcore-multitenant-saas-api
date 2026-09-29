namespace SaasApi.Data;

/// <summary>A tenant-owned resource. TenantId doubles as the Cosmos DB partition key.</summary>
public sealed record Project(string Id, string TenantId, string Name, string? Description, DateTimeOffset CreatedAt, string CreatedBy);
