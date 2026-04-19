using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/convert", async (Request request, IHttpClientFactory httpFactory) =>
{
    if (string.IsNullOrWhiteSpace(request?.Url))
        return Results.BadRequest("URL is required.");

    try
    {
        var httpClient = httpFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

        var httpResponse = await httpClient.GetAsync(request.Url);
        if (!httpResponse.IsSuccessStatusCode)
            return Results.Problem($"Request failed with status: {httpResponse.StatusCode}");

        var rawContent = await httpResponse.Content.ReadAsStringAsync();

        // If response is not HTML, assume JSON
        if (!rawContent.TrimStart().StartsWith("<"))
        {
            using var jsonDoc = JsonDocument.Parse(rawContent);
            var root = jsonDoc.RootElement;

            // Try to map Speedhive-style structure
            if (root.TryGetProperty("rows", out var rows))
            {
                var items = rows.EnumerateArray().Select((row, index) => new
                {
                    id = $"racer-{index + 1}",
                    fullName = row.TryGetProperty("name", out var name) ? name.GetString() : "N/A",
                    deviceNumber = row.TryGetProperty("transponder", out var transponder) &&
                                   transponder.ValueKind != JsonValueKind.Null &&
                                   int.TryParse(transponder.GetString(), out var device)
                                   ? device
                                   : 0,
                    position = row.TryGetProperty("position", out var pos) ? pos.GetInt32() : 0,
                    kartNumber = row.TryGetProperty("startNumber", out var kart) &&
                                 int.TryParse(kart.GetString(), out var number)
                                 ? number
                                 : 0,
                    lastTime = row.TryGetProperty("lastTime", out var last) ? last.GetString() : (string?)null,
                    bestTime = row.TryGetProperty("bestTime", out var best) ? best.GetString() : (string?)null
                });

                return Results.Json(new
                {
                    title = "Qualifying",
                    sessionTime = "LIVE",
                    items
                });
            }

            // Fallback: generic JSON array
            if (root.ValueKind == JsonValueKind.Array)
            {
                var items = root.EnumerateArray().Select((item, index) => new
                {
                    id = $"racer-{index + 1}",
                    fullName = item.TryGetProperty("name", out var name) ? name.GetString() : "Unknown",
                    deviceNumber = 0,
                    position = index + 1,
                    kartNumber = 0,
                    lastTime = (string?)null,
                    bestTime = (string?)null
                });

                return Results.Json(new
                {
                    title = "Generic Data",
                    sessionTime = "N/A",
                    items
                });
            }
        }

        return Results.Problem("The URL did not return usable JSON data.");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unexpected error: {ex.Message}");
    }
});

app.Run();

record Request(string? Url);