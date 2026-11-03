using System.Buffers.Binary;

namespace DeepCoolDisplay;

interface IDisplay : IDisposable
{
    string Name { get; }
    void Init();
    void Update(Sensors sensors);
}

enum ChMode { Temperature, Usage, Auto }

/// <summary>
/// LD240 / LD360 (PID 10). Port of ld_series.rs: shows CPU temperature, power and usage.
/// </summary>
sealed class LdSeriesDisplay(DeepCoolDevice device, bool leadingZeros = false) : IDisplay
{
    public string Name => "LD series";

    static byte[] Header() => [16, 104, 1, 1, .. new byte[60]];

    public void Init()
    {
        var init = Header();
        init[4] = 2; init[5] = 3; init[6] = 1; init[7] = 112; init[8] = 22;
        device.Write(init);

        // Display format (show/hide leading zeros)
        init[5] = 2;
        if (leadingZeros)
            init[7] = 111;
        else
        {
            init[6] = 0;
            init[7] = 110;
        }
        device.Write(init);
    }

    public void Update(Sensors sensors)
    {
        var data = Header();
        data[4] = 11; data[5] = 1; data[6] = 2; data[7] = 5;

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), (ushort)Math.Clamp(MathF.Round(sensors.CpuPower ?? 0), 0, ushort.MaxValue));
        data[10] = 0; // Celsius
        BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(11), Whole(sensors.CpuTemperature));
        data[15] = Percent(sensors.CpuUsage);

        int checksum = 0;
        for (int i = 1; i <= 15; i++) checksum += data[i];
        data[16] = (byte)(checksum % 256);
        data[17] = 22;

        device.Write(data);
    }

    static float Whole(float? value) => Math.Clamp(MathF.Round(value ?? 0), 0, 255);
    static byte Percent(float? value) => (byte)Math.Clamp(MathF.Round(value ?? 0), 0, 100);

    public void Dispose() => device.Dispose();
}

/// <summary>
/// CH360 / CH560 DIGITAL, MORPHEUS (PID 5/7/21). Port of ch_series.rs:
/// CPU on the main readout, GPU on the secondary, usage bars on both.
/// </summary>
sealed class ChSeriesDisplay(DeepCoolDevice device, ChMode mode) : IDisplay
{
    static readonly TimeSpan AutoInterval = TimeSpan.FromSeconds(5);
    const byte CelsiusSymbol = 19, PercentSymbol = 76;

    DateTime _autoSwitch = DateTime.UtcNow + AutoInterval;
    ChMode _autoCurrent = ChMode.Temperature;

    public string Name => "CH series";

    public void Init()
    {
        var init = new byte[64];
        init[0] = 16; init[1] = 170;
        device.Write(init);
    }

    public void Update(Sensors sensors)
    {
        var current = mode;
        if (mode == ChMode.Auto)
        {
            if (DateTime.UtcNow >= _autoSwitch)
            {
                _autoCurrent = _autoCurrent == ChMode.Temperature ? ChMode.Usage : ChMode.Temperature;
                _autoSwitch = DateTime.UtcNow + AutoInterval;
            }
            current = _autoCurrent;
        }

        var cpuUsage = Whole(sensors.CpuUsage);
        var gpuUsage = Whole(sensors.GpuUsage);

        var data = new byte[64];
        data[0] = 16;
        if (current == ChMode.Temperature)
        {
            Digits(data, 1, CelsiusSymbol, Whole(sensors.CpuTemperature));
            Digits(data, 6, CelsiusSymbol, Whole(sensors.GpuTemperature));
        }
        else
        {
            Digits(data, 1, PercentSymbol, cpuUsage);
            Digits(data, 6, PercentSymbol, gpuUsage);
        }

        // Status bars (1..10)
        data[2] = Bar(cpuUsage);
        data[7] = Bar(gpuUsage);

        device.Write(data);
    }

    static void Digits(byte[] data, int at, byte symbol, int value)
    {
        data[at] = symbol;
        data[at + 2] = (byte)(value / 100);
        data[at + 3] = (byte)(value % 100 / 10);
        data[at + 4] = (byte)(value % 10);
    }

    static byte Bar(int usage) => usage < 15 ? (byte)1 : (byte)MathF.Round(usage / 10f);
    static int Whole(float? value) => (int)Math.Clamp(MathF.Round(value ?? 0), 0, 255);

    public void Dispose() => device.Dispose();
}
