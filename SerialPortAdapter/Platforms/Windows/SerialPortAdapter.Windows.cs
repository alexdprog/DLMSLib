#if WINDOWS
using System.IO.Ports;

namespace SerialPortAdapter;

public partial class SerialPortAdapter
{
    private SerialPort? _serialPort;

    private partial Task OpenPlatformAsync(string portName, int baudRate)
    {
        _serialPort = new SerialPort(portName, baudRate)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000
        };
        _serialPort.Open();
        return Task.CompletedTask;
    }

    private partial Task ClosePlatformAsync()
    {
        _serialPort?.Close();
        _serialPort?.Dispose();
        _serialPort = null;
        return Task.CompletedTask;
    }

    private partial async Task WritePlatformAsync(byte[] data)
    {
        if (_serialPort is null)
        {
            throw new InvalidOperationException("Порт не открыт.");
        }

        await _serialPort.BaseStream.WriteAsync(data, 0, data.Length);
        await _serialPort.BaseStream.FlushAsync();
    }

    private partial async Task<byte[]> ReadPlatformAsync(int timeoutMs)
    {
        if (_serialPort is null)
        {
            throw new InvalidOperationException("Порт не открыт.");
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        var buffer = new byte[1024];

        var read = await _serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
        var result = new byte[read];
        Array.Copy(buffer, result, read);
        return result;
    }
}
#endif
