namespace SerialPortAdapter;

/// <summary>
/// Абстракция для работы с serial-портом на разных платформах.
/// </summary>
public interface ISerialPortAdapter
{
    /// <summary>
    /// Открывает соединение с портом.
    /// </summary>
    Task OpenAsync();

    /// <summary>
    /// Закрывает соединение.
    /// </summary>
    Task CloseAsync();

    /// <summary>
    /// Отправляет данные в порт.
    /// </summary>
    /// <param name="data">Массив байтов для отправки.</param>
    Task WriteAsync(byte[] data);

    /// <summary>
    /// Читает данные из порта с таймаутом.
    /// </summary>
    /// <param name="timeoutMs">Таймаут чтения в миллисекундах.</param>
    /// <returns>Прочитанные байты.</returns>
    Task<byte[]> ReadAsync(int timeoutMs);
}
