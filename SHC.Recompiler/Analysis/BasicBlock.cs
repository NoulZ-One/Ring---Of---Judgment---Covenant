namespace SHC.Recompiler.Analysis;

/// <summary>
/// Sequência de instruções com uma única entrada (no início) e uma única
/// saída (no fim). Chamadas (JAL/JALR) não encerram o bloco. O delay slot
/// de um desvio pertence ao bloco do desvio.
/// </summary>
/// <param name="Start">Endereço da primeira instrução.</param>
/// <param name="End">Endereço seguinte à última instrução, delay slot incluído.</param>
/// <param name="Terminator">Como o bloco termina.</param>
/// <param name="Successors">Endereços dos blocos que podem executar em seguida.</param>
public sealed record BasicBlock(
    uint Start,
    uint End,
    BlockTerminator Terminator,
    IReadOnlyList<uint> Successors);
