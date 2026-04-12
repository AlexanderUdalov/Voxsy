using System.Net.Http.Headers;
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

app.MapPost("/api/transcribe", async (IFormFile file, IHttpClientFactory httpFactory, IConfiguration config) =>
{
    var client = httpFactory.CreateClient("OpenAI");
    var model = config["OpenAI:TranscriptionModel"] ?? "whisper-1";

    using var content = new MultipartFormDataContent();
    await using var fileStream = file.OpenReadStream();
    var streamContent = new StreamContent(fileStream);
    streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType ?? "audio/webm");
    content.Add(streamContent, "file", file.FileName ?? "recording.webm");
    content.Add(new StringContent(model), "model");
    content.Add(new StringContent("en"), "language");

    var response = await client.PostAsync("audio/transcriptions", content);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return Results.Problem(detail: body, statusCode: StatusCodes.Status502BadGateway);

    return Results.Content(body, "application/json");
})
.DisableAntiforgery();

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
