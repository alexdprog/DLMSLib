namespace DLMSReader;

/// <summary>
/// Формирует минимальные DLMS-запросы в HDLC-кадрах.
/// </summary>
public sealed class DlmsRequestBuilder
{
    private readonly int _serverAddress;
    private readonly int _clientAddress;
    private byte _nextSendControl = 0x10;

    /// <summary>
    /// Создает экземпляр билдера DLMS-запросов.
    /// </summary>
    /// <param name="serverAddress">Адрес DLMS-сервера.</param>
    /// <param name="clientAddress">Клиентский адрес.</param>
    public DlmsRequestBuilder(int serverAddress, int clientAddress)
    {
        _serverAddress = serverAddress;
        _clientAddress = clientAddress;
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
    /// Сбрасывает счетчик control-поля до начального значения.
    /// </summary>
    public void ResetControlSequence()
    {
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
}
