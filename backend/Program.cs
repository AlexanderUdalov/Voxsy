using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                      ?? ["http://localhost:5173"];
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient("OpenAI", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/";
    var apiKey = config["OpenAI:ApiKey"]
                 ?? throw new InvalidOperationException(
                     "OpenAI:ApiKey is not configured. Use: dotnet user-secrets set \"OpenAI:ApiKey\" \"sk-...\"");

    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    client.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddSingleton<ChatSessionService>();
builder.Services.AddSingleton<LearnerMemoryService>();
builder.Services.AddScoped<SessionErrorAggregator>();
builder.Services.AddScoped<TutorOrchestrator>();

var app = builder.Build();
app.UseCors();

app.MapPost("/api/speech", async (SpeechRequest req, IHttpClientFactory httpFactory, IConfiguration config, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(req.Input))
        return Results.BadRequest("Missing input");

    var client = httpFactory.CreateClient("OpenAI");
    var model = string.IsNullOrWhiteSpace(req.Model)
        ? (config["OpenAI:TtsModel"] ?? "gpt-4o-mini-tts")
        : req.Model!;
    var voice = string.IsNullOrWhiteSpace(req.Voice)
        ? (config["OpenAI:TtsVoice"] ?? "alloy")
        : req.Voice!;
    var responseFormat = string.IsNullOrWhiteSpace(req.Format) ? "mp3" : req.Format!;

    var payload = new JsonObject
    {
        ["model"] = model,
        ["input"] = req.Input.Trim(),
        ["voice"] = voice,
        ["response_format"] = responseFormat
    };

    var httpRequest = new HttpRequestMessage(HttpMethod.Post, "audio/speech")
    {
        Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
    };

    using var response = await client.SendAsync(
        httpRequest,
        HttpCompletionOption.ResponseHeadersRead,
        ctx.RequestAborted
    );

    if (!response.IsSuccessStatusCode)
    {
        var err = await response.Content.ReadAsStringAsync();
        return Results.Problem(detail: err, statusCode: StatusCodes.Status502BadGateway);
    }

    var mime = responseFormat.ToLowerInvariant() switch
    {
        "mp3" => "audio/mpeg",
        "opus" => "audio/opus",
        "aac" => "audio/aac",
        "flac" => "audio/flac",
        "wav" => "audio/wav",
        "pcm" => "audio/pcm",
        _ => "application/octet-stream"
    };

    ctx.Response.StatusCode = StatusCodes.Status200OK;
    ctx.Response.ContentType = mime;
    ctx.Response.Headers.CacheControl = "no-cache";

    await using var upstream = await response.Content.ReadAsStreamAsync(ctx.RequestAborted);
    await upstream.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

    return Results.Empty;
});

app.MapPost("/api/transcribe", async (HttpRequest request, IHttpClientFactory httpFactory, IConfiguration config, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data");

    var form = await request.ReadFormAsync(ct);
    var audio = form.Files.GetFile("audio");
    if (audio is null || audio.Length == 0)
        return Results.BadRequest("Missing audio file");

    var model = form["model"].ToString();
    if (string.IsNullOrWhiteSpace(model))
        model = config["OpenAI:TranscriptionModel"] ?? "gpt-4o-mini-transcribe";

    await using var sourceStream = audio.OpenReadStream();
    using var content = new MultipartFormDataContent();
    content.Add(new StringContent(model), "model");
    content.Add(
        new StringContent("You are transcribing speech for Voxsy, an English speaking tutor. Preserve pauses, hesitations, fillers, and non-lexical sounds exactly when audible (e.g., 'uh', 'umm', 'mmm', 'eee'). Keep disfluencies because they are used for fluency diagnostics."),
        "prompt"
    );
    content.Add(new StringContent("json"), "response_format");

    var fileContent = new StreamContent(sourceStream);
    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(audio.ContentType ?? "application/octet-stream");
    content.Add(fileContent, "file", string.IsNullOrWhiteSpace(audio.FileName) ? "voice-message.webm" : audio.FileName);

    var client = httpFactory.CreateClient("OpenAI");
    using var upstreamResponse = await client.PostAsync("audio/transcriptions", content, ct);

    if (!upstreamResponse.IsSuccessStatusCode)
    {
        var err = await upstreamResponse.Content.ReadAsStringAsync(ct);
        return Results.Problem(detail: err, statusCode: StatusCodes.Status502BadGateway);
    }

    await using var payloadStream = await upstreamResponse.Content.ReadAsStreamAsync(ct);
    using var doc = await JsonDocument.ParseAsync(payloadStream, cancellationToken: ct);
    var text = doc.RootElement.TryGetProperty("text", out var textNode)
        ? textNode.GetString() ?? string.Empty
        : string.Empty;

    return Results.Json(new { text });
});

app.MapPost("/api/chat/stream", async (HttpContext ctx, IHttpClientFactory httpFactory, IConfiguration config) =>
{
    var client = httpFactory.CreateClient("OpenAI");
    var defaultModel = config["OpenAI:ChatModel"] ?? "gpt-4o-mini";

    using var reader = new StreamReader(ctx.Request.Body);
    var bodyText = await reader.ReadToEndAsync();
    var root = JsonNode.Parse(bodyText)?.AsObject();

    if (root is null)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Invalid JSON body");
        return;
    }

    root["stream"] = true;
    if (!root.ContainsKey("model"))
        root["model"] = defaultModel;

    var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
    {
        Content = new StringContent(root.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
    };

    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

    if (!response.IsSuccessStatusCode)
    {
        var error = await response.Content.ReadAsStringAsync();
        ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(error);
        return;
    }

    ctx.Response.StatusCode = 200;
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";

    await using var upstream = await response.Content.ReadAsStreamAsync();
    var buffer = new byte[8192];
    int bytesRead;
    while ((bytesRead = await upstream.ReadAsync(buffer)) > 0)
    {
        await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead));
        await ctx.Response.Body.FlushAsync();
    }
});

