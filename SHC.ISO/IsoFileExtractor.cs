
using SHC.Core.Models; // Importa o modelo que representa arquivos da ISO.

namespace SHC.ISO;

/// <summary>
/// Extrai arquivos de uma imagem ISO para arquivos independentes.
/// A imagem original é aberta somente para leitura.
/// </summary>
public static class IsoFileExtractor
{
    /// <summary>
    /// Extrai um arquivo usando sua posição e seu tamanho registrados na ISO.
    /// </summary>
    /// <param name="isoPath">Caminho da imagem ISO de origem.</param>
    /// <param name="entry">Informações do arquivo a extrair.</param>
    /// <param name="destinationPath">Caminho do arquivo extraído.</param>
    public static void ExtractFile(
        string isoPath,
        DiscFileEntry entry,
        string destinationPath)
    {
        // Valida os argumentos obrigatórios.
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        // Não permite tentar extrair um diretório como se fosse um arquivo.
        if (entry.IsDirectory)
        {
            throw new InvalidOperationException(
                "A entrada selecionada é um diretório, não um arquivo.");
        }

        // Confirma que a imagem de origem existe.
        if (!File.Exists(isoPath))
        {
            throw new FileNotFoundException(
                "A imagem ISO não foi encontrada.",
                isoPath);
        }

        // Abre a ISO em modo somente leitura.
        using var input = File.OpenRead(isoPath);

        // Confirma que o deslocamento e o tamanho estão dentro da imagem.
        // A subtração evita somar offset e size antes de validar os limites.
        if (entry.Offset < 0 ||
            entry.Size < 0 ||
            entry.Offset > input.Length ||
            entry.Size > input.Length - entry.Offset)
        {
            throw new InvalidDataException(
                "A posição ou o tamanho do arquivo ultrapassa os limites da ISO.");
        }

        // Posiciona a leitura no início dos dados do arquivo desejado.
        input.Position = entry.Offset;

        // Obtém a pasta de destino e cria-a se ainda não existir.
        string? destinationDirectory =
            Path.GetDirectoryName(Path.GetFullPath(destinationPath));

        if (destinationDirectory is not null)
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        // Cria o arquivo de destino ou substitui um arquivo existente.
        using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        // Usa um buffer para copiar os dados sem carregar o arquivo inteiro
        // na memória, algo importante para arquivos grandes.
        byte[] buffer = new byte[81920];

        // Controla quantos bytes ainda precisam ser copiados.
        long remaining = entry.Size;

        while (remaining > 0)
        {
            // Lê no máximo o tamanho do buffer ou os bytes restantes.
            int bytesToRead = (int)Math.Min(buffer.Length, remaining);

            // Lê os dados a partir da posição atual da ISO.
            int bytesRead = input.Read(
                buffer,
                0,
                bytesToRead);

            // Detecta uma leitura incompleta inesperada.
            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    "A ISO terminou antes que o arquivo fosse extraído por completo.");
            }

            // Grava os bytes lidos no arquivo de destino.
            output.Write(buffer, 0, bytesRead);

            // Atualiza a quantidade de dados que ainda falta copiar.
            remaining -= bytesRead;
        }
    }
}
