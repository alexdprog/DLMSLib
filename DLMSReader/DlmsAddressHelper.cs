namespace DLMSReader;

/// <summary>
/// Вспомогательные методы для формирования адресов DLMS-сервера.
/// </summary>
public static class DlmsAddressHelper
{
    /// <summary>
    /// Формирует адрес сервера по логическому и физическому адресам.
    /// </summary>
    /// <param name="logicalAddress">Логический адрес (например, 0x1 или 0x10).</param>
    /// <param name="physicalAddress">Физический адрес (например, 127 или 0x3FFF).</param>
    /// <returns>Сформированный адрес сервера.</returns>
    public static int GetServerAddress(int logicalAddress, int physicalAddress)
    {
        if (logicalAddress < 0 || physicalAddress < 0)
        {
            throw new ArgumentOutOfRangeException("Адреса должны быть неотрицательными.");
        }

        if (logicalAddress <= 0x7F && physicalAddress <= 0x7F)
        {
            return (logicalAddress << 7) | physicalAddress;
        }

        if (logicalAddress <= 0x3FFF && physicalAddress <= 0x3FFF)
        {
            return (logicalAddress << 14) | physicalAddress;
        }

        throw new ArgumentOutOfRangeException("Поддерживаются только 7-битные и 14-битные адреса.");
    }
}
