using MxControls;
using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Input;
using MxControls.Actions;

const string Usage = """
    Usage:
      mx info                                  Feature table, firmware, buttons and settings (read-only)
      mx listen [seconds] [--all]              Log diverted button presses; --all diverts every button meanwhile
      mx gestures [seconds] [--repeat] [--trace <file>]
                                               Log gesture button swipes and what they'd fire; --repeat as if every
                                               direction repeats, --trace saves them as JSON lines
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
        "gestures" => await Gestures(mx, args[1..]),
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
    if (mx.Hosts.Supported)
    {
        var hosts = await mx.Hosts.GetAsync();
        if (mx.Has(FeatureIds.HostsInfo))
        {
            // Byte 0: 0x01 read names, 0x02 write names, 0x04 move a channel, 0x08 unpair one, 0x10 set the OS.
            var caps = await mx.CallAsync(FeatureIds.HostsInfo, 0);
            string[] names = ["read names", "rename", "move", "unpair", "set OS"];
            var can = names.Where((_, bit) => (caps[0] & 1 << bit) != 0);
            Console.WriteLine($"  Easy-Switch  capabilities {caps[0]:X2}{caps[1]:X2}: {string.Join(", ", can)}");
        }
        foreach (var channel in hosts.Channels)
            Console.WriteLine($"  Channel {channel.Index + 1}    {(channel.Paired ? channel.Name is "" ? "paired" : channel.Name : "empty")}"
                + (channel.Index == hosts.Current ? " (this computer)" : ""));
    }
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

static async Task<int> Gestures(MxDevice mx, string[] args)
{
    double seconds = 60;
    string? tracePath = null;
    bool repeat = false;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--trace" && i + 1 < args.Length)
            tracePath = args[++i];
        else if (args[i] == "--repeat")
            repeat = true;
        else if (double.TryParse(args[i], out var s))
            seconds = s;
        else
            return Fail(Usage);
    }

    // The trace is JSON whatever the locale's decimal separator.
    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
    ushort gesture = ReprogControls.KnownButtons["gesture"];
    int dpi = (await mx.Pointer.GetDpiAsync()).Current;
    var tracker = new GestureTracker { Dpi = dpi };
    await using var trace = tracePath is null ? null : new StreamWriter(tracePath);
    trace?.WriteLine($$"""{"dpi":{{dpi}},"thresholdMm":{{GestureTracker.DefaultThresholdMm}}}""");

    var gate = new object();
    var clock = System.Diagnostics.Stopwatch.StartNew();
    bool held = false;
    TimeSpan pressedAt = default, lastReport = default;
    void OnEvent(MxControls.Hidpp.HidppEvent e)
    {
        lock (gate)
        {
            var t = clock.Elapsed;
            if (mx.Controls.ParseHeld(e) is { } now && now.Contains(gesture) != held)
            {
                held = !held;
                trace?.WriteLine($$"""{"t":{{t.TotalMilliseconds:F1}},"held":{{(held ? "true" : "false")}}}""");
                if (held)
                {
                    tracker.Reset(_ => repeat);
                    pressedAt = lastReport = t;
                    Console.WriteLine($"{t.TotalSeconds,8:F3}s  down");
                    return;
                }
                string result = tracker.Direction is { } d ? $"{d} fired last" : tracker.IsClick ? "click" : "nothing (diagonal)";
                Console.WriteLine($"{t.TotalSeconds,8:F3}s  up    after {(t - pressedAt).TotalMilliseconds:F0} ms, {tracker.TravelMm:F1} mm: {result}");
            }
            else if (held && mx.Controls.ParseRawXY(e) is { } move)
            {
                trace?.WriteLine($$"""{"t":{{t.TotalMilliseconds:F1}},"dx":{{move.Dx}},"dy":{{move.Dy}}}""");
                var fired = tracker.Add(move.Dx, move.Dy, (long)t.TotalMilliseconds);
                Console.WriteLine($"{t.TotalSeconds,8:F3}s    {move.Dx,5} {move.Dy,5}  +{(t - lastReport).TotalMilliseconds,4:F0} ms  {tracker.TravelMm,5:F1} mm"
                    + (fired is { } direction ? $"  -> {direction} after {(t - pressedAt).TotalMilliseconds:F0} ms" : ""));
                lastReport = t;
            }
        }
    }

    mx.Channel.EventReceived += OnEvent;
    var original = await mx.Controls.GetReportingAsync(gesture);
    await mx.Controls.SetDivertAsync(gesture, divert: true, rawXY: true);
    try
    {
        var done = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
        Console.WriteLine($"Swipe with the gesture button held for {seconds:F0}s at {dpi} DPI; nothing fires. Ctrl+C stops early.");
        await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
    }
    finally
    {
        await mx.Controls.SetDivertAsync(gesture, original.Diverted, original.RawXY);
        mx.Channel.EventReceived -= OnEvent;
    }
    if (tracePath is not null)
        Console.WriteLine($"Saved the trace to {Path.GetFullPath(tracePath)}");
    return 0;
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
    var bindings = new List<ButtonBinding>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--seconds" && i + 1 < args.Length)
        {
            seconds = double.Parse(args[++i]);
            continue;
        }
        var parts = args[i].Split('=', 2);
        if (parts.Length != 2 || !ReprogControls.KnownButtons.ContainsKey(parts[0]))
            return Fail($"Expected <button>=<key>, such as back=Shift; got '{args[i]}'");
        var code = KeyNames.Normalize(parts[1]);
        if (!platform.Input.Supports(code))
            return Fail($"Unknown key '{parts[1]}'");
        bindings.Add(ButtonBinding.Hold(parts[0].ToLowerInvariant(), code));
    }
    if (bindings.Count == 0)
        return Fail("Map at least one button, such as back=Shift");

    var clock = System.Diagnostics.Stopwatch.StartNew();
    await using var engine = await ButtonEngine.StartAsync(mx, platform, bindings);
    engine.Failed += error => Console.Error.WriteLine($"Key injection failed: {error.Message}");
    var held = new HashSet<ushort>();
    mx.Channel.EventReceived += e =>
    {
        if (mx.Controls.ParseHeld(e) is not { } now)
            return;
        foreach (var cid in now.Except(held))
            Console.WriteLine($"{clock.Elapsed.TotalSeconds,8:F3}s  down  {ReprogControls.NameOf(cid)}");
        foreach (var cid in held.Except(now))
            Console.WriteLine($"{clock.Elapsed.TotalSeconds,8:F3}s  up    {ReprogControls.NameOf(cid)}");
        held = [.. now];
    };
    foreach (var cid in bindings.Select(b => ReprogControls.KnownButtons[b.Button]).Where(cid => engine.DivertedBefore(cid) == true))
        Console.WriteLine($"Note: {ReprogControls.NameOf(cid)} was already diverted, probably by Logi Options+, which will also act on it.");

    var done = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
    mx.Channel.Disconnected += _ => done.TrySetResult();
    Console.WriteLine($"Holding for {seconds:F0}s; Ctrl+C stops early and restores the buttons.");
    await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
    return 0;
}
