using LibreHardwareMonitor.Hardware;

namespace DeepCoolDisplay;

/// <summary>
/// How readings are calmed before they reach the display: an exponential moving average over
/// <see cref="Seconds"/> (0 = raw values), and per-value dead bands below which the shown value stays put.
/// </summary>
sealed record Smoothing(double Seconds = 5, double DeadbandTemp = 1, double DeadbandUsage = 3, double DeadbandPower = 3);

/// <summary>
/// CPU/GPU readings via LibreHardwareMonitor. CPU temperature and power need the
/// PawnIO driver and admin rights; usage and GPU readings work without them.
/// </summary>
sealed class Sensors : IDisposable
{
    readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    readonly IHardware? _cpu;
    readonly IHardware? _gpu;
    readonly ISensor? _cpuTemp, _cpuPower, _cpuLoad, _gpuTemp, _gpuLoad;

    public Sensors(Smoothing smoothing)
    {
        _cpuTempAvg = new(smoothing.Seconds, smoothing.DeadbandTemp);
        _gpuTempAvg = new(smoothing.Seconds, smoothing.DeadbandTemp);
        _cpuPowerAvg = new(smoothing.Seconds, smoothing.DeadbandPower);
        _cpuLoadAvg = new(smoothing.Seconds, smoothing.DeadbandUsage);
        _gpuLoadAvg = new(smoothing.Seconds, smoothing.DeadbandUsage);

        _computer.Open();
        foreach (var hw in _computer.Hardware)
            hw.Update();

        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _gpu = PickDiscreteGpu(_computer.Hardware);

        // AMD reports Tctl/Tdie; Intel reports "CPU Package". Fall back to any temperature.
        _cpuTemp = Find(_cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Package");
        _cpuPower = Find(_cpu, SensorType.Power, "Package", "CPU Package");
        _cpuLoad = Find(_cpu, SensorType.Load, "CPU Total");
        _gpuTemp = Find(_gpu, SensorType.Temperature, "GPU Core", "GPU Hot Spot");
        _gpuLoad = Find(_gpu, SensorType.Load, "GPU Core", "D3D 3D");
    }

    public string CpuName => _cpu?.Name ?? "(none)";
    public string GpuName => _gpu?.Name ?? "(none)";

    // Raw sensor values jump several degrees per second (Ryzen Tctl, GPU under bursty load),
    // so the display shows exponential moving averages instead.
    // The shown value only moves when the average drifts at least a dead band away from it,
    // so small fluctuations do not make the digits flicker every second (see Smoothing).
    readonly Smoothed _cpuTempAvg, _gpuTempAvg, _cpuPowerAvg, _cpuLoadAvg, _gpuLoadAvg;
    readonly System.Diagnostics.Stopwatch _sinceUpdate = new();

    public float? CpuTemperature => _cpuTempAvg.Value;
    public float? CpuPower => _cpuPowerAvg.Value;
    public float? CpuUsage => _cpuLoadAvg.Value;
    public float? GpuTemperature => _gpuTempAvg.Value;
    public float? GpuUsage => _gpuLoadAvg.Value;

    public void Update()
    {
        _cpu?.Update();
        _gpu?.Update();

        double dt = _sinceUpdate.IsRunning ? _sinceUpdate.Elapsed.TotalSeconds : 0;
        _sinceUpdate.Restart();
        _cpuTempAvg.Push(_cpuTemp?.Value, dt);
        _gpuTempAvg.Push(_gpuTemp?.Value, dt);
        _cpuPowerAvg.Push(_cpuPower?.Value, dt);
        _cpuLoadAvg.Push(_cpuLoad?.Value, dt);
        _gpuLoadAvg.Push(_gpuLoad?.Value, dt);
    }

    /// <summary>
    /// Exponential moving average with a time constant in seconds, shown with a dead band:
    /// the reported value only changes when the average moves at least <c>deadBand</c> away.
    /// </summary>
    sealed class Smoothed(double seconds, double deadBand)
    {
        double? _average;
        double? _shown;

        public float? Value => _shown is double v ? (float)v : null;

        public void Push(float? sample, double dt)
        {
            if (sample is not float x || float.IsNaN(x)) return;
            if (_average is not double a || dt <= 0) { _average = _shown = x; return; }
            _average = seconds <= 0 ? x : a + (1 - Math.Exp(-dt / seconds)) * (x - a);
            if (_shown is not double s || Math.Abs(_average.Value - s) >= deadBand)
                _shown = _average;
        }
    }

    public IEnumerable<string> Describe()
    {
        foreach (var hw in new[] { _cpu, _gpu })
        {
            if (hw is null) continue;
            yield return $"{hw.HardwareType}: {hw.Name}";
            foreach (var s in hw.Sensors.Where(s => s.SensorType is SensorType.Temperature or SensorType.Power or SensorType.Load))
                yield return $"    {s.SensorType,-12} {s.Name,-24} {s.Value?.ToString("0.0") ?? "n/a"}";
        }
    }

    static IHardware? PickDiscreteGpu(IList<IHardware> hardware)
    {
        // With an iGPU present, prefer the GPU with the most dedicated memory.
        var gpus = hardware.Where(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel).ToList();
        return gpus
            .OrderByDescending(g => g.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name == "GPU Memory Total")?.Value ?? 0)
            .FirstOrDefault();
    }

    static ISensor? Find(IHardware? hw, SensorType type, params string[] names)
    {
        if (hw is null) return null;
        foreach (var name in names)
        {
            var match = hw.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name);
            if (match is not null) return match;
        }
        return hw.Sensors.FirstOrDefault(s => s.SensorType == type);
    }

    public void Dispose() => _computer.Close();
}
