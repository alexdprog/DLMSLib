using SerialPortAdapter;
using System.Text;

namespace DLMSReader;

/// <summary>
/// Минимальный клиент для чтения ограниченного набора OBIS-кодов по serial.
/// </summary>
public sealed class MinimalDlmsClient
{
    /// <summary>
    /// OBIS-код устройства (Logical Device Name).
    /// </summary>
    public const string DeviceLogicalNameObis = "0.0.42.0.0.255";

    /// <summary>
    /// OBIS-код серийного номера.
    /// </summary>
    public const string SerialNumberObis = "0.0.96.1.0.255";

    private readonly ISerialPortAdapter _portAdapter;

    /// <summary>
    /// Создает экземпляр минимального DLMS-клиента.
    /// </summary>
    /// <param name="portAdapter">Адаптер порта.</param>
    public MinimalDlmsClient(ISerialPortAdapter portAdapter)
    {
        _portAdapter = portAdapter;
    }

    /// <summary>
    /// Читает два обязательных OBIS-кода: 0.0.42.0.0.255 и 0.0.96.1.0.255.
    /// </summary>
    /// <param name="timeoutMs">Таймаут чтения ответа в миллисекундах.</param>
    /// <returns>Результаты чтения двух OBIS-кодов.</returns>
    public async Task<IReadOnlyList<DlmsReadResult>> ReadRequiredObisAsync(int timeoutMs)
    {
        var first = await ReadObisAsync(DeviceLogicalNameObis, timeoutMs);
        var second = await ReadObisAsync(SerialNumberObis, timeoutMs);
        return new[] { first, second };
    }

    /// <summary>
    /// Выполняет минимальный GET-запрос для чтения одного OBIS-кода.
    /// </summary>
    /// <param name="obis">OBIS-код в формате A.B.C.D.E.F.</param>
    /// <param name="timeoutMs">Таймаут чтения ответа в миллисекундах.</param>
    /// <returns>Результат чтения.</returns>
    public async Task<DlmsReadResult> ReadObisAsync(string obis, int timeoutMs)
    {
        var request = BuildGetRequest(obis);
        await _portAdapter.WriteAsync(request);
        var response = await _portAdapter.ReadAsync(timeoutMs);
        var textValue = TryExtractText(response);
        return new DlmsReadResult(obis, response, textValue);
    }

    /// <summary>
    /// Формирует минимальный DLMS GET-запрос.
    /// </summary>
    /// <param name="obis">OBIS-код в формате A.B.C.D.E.F.</param>
    /// <returns>Байты GET-запроса.</returns>
    public byte[] BuildGetRequest(string obis)
    {
        var obisBytes = ParseObis(obis);

        return new byte[]
        {
            0xC0, // GET-Request
            0x01, // Normal
            0x01, // Invoke-Id-And-Priority
            0x00, 0x01, // ClassId = 1 (Data)
            0x00, // InstanceId tag placeholder
            obisBytes[0], obisBytes[1], obisBytes[2], obisBytes[3], obisBytes[4], obisBytes[5],
            0x02, // AttributeId = 2 (value)
            0x00  // Access selection = false
        };
    }

    private static byte[] ParseObis(string obis)
    {
        var parts = obis.Split('.');
        if (parts.Length != 6)
        {
            throw new ArgumentException("OBIS должен состоять из 6 частей.", nameof(obis));
        }

        return parts.Select(byte.Parse).ToArray();
    }

    private static string? TryExtractText(byte[] response)
    {
        var octetTagIndex = Array.IndexOf(response, (byte)0x09);
        if (octetTagIndex >= 0 && octetTagIndex + 1 < response.Length)
        {
            var len = response[octetTagIndex + 1];
            var start = octetTagIndex + 2;
            if (start + len <= response.Length)
            {
                var bytes = response.Skip(start).Take(len).ToArray();
                return Encoding.ASCII.GetString(bytes);
            }
        }

        return response.Length > 0 ? BitConverter.ToString(response) : null;
    }
}
