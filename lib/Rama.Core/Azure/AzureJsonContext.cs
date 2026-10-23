using System.Text.Json.Serialization;

namespace Rama.Core.Azure;

// DTOs crudos de la API REST (forma según documentación oficial; ver SUPUESTO en el cliente).
internal sealed class ListDto<T> { public int Count { get; set; } public List<T>? Value { get; set; } }
internal sealed class IdentityDto { public string? Id { get; set; } public string? DisplayName { get; set; } public bool IsContainer { get; set; } }
internal sealed class ConnectionDataDto { public IdentityDto? AuthenticatedUser { get; set; } }
internal sealed class ProjectRefDto { public string? Id { get; set; } public string? Name { get; set; } }
internal sealed class RepoDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? DefaultBranch { get; set; }
    public string? RemoteUrl { get; set; }
    public long? Size { get; set; }
    public DateTimeOffset? LastUpdate { get; set; }
    public ProjectRefDto? Project { get; set; }
}
internal sealed class ReviewerDto
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public int Vote { get; set; }
    public bool IsRequired { get; set; }
    public bool IsContainer { get; set; }
}
internal sealed class CommitRefDto { public string? CommitId { get; set; } }
internal sealed class PrRepoDto { public string? Id { get; set; } public string? Name { get; set; } public ProjectRefDto? Project { get; set; } }
internal sealed class PrDto
{
    public int PullRequestId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public bool IsDraft { get; set; }
    public IdentityDto? CreatedBy { get; set; }
    public DateTimeOffset? CreationDate { get; set; }
    public string? SourceRefName { get; set; }
    public string? TargetRefName { get; set; }
    public string? MergeStatus { get; set; }
    public PrRepoDto? Repository { get; set; }
    public CommitRefDto? LastMergeSourceCommit { get; set; }
    public List<ReviewerDto>? Reviewers { get; set; }
}
internal sealed class PolicyTypeDto { public string? DisplayName { get; set; } }
internal sealed class PolicyConfigDto { public bool IsBlocking { get; set; } public PolicyTypeDto? Type { get; set; } }
internal sealed class PolicyEvalDto { public string? Status { get; set; } public PolicyConfigDto? Configuration { get; set; } }
internal sealed class ApprovalStepDto { public IdentityDto? AssignedApprover { get; set; } public string? Status { get; set; } }
internal sealed class ApprovalPipelineDto { public string? Id { get; set; } public string? Name { get; set; } }
internal sealed class ApprovalDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? Instructions { get; set; }
    public DateTimeOffset? CreatedOn { get; set; }
    public ApprovalPipelineDto? Pipeline { get; set; }
    public List<ApprovalStepDto>? Steps { get; set; }
}
internal sealed class VoteBodyDto { public int Vote { get; set; } public string? Id { get; set; } }
internal sealed class ApprovalUpdateDto { public string? ApprovalId { get; set; } public string? Status { get; set; } public string? Comment { get; set; } }

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConnectionDataDto))]
[JsonSerializable(typeof(ListDto<RepoDto>))]
[JsonSerializable(typeof(ListDto<PrDto>))]
[JsonSerializable(typeof(ListDto<PolicyEvalDto>))]
[JsonSerializable(typeof(ListDto<ApprovalDto>))]
[JsonSerializable(typeof(ListDto<CommitRefDto>))]
[JsonSerializable(typeof(PrDto))]
[JsonSerializable(typeof(ReviewerDto))]
[JsonSerializable(typeof(VoteBodyDto))]
[JsonSerializable(typeof(List<ApprovalUpdateDto>))]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Código source-gen")]
internal partial class AzureJsonContext : JsonSerializerContext
{
}
