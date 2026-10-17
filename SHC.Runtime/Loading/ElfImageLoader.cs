using SHC.ELF;
using SHC.ELF.Models;
using SHC.Runtime.Machine;

namespace SHC.Runtime.Loading;

/// <summary>
/// Copia os segmentos carregáveis (PT_LOAD) de um ELF para a RAM do PS2.
/// </summary>
public static class ElfImageLoader
{
    // Valor de e_machine para MIPS (o EE do PS2 usa este identificador).
    private const ushort MachineMips = 8;

    // Tipo de segmento carregável.
    private const uint SegmentTypeLoad = 1;

    /// <summary>
    /// Carrega o ELF na memória. Os bytes que existem no arquivo são copiados
    /// e o restante de cada segmento (BSS) é zerado.
    /// </summary>
    public static ElfLoadResult Load(string filePath, Ps2Memory memory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(memory);

        ElfHeaderInfo header = ElfHeaderReader.Read(filePath);

        if (header.Machine != MachineMips)
        {
            throw new InvalidDataException(
                $"Arquitetura inesperada: {header.Machine}. Esperado: MIPS (8).");
        }

        List<ElfProgramHeaderInfo> programHeaders =
            ElfProgramHeaderReader.Read(filePath, header);

        var loadedSegments = new List<ElfProgramHeaderInfo>();

        using var stream = File.OpenRead(filePath);

        foreach (var segment in programHeaders)
        {
            if (segment.Type != SegmentTypeLoad)
            {
                continue;
            }

            if (segment.MemorySize < segment.FileSize)
            {
                throw new InvalidDataException(
                    "Um segmento declara MemorySize menor que FileSize.");
            }

            // Copia os bytes que estão armazenados no arquivo.
            if (segment.FileSize > 0)
            {
                stream.Position = segment.Offset;

                Span<byte> destination = memory.GetSpan(
                    segment.VirtualAddress,
                    checked((int)segment.FileSize));

                stream.ReadExactly(destination);
            }

            // Zera a parte do segmento que não existe no arquivo.
            uint zeroBytes = segment.MemorySize - segment.FileSize;

            if (zeroBytes > 0)
            {
                memory.Fill(
                    segment.VirtualAddress + segment.FileSize,
                    checked((int)zeroBytes),
                    0);
            }

            loadedSegments.Add(segment);
        }

        return new ElfLoadResult(header.EntryPoint, loadedSegments);
    }
}
