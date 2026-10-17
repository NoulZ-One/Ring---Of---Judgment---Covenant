namespace SHC.Recompiler.Decoding;

/// <summary>Utilidades para o enum <see cref="Opcode"/>.</summary>
public static class OpcodeExtensions
{
    /// <summary>
    /// Mnemônico em minúsculas, com ponto no lugar do sublinhado
    /// (Add_S vira "add.s").
    /// </summary>
    public static string GetMnemonic(this Opcode opcode) =>
        opcode.ToString().ToLowerInvariant().Replace('_', '.');
}
