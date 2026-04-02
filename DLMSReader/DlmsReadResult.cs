namespace DLMSReader;

/// <summary>
/// Результат чтения одного OBIS-кода.
/// </summary>
public sealed class DlmsReadResult
{
    /// <summary>
    /// Инициализирует новый экземпляр результата чтения.
    /// </summary>
    /// <param name="obis">OBIS-код.</param>
    /// <param name="rawData">Сырые данные ответа.</param>
    /// <param name="textValue">Текстовое значение, если удалось извлечь.</param>
    public DlmsReadResult(string obis, byte[] rawData, string? textValue)
    {
        Obis = obis;
        RawData = rawData;
        TextValue = textValue;
    }

    /// <summary>
    /// OBIS-код.
    /// </summary>
    public string Obis { get; }

    /// <summary>
    /// Сырые данные ответа.
    /// </summary>
    public byte[] RawData { get; }

    /// <summary>
    /// Текстовое представление значения.
    /// </summary>
    public string? TextValue { get; }
}
