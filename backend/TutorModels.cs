using System.Collections.Concurrent;

public enum ResponseType
{
    Dialogue,
    Feedback
}

public sealed record SessionMessage(
    string Role,
    string Content,
    string Source,
    DateTimeOffset Timestamp
);

public sealed record ErrorEvent(
    string ErrorKey,
    string Category,
    string Example,
    string Hint,
    int Severity,
    DateTimeOffset Timestamp
);

public sealed class ErrorAggregate
{
    public required string ErrorKey { get; init; }
    public required string Category { get; init; }
    public required string Hint { get; init; }
    public string Example { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Severity { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class DialogueSession
{
    public required string SessionId { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public bool FeedbackRequestedByUser { get; set; }
    public List<SessionMessage> Messages { get; } = [];
    public Dictionary<string, ErrorAggregate> ErrorPool { get; } = [];
}

public sealed class LearnerMemoryItem
{
    public required string ErrorKey { get; init; }
    public required string Category { get; init; }
    public required string Hint { get; init; }
    public string Example { get; set; } = string.Empty;
    public int CountTotal { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string Trend { get; set; } = "stable";
    public string Status { get; set; } = "active";
}

public sealed class SessionStartResponse
{
    public required string SessionId { get; init; }
}

public sealed class SessionMessageRequest
{
    public string Content { get; init; } = string.Empty;
    public string Source { get; init; } = "text";
    public string? Model { get; init; }
    public string? AudioBase64 { get; init; }
    public string? AudioFormat { get; init; }
}

public sealed class SessionFeedbackRequest
{
    public string? Model { get; init; }
}

public sealed class SessionChatResult
{
    public required string Content { get; init; }
    public required ResponseType ResponseType { get; init; }
    public string FinishReason { get; init; } = "stop";
}

public sealed class SessionTurnInput
{
    public required string UserText { get; init; }
    public required string Source { get; init; }
    public string? AudioBase64 { get; init; }
    public string? AudioFormat { get; init; }
}

public sealed class LearnerMemoryResponse
{
    public required IReadOnlyList<LearnerMemoryItem> FocusAreas { get; init; }
}

