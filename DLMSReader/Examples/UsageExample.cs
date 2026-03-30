using SerialPortAdapter;

namespace DLMSReader.Examples;

/// <summary>
/// Пример минимального использования DLMSReader + SerialPortAdapter.
/// </summary>
public static class UsageExample
{
    /// <summary>
    /// Выполняет чтение двух обязательных OBIS-кодов и выводит результат.
    /// </summary>
    /// <returns>Задача выполнения примера.</returns>
    public static async Task RunAsync()
    {
        ISerialPortAdapter adapter = new SerialPortAdapter.SerialPortAdapter("COM3", 9600);
        await adapter.OpenAsync();

        try
        {
            var client = new MinimalDlmsClient(adapter);
            var results = await client.ReadRequiredObisAsync(timeoutMs: 3000);

            foreach (var item in results)
            {
                Console.WriteLine($"OBIS: {item.Obis}");
                Console.WriteLine($"Text: {item.TextValue ?? "<null>"}");
                Console.WriteLine($"Raw:  {BitConverter.ToString(item.RawData)}");
                Console.WriteLine();
            }
        }
        finally
        {
            await adapter.CloseAsync();
        }
    }
}
