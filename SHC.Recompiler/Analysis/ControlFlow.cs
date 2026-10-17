using SHC.Recompiler.Decoding;

namespace SHC.Recompiler.Analysis;

/// <summary>
/// Classificação das instruções quanto ao fluxo de controle.
/// </summary>
public static class ControlFlow
{
    /// <summary>Identifica como a instrução altera o fluxo de execução.</summary>
    public static ControlFlowKind Classify(Instruction instruction) =>
        instruction.Opcode switch
        {
            Opcode.Beq or Opcode.Bne or Opcode.Blez or Opcode.Bgtz or
            Opcode.Bltz or Opcode.Bgez or
            Opcode.Bc0f or Opcode.Bc0t or Opcode.Bc1f or Opcode.Bc1t or
            Opcode.Bc2f or Opcode.Bc2t => ControlFlowKind.Branch,

            Opcode.Beql or Opcode.Bnel or Opcode.Blezl or Opcode.Bgtzl or
            Opcode.Bltzl or Opcode.Bgezl or
            Opcode.Bc0fl or Opcode.Bc0tl or Opcode.Bc1fl or Opcode.Bc1tl or
            Opcode.Bc2fl or Opcode.Bc2tl => ControlFlowKind.BranchLikely,

            Opcode.J => ControlFlowKind.Jump,

            Opcode.Jal or Opcode.Bltzal or Opcode.Bgezal or
            Opcode.Bltzall or Opcode.Bgezall => ControlFlowKind.Call,

            Opcode.Jalr => ControlFlowKind.IndirectCall,

            Opcode.Jr => instruction.Rs == 31
                ? ControlFlowKind.Return
                : ControlFlowKind.IndirectJump,

            Opcode.Eret => ControlFlowKind.Halt,

            _ => ControlFlowKind.Sequential
        };

    /// <summary>Indica se a instrução, de acordo com o tipo, possui delay slot.</summary>
    public static bool HasDelaySlot(ControlFlowKind kind) =>
        kind is not (ControlFlowKind.Sequential or ControlFlowKind.Halt);

    /// <summary>
    /// Indica se um desvio é sempre tomado: BEQ com os dois registradores
    /// iguais (o "b" do assembly) ou BGEZ $zero.
    /// </summary>
    public static bool IsUnconditional(Instruction instruction) =>
        (instruction.Opcode == Opcode.Beq && instruction.Rs == instruction.Rt) ||
        (instruction.Opcode == Opcode.Bgez && instruction.Rs == 0);

    /// <summary>Endereço de destino de um salto ou desvio direto.</summary>
    public static uint GetTarget(Instruction instruction) =>
        (instruction.Opcode is Opcode.J or Opcode.Jal)
            ? instruction.JumpTarget
            : instruction.BranchTarget;
}
