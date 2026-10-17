namespace SHC.Recompiler.Analysis;

/// <summary>Como um bloco básico termina.</summary>
public enum BlockTerminator
{
    /// <summary>Termina porque o próximo endereço começa outro bloco.</summary>
    FallThrough,

    /// <summary>Desvio condicional ou incondicional.</summary>
    Branch,

    /// <summary>Desvio "likely".</summary>
    BranchLikely,

    /// <summary>Salto direto para outro ponto da mesma função.</summary>
    Jump,

    /// <summary>Salto direto para fora da função (chamada em cauda).</summary>
    TailCall,

    /// <summary>Retorno da função.</summary>
    Return,

    /// <summary>Salto por registrador que não foi possível resolver.</summary>
    IndirectJump,

    /// <summary>Salto por tabela de switch resolvida: os sucessores são os destinos da tabela.</summary>
    JumpTable,

    /// <summary>Fim sem delay slot (ERET).</summary>
    Halt
}
