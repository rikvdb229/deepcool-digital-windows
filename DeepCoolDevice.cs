using HidSharp;

namespace DeepCoolDisplay;

/// <summary>
/// HID connection to a DeepCool display. Packets are built the way the Linux
/// project builds them for hidapi: byte 0 is the report ID (16).
/// </summary>
sealed class DeepCoolDevice : IDisposable
{
    public const int VendorId = 0x3633;

    readonly HidStream _stream;
    readonly int _reportLength;
    readonly bool _prependZero;

    public int ProductId { get; }
    public string Path { get; }

    public static IEnumerable<HidDevice> Find() =>
        DeviceList.Local.GetHidDevices(VendorId).Where(d => SafeOutputLength(d) > 0);

    public DeepCoolDevice(HidDevice device)
    {
        ProductId = device.ProductID;
        Path = device.DevicePath;
        _reportLength = device.GetMaxOutputReportLength();
        _prependZero = !UsesReportIds(device);
        _stream = device.Open();
        _stream.WriteTimeout = 2000;
    }

    public void Write(byte[] packet)
    {
        // Windows wants exactly one output report per write, including the report ID byte.
        var buffer = new byte[_reportLength];
        var offset = _prependZero ? 1 : 0;
        Array.Copy(packet, 0, buffer, offset, Math.Min(packet.Length, _reportLength - offset));
        _stream.Write(buffer);
    }

    static bool UsesReportIds(HidDevice device)
    {
        try
        {
            return device.GetReportDescriptor().OutputReports.Any(r => r.ReportID != 0);
        }
        catch
        {
            return true;
        }
    }

    static int SafeOutputLength(HidDevice device)
    {
        try { return device.GetMaxOutputReportLength(); }
        catch { return 0; }
    }

    public void Dispose() => _stream.Dispose();
}
