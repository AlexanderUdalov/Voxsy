using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class ChatSessionService
{
    private readonly ConcurrentDictionary<string, DialogueSession> _sessions = new();

    public DialogueSession Start()
    {
        var session = new DialogueSession
        {
            SessionId = Guid.NewGuid().ToString("N")
        };
        _sessions[session.SessionId] = session;
        return session;
    }

    public DialogueSession GetOrThrow(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            return session;
        throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
    }
}

public sealed class LearnerMemoryService
{
    private readonly ConcurrentDictionary<string, LearnerMemoryItem> _memory = new();

    public IReadOnlyList<LearnerMemoryItem> GetFocusAreas(int take = 4)
    {
        return _memory.Values
            .Where(x => x.Status == "active")
            .OrderByDescending(x => x.SeverityScore())
            .ThenByDescending(x => x.LastSeenAt)
            .Take(take)
            .ToList();
    }

    public void MergeSessionPool(IEnumerable<ErrorAggregate> aggregates)
    {
        foreach (var aggregate in aggregates)
        {
            _memory.AddOrUpdate(
                aggregate.ErrorKey,
                key => new LearnerMemoryItem
                {
                    ErrorKey = key,
                    Category = aggregate.Category,
                    Hint = aggregate.Hint,
                    Example = aggregate.Example,
                    CountTotal = aggregate.Count,
                    LastSeenAt = aggregate.LastSeenAt,
                    Trend = "stable",
                    Status = "active"
                },
                (_, existing) =>
                {
                    var previousCount = existing.CountTotal;
                    existing.CountTotal += aggregate.Count;
                    existing.LastSeenAt = aggregate.LastSeenAt;
                    existing.Example = aggregate.Example;
                    existing.Trend = aggregate.Count <= 1 && previousCount > 2 ? "improving" : "stable";
                    existing.Status = "active";
                    return existing;
                }
            );
        }
    }

    public void Reset() => _memory.Clear();
}

public sealed class SessionErrorAggregator(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<SessionErrorAggregator> logger)
{
    public async Task MergeFromTurnAsync(DialogueSession session, string userText, string assistantText, CancellationToken ct)
    {
        var events = await ExtractEventsAsync(userText, assistantText, ct);
        foreach (var ev in events)
        {
            if (session.ErrorPool.TryGetValue(ev.ErrorKey, out var existing))
            {
                existing.Count += 1;
                existing.LastSeenAt = ev.Timestamp;
                existing.Example = ev.Example;
                existing.Severity = Math.Max(existing.Severity, ev.Severity);
                continue;
            }

            session.ErrorPool[ev.ErrorKey] = new ErrorAggregate
            {
                ErrorKey = ev.ErrorKey,
                Category = ev.Category,
                Hint = ev.Hint,
                Example = ev.Example,
                Count = 1,
                Severity = ev.Severity,
                LastSeenAt = ev.Timestamp
            };
        }
    }

    private async Task<IReadOnlyList<ErrorEvent>> ExtractEventsAsync(string userText, string assistantText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userText) || string.IsNullOrWhiteSpace(assistantText))
            return [];

        try
        {
            var model = config["OpenAI:ChatModel"] ?? "gpt-4o-mini";
            var client = httpClientFactory.CreateClient("OpenAI");
            var payload = new JsonObject
            {
                ["model"] = model,
                ["temperature"] = 0.1,
                ["response_format"] = new JsonObject { ["type"] = "json_object" },
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "system",
                        ["content"] = "Extract up to 3 language mistakes from the user's text using assistant's correction context. Return JSON object: {\"events\":[{\"errorKey\":\"...\",\"category\":\"grammar|vocabulary|pronunciation|fluency\",\"example\":\"...\",\"hint\":\"...\",\"severity\":1-5}]}. If no issue, return {\"events\":[]}."
                    },
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = $"User message:\n{userText}\n\nAssistant response:\n{assistantText}"
                    }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            using var res = await client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                return [];

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var raw = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return [];

            using var eventsDoc = JsonDocument.Parse(raw);
            if (!eventsDoc.RootElement.TryGetProperty("events", out var eventsArray) || eventsArray.ValueKind != JsonValueKind.Array)
                return [];

            var now = DateTimeOffset.UtcNow;
            var result = new List<ErrorEvent>();
            foreach (var item in eventsArray.EnumerateArray())
            {
                var key = item.TryGetProperty("errorKey", out var keyNode) ? (keyNode.GetString() ?? string.Empty).Trim() : string.Empty;
                var category = item.TryGetProperty("category", out var catNode) ? (catNode.GetString() ?? "grammar").Trim() : "grammar";
                var example = item.TryGetProperty("example", out var exNode) ? (exNode.GetString() ?? string.Empty).Trim() : string.Empty;
                var hint = item.TryGetProperty("hint", out var hintNode) ? (hintNode.GetString() ?? string.Empty).Trim() : string.Empty;
                var severity = item.TryGetProperty("severity", out var sevNode) && sevNode.TryGetInt32(out var s) ? Math.Clamp(s, 1, 5) : 2;
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                result.Add(new ErrorEvent(key, category, example, hint, severity, now));
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract error events.");
            return [];
        }
    }
}

