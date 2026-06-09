namespace Gpt2Image.Core.Models;

public sealed class ChatConversation
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string? BackendProfileId { get; init; }
    public string Model { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class ChatAttachment
{
    public long Id { get; init; }
    public long MessageId { get; init; }
    public string ConversationId { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string MimeType { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long ByteLength { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public bool IsImage => MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public bool IsText => MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                         || MimeType is "application/json" or "application/xml" or "application/javascript";
}

public sealed class ChatMessage
{
    public long Id { get; init; }
    public string ConversationId { get; init; } = "";
    public string Role { get; init; } = "";
    public string Content { get; init; } = "";
    public IReadOnlyList<ChatAttachment> Attachments { get; init; } = Array.Empty<ChatAttachment>();
    public string? RawJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class ChatRequest
{
    public string ConversationId { get; init; } = "";
    public string Model { get; init; } = "";
    public IReadOnlyList<ChatMessage> Messages { get; init; } = Array.Empty<ChatMessage>();
}

public sealed class ChatResult
{
    public string? Content { get; init; }
    public string? RawJson { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? Error { get; init; }
}
