using System.IO.Pipes;
using MxControls.Agent;

namespace MxControls.Tests;

public class AgentClientTests
{
    private static readonly TimeSpan Reply = TimeSpan.FromSeconds(5);

    private static string UniquePipe() => $"mx-controls-test-{Guid.NewGuid():N}";

    // Buffered like PipeServer's: without a buffer, a write blocks until the other side reads, and these would deadlock.
    private static NamedPipeServerStream Server(string name, int bufferSize = 64 * 1024) =>
        new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, bufferSize, bufferSize);

    /// <summary>Plays the agent's side of one connection: pushes <paramref name="before"/>, reads the request, sends <paramref name="after"/>.</summary>
    private static async Task<string?> ServeOnceAsync(string pipeName, string[] before, string[] after)
    {
        await using var pipe = Server(pipeName);
        await pipe.WaitForConnectionAsync();
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        foreach (var line in before)
            await writer.WriteLineAsync(line);
        var request = await reader.ReadLineAsync();
        foreach (var line in after)
            await writer.WriteLineAsync(line);
        return request;
    }

    [Fact]
    public async Task SendsOneRequestAndSkipsEventsUntilTheReply()
    {
        var name = UniquePipe();
        var server = ServeOnceAsync(name,
            before: ["""{"event":"state","data":{"profiles":[{"id":"default"}]}}"""],
            after: ["""{"event":"button","data":{"cid":1}}""", """{"result":null,"id":1}"""]);

        var error = await AgentClient.CallAsync("playHaptic", """{"waveform":"Knock"}""", 2000, Reply, name);

        Assert.Null(error);
        Assert.Equal("""{"id":1,"method":"playHaptic","params":{"waveform":"Knock"}}""", await server);
    }

    [Fact]
    public async Task ReturnsTheAgentsError()
    {
        var name = UniquePipe();
        var server = ServeOnceAsync(name, before: [], after: ["""{"error":"The mouse isn't connected.","id":1}"""]);

        var error = await AgentClient.CallAsync("playHaptic", "{}", 2000, Reply, name);

        Assert.Equal("The mouse isn't connected.", error);
        await server;
    }

    [Fact]
    public async Task FailsWhenNoAgentIsListening() =>
        await Assert.ThrowsAsync<TimeoutException>(() => AgentClient.CallAsync("playHaptic", "{}", 50, Reply, UniquePipe()));

    [Fact]
    public async Task FailsWhenTheConnectionClosesBeforeAReply()
    {
        var name = UniquePipe();
        var server = ServeOnceAsync(name, before: [], after: []);

        await Assert.ThrowsAsync<IOException>(() => AgentClient.CallAsync("playHaptic", "{}", 2000, Reply, name));
        await server;
    }

    [Fact(Timeout = 10_000)]
    public async Task GivesUpWhenTheAgentStopsReading()
    {
        var name = UniquePipe();
        await using var pipe = Server(name, bufferSize: 0);
        var connected = pipe.WaitForConnectionAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AgentClient.CallAsync("playHaptic", "{}", 2000, TimeSpan.FromMilliseconds(100), name));
        await connected;
    }

    [Fact(Timeout = 10_000)]
    public async Task GivesUpWhenTheAgentNeverReplies()
    {
        var name = UniquePipe();
        await using var pipe = Server(name);
        var connected = pipe.WaitForConnectionAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AgentClient.CallAsync("playHaptic", "{}", 2000, TimeSpan.FromMilliseconds(100), name));
        await connected;
    }

    [Theory]
    [InlineData("Completed", "Completed")]
    [InlineData("completed", "Completed")]
    [InlineData("KNOCK", "Knock")]
    [InlineData("WhisperCollision", "WhisperCollision")]
    public void FindsAWaveformIgnoringCase(string input, string expected) =>
        Assert.Equal(expected, HapticWaveforms.Find(input));

    [Theory]
    [InlineData("")]
    [InlineData("Buzz")]
    [InlineData("5")]
    [InlineData("\"},\"x\":{\"")]
    public void RejectsAnythingElse(string input) => Assert.Null(HapticWaveforms.Find(input));
}
