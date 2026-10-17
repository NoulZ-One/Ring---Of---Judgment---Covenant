namespace SHC.Recompiler.Analysis;

/// <summary>
/// Como uma instrução afeta o fluxo de execução.
/// Todas, exceto Sequential e Halt, possuem delay slot.
/// </summary>
public enum ControlFlowKind
{
    /// <summary>Segue para a próxima instrução.</summary>
    Sequential,

    /// <summary>Desvio condicional ou incondicional (BEQ, BNE, BGEZ, BC1T...).</summary>
    Branch,

    /// <summary>Desvio "likely": o delay slot só executa se o desvio for tomado.</summary>
    BranchLikely,

    /// <summary>Salto direto (J).</summary>
    Jump,

    /// <summary>Chamada direta (JAL e a família BAL).</summary>
    Call,

    /// <summary>Chamada por registrador (JALR).</summary>
    IndirectCall,

    /// <summary>Retorno (JR $ra).</summary>
    Return,

    /// <summary>Salto por registrador que não é retorno, típico de tabela de saltos (JR).</summary>
    IndirectJump,

    /// <summary>Interrompe a execução sequencial sem delay slot (ERET).</summary>
    Halt
}
