using SHC.Runtime.Machine;

namespace SHC.Runtime.Overlays;

/// <summary>
/// Carrega um overlay (.BIN) na área de overlay da memória do PS2.
/// O arquivo é uma imagem de memória: o offset 0 corresponde ao
/// endereço de carga, com o cabeçalho incluído.
/// </summary>
public static class OverlayLoader
{
    public static OverlayLoadResult Load(string filePath, Ps2Memory memory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(memory);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "O arquivo de overlay não foi encontrado.",
                filePath);
        }

        byte[] data = File.ReadAllBytes(filePath);

        OverlayHeaderInfo header = OverlayHeaderReader.Parse(data);

        if (header.LoadAddress != ShcMemoryMap.OverlayBase)
        {
            throw new InvalidDataException(
                $"Endereço de carga inesperado: 0x{header.LoadAddress:X8}. " +
                $"Esperado: 0x{ShcMemoryMap.OverlayBase:X8}.");
        }

        long memorySize = (long)data.Length + header.ZeroTailSize;

        if (memorySize > ShcMemoryMap.OverlayMaxSize)
        {
            throw new InvalidDataException(
                "O overlay não cabe na área de overlay.");
        }

        memory.Write(header.LoadAddress, data);

        if (header.ZeroTailSize > 0)
        {
            memory.Fill(
                header.LoadAddress + (uint)data.Length,
                (int)header.ZeroTailSize,
                0);
        }

        return new OverlayLoadResult(header, data.Length, (int)memorySize);
    }
}
