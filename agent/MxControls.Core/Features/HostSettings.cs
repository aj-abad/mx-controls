using System.Text;
using MxControls.Hidpp;

namespace MxControls.Features;

/// <param name="Index">The channel, from 0; the mouse labels it 1 to 3.</param>
/// <param name="Name">The computer's name as it told the mouse, or empty when unknown.</param>
public sealed record HostChannel(int Index, bool Paired, string Name);

/// <param name="Current">The channel this computer is on.</param>
/// <param name="CanRename">Whether the current channel's name can be written, and up to how many bytes.</param>
public sealed record HostsInfo(int Current, bool CanRename, int MaxNameLength, IReadOnlyList<HostChannel> Channels);

/// <summary>
/// CHANGE_HOST (0x1814) and HOSTS_INFO (0x1815), the Easy-Switch channels. Function layouts follow Solaar.
/// Switching sends the mouse to another computer, so this one loses it until it switches back.
/// </summary>
public sealed class HostSettings(MxDevice device)
{
    private const int NameChunk = 14;

    public bool Supported => device.Has(FeatureIds.ChangeHost);

    public async Task<HostsInfo> GetAsync()
    {
        var change = await device.CallAsync(FeatureIds.ChangeHost, 0);
        int count = change[0], current = change[1];
        if (!device.Has(FeatureIds.HostsInfo))
            return new HostsInfo(current, false, 0, Enumerable.Range(0, count).Select(i => new HostChannel(i, i == current, "")).ToArray());

        var info = await device.CallAsync(FeatureIds.HostsInfo, 0);
        bool canRead = (info[0] & 0x01) != 0, canWrite = (info[0] & 0x02) != 0;
        var channels = new List<HostChannel>(count);
        int maxName = 0;
        for (int host = 0; host < count; host++)
        {
            var r = await device.CallAsync(FeatureIds.HostsInfo, 1, (byte)host);
            bool paired = r[1] != 0;
            int nameLength = r[4];
            if (host == current)
                maxName = r[5];
            channels.Add(new HostChannel(host, paired, canRead && paired ? await ReadNameAsync(host, nameLength) : ""));
        }
        return new HostsInfo(current, canWrite && maxName > 0, maxName, channels);
    }

    private async Task<string> ReadNameAsync(int host, int length)
    {
        var name = new List<byte>(length);
        while (name.Count < length)
        {
            var chunk = await device.CallAsync(FeatureIds.HostsInfo, 3, (byte)host, (byte)name.Count);
            name.AddRange(chunk.AsSpan(2, Math.Min(NameChunk, length - name.Count)).ToArray());
        }
        return Encoding.UTF8.GetString(name.ToArray()).TrimEnd('\0');
    }

    /// <summary>Names the current channel, cut to what the mouse keeps.</summary>
    public async Task RenameCurrentAsync(string name)
    {
        var hosts = await GetAsync();
        if (!hosts.CanRename)
            throw new NotSupportedException("This mouse doesn't let computers rename their channel.");
        var bytes = Truncate(Encoding.UTF8.GetBytes(name), hosts.MaxNameLength);
        for (int offset = 0; offset < bytes.Length; offset += NameChunk)
        {
            var chunk = bytes.AsSpan(offset, Math.Min(NameChunk, bytes.Length - offset));
            await device.CallAsync(FeatureIds.HostsInfo, 4, [(byte)hosts.Current, (byte)offset, .. chunk]);
        }
    }

    /// <summary>Sends the mouse to another channel. It answers from there, if at all, so no reply is awaited.</summary>
    public Task SwitchAsync(int host) => device.SendAsync(FeatureIds.ChangeHost, 1, (byte)host);

    // Cutting mid-character would leave the mouse a name no computer can read.
    private static byte[] Truncate(byte[] utf8, int max)
    {
        if (utf8.Length <= max)
            return utf8;
        int end = max;
        while (end > 0 && (utf8[end] & 0xC0) == 0x80)
            end--;
        return utf8[..end];
    }
}
