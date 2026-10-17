namespace SHC.Runtime.Overlays;

/// <summary>
/// Campos do cabeçalho de 0x80 bytes de um overlay (magic "MWo3").
/// Os campos ainda sem significado confirmado levam o nome do offset.
/// </summary>
/// <param name="Id">Identificador do overlay (offset 0x04).</param>
/// <param name="LoadAddress">Endereço de carga na memória (offset 0x08).</param>
/// <param name="Word0C">Valor ainda não decifrado (offset 0x0C).</param>
/// <param name="Word10">Valor ainda não decifrado (offset 0x10).</param>
/// <param name="ZeroTailSize">
/// Bytes zerados após o fim do arquivo na memória (offset 0x14).
/// Tamanho em memória = tamanho do arquivo + este campo.
/// </param>
/// <param name="Word18">Valor ainda não decifrado (offset 0x18).</param>
/// <param name="Word1C">Valor ainda não decifrado (offset 0x1C).</param>
/// <param name="Name">Nome interno gravado no offset 0x20.</param>
public sealed record OverlayHeaderInfo(
    OverlayId Id,
    uint LoadAddress,
    uint Word0C,
    uint Word10,
    uint ZeroTailSize,
    uint Word18,
    uint Word1C,
    string Name);
