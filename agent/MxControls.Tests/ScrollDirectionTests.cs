using System.Text.Json;
using System.Threading.Channels;
using MxControls.Agent;
using MxControls.Hidpp;
using MxControls.Platform;

namespace MxControls.Tests;

public class ScrollDirectionTests
{
    private const byte HighResolution = 0x02;
    private const byte Inverted = 0x04;

    [Fact]
    public async Task Reversing_keeps_the_wheels_other_modes()
    {
        var mouse = new FakeMouse(mode: HighResolution);
        await using var mx = await MxDevice.OpenAsync(mouse, FakeMouse.Device);

        Assert.True(await mx.Wheels.SetHiResInvertedAsync(true));
        Assert.Equal(HighResolution | Inverted, mouse.Mode);

        Assert.True(await mx.Wheels.SetHiResInvertedAsync(false));
        Assert.Equal(HighResolution, mouse.Mode);
    }

    [Fact]
    public async Task A_wheel_already_set_is_not_written_again()
    {
        var mouse = new FakeMouse(mode: Inverted);
        await using var mx = await MxDevice.OpenAsync(mouse, FakeMouse.Device);

        Assert.False(await mx.Wheels.SetHiResInvertedAsync(true));
        Assert.Equal(0, mouse.ModeWrites);
    }

    [Fact]
    public async Task The_wheel_can_be_set_before_the_feature_table_is_read()
    {
        var mouse = new FakeMouse(mode: HighResolution);
        int tableReadsBefore = -1;

        await using var mx = await MxDevice.OpenAsync(mouse, FakeMouse.Device, first: async device =>
        {
            Assert.True(await device.FindFeatureAsync(FeatureIds.HiResWheel));
            await device.Wheels.SetHiResInvertedAsync(true);
            tableReadsBefore = mouse.TableReads;
        });

        Assert.Equal(0, tableReadsBefore);
        Assert.True(mouse.TableReads > 0);
        Assert.Equal(HighResolution | Inverted, mouse.Mode);
    }

    [Fact]
    public async Task A_feature_the_mouse_lacks_is_not_found()
    {
        await using var mx = await MxDevice.OpenAsync(new FakeMouse(mode: 0), FakeMouse.Device);
        Assert.False(await mx.FindFeatureAsync(FeatureIds.ThumbWheel));
    }

    [Fact]
    public void The_direction_stays_unset_until_chosen_and_survives_a_save()
    {
        Assert.Null(AgentSettings.Empty.ScrollInverted);
        var json = JsonSerializer.Serialize(new AgentSettings(ScrollInverted: true), Protocol.Json);
        Assert.True(JsonSerializer.Deserialize<AgentSettings>(json, Protocol.Json)!.ScrollInverted);
    }

    /// <summary>Answers HID++ calls for the feature table and the main wheel's mode.</summary>
    private sealed class FakeMouse(byte mode) : Stream, IHidBackend
    {
        private const byte FeatureSetIndex = 1;
        private const byte HiResIndex = 2;

        public static readonly HidDeviceInfo Device = new("fake", MxDevice.LogitechVendorId, 0xB042, 0xFF43, 0x0202, "Fake");

        private readonly Channel<byte[]> _replies = Channel.CreateUnbounded<byte[]>();

        public byte Mode { get; private set; } = mode;
        public int ModeWrites { get; private set; }
        public int TableReads { get; private set; }

        public IReadOnlyList<HidDeviceInfo> Enumerate(ushort vendorId) => [Device];
        public Stream Open(HidDeviceInfo device) => this;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            var reply = await _replies.Reader.ReadAsync(cancel);
            reply.CopyTo(buffer);
            return reply.Length;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
        {
            var report = buffer.ToArray();
            var reply = (byte[])report.Clone();
            Array.Clear(reply, 4, reply.Length - 4);
            Answer(report[2], report[3] >> 4, report[4], report[5], reply);
            _replies.Writer.TryWrite(reply);
            return ValueTask.CompletedTask;
        }

        private void Answer(byte feature, int function, byte argument, byte argument2, byte[] reply)
        {
            switch (feature, function)
            {
                case (0, 0):
                    int wanted = (argument << 8) | argument2;
                    reply[4] = wanted == FeatureIds.FeatureSet ? FeatureSetIndex : wanted == FeatureIds.HiResWheel ? HiResIndex : (byte)0;
                    break;
                case (FeatureSetIndex, 0):
                    TableReads++;
                    reply[4] = HiResIndex;
                    break;
                case (FeatureSetIndex, 1) when argument == HiResIndex:
                    reply[4] = FeatureIds.HiResWheel >> 8;
                    reply[5] = FeatureIds.HiResWheel & 0xFF;
                    break;
                case (HiResIndex, 1):
                    reply[4] = Mode;
                    break;
                case (HiResIndex, 2):
                    Mode = argument;
                    ModeWrites++;
                    break;
            }
        }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
