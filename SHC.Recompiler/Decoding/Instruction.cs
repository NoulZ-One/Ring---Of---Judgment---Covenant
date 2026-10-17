namespace SHC.Recompiler.Decoding;

/// <summary>
/// Uma instrução do R5900 já decodificada, junto com a palavra original
/// e o endereço onde ela está. Os campos são extraídos da palavra sob demanda.
/// </summary>
/// <param name="Address">Endereço da instrução na memória do PS2.</param>
/// <param name="Word">Palavra de 32 bits original.</param>
/// <param name="Opcode">Instrução identificada.</param>
/// <param name="Format">Forma dos operandos.</param>
public readonly record struct Instruction(
    uint Address,
    uint Word,
    Opcode Opcode,
    OperandFormat Format)
{
    /// <summary>Registrador rs (bits 25-21). Também é a base nos acessos à memória.</summary>
    public int Rs => (int)((Word >> 21) & 0x1F);

    /// <summary>Registrador rt (bits 20-16). Também é ft nas instruções de FPU.</summary>
    public int Rt => (int)((Word >> 16) & 0x1F);

    /// <summary>Registrador rd (bits 15-11). Também é fs nas instruções de FPU.</summary>
    public int Rd => (int)((Word >> 11) & 0x1F);

    /// <summary>Quantidade de deslocamento (bits 10-6). Também é fd nas instruções de FPU.</summary>
    public int Sa => (int)((Word >> 6) & 0x1F);

    /// <summary>Imediato de 16 bits sem sinal.</summary>
    public ushort ImmediateUnsigned => (ushort)(Word & 0xFFFF);

    /// <summary>Imediato de 16 bits com sinal.</summary>
    public short ImmediateSigned => (short)(Word & 0xFFFF);

    /// <summary>Código de 20 bits de SYSCALL e BREAK.</summary>
    public uint Code => (Word >> 6) & 0xFFFFF;

    /// <summary>
    /// Alvo de J e JAL: os 4 bits altos vêm do endereço do delay slot
    /// e os demais, do campo de 26 bits multiplicado por 4.
    /// </summary>
    public uint JumpTarget =>
        ((Address + 4) & 0xF0000000) | ((Word & 0x03FFFFFF) << 2);

    /// <summary>
    /// Alvo dos desvios condicionais: relativo ao delay slot,
    /// com deslocamento de 16 bits em palavras.
    /// </summary>
    public uint BranchTarget =>
        Address + 4 + (uint)(ImmediateSigned << 2);
}
