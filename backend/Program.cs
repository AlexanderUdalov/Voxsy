using System.Net.Http.Headers;
using System.Text;
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

var app = builder.Build();
app.UseCors();

app.MapPost("/api/speech", async (SpeechRequest req, IHttpClientFactory httpFactory, IConfiguration config) =>
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

    using var response = await client.SendAsync(httpRequest);

    if (!response.IsSuccessStatusCode)
    {
        var err = await response.Content.ReadAsStringAsync();
        return Results.Problem(detail: err, statusCode: StatusCodes.Status502BadGateway);
    }

    var bytes = await response.Content.ReadAsByteArrayAsync();
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

    return Results.File(bytes, mime);
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

app.Run();
