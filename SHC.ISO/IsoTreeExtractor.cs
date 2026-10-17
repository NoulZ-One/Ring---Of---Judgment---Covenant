using SHC.Core.Models; // Importa o modelo DiscFileEntry.

namespace SHC.ISO;

/// <summary>
/// Extrai todos os arquivos de uma imagem ISO, mantendo a estrutura
/// de pastas original dentro de um diretório de saída.
/// </summary>
public static class IsoTreeExtractor
{
    /// <summary>
    /// Extrai as entradas informadas para o diretório de saída.
    /// </summary>
    /// <param name="isoPath">Caminho da imagem ISO.</param>
    /// <param name="entries">Entradas obtidas de IsoTreeReader.ListAll.</param>
    /// <param name="outputDirectory">Pasta onde a árvore será criada.</param>
    /// <param name="onFileExtracted">Chamado após cada arquivo extraído.</param>
    /// <param name="maxFileSize">
    /// Tamanho máximo, em bytes, dos arquivos a extrair. Arquivos maiores
    /// são ignorados. Quando nulo, todos os arquivos são extraídos.
    /// </param>
    /// <returns>Quantidade de arquivos extraídos.</returns>
    public static int ExtractAll(
        string isoPath,
        IReadOnlyList<DiscFileEntry> entries,
        string outputDirectory,
        Action<DiscFileEntry>? onFileExtracted = null,
        long? maxFileSize = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        // Caminho absoluto da raiz, terminado em separador, usado para
        // garantir que nenhuma entrada seja gravada fora dela.
        string root = Path.GetFullPath(outputDirectory);

        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(root);

        int extractedCount = 0;

        foreach (var entry in entries)
        {
            string relativePath = entry.Path.TrimStart('/');

            string destination = Path.GetFullPath(
                Path.Combine(root, relativePath));

            if (!destination.StartsWith(
                    rootWithSeparator,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"A entrada '{entry.Path}' resolve para fora da pasta de saída.");
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            // Ignora arquivos grandes, como os pacotes .PKB de vários GB.
            if (maxFileSize is not null && entry.Size > maxFileSize)
            {
                continue;
            }

            IsoFileExtractor.ExtractFile(isoPath, entry, destination);

            extractedCount++;
            onFileExtracted?.Invoke(entry);
        }

        return extractedCount;
    }
}
