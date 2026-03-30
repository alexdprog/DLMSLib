#if ANDROID
using Android.Content;
using Android.Hardware.Usb;
using Hoho.Android.UsbSerial.Driver;

namespace SerialPortAdapter;

public partial class SerialPortAdapter
{
    private UsbSerialPort? _usbSerialPort;

    private partial Task OpenPlatformAsync(string portName, int baudRate)
    {
        var context = Android.App.Application.Context;
        var usbManager = (UsbManager?)context.GetSystemService(Context.UsbService)
            ?? throw new InvalidOperationException("UsbManager недоступен.");

        var availableDrivers = UsbSerialProber.DefaultProber.FindAllDrivers(usbManager);
        var driver = availableDrivers.FirstOrDefault()
            ?? throw new InvalidOperationException("USB serial устройство не найдено.");

        var connection = usbManager.OpenDevice(driver.Device)
            ?? throw new InvalidOperationException("Не удалось открыть USB устройство.");

        _usbSerialPort = driver.Ports.First();
        _usbSerialPort.Open(connection);
        _usbSerialPort.SetParameters(baudRate, 8, StopBits.One, Parity.None);

        return Task.CompletedTask;
    }

    private partial Task ClosePlatformAsync()
    {
        _usbSerialPort?.Close();
        _usbSerialPort = null;
        return Task.CompletedTask;
    }

    private partial Task WritePlatformAsync(byte[] data)
    {
        if (_usbSerialPort is null)
        {
            throw new InvalidOperationException("Порт не открыт.");
        }

        _usbSerialPort.Write(data, 2000);
        return Task.CompletedTask;
    }

    private partial Task<byte[]> ReadPlatformAsync(int timeoutMs)
    {
        if (_usbSerialPort is null)
        {
            throw new InvalidOperationException("Порт не открыт.");
        }

        var buffer = new byte[1024];
        var bytesRead = _usbSerialPort.Read(buffer, timeoutMs);
        var result = new byte[bytesRead];
        Array.Copy(buffer, result, bytesRead);
        return Task.FromResult(result);
    }
}
#endif
