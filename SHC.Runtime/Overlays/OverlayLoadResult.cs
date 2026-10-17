namespace SHC.Runtime.Overlays;

/// <summary>Resultado do carregamento de um overlay na memória.</summary>
/// <param name="Header">Cabeçalho interpretado do overlay.</param>
/// <param name="FileSize">Bytes copiados do arquivo.</param>
/// <param name="MemorySize">Bytes ocupados na memória, incluindo a cauda zerada.</param>
public sealed record OverlayLoadResult(
    OverlayHeaderInfo Header,
    int FileSize,
    int MemorySize);
