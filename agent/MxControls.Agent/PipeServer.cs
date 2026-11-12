using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MxControls.Agent;

/// <summary>
/// Serves the agent to the app and other local clients. The pipe is a named pipe on Windows and a Unix domain
/// socket elsewhere; either way only the current user can connect.
/// </summary>
public sealed class PipeServer(AgentService service)
{
    private const int BufferSize = 64 * 1024;

    private readonly ConcurrentDictionary<Client, byte> _clients = new();

    public async Task RunAsync(CancellationToken stop)
    {
        service.Published += Broadcast;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                // With no buffer, a write blocks until the client reads it, so a client that only writes would stall.
                var pipe = new NamedPipeServerStream(Protocol.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, BufferSize, BufferSize);
                try
                {
                    await pipe.WaitForConnectionAsync(stop);
                }
                catch (OperationCanceledException)
                {
                    await pipe.DisposeAsync();
                    break;
                }
                _ = ServeAsync(new Client(pipe), stop);
            }
        }
        finally
        {
            service.Published -= Broadcast;
        }
    }

    private async Task ServeAsync(Client client, CancellationToken stop)
    {
        _clients[client] = 0;
        Log.Write($"Client connected ({_clients.Count} open)");
        try
        {
            _ = client.SendAsync(new JsonObject { ["event"] = "state", ["data"] = ToNode(service.State) });
            using var reader = new StreamReader(client.Pipe, Encoding.UTF8, leaveOpen: true);
            while (!stop.IsCancellationRequested && await reader.ReadLineAsync(stop) is { } line)
            {
                if (line.Length > 0)
                    _ = RespondAsync(client, line);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            _clients.TryRemove(client, out _);
            // An app that closed or crashed mid-recording mustn't leave the keyboard captured.
            if (_clients.IsEmpty)
                service.StopKeyCapture();
            await client.Pipe.DisposeAsync();
            Log.Write($"Client disconnected ({_clients.Count} open)");
        }
    }

    private async Task RespondAsync(Client client, string line)
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
            reply["result"] = ToNode(await service.HandleAsync(method, parameters));
        }
        catch (Exception error)
        {
            reply["error"] = error.Message;
            Log.Write($"{method} {args} failed after {clock.ElapsedMilliseconds} ms: {error.Message}");
        }
        reply["id"] = id;
        await client.SendAsync(reply);
    }

    private void Broadcast(string name, object data)
    {
        var message = new JsonObject { ["event"] = name, ["data"] = ToNode(data) };
        foreach (var client in _clients.Keys)
            _ = client.SendAsync(message.DeepClone());
    }

    private static JsonNode? ToNode(object? value) =>
        value is null ? null : JsonSerializer.SerializeToNode(value, Protocol.Json.GetTypeInfo(value.GetType()));

    private sealed class Client(NamedPipeServerStream pipe)
    {
        private readonly SemaphoreSlim _write = new(1, 1);
        public NamedPipeServerStream Pipe { get; } = pipe;

        public async Task SendAsync(JsonNode message)
        {
            var bytes = Encoding.UTF8.GetBytes(message.ToJsonString(Protocol.Json) + "\n");
            await _write.WaitAsync();
            try
            {
                await Pipe.WriteAsync(bytes);
                await Pipe.FlushAsync();
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
            }
            finally
            {
                _write.Release();
            }
        }
    }
}
