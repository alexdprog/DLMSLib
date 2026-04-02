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
    private readonly int _serverAddress;
    private readonly int _clientAddress;

    /// <summary>
    /// Создает экземпляр минимального DLMS-клиента.
    /// </summary>
    /// <param name="portAdapter">Адаптер порта.</param>
    /// <param name="serverAddress">Адрес DLMS-сервера, сформированный через DlmsAddressHelper.GetServerAddress.</param>
    /// <param name="clientAddress">Клиентский адрес (по умолчанию 0x10).</param>
    public MinimalDlmsClient(ISerialPortAdapter portAdapter, int serverAddress, int clientAddress = 0x10)
    {
        _portAdapter = portAdapter;
        _serverAddress = serverAddress;
        _clientAddress = clientAddress;
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
        var getApdu = new byte[]
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

        return BuildMinimalHdlcFrame(getApdu);
    }

    private byte[] BuildMinimalHdlcFrame(byte[] payload)
    {
        var serverBytes = EncodeHdlcAddress(_serverAddress);
        var clientBytes = EncodeHdlcAddress(_clientAddress);

        // Минимальный HDLC каркас: флаг + адреса + payload + флаг.
        // FCS/LLC здесь намеренно опущены, так как библиотека делает только минимальное чтение.
        var frame = new byte[1 + serverBytes.Length + clientBytes.Length + payload.Length + 1];
        var index = 0;
        frame[index++] = 0x7E;

        Array.Copy(serverBytes, 0, frame, index, serverBytes.Length);
        index += serverBytes.Length;

        Array.Copy(clientBytes, 0, frame, index, clientBytes.Length);
        index += clientBytes.Length;

        Array.Copy(payload, 0, frame, index, payload.Length);
        index += payload.Length;

        frame[index] = 0x7E;
        return frame;
    }

    private static byte[] EncodeHdlcAddress(int address)
    {
        if (address < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Адрес не может быть отрицательным.");
        }

        if (address <= 0x7F)
        {
            return new[] { (byte)((address << 1) | 0x01) };
        }

        if (address <= 0x3FFF)
        {
            var hi = (byte)(((address >> 7) & 0x7F) << 1);
            var lo = (byte)(((address & 0x7F) << 1) | 0x01);
            return new[] { hi, lo };
        }

        throw new ArgumentOutOfRangeException(nameof(address), "Поддерживаются адреса до 14 бит.");
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
