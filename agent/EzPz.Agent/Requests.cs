using System.Text.Json;
using System.Text.Json.Nodes;

namespace EzPz.Agent;

/// <summary>
/// Turns one line of the wire format into the agent's reply, for every client: the pipe and the agent's own window.
/// </summary>
public static class Requests
{
    /// <summary>The reply to <paramref name="line"/>: <c>{"id", "result"}</c> or <c>{"id", "error"}</c>.</summary>
    /// <param name="local">Answers methods that belong to the client rather than the agent, or returns false.</param>
    public static async Task<JsonObject> RespondAsync(AgentService service, string line, LocalHandler? local = null)
    {
        JsonNode? id = null;
        var reply = new JsonObject();
        string method = "?", args = "";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var request = JsonDocument.Parse(line, Protocol.Document);
            var root = request.RootElement;
            id = root.TryGetProperty("id", out var idElement) ? JsonNode.Parse(idElement.GetRawText(), documentOptions: Protocol.Document) : null;
            method = root.GetProperty("method").GetString() ?? "";
            var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : JsonDocument.Parse("{}").RootElement;
            args = parameters.GetRawText();
            reply["result"] = local is not null && local(method, parameters, out var result)
                ? ToNode(result)
                : ToNode(await service.HandleAsync(method, parameters));
        }
        catch (Exception error)
        {
            reply["error"] = error.Message;
            Log.Write($"{method} {args} failed after {clock.ElapsedMilliseconds} ms: {error.Message}");
        }
        reply["id"] = id;
        return reply;
    }

    public delegate bool LocalHandler(string method, JsonElement args, out object? result);

    /// <summary>An event as clients get it: <c>{"event", "data"}</c>.</summary>
    public static JsonObject Event(string name, object? data) => new() { ["event"] = name, ["data"] = ToNode(data) };

    public static JsonNode? ToNode(object? value) =>
        value is null ? null : JsonSerializer.SerializeToNode(value, Protocol.Json.GetTypeInfo(value.GetType()));
}