public sealed class TutorOrchestrator(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<TutorOrchestrator> logger)
{
    public async Task<SessionChatResult> CreateReplyAsync(
        DialogueSession session,
        LearnerMemoryService memoryService,
        SessionTurnInput turnInput,
        string model,
        CancellationToken ct)
    {
        var responseType = await DecideResponseTypeAsync(session, ct);
        var prompt = PromptBuilder.BuildSystemPrompt(responseType, memoryService.GetFocusAreas(), session.ErrorPool.Values.ToList());
        var messages = BuildMessages(session, prompt, turnInput);
        var content = await CompleteAsync(messages, model, ct);
        return new SessionChatResult
        {
            Content = content,
            ResponseType = responseType
        };
    }

    private static JsonArray BuildMessages(DialogueSession session, string systemPrompt, SessionTurnInput turnInput)
    {
        var arr = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            }
        };

        var history = session.Messages.TakeLast(20).ToList();
        for (var index = 0; index < history.Count; index++)
        {
            var msg = history[index];
            var isLatestUserTurn = msg.Role == "user" && index == history.Count - 1;
            if (isLatestUserTurn
                && turnInput.Source == "voice"
                && !string.IsNullOrWhiteSpace(turnInput.AudioBase64)
                && !string.IsNullOrWhiteSpace(turnInput.AudioFormat))
            {
                arr.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = $"Voice transcript from user:\n{turnInput.UserText}"
                        },
                        new JsonObject
                        {
                            ["type"] = "input_audio",
                            ["input_audio"] = new JsonObject
                            {
                                ["data"] = turnInput.AudioBase64,
                                ["format"] = turnInput.AudioFormat
                            }
                        }
                    }
                });
                continue;
            }

            arr.Add(new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content
            });
        }
        return arr;
    }

    private async Task<ResponseType> DecideResponseTypeAsync(DialogueSession session, CancellationToken ct)
    {
        if (session.FeedbackRequestedByUser)
            return ResponseType.Feedback;

        var userTurns = session.Messages.Count(m => m.Role == "user");
        if (userTurns < 4 || DateTimeOffset.UtcNow - session.StartedAt < TimeSpan.FromMinutes(5))
            return ResponseType.Dialogue;

        try
        {
            var client = httpClientFactory.CreateClient("OpenAI");
            var model = config["OpenAI:ChatModel"] ?? "gpt-4o-mini";
            var transcript = string.Join("\n", session.Messages.TakeLast(8).Select(m => $"{m.Role}: {m.Content}"));
            var payload = new JsonObject
            {
                ["model"] = model,
                ["temperature"] = 0.1,
                ["response_format"] = new JsonObject { ["type"] = "json_object" },
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "system",
                        ["content"] = "Decide if learning dialog should end with final feedback now. Return JSON: {\"decision\":\"continue|ready_for_feedback\",\"confidence\":0..1}."
                    },
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = transcript
                    }
                }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            using var res = await client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                return ResponseType.Dialogue;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var raw = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return ResponseType.Dialogue;
            using var ddoc = JsonDocument.Parse(raw);
            var decision = ddoc.RootElement.TryGetProperty("decision", out var decNode) ? decNode.GetString() : "continue";
            var confidence = ddoc.RootElement.TryGetProperty("confidence", out var confNode) && confNode.TryGetDouble(out var c) ? c : 0d;
            return decision == "ready_for_feedback" && confidence >= 0.65 ? ResponseType.Feedback : ResponseType.Dialogue;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to classify session completion.");
            return ResponseType.Dialogue;
        }
    }

    private async Task<string> CompleteAsync(JsonArray messages, string model, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        var payload = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = 0.5,
            ["messages"] = messages
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var res = await client.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}

public static class PromptBuilder
{
    public static string BuildSystemPrompt(
        ResponseType responseType,
        IReadOnlyList<LearnerMemoryItem> focusAreas,
        IReadOnlyList<ErrorAggregate> sessionErrors)
    {
        if (responseType == ResponseType.Feedback)
        {
            var top = sessionErrors
                .OrderByDescending(x => x.Severity * 10 + x.Count)
                .ThenByDescending(x => x.LastSeenAt)
                .Take(5)
                .Select(x => $"- [{x.Category}] {x.Hint} (seen {x.Count}x, example: {x.Example})");
            return """
You are an encouraging English tutor.
Provide a final feedback report for the completed dialogue.
Rules:
- Start with short praise.
- Give 3-5 prioritized mistakes.
- For each: explain issue, give corrected example, and one mini drill.
- Keep it concise and actionable.
- Do not continue open-ended conversation in this response.
Session aggregate:
""" + "\n" + string.Join("\n", top);
        }

        var focus = focusAreas.Any()
            ? "Previous focus areas:\n" + string.Join("\n", focusAreas.Select(x => $"- {x.Hint}"))
            : "No previous focus areas.";

        return """
You are a friendly English conversation partner.
Primary goal: natural dialogue first, corrections should be minimal during conversation.
Rules:
- Keep conversation natural and engaging.
- Do not provide full correction lists each turn.
- If the user repeats a known mistake, give one gentle inline hint only.
- For voice messages, include 1 short pronunciation or fluency note based on the audio.
- Save major feedback for final session summary.
""" + "\n" + focus;
    }
}

file static class LearnerMemoryExtensions
{
    public static int SeverityScore(this LearnerMemoryItem item)
    {
        return item.CountTotal * 10 + (int)(DateTimeOffset.UtcNow - item.LastSeenAt).TotalDays * -1;
    }
}

