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
    private readonly DlmsRequestBuilder _requestBuilder;
    private bool _associationEstablished;

    /// <summary>
    /// Создает экземпляр минимального DLMS-клиента.
    /// </summary>
    /// <param name="portAdapter">Адаптер порта.</param>
    /// <param name="serverAddress">Адрес DLMS-сервера, сформированный через DlmsAddressHelper.GetServerAddress.</param>
    /// <param name="clientAddress">Клиентский адрес (по умолчанию 0x10).</param>
    public MinimalDlmsClient(ISerialPortAdapter portAdapter, int serverAddress, int clientAddress = 0x10)
    {
        if (clientAddress <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientAddress), "Клиентский адрес должен быть больше 0. Для Public client используйте 0x10.");
        }

        _portAdapter = portAdapter;
        _requestBuilder = new DlmsRequestBuilder(serverAddress, clientAddress);
    }

    /// <summary>
    /// Создает экземпляр клиента по логическому и физическому адресу сервера.
    /// </summary>
    /// <param name="portAdapter">Адаптер порта.</param>
    /// <param name="logicalAddress">Логический адрес сервера.</param>
    /// <param name="physicalAddress">Физический адрес сервера.</param>
    /// <param name="clientAddress">Клиентский адрес (по умолчанию 0x10).</param>
    public MinimalDlmsClient(ISerialPortAdapter portAdapter, int logicalAddress, int physicalAddress, int clientAddress = 0x10)
        : this(portAdapter, DlmsAddressHelper.GetServerAddress(logicalAddress, physicalAddress), clientAddress)
    {
    }

    /// <summary>
    /// Читает два обязательных OBIS-кода: 0.0.42.0.0.255 и 0.0.96.1.0.255.
    /// </summary>
    /// <param name="timeoutMs">Таймаут чтения ответа в миллисекундах.</param>
    /// <returns>Результаты чтения двух OBIS-кодов.</returns>
    public async Task<IReadOnlyList<DlmsReadResult>> ReadRequiredObisAsync(int timeoutMs)
    {
        await EnsureAssociationAsync(timeoutMs);

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
        await EnsureAssociationAsync(timeoutMs);

        var request = BuildGetRequest(obis);
        await _portAdapter.WriteAsync(request);
        var response = await _portAdapter.ReadAsync(timeoutMs);
        var textValue = TryExtractText(response);
        return new DlmsReadResult(obis, response, textValue);
    }

    /// <summary>
    /// Формирует минимальный DLMS GET-запрос в HDLC-кадре.
    /// </summary>
    /// <param name="obis">OBIS-код в формате A.B.C.D.E.F.</param>
    /// <returns>Байты GET-запроса.</returns>
    public byte[] BuildGetRequest(string obis)
    {
        return _requestBuilder.BuildGetRequest(obis);
    }

    /// <summary>
    /// Формирует SNRM-запрос.
    /// </summary>
    /// <returns>Байты SNRM кадра.</returns>
    public byte[] BuildSnrmRequest()
    {
        return _requestBuilder.BuildSnrmRequest();
    }

    /// <summary>
    /// Формирует AARQ-запрос (LN, без аутентификации).
    /// </summary>
    /// <returns>Байты AARQ кадра.</returns>
    public byte[] BuildAarqRequest()
    {
        return _requestBuilder.BuildAarqRequest();
    }

    /// <summary>
    /// Формирует DISC-запрос.
    /// </summary>
    /// <returns>Байты DISC кадра.</returns>
    public byte[] BuildDisconnectRequest()
    {
        return _requestBuilder.BuildDisconnectRequest();
    }

    /// <summary>
    /// Открывает DLMS-ассоциацию: SNRM -> UA, затем AARQ -> AARE.
    /// </summary>
    /// <param name="timeoutMs">Таймаут обмена в миллисекундах.</param>
    /// <returns>Задача выполнения инициализации.</returns>
    public async Task EnsureAssociationAsync(int timeoutMs)
    {
        if (_associationEstablished)
        {
            return;
        }

        var snrm = BuildSnrmRequest();
        await _portAdapter.WriteAsync(snrm);
        _ = await _portAdapter.ReadAsync(timeoutMs); // UA

        var aarq = BuildAarqRequest();
        await _portAdapter.WriteAsync(aarq);
        _ = await _portAdapter.ReadAsync(timeoutMs); // AARE

        _associationEstablished = true;
    }

    /// <summary>
    /// Закрывает DLMS-ассоциацию через DISC.
    /// </summary>
    /// <param name="timeoutMs">Таймаут обмена в миллисекундах.</param>
    /// <returns>Задача выполнения закрытия.</returns>
    public async Task DisconnectAsync(int timeoutMs)
    {
        if (!_associationEstablished)
        {
            return;
        }

        var disc = BuildDisconnectRequest();
        await _portAdapter.WriteAsync(disc);
        _ = await _portAdapter.ReadAsync(timeoutMs);

        _associationEstablished = false;
        _requestBuilder.ResetControlSequence();
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
