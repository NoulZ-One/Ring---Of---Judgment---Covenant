using SHC.ELF.Models;

namespace SHC.Runtime.Loading;

/// <summary>
/// Resultado do carregamento de um ELF na memória do PS2.
/// </summary>
/// <param name="EntryPoint">Endereço onde a execução deve começar.</param>
/// <param name="Segments">Segmentos PT_LOAD que foram processados.</param>
public sealed record ElfLoadResult(
    uint EntryPoint,
    IReadOnlyList<ElfProgramHeaderInfo> Segments);
