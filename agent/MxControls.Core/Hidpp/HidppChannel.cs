namespace MxControls.Hidpp;

/// <summary>A notification the device sent on its own (software ID 0), such as a diverted button event.</summary>
public readonly record struct HidppEvent(byte FeatureIndex, byte Function, byte[] Payload);

public sealed class HidppException(byte featureIndex, byte function, byte code)
    : Exception($"HID++ error 0x{code:X2} from feature {featureIndex} function {function}")
{
    public byte Code { get; } = code;
}

/// <summary>
/// HID++ 2.0 long reports over one vendor collection. Requests are serialized; replies are matched by
/// feature index, function and software ID, and everything else is raised as an event.
/// </summary>
public sealed class HidppChannel : IAsyncDisposable
{
    public const byte LongReportId = 0x11;
    public const int LongReportLength = 20;
    public const byte DirectDevice = 0xFF;
    private const byte ErrorFeature = 0xFF;
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2);

    private readonly Stream _stream;
    private readonly byte _deviceIndex;
    private readonly byte _softwareId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    private TaskCompletionSource<byte[]>? _pending;
    private byte _pendingFeature;
    private byte _pendingFunction;

    public event Action<HidppEvent>? EventReceived;
    public event Action<Exception>? Disconnected;

    /// <param name="stream">The HID++ collection, from <see cref="Platform.IHidBackend.Open"/>. The channel owns it.</param>
    public HidppChannel(Stream stream, byte deviceIndex = DirectDevice, byte softwareId = 0x0A)
    {
        if (softwareId is 0 or > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(softwareId), "Software IDs are 1 to 15; 0 marks device events.");
        _stream = stream;
        _deviceIndex = deviceIndex;
        _softwareId = softwareId;
        _reader = Task.Run(ReadLoop);
    }

    public async Task<byte[]> CallAsync(byte featureIndex, byte function, ReadOnlyMemory<byte> args, CancellationToken cancel = default)
    {
        if (args.Length > LongReportLength - 4)
            throw new ArgumentException("Too many parameters for a long report", nameof(args));
        await _gate.WaitAsync(cancel);
        try
        {
            var reply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingFeature = featureIndex;
            _pendingFunction = (byte)((function << 4) | _softwareId);
            _pending = reply;
            await _stream.WriteAsync(Report(featureIndex, _pendingFunction, args), cancel);
            return await reply.Task.WaitAsync(ReplyTimeout, cancel);
        }
        finally
        {
            _pending = null;
            _gate.Release();
        }
    }

    /// <summary>Sends a request without waiting for its reply, for one that may never come, such as a host switch.</summary>
    public async Task SendAsync(byte featureIndex, byte function, ReadOnlyMemory<byte> args, CancellationToken cancel = default)
    {
        if (args.Length > LongReportLength - 4)
            throw new ArgumentException("Too many parameters for a long report", nameof(args));
        await _gate.WaitAsync(cancel);
        try
        {
            await _stream.WriteAsync(Report(featureIndex, (byte)((function << 4) | _softwareId), args), cancel);
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[] Report(byte featureIndex, byte function, ReadOnlyMemory<byte> args)
    {
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = _deviceIndex;
        report[2] = featureIndex;
        report[3] = function;
        args.CopyTo(report.AsMemory(4));
        return report;
    }

    private async Task ReadLoop()
    {
        var buffer = new byte[64];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(buffer, _stop.Token);
                if (read < 4 || buffer[0] != LongReportId || buffer[1] != _deviceIndex)
                    continue;
                Dispatch(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _pending?.TrySetException(error);
            Disconnected?.Invoke(error);
        }
    }

    private void Dispatch(ReadOnlySpan<byte> report)
    {
        byte feature = report[2], function = report[3];
        var pending = _pending;
        if (pending is not null)
        {
            if (feature == _pendingFeature && function == _pendingFunction)
            {
                pending.TrySetResult(report[4..].ToArray());
                return;
            }
            if (feature == ErrorFeature && function == _pendingFeature && report[4] == _pendingFunction)
            {
                pending.TrySetException(new HidppException(_pendingFeature, (byte)(_pendingFunction >> 4), report[5]));
                return;
            }
        }
        // Replies to other software (Options+ shares this collection) carry their own software IDs.
        if ((function & 0x0F) == 0 && feature != ErrorFeature)
            EventReceived?.Invoke(new HidppEvent(feature, (byte)(function >> 4), report[4..].ToArray()));
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _stream.DisposeAsync();
        try { await _reader; } catch { }
        _stop.Dispose();
        _gate.Dispose();
    }
}