app.MapPost("/api/session/start", (ChatSessionService sessions) =>
{
    var session = sessions.Start();
    return Results.Json(new SessionStartResponse { SessionId = session.SessionId });
});

app.MapGet("/api/learner-memory", (LearnerMemoryService memory) =>
{
    return Results.Json(new LearnerMemoryResponse { FocusAreas = memory.GetFocusAreas() });
});

app.MapDelete("/api/learner-memory", (LearnerMemoryService memory) =>
{
    memory.Reset();
    return Results.NoContent();
});

app.MapPost("/api/session/{sessionId}/feedback", async (
    string sessionId,
    SessionFeedbackRequest request,
    ChatSessionService sessions,
    LearnerMemoryService memory,
    TutorOrchestrator orchestrator,
    HttpContext ctx,
    IConfiguration config,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("SessionFeedback");
    DialogueSession session;
    try
    {
        session = sessions.GetOrThrow(sessionId);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "Session not found." });
    }

    session.LastActivityAt = DateTimeOffset.UtcNow;
    session.FeedbackRequestedByUser = true;

    var model = request.Model ?? config["OpenAI:ChatModel"] ?? "gpt-4o-mini";
    var result = await orchestrator.CreateReplyAsync(
        session,
        memory,
        new SessionTurnInput
        {
            UserText = string.Empty,
            Source = "text"
        },
        model,
        ctx.RequestAborted);
    session.Messages.Add(new SessionMessage("assistant", result.Content, "text", DateTimeOffset.UtcNow));
    memory.MergeSessionPool(session.ErrorPool.Values);

    logger.LogInformation("Session {SessionId} feedback generated. reason={Reason}", sessionId, "manual");

    await WriteSyntheticSseResponse(ctx, result.Content, result.ResponseType, ctx.RequestAborted);
    return Results.Empty;
});

app.MapPost("/api/session/{sessionId}/message", async (
    string sessionId,
    SessionMessageRequest request,
    ChatSessionService sessions,
    LearnerMemoryService memory,
    SessionErrorAggregator errorAggregator,
    TutorOrchestrator orchestrator,
    HttpContext ctx,
    IConfiguration config,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("SessionMessage");
    if (string.IsNullOrWhiteSpace(request.Content))
        return Results.BadRequest(new { error = "Message content is required." });
    if (request.Source is "voice" && string.IsNullOrWhiteSpace(request.AudioBase64))
        return Results.BadRequest(new { error = "Voice message must include audio payload." });

    DialogueSession session;
    try
    {
        session = sessions.GetOrThrow(sessionId);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "Session not found." });
    }

    var normalizedSource = request.Source is "voice" ? "voice" : "text";
    session.Messages.Add(new SessionMessage("user", request.Content.Trim(), normalizedSource, DateTimeOffset.UtcNow));
    session.LastActivityAt = DateTimeOffset.UtcNow;

    var model = request.Model ?? config["OpenAI:ChatModel"] ?? "gpt-4o-mini";
    var result = await orchestrator.CreateReplyAsync(
        session,
        memory,
        new SessionTurnInput
        {
            UserText = request.Content.Trim(),
            Source = normalizedSource,
            AudioBase64 = request.AudioBase64,
            AudioFormat = string.IsNullOrWhiteSpace(request.AudioFormat) ? "wav" : request.AudioFormat
        },
        model,
        ctx.RequestAborted);
    session.Messages.Add(new SessionMessage("assistant", result.Content, "text", DateTimeOffset.UtcNow));

    await errorAggregator.MergeFromTurnAsync(session, request.Content, result.Content, ctx.RequestAborted);
    if (result.ResponseType == ResponseType.Feedback)
    {
        memory.MergeSessionPool(session.ErrorPool.Values);
    }

    logger.LogInformation(
        "Session {SessionId} turn processed. responseType={ResponseType} userTurns={UserTurns} errorPool={ErrorPoolCount}",
        sessionId,
        result.ResponseType,
        session.Messages.Count(m => m.Role == "user"),
        session.ErrorPool.Count
    );

    await WriteSyntheticSseResponse(ctx, result.Content, result.ResponseType, ctx.RequestAborted);
    return Results.Empty;
});

app.Run();

static async Task WriteSyntheticSseResponse(
    HttpContext ctx,
    string content,
    ResponseType responseType,
    CancellationToken ct)
{
    ctx.Response.StatusCode = StatusCodes.Status200OK;
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";

    var metaPayload = JsonSerializer.Serialize(new { responseType = responseType == ResponseType.Feedback ? "feedback" : "dialogue" });
    await ctx.Response.WriteAsync($"event: meta\ndata: {metaPayload}\n\n", ct);
    await ctx.Response.Body.FlushAsync(ct);

    foreach (var chunk in ChunkForSse(content))
    {
        var payload = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    delta = new
                    {
                        content = chunk
                    }
                }
            }
        });
        await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }

    await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
    await ctx.Response.Body.FlushAsync(ct);
}

static IEnumerable<string> ChunkForSse(string text)
{
    const int size = 48;
    if (string.IsNullOrEmpty(text))
        yield break;

    for (var i = 0; i < text.Length; i += size)
    {
        var len = Math.Min(size, text.Length - i);
        yield return text.Substring(i, len);
    }
}
