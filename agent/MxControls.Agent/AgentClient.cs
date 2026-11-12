using System.IO.Pipes;
using System.Text.Json;

namespace MxControls.Agent;

/// <summary>A one-shot client of a running agent, for the command-line switches that ask it to do something.</summary>
public static class AgentClient
{
    /// <returns>The agent's error message, or null if it did what was asked.</returns>
    /// <exception cref="TimeoutException">No agent is listening.</exception>
    /// <exception cref="IOException">The connection dropped before the agent replied.</exception>
    /// <exception cref="OperationCanceledException">The agent didn't reply in time.</exception>
    public static async Task<string?> CallAsync(
        string method, string paramsJson, int connectTimeoutMs, TimeSpan replyTimeout, string pipeName = Protocol.PipeName)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(connectTimeoutMs);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        // Covers the write too: a stuck agent that has stopped reading would otherwise block it for good.
        using var timeout = new CancellationTokenSource(replyTimeout);
        var request = $$"""{"id":1,"method":"{{method}}","params":{{paramsJson}}}""";
        await writer.WriteLineAsync(request.AsMemory(), timeout.Token);

        // The agent sends its state first and can push events at any time, so read until the reply.
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            using var message = JsonDocument.Parse(line, Protocol.Document);
            if (message.RootElement.TryGetProperty("id", out _))
                return message.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
        }
        throw new IOException("The agent closed the connection without replying.");
    }
}
