namespace SHC.Core.Models;

/// <summary>
/// Representa um arquivo ou diretório encontrado dentro de uma imagem de disco.
/// </summary>
/// <param name="Name">Nome da entrada no sistema de arquivos.</param>
/// <param name="Path">Caminho da entrada dentro do disco.</param>
/// <param name="Offset">Posição inicial dos dados, em bytes, na imagem.</param>
/// <param name="Size">Tamanho dos dados, em bytes.</param>
/// <param name="IsDirectory">Indica se a entrada representa um diretório.</param>
public sealed record DiscFileEntry(
    string Name,
    string Path,
    long Offset,
    long Size,
    bool IsDirectory);