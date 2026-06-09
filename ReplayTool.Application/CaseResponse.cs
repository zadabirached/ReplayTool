using ReplayTool.Domain.Entities;

namespace ReplayTool.Application;

public record CaseResponse(
    Guid Id,
    string Name,
    string? Description,
    CaseStatus Status,
    DateTime CreatedAt,
    IReadOnlyList<string> UploadedTypes
);
