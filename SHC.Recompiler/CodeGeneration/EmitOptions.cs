namespace SHC.Recompiler.CodeGeneration;

/// <summary>Opções da geração de código C#.</summary>
/// <param name="IncludeComments">
/// Quando verdadeiro, cada instrução recebe o assembly original como comentário
/// e cada bloco recebe um cabeçalho. Desligar reduz bastante o tamanho do código.
/// </param>
public sealed record EmitOptions(bool IncludeComments = true);
