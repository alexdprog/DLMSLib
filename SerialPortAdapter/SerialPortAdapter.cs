namespace SerialPortAdapter;

/// <summary>
/// Платформенная реализация serial-адаптера.
/// </summary>
public partial class SerialPortAdapter : ISerialPortAdapter
{
    private readonly string _portName;
    private readonly int _baudRate;

    /// <summary>
    /// Создает адаптер для указанного порта.
    /// </summary>
    /// <param name="portName">Имя порта (например, COM3 или Android USB id).</param>
    /// <param name="baudRate">Скорость порта.</param>
    public SerialPortAdapter(string portName, int baudRate)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    /// <summary>
    /// Открывает соединение с портом.
    /// </summary>
    public Task OpenAsync() => OpenPlatformAsync(_portName, _baudRate);

    /// <summary>
    /// Закрывает соединение.
    /// </summary>
    public Task CloseAsync() => ClosePlatformAsync();

    /// <summary>
    /// Отправляет данные в порт.
    /// </summary>
    /// <param name="data">Массив байтов для отправки.</param>
    public Task WriteAsync(byte[] data) => WritePlatformAsync(data);

    /// <summary>
    /// Читает данные из порта с таймаутом.
    /// </summary>
    /// <param name="timeoutMs">Таймаут чтения в миллисекундах.</param>
    /// <returns>Прочитанные байты.</returns>
    public Task<byte[]> ReadAsync(int timeoutMs) => ReadPlatformAsync(timeoutMs);

    private partial Task OpenPlatformAsync(string portName, int baudRate);
    private partial Task ClosePlatformAsync();
    private partial Task WritePlatformAsync(byte[] data);
    private partial Task<byte[]> ReadPlatformAsync(int timeoutMs);
}
