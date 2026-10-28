using MxControls;
using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Input;
using MxControls.Remap;

const string Usage = """
    Usage:
      mx info                                  Feature table, firmware, buttons and settings (read-only)
      mx listen [seconds] [--all]              Log diverted button presses; --all diverts every button meanwhile
      mx haptic <waveform>                     Play a haptic waveform, such as Completed or Knock
      mx hold <button>=<key> ... [--seconds N] Hold keyboard keys while mouse buttons are held

    Buttons: middle, back, forward, gesture, smartshift, haptic
    Keys: A-Z, 0-9, F1-F24, Shift, Ctrl, Alt, Win, Space, arrows, or any KeyboardEvent.code such as ShiftRight
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return 0;
}

try
{
    var platform = Platforms.Current();
    await using var mx = await MxDevice.OpenAsync(platform.Hid);
    if (Environment.GetEnvironmentVariable("MX_TRACE") is "1")
        mx.Channel.EventReceived += e => Console.WriteLine($"  trace: feature {e.FeatureIndex} event {e.Function} {Convert.ToHexString(e.Payload)}");
    return args[0] switch
    {
        "info" => await Info(mx),
        "listen" => await Listen(mx, args.Skip(1).Where(a => a != "--all").Select(double.Parse).FirstOrDefault(30), args.Contains("--all")),
        "haptic" when args.Length == 2 => await Haptic(mx, args[1]),
        "hold" when args.Length > 1 => await Hold(mx, platform, args[1..]),
        _ => Fail(Usage),
    };
}
catch (Exception error) when (error is InvalidOperationException or NotSupportedException or HidppException or IOException or TimeoutException)
{
    return Fail(error.Message);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static async Task<int> Info(MxDevice mx)
{
    Console.WriteLine($"{await mx.Info.GetNameAsync()}  (PID {mx.Interface.ProductId:X4}, Bluetooth)");
    foreach (var fw in await mx.Info.GetFirmwareAsync())
        Console.WriteLine($"  {fw.TypeName,-12} {fw}");
    var battery = await mx.Info.GetBatteryAsync();
    Console.WriteLine($"  Battery      {battery.Percent}%{(battery.Charging ? ", charging" : "")}");

    Console.WriteLine($"\n{mx.Features.Count} features (ONBOARD_PROFILES {(mx.Has(FeatureIds.OnboardProfiles) ? "present" : "absent")}):");
    foreach (var f in mx.Features)
        Console.WriteLine($"  {f.Index,2}  {f.Id:X4}  {f.Name,-24} v{f.Version}{(f.Hidden ? "  hidden" : "")}{(f.Engineering ? " engineering" : "")}");

    Console.WriteLine("\nButtons:");
    foreach (var c in await mx.Controls.GetControlsAsync())
    {
        var state = c.Reporting.Diverted ? (c.Reporting.RawXY ? "diverted + raw XY" : "diverted") : "native";
        Console.WriteLine($"  {c.Cid:X4}  {c.Name,-16} {state,-18} {c.Flags}");
    }

    Console.WriteLine("\nSettings:");
    var (dpi, defaultDpi) = await mx.Pointer.GetDpiAsync();
    var range = await mx.Pointer.GetDpiRangeAsync();
    Console.WriteLine($"  DPI          {dpi} (default {defaultDpi}{(range is null ? "" : $", {range.Min}-{range.Max} in steps of {range.Step}")})");
    var shift = await mx.Wheels.GetSmartShiftAsync();
    Console.WriteLine($"  Smart Shift  {(shift.Ratchet ? "ratchet" : "free spin")}, free spin above speed {shift.AutoDisengage}, torque {shift.Torque}");
    var wheel = await mx.Wheels.GetHiResWheelAsync();
    Console.WriteLine($"  Wheel        {(wheel.HighResolution ? $"high resolution (x{wheel.Multiplier})" : "standard resolution")}"
        + $"{(wheel.Inverted ? ", inverted" : "")}{(wheel.Diverted ? ", diverted" : "")}");
    var thumb = await mx.Wheels.GetThumbWheelAsync();
    Console.WriteLine($"  Thumb wheel  {(thumb.Diverted ? "diverted" : "native")}{(thumb.Inverted ? ", inverted" : "")}"
        + $" ({thumb.NativeResolution} native / {thumb.DivertedResolution} diverted counts per turn)");
    var haptic = await mx.Haptics.GetLevelAsync();
    Console.WriteLine($"  Haptics      {(haptic.Enabled ? $"level {haptic.Level}" : "off")}, {(await mx.Haptics.GetWaveformsAsync()).Count} waveforms");
    foreach (var b in await mx.Haptics.GetForceButtonsAsync())
        Console.WriteLine($"  Force button {b.Number}: {b.Current} (default {b.Default}, {b.Min}-{b.Max}{(b.Changeable ? "" : ", fixed")})");
    return 0;
}

static async Task<int> Listen(MxDevice mx, double seconds, bool divertAll)
{
    // With --all, every divertable button is diverted for the session so each press can be identified.
    var restore = new Dictionary<ushort, ControlReporting>();
    if (divertAll)
    {
        foreach (var c in (await mx.Controls.GetControlsAsync()).Where(c => c.CanDivert && !c.Flags.HasFlag(ControlFlags.Virtual) && !c.Reporting.Diverted))
        {
            restore[c.Cid] = c.Reporting;
            await mx.Controls.SetDivertAsync(c.Cid, divert: true, rawXY: c.Reporting.RawXY);
        }
    }
    try
    {
        await LogPresses(mx, seconds, divertAll);
    }
    finally
    {
        foreach (var (cid, original) in restore)
            await mx.Controls.SetDivertAsync(cid, original.Diverted, original.RawXY);
    }
    return 0;
}

static async Task LogPresses(MxDevice mx, double seconds, bool divertAll)
{
    var gate = new object();
    var held = new HashSet<ushort>();
    var since = new Dictionary<ushort, TimeSpan>();
    var clock = System.Diagnostics.Stopwatch.StartNew();
    mx.Channel.EventReceived += e =>
    {
        if (mx.Controls.ParseHeld(e) is not { } now)
            return;
        lock (gate)
        {
            foreach (var cid in now.Except(held))
            {
                since[cid] = clock.Elapsed;
                Console.WriteLine($"{clock.Elapsed.TotalSeconds,8:F3}s  down  {ReprogControls.NameOf(cid)} ({cid:X4})");
            }
            foreach (var cid in held.Except(now))
                Console.WriteLine($"{clock.Elapsed.TotalSeconds,8:F3}s  up    {ReprogControls.NameOf(cid)} ({cid:X4})  held {(clock.Elapsed - since[cid]).TotalSeconds:F3}s");
            held = [.. now];
        }
    };
    Console.WriteLine(divertAll
        ? $"Listening for {seconds:F0}s. Every button except Left and Right is diverted and restored afterwards."
        : $"Listening for {seconds:F0}s. Only buttons another app has diverted are reported.");
    await Task.Delay(TimeSpan.FromSeconds(seconds));
}

static async Task<int> Haptic(MxDevice mx, string name)
{
    if (!Enum.TryParse<HapticWaveform>(name, ignoreCase: true, out var waveform))
        return Fail($"Unknown waveform. Choose one of: {string.Join(", ", await mx.Haptics.GetWaveformsAsync())}");
    await mx.Haptics.PlayAsync(waveform);
    Console.WriteLine($"Played {waveform}");
    return 0;
}

static async Task<int> Hold(MxDevice mx, MxControls.Platform.IPlatform platform, string[] args)
{
    double seconds = 60;
    var keys = new Dictionary<ushort, string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--seconds" && i + 1 < args.Length)
        {
            seconds = double.Parse(args[++i]);
            continue;
        }
        var parts = args[i].Split('=', 2);
        if (parts.Length != 2 || !ReprogControls.KnownButtons.TryGetValue(parts[0], out var cid))
            return Fail($"Expected <button>=<key>, such as back=Shift; got '{args[i]}'");
        var code = KeyNames.Normalize(parts[1]);
        if (!platform.Keys.Supports(code))
            return Fail($"Unknown key '{parts[1]}'");
        keys[cid] = code;
    }
    if (keys.Count == 0)
        return Fail("Map at least one button, such as back=Shift");

    var clock = System.Diagnostics.Stopwatch.StartNew();
    await using var remapper = await HoldRemapper.StartAsync(mx, platform.Keys, keys);
    remapper.Changed += (cid, code, down) =>
        Console.WriteLine($"{clock.Elapsed.TotalSeconds,8:F3}s  {(down ? "down" : "up  ")}  {ReprogControls.NameOf(cid)} -> {code}");
    remapper.Failed += error => Console.Error.WriteLine($"Key injection failed: {error.Message}");
    foreach (var cid in remapper.SharedControls)
        Console.WriteLine($"Note: {ReprogControls.NameOf(cid)} was already diverted, probably by Logi Options+, which will also act on it.");

    var done = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
    mx.Channel.Disconnected += _ => done.TrySetResult();
    Console.WriteLine($"Holding for {seconds:F0}s; Ctrl+C stops early and restores the buttons.");
    await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
    return 0;
}
