namespace SHC.Recompiler.Analysis;

/// <summary>
/// Intervalo de endereços que contém código executável
/// (o restante da unidade é dado).
/// </summary>
/// <param name="Start">Primeiro endereço do código.</param>
/// <param name="End">Primeiro endereço após o código.</param>
public readonly record struct CodeRange(uint Start, uint End)
{
    /// <summary>Quantidade de palavras de 32 bits no intervalo.</summary>
    public int WordCount => (int)((End - Start) / 4);

    /// <summary>Indica se o endereço é alinhado e está dentro do intervalo.</summary>
    public bool Contains(uint address) =>
        address >= Start && address < End && (address & 3) == 0;
}
