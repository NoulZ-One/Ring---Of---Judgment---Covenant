namespace SHC.Recompiler.Analysis;

/// <summary>
/// Função descoberta na análise: seus blocos básicos e as referências
/// que ela faz a outros endereços.
/// </summary>
public sealed class FunctionInfo
{
    /// <summary>Endereço da primeira instrução.</summary>
    public required uint Entry { get; init; }

    /// <summary>Blocos básicos, em ordem crescente de endereço.</summary>
    public required IReadOnlyList<BasicBlock> Blocks { get; init; }

    /// <summary>Endereços de todas as instruções alcançadas, em ordem crescente (delay slots incluídos).</summary>
    public required uint[] Addresses { get; init; }

    /// <summary>Destinos de chamadas diretas (JAL) dentro da região de código.</summary>
    public required IReadOnlySet<uint> Calls { get; init; }

    /// <summary>Destinos de saltos diretos que saem da função (chamadas em cauda) dentro da região de código.</summary>
    public required IReadOnlySet<uint> TailCalls { get; init; }

    /// <summary>Destinos de chamadas ou saltos fora da região de código desta unidade.</summary>
    public required IReadOnlySet<uint> ExternalTargets { get; init; }

    /// <summary>Endereços dos JR que não são retorno e não puderam ser resolvidos.</summary>
    public required IReadOnlyList<uint> IndirectJumps { get; init; }

    /// <summary>Tabelas de switch resolvidas nesta função.</summary>
    public required IReadOnlyList<JumpTableInfo> JumpTables { get; init; }

    /// <summary>Endereços alcançados pelo fluxo que ficam fora da região de código.</summary>
    public required IReadOnlyList<uint> OutOfRangeAddresses { get; init; }

    /// <summary>Indica se o endereço pertence a alguma instrução desta função.</summary>
    public bool Contains(uint address) => Array.BinarySearch(Addresses, address) >= 0;
}
