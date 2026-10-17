namespace SHC.Recompiler;

/// <summary>
/// Bloco de código e dados que será recompilado como uma unidade:
/// o executável principal ou um dos overlays.
/// </summary>
/// <param name="Name">Nome da unidade (arquivo de origem ou nome interno).</param>
/// <param name="BaseAddress">Endereço de memória do primeiro byte de Data.</param>
/// <param name="Data">Bytes que existem no arquivo, a partir de BaseAddress.</param>
/// <param name="EntryPoint">Ponto de entrada do executável; nulo nos overlays.</param>
public sealed record RecompilationUnit(
    string Name,
    uint BaseAddress,
    byte[] Data,
    uint? EntryPoint = null)
{
    /// <summary>Primeiro endereço após o último byte de Data.</summary>
    public uint EndAddress => BaseAddress + (uint)Data.Length;
}
