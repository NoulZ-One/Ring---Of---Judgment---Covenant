using SHC.Runtime.Machine;
using SHC.Runtime.Overlays;

namespace SHC.Recompiler.Analysis;

/// <summary>
/// Define qual parte de cada unidade é código. Dados costumam decodificar
/// como instruções válidas, então a análise precisa saber onde o código termina.
/// </summary>
public static class UnitCodeRanges
{
    // Somado ao campo Word0C do cabeçalho do overlay para obter o fim do código.
    // Em FIELD, BATTLE e OPENING o último código real fica logo antes de
    // Word0C + 0x40 (referência: o código vai de 0x80 até esse ponto no arquivo).
    private const uint OverlayCodeSizeBias = 0x40;

    /// <summary>Devolve a região de código da unidade.</summary>
    public static CodeRange For(RecompilationUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        // Executável principal: o código começa na base e termina em um
        // limite determinado por análise (veja ShcMemoryMap.MainCodeEnd).
        if (unit.EntryPoint is not null)
        {
            return new CodeRange(
                unit.BaseAddress,
                Math.Min(ShcMemoryMap.MainCodeEnd, unit.EndAddress));
        }

        // Overlay: o código começa após o cabeçalho e o fim vem do cabeçalho.
        OverlayHeaderInfo header = OverlayHeaderReader.Parse(unit.Data);

        uint start = unit.BaseAddress + OverlayHeaderReader.HeaderSize;
        uint end = Math.Min(
            unit.BaseAddress + header.Word0C + OverlayCodeSizeBias,
            unit.EndAddress);

        return new CodeRange(start, end);
    }
}
