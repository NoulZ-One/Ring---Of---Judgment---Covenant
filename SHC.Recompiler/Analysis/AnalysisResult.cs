namespace SHC.Recompiler.Analysis;

/// <summary>
/// Resultado da análise de uma unidade: as funções encontradas
/// e a cobertura obtida sobre a região de código.
/// </summary>
public sealed class AnalysisResult
{
    /// <summary>Unidade analisada.</summary>
    public required RecompilationUnit Unit { get; init; }

    /// <summary>Região considerada código.</summary>
    public required CodeRange CodeRange { get; init; }

    /// <summary>
    /// Quantidade de raízes iniciais: o ponto de entrada mais os prólogos
    /// de função (ADDIU $sp, $sp, -N) encontrados na região de código.
    /// </summary>
    public required int BaseRootCount { get; init; }

    /// <summary>
    /// Raízes adicionais: endereços de código referenciados por ponteiros
    /// em dados (tabelas de funções) e ainda não alcançados.
    /// </summary>
    public required int DataPointerRootCount { get; init; }

    /// <summary>Funções por endereço de entrada.</summary>
    public required IReadOnlyDictionary<uint, FunctionInfo> Functions { get; init; }

    /// <summary>Destinos de chamadas fora da região de código desta unidade.</summary>
    public required IReadOnlySet<uint> ExternalTargets { get; init; }

    /// <summary>Todos os endereços de instrução alcançados pela análise.</summary>
    public required IReadOnlySet<uint> CoveredAddresses { get; init; }
}
