using System.Text.Json.Serialization;

namespace AskRamseyMCP.Models;

// --- Chat Message Request (authenticated follow-up) ---

public record ChatMessageRequest(
    [property: JsonPropertyName("chatSessionId")] string ChatSessionId,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("questionSource")] string QuestionSource = "INPUT"
);

// --- New Chat Request (authenticated, starts a new conversation) ---

public record NewChatRequest(
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("questionSource")] string QuestionSource = "INPUT",
    [property: JsonPropertyName("tenant")] string Tenant = "rp1"
);

// --- Entry-Point Chat Request (anonymous, no auth required) ---

public record EntryPointChatRequest(
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("questionSource")] string QuestionSource,
    [property: JsonPropertyName("tenant")] string Tenant,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("anonymousId")] string AnonymousId
);

// --- Chat Message Response ---

public record ChatSegment(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("content")] string Content
);

public record ChatMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("response")] string? Response,
    [property: JsonPropertyName("flaggedForReview")] bool FlaggedForReview,
    [property: JsonPropertyName("uiArtifacts")] List<object>? UiArtifacts,
    [property: JsonPropertyName("segments")] List<ChatSegment>? Segments
);

public record ChatResponse(
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("questionSource")] string QuestionSource,
    [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
    [property: JsonPropertyName("tenant")] string Tenant,
    [property: JsonPropertyName("teamMember")] bool TeamMember
);

// --- SSE Stream Events ---

public record ThinkingEvent(
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("message")] string Message
);

public record TokenEvent(
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("text")] string Text
);

public record DoneEvent(
    [property: JsonPropertyName("seq")] int Seq
);

// --- Sources ---

public record RagSource(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text
);

public record SourcesResponse(
    [property: JsonPropertyName("ragSources")] List<RagSource> RagSources,
    [property: JsonPropertyName("webSources")] List<object> WebSources
);
