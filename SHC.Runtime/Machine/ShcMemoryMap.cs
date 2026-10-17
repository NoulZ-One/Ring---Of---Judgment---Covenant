namespace SHC.Runtime.Machine;

/// <summary>
/// Endereços fixos do mapa de memória de Shadow Hearts: Covenant,
/// obtidos da análise do SLUS e dos overlays.
/// </summary>
public static class ShcMemoryMap
{
    /// <summary>Endereço de carga do executável principal.</summary>
    public const uint MainImageBase = 0x00200000;

    /// <summary>
    /// Fim aproximado do código do executável principal. O último retorno de
    /// função fica em 0x416F34 e a partir de 0x417C00 há dados que decodificam
    /// como instruções válidas. Valor obtido por análise.
    /// </summary>
    public const uint MainCodeEnd = 0x00417C00;

    /// <summary>
    /// Início da área de overlay. OPENING.BIN, FIELD.BIN e BATTLE.BIN
    /// são carregados aqui, um de cada vez.
    /// </summary>
    public const uint OverlayBase = 0x0045EA80;

    /// <summary>Fim da área de overlay (término do maior overlay, BATTLE).</summary>
    public const uint OverlayEnd = 0x006E8480;

    /// <summary>Tamanho máximo, em bytes, de um overlay em memória.</summary>
    public const uint OverlayMaxSize = OverlayEnd - OverlayBase;
}
