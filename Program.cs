using System.Globalization;
using DeepCoolDisplay;

// Usage:
//   DeepCoolDisplay.exe                    run the display loop
//   DeepCoolDisplay.exe --list             show detected devices and sensor readings, then exit
//   --ch-mode temp|usage|auto              CH case display (default temp; auto alternates every 5 s)
//   --smooth <seconds>                     averaging time (default 5; 0 = raw value every second)
//   --deadband-temp <°C>                   minimum change before the shown temperature moves (default 1)
//   --deadband-usage <%>                   same for usage (default 3)
//   --deadband-power <W>                   same for CPU power (default 3)
var listOnly = args.Contains("--list");
var chMode = Option("--ch-mode") switch { "usage" => ChMode.Usage, "auto" => ChMode.Auto, _ => ChMode.Temperature };
var defaults = new Smoothing();
var smoothing = new Smoothing(
    Number("--smooth", defaults.Seconds),
    Number("--deadband-temp", defaults.DeadbandTemp),
    Number("--deadband-usage", defaults.DeadbandUsage),
    Number("--deadband-power", defaults.DeadbandPower));

using var sensors = new Sensors(smoothing);

if (listOnly)
{
    Console.WriteLine("DeepCool HID devices:");
    foreach (var d in DeepCoolDevice.Find())
        Console.WriteLine($"    PID {d.ProductID} ({Describe(d.ProductID)}), output report {d.GetMaxOutputReportLength()} bytes");
    Console.WriteLine();
    sensors.Update();
    foreach (var line in sensors.Describe())
        Console.WriteLine(line);
    Console.WriteLine();
    Console.WriteLine($"Used: CPU {sensors.CpuTemperature:0.0} °C {sensors.CpuPower:0.0} W {sensors.CpuUsage:0}% | GPU {sensors.GpuTemperature:0.0} °C {sensors.GpuUsage:0}%");
    return 0;
}

// The installed copy runs as SYSTEM; a second copy fails to open its mutex (access denied) or finds it taken.
Mutex? mutex = null;
var firstInstance = false;
try { mutex = new Mutex(true, @"Global\DeepCoolDisplay", out firstInstance); }
catch (UnauthorizedAccessException) { }
if (!firstInstance)
{
    Console.Error.WriteLine("DeepCoolDisplay is already running.");
    return 1;
}

var displays = new Dictionary<string, IDisplay>();
var nextScan = DateTime.MinValue;
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

while (!stop.IsCancellationRequested)
{
    // Pick up displays that appear later (boot, USB reconnect, resume from sleep).
    if (DateTime.UtcNow >= nextScan)
    {
        foreach (var hid in DeepCoolDevice.Find())
        {
            if (displays.ContainsKey(hid.DevicePath) || Describe(hid.ProductID) is null) continue;
            try
            {
                var device = new DeepCoolDevice(hid);
                IDisplay display = hid.ProductID == 10 ? new LdSeriesDisplay(device) : new ChSeriesDisplay(device, chMode);
                display.Init();
                displays[hid.DevicePath] = display;
                Log.Info($"Connected {display.Name} (PID {hid.ProductID})");
            }
            catch (Exception ex)
            {
                Log.Error($"Could not open PID {hid.ProductID}: {ex.Message}");
            }
        }
        nextScan = DateTime.UtcNow.AddSeconds(5);
    }

    sensors.Update();
    foreach (var (path, display) in displays.ToList())
    {
        try
        {
            display.Update(sensors);
        }
        catch (Exception ex)
        {
            Log.Error($"Lost {display.Name}: {ex.Message}");
            display.Dispose();
            displays.Remove(path);
        }
    }

    stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
}

foreach (var display in displays.Values)
    display.Dispose();
mutex?.Dispose();
return 0;

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

double Number(string name, double fallback) =>
    double.TryParse(Option(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;

static string? Describe(int productId) => productId switch
{
    10 => "LD series",
    5 or 7 or 21 => "CH series / MORPHEUS",
    _ => null,
};
