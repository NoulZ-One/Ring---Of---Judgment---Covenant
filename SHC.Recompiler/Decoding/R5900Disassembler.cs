namespace SHC.Recompiler.Decoding;

/// <summary>
/// Converte instruções decodificadas em texto no estilo assembly MIPS.
/// </summary>
public static class R5900Disassembler
{
    private static readonly string[] RegisterNames =
    {
        "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
        "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
        "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
        "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra"
    };

    /// <summary>Formata uma instrução, por exemplo "addiu    $sp, $sp, -48".</summary>
    public static string Format(Instruction instruction)
    {
        // SLL $zero, $zero, 0 é a palavra zero, o NOP.
        if (instruction.Word == 0)
        {
            return "nop";
        }

        if (instruction.Opcode == Opcode.Unknown)
        {
            return $".word    0x{instruction.Word:X8}";
        }

        string mnemonic = instruction.Opcode.GetMnemonic();
        string operands = FormatOperands(instruction);

        return operands.Length == 0
            ? mnemonic
            : $"{mnemonic,-8} {operands}";
    }

    private static string FormatOperands(Instruction i) => i.Format switch
    {
        OperandFormat.None => string.Empty,

        OperandFormat.RdRsRt => $"{R(i.Rd)}, {R(i.Rs)}, {R(i.Rt)}",
        OperandFormat.RdRsRtOpt => i.Rd == 0
            ? $"{R(i.Rs)}, {R(i.Rt)}"
            : $"{R(i.Rd)}, {R(i.Rs)}, {R(i.Rt)}",
        OperandFormat.RdRtSa => $"{R(i.Rd)}, {R(i.Rt)}, {i.Sa}",
        OperandFormat.RdRtRs => $"{R(i.Rd)}, {R(i.Rt)}, {R(i.Rs)}",
        OperandFormat.RdRt => $"{R(i.Rd)}, {R(i.Rt)}",
        OperandFormat.RdRs => $"{R(i.Rd)}, {R(i.Rs)}",
        OperandFormat.RsRt => $"{R(i.Rs)}, {R(i.Rt)}",
        OperandFormat.Rs => R(i.Rs),
        OperandFormat.Rd => R(i.Rd),
        OperandFormat.Jalr => i.Rd == 31
            ? R(i.Rs)
            : $"{R(i.Rd)}, {R(i.Rs)}",

        OperandFormat.RtRsImm => $"{R(i.Rt)}, {R(i.Rs)}, {Immediate(i)}",
        OperandFormat.RtImm => $"{R(i.Rt)}, 0x{i.ImmediateUnsigned:X}",
        OperandFormat.RsImm => $"{R(i.Rs)}, {i.ImmediateSigned}",

        OperandFormat.RtMem => $"{R(i.Rt)}, {i.ImmediateSigned}({R(i.Rs)})",
        OperandFormat.FtMem => $"$f{i.Rt}, {i.ImmediateSigned}({R(i.Rs)})",
        OperandFormat.VfMem => $"$vf{i.Rt}, {i.ImmediateSigned}({R(i.Rs)})",
        OperandFormat.CacheOp => $"{i.Rt}, {i.ImmediateSigned}({R(i.Rs)})",

        OperandFormat.RsRtBranch =>
            $"{R(i.Rs)}, {R(i.Rt)}, 0x{i.BranchTarget:X8}",
        OperandFormat.RsBranch => $"{R(i.Rs)}, 0x{i.BranchTarget:X8}",
        OperandFormat.Branch => $"0x{i.BranchTarget:X8}",
        OperandFormat.Jump => $"0x{i.JumpTarget:X8}",
        OperandFormat.Code => $"0x{i.Code:X}",

        OperandFormat.RtRd => $"{R(i.Rt)}, ${i.Rd}",
        OperandFormat.RtFs => $"{R(i.Rt)}, $f{i.Rd}",
        OperandFormat.RtVf => $"{R(i.Rt)}, $vf{i.Rd}",
        OperandFormat.RtVi => $"{R(i.Rt)}, $vi{i.Rd}",

        OperandFormat.FdFsFt => $"$f{i.Sa}, $f{i.Rd}, $f{i.Rt}",
        OperandFormat.FdFs => $"$f{i.Sa}, $f{i.Rd}",
        OperandFormat.FdFt => $"$f{i.Sa}, $f{i.Rt}",
        OperandFormat.FsFt => $"$f{i.Rd}, $f{i.Rt}",

        OperandFormat.Vu0Macro => $"0x{i.Word:X8}",

        _ => $"0x{i.Word:X8}"
    };

    // Nome do registrador geral, com o prefixo $.
    private static string R(int index) => "$" + RegisterNames[index];

    // ANDI, ORI e XORI usam o imediato sem sinal; as demais, com sinal.
    private static string Immediate(Instruction i) => i.Opcode switch
    {
        Opcode.Andi or Opcode.Ori or Opcode.Xori =>
            $"0x{i.ImmediateUnsigned:X}",
        _ => i.ImmediateSigned.ToString()
    };
}
