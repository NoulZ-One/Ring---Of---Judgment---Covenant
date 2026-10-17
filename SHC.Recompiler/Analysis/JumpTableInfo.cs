namespace SHC.Recompiler.Analysis;

/// <summary>
/// Tabela de saltos de um comando switch, resolvida a partir do padrão
/// SLTIU (limite), SLL (índice x 4), LUI/ADDIU (endereço), ADDU, LW e JR.
/// </summary>
/// <param name="JumpAddress">Endereço do JR que usa a tabela.</param>
/// <param name="TableAddress">Endereço da tabela de ponteiros nos dados.</param>
/// <param name="EntryCount">Quantidade de entradas (o limite do SLTIU).</param>
/// <param name="Targets">Destino de cada entrada, na ordem do índice (pode repetir endereços).</param>
public sealed record JumpTableInfo(
    uint JumpAddress,
    uint TableAddress,
    int EntryCount,
    IReadOnlyList<uint> Targets);
