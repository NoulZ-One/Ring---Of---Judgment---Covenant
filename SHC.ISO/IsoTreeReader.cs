using System.Buffers.Binary; // Interpreta números armazenados em bytes.
using System.Text;            // Converte os nomes dos arquivos em texto.
using SHC.Core.Models;        // Importa o modelo DiscFileEntry.

namespace SHC.ISO;

/// <summary>
/// Lê a árvore completa de uma imagem ISO 9660, percorrendo todos os
/// subdiretórios a partir da raiz. A imagem é aberta somente para leitura.
/// </summary>
public static class IsoTreeReader
{
    // Tamanho de um setor lógico ISO 9660, em bytes.
    private const int SectorSize = 2048;

    // Setor que contém o descritor primário de volume.
    private const int PrimaryVolumeDescriptorSector = 16;

    // Tamanho mínimo de um registro de diretório ISO 9660.
    private const int MinimumRecordLength = 34;

    // Limite de profundidade para proteger contra estruturas corrompidas.
    private const int MaxDepth = 32;

    /// <summary>
    /// Retorna todos os arquivos e diretórios da imagem.
    /// Cada diretório aparece antes do seu conteúdo e os caminhos usam "/".
    /// </summary>
    public static IReadOnlyList<DiscFileEntry> ListAll(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "A imagem ISO não foi encontrada.",
                filePath);
        }

        using var stream = File.OpenRead(filePath);

        // Localiza o diretório raiz a partir do descritor primário.
        (uint rootExtent, uint rootSize) = ReadRootRecord(stream);

        var entries = new List<DiscFileEntry>();

        // Guarda os diretórios já visitados para evitar laços infinitos.
        var visitedDirectories = new HashSet<uint>();

        ReadDirectory(
            stream,
            rootExtent,
            rootSize,
            parentPath: string.Empty,
            depth: 0,
            entries,
            visitedDirectories);

        return entries;
    }

    /// <summary>
    /// Lê o descritor primário e devolve o setor inicial e o tamanho
    /// do diretório raiz.
    /// </summary>
    private static (uint Extent, uint Size) ReadRootRecord(FileStream stream)
    {
        long descriptorOffset =
            (long)PrimaryVolumeDescriptorSector * SectorSize;

        if (stream.Length < descriptorOffset + SectorSize)
        {
            throw new InvalidDataException(
                "A imagem é pequena demais para conter o descritor ISO 9660.");
        }

        byte[] descriptor = new byte[SectorSize];

        stream.Position = descriptorOffset;
        stream.ReadExactly(descriptor);

        if (descriptor[0] != 1 ||
            Encoding.ASCII.GetString(descriptor, 1, 5) != "CD001" ||
            descriptor[6] != 1)
        {
            throw new InvalidDataException(
                "O descritor primário ISO 9660 não é válido.");
        }

        // O registro do diretório raiz começa no byte 156 do descritor.
        const int rootRecordOffset = 156;

        if (descriptor[rootRecordOffset] < MinimumRecordLength)
        {
            throw new InvalidDataException(
                "O registro do diretório raiz é inválido.");
        }

        uint extent = BinaryPrimitives.ReadUInt32LittleEndian(
            descriptor.AsSpan(rootRecordOffset + 2, 4));

        uint size = BinaryPrimitives.ReadUInt32LittleEndian(
            descriptor.AsSpan(rootRecordOffset + 10, 4));

        return (extent, size);
    }

    /// <summary>
    /// Lê um diretório e, ao encontrar subdiretórios, desce recursivamente.
    /// </summary>
    private static void ReadDirectory(
        FileStream stream,
        uint extent,
        uint size,
        string parentPath,
        int depth,
        List<DiscFileEntry> entries,
        HashSet<uint> visitedDirectories)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException(
                "A hierarquia de diretórios é profunda demais.");
        }

        // Ignora um diretório já lido, o que indica referência circular.
        if (!visitedDirectories.Add(extent))
        {
            return;
        }

        long directoryOffset = (long)extent * SectorSize;

        if (size > int.MaxValue ||
            directoryOffset > stream.Length ||
            (long)size > stream.Length - directoryOffset)
        {
            throw new InvalidDataException(
                $"O diretório '{parentPath}/' aponta para fora da imagem.");
        }

        byte[] data = new byte[(int)size];

        stream.Position = directoryOffset;
        stream.ReadExactly(data);

        int position = 0;

        while (position < data.Length)
        {
            int recordLength = data[position];

            // Tamanho zero indica preenchimento até o próximo setor.
            if (recordLength == 0)
            {
                position = ((position / SectorSize) + 1) * SectorSize;
                continue;
            }

            if (recordLength < MinimumRecordLength ||
                position + recordLength > data.Length)
            {
                throw new InvalidDataException(
                    "Foi encontrado um registro de diretório inválido.");
            }

            ReadOnlySpan<byte> record = data.AsSpan(position, recordLength);

            position += recordLength;

            bool isDirectory = (record[25] & 0x02) != 0;
            int identifierLength = record[32];

            if (33 + identifierLength > recordLength)
            {
                throw new InvalidDataException(
                    "O nome de uma entrada ultrapassa os limites do registro.");
            }

            ReadOnlySpan<byte> identifier = record.Slice(33, identifierLength);

            // Os identificadores 0 e 1 são "." e "..".
            if (identifierLength == 1 &&
                (identifier[0] == 0 || identifier[0] == 1))
            {
                continue;
            }

            string name = NormalizeName(identifier);

            uint entryExtent = BinaryPrimitives.ReadUInt32LittleEndian(
                record.Slice(2, 4));

            uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(
                record.Slice(10, 4));

            string entryPath = parentPath + "/" + name;

            entries.Add(new DiscFileEntry(
                name,
                entryPath,
                (long)entryExtent * SectorSize,
                entrySize,
                isDirectory));

            if (isDirectory)
            {
                ReadDirectory(
                    stream,
                    entryExtent,
                    entrySize,
                    entryPath,
                    depth + 1,
                    entries,
                    visitedDirectories);
            }
        }
    }

    /// <summary>
    /// Converte o identificador ISO 9660 em um nome legível, removendo
    /// o sufixo de versão (";1") e o ponto final de arquivos sem extensão.
    /// </summary>
    private static string NormalizeName(ReadOnlySpan<byte> identifier)
    {
        string name = Encoding.ASCII.GetString(identifier);

        int versionSeparator = name.LastIndexOf(';');

        if (versionSeparator >= 0)
        {
            name = name[..versionSeparator];
        }

        // Em ISO 9660 nível 1, "ARQUIVO" é gravado como "ARQUIVO.".
        if (name.Length > 1 && name.EndsWith('.'))
        {
            name = name[..^1];
        }

        return name;
    }
}
