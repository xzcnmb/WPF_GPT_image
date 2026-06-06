namespace Gpt2Image.Core.Models;

public enum AgentRunEventKind
{
    Message,
    WebSearch,
    ImageGeneration,
    ImagePartial,
    Tool,
    Error
}

public enum AgentRunEventStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public sealed record AgentRunEvent(
    string Id,
    int Round,
    AgentRunEventKind Kind,
    string Title,
    string? Detail,
    AgentRunEventStatus Status,
    string? ImageBase64,
    int? PartialImageIndex,
    DateTimeOffset Timestamp);

public sealed record AgentTaskCard(
    string Key,
    AgentRunEventKind Kind,
    string Title,
    string? Detail,
    AgentRunEventStatus Status,
    string? ImageBase64,
    IReadOnlyList<AgentRunEvent> Events);

public sealed record AgentRoundCard(
    int Round,
    string Title,
    AgentRunEventStatus Status,
    IReadOnlyList<AgentTaskCard> Tasks,
    IReadOnlyList<AgentRunEvent> Notes);
