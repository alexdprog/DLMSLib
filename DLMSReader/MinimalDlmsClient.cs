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
    private bool _associationEstablished;
    private byte _nextSendControl = 0x10;

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
        _serverAddress = serverAddress;
        _clientAddress = clientAddress;
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

        var llcPayload = new byte[] { 0xE6, 0xE6, 0x00 }
            .Concat(getApdu)
            .ToArray();

        var frame = BuildHdlcInformationFrame(_nextSendControl, llcPayload);
        _nextSendControl = (byte)((_nextSendControl + 0x22) & 0xFE);
        return frame;
    }

    /// <summary>
    /// Формирует SNRM-запрос.
    /// </summary>
    /// <returns>Байты SNRM кадра.</returns>
    public byte[] BuildSnrmRequest()
    {
        return BuildHdlcCommandFrame(0x93);
    }

    /// <summary>
    /// Формирует AARQ-запрос (LN, без аутентификации).
    /// </summary>
    /// <returns>Байты AARQ кадра.</returns>
    public byte[] BuildAarqRequest()
    {
        var aarqApdu = new byte[]
        {
            0xE6, 0xE6, 0x00,
            0x60, 0x1D,
            0xA1, 0x09, 0x06, 0x07, 0x60, 0x85, 0x74, 0x05, 0x08, 0x01, 0x01,
            0xBE, 0x10,
            0x04, 0x0E,
            0x01, 0x00, 0x00, 0x00,
            0x06, 0x5F, 0x1F, 0x04, 0x00,
            0x62, 0x1E, 0x5D,
            0xFF, 0xFF
        };

        return BuildHdlcInformationFrame(_nextSendControl, aarqApdu);
    }

    /// <summary>
    /// Формирует DISC-запрос.
    /// </summary>
    /// <returns>Байты DISC кадра.</returns>
    public byte[] BuildDisconnectRequest()
    {
        return BuildHdlcCommandFrame(0x53);
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
        _nextSendControl = 0x10;
    }

    private byte[] BuildHdlcCommandFrame(byte control)
    {
        var destination = EncodeHdlcAddress(_serverAddress);
        var source = EncodeHdlcAddress(_clientAddress);

        var bodyLength = 2 + destination.Length + source.Length + 1 + 2;
        var frameBody = new List<byte>
        {
            0xA0,
            (byte)bodyLength
        };

        frameBody.AddRange(destination);
        frameBody.AddRange(source);
        frameBody.Add(control);

        var fcs = ComputeCrc16Ccitt(frameBody.ToArray());
        frameBody.Add((byte)(fcs & 0xFF));
        frameBody.Add((byte)(fcs >> 8));

        return WrapWithFlags(frameBody);
    }

    private byte[] BuildHdlcInformationFrame(byte control, byte[] information)
    {
        var destination = EncodeHdlcAddress(_serverAddress);
        var source = EncodeHdlcAddress(_clientAddress);

        var bodyLength = 2 + destination.Length + source.Length + 1 + 2 + information.Length + 2;
        var frameBody = new List<byte>
        {
            0xA0,
            (byte)bodyLength
        };

        frameBody.AddRange(destination);
        frameBody.AddRange(source);
        frameBody.Add(control);

        var headerForCrc = frameBody.ToArray();
        var hcs = ComputeCrc16Ccitt(headerForCrc);
        frameBody.Add((byte)(hcs & 0xFF));
        frameBody.Add((byte)(hcs >> 8));

        frameBody.AddRange(information);

        var fcs = ComputeCrc16Ccitt(frameBody.ToArray());
        frameBody.Add((byte)(fcs & 0xFF));
        frameBody.Add((byte)(fcs >> 8));

        return WrapWithFlags(frameBody);
    }

    private static byte[] WrapWithFlags(List<byte> frameBody)
    {
        var frame = new byte[frameBody.Count + 2];
        frame[0] = 0x7E;
        frameBody.CopyTo(frame, 1);
        frame[^1] = 0x7E;
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

    private static ushort ComputeCrc16Ccitt(byte[] bytes)
    {
        ushort crc = 0xFFFF;

        foreach (var value in bytes)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0x8408);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        crc ^= 0xFFFF;
        return crc;
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
