
using System.Buffers.Binary; // Permite interpretar números armazenados em bytes.
using System.Text;            // Permite converter os nomes dos arquivos em texto.
using SHC.Core.Models;        // Importa o modelo DiscFileEntry.

namespace SHC.ISO;

/// <summary>
/// Lê as entradas do diretório raiz de uma imagem ISO 9660.
/// Nesta primeira versão, não percorre subdiretórios recursivamente.
/// </summary>
public static class IsoDirectoryReader
{
    // Tamanho de um setor lógico ISO 9660, em bytes.
    private const int SectorSize = 2048;

    // Setor que contém o descritor primário de volume ISO 9660.
    private const int PrimaryVolumeDescriptorSector = 16;

    /// <summary>
    /// Lê o diretório raiz e retorna seus arquivos e subdiretórios.
    /// A imagem é aberta somente para leitura.
    /// </summary>
    public static IReadOnlyList<DiscFileEntry> ListRootDirectory(
        string filePath)
    {
        // Impede que o método receba um caminho vazio.
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Confirma que o arquivo existe antes de tentar abri-lo.
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "A imagem ISO não foi encontrada.",
                filePath);
        }

        // Abre a ISO somente para leitura.
        using var stream = File.OpenRead(filePath);

        // Calcula a posição, em bytes, do setor 16.
        long descriptorOffset =
            (long)PrimaryVolumeDescriptorSector * SectorSize;

        // Confirma que o arquivo contém o setor completo.
        if (stream.Length < descriptorOffset + SectorSize)
        {
            throw new InvalidDataException(
                "A imagem é pequena demais para conter o descritor ISO 9660.");
        }

        // Lê o descritor primário de volume.
        byte[] descriptor = new byte[SectorSize];

        stream.Position = descriptorOffset;
        stream.ReadExactly(descriptor);

        // Confere o tipo, a assinatura e a versão do descritor.
        if (descriptor[0] != 1 ||
            Encoding.ASCII.GetString(descriptor, 1, 5) != "CD001" ||
            descriptor[6] != 1)
        {
            throw new InvalidDataException(
                "O descritor primário ISO 9660 não é válido.");
        }

        // O registro do diretório raiz começa no byte 156 do descritor.
        const int rootRecordOffset = 156;

        // Obtém o tamanho do registro do diretório raiz.
        int rootRecordLength = descriptor[rootRecordOffset];

        // Um registro de diretório ISO 9660 precisa ter ao menos 34 bytes.
        if (rootRecordLength < 34)
        {
            throw new InvalidDataException(
                "O registro do diretório raiz é inválido.");
        }

        // Lê o setor inicial do diretório raiz.
        // O campo é armazenado em formato little-endian nos bytes 2 a 5.
        uint rootExtent = BinaryPrimitives.ReadUInt32LittleEndian(
            descriptor.AsSpan(rootRecordOffset + 2, 4));

        // Lê o tamanho total do diretório raiz, em bytes.
        // O campo fica nos bytes 10 a 13 do registro.
        uint rootSize = BinaryPrimitives.ReadUInt32LittleEndian(
            descriptor.AsSpan(rootRecordOffset + 10, 4));

        // Converte o setor inicial em uma posição absoluta na imagem.
        long rootOffset = (long)rootExtent * SectorSize;

        // Impede tamanhos incompatíveis com um array do .NET.
        if (rootSize > int.MaxValue)
        {
            throw new InvalidDataException(
                "O diretório raiz é grande demais para esta implementação.");
        }

        // Confere se a região do diretório está dentro da imagem.
        if (rootOffset > stream.Length ||
            rootSize > (ulong)(stream.Length - rootOffset))
        {
            throw new InvalidDataException(
                "O diretório raiz aponta para uma região fora da imagem.");
        }

        // Lê o conteúdo completo do diretório raiz para a memória.
        byte[] directoryData = new byte[(int)rootSize];

        stream.Position = rootOffset;
        stream.ReadExactly(directoryData);

        // Armazena as entradas encontradas no diretório.
        var entries = new List<DiscFileEntry>();

        // Percorre os registros que compõem o diretório.
        int position = 0;

        while (position < directoryData.Length)
        {
            // O primeiro byte informa o tamanho do registro atual.
            int recordLength = directoryData[position];

            // Um registro com tamanho zero indica preenchimento até
            // o próximo limite de setor.
            if (recordLength == 0)
            {
                // Avança para o início do próximo setor de 2048 bytes.
                position = ((position / SectorSize) + 1) * SectorSize;
                continue;
            }

            // Verifica se o registro cabe nos dados que foram lidos.
            if (recordLength < 34 ||
                position + recordLength > directoryData.Length)
            {
                throw new InvalidDataException(
                    "Foi encontrado um registro de diretório inválido.");
            }

            // Obtém os bytes que formam o registro atual.
            ReadOnlySpan<byte> record = directoryData.AsSpan(
                position,
                recordLength);

            // O byte 25 contém as flags do registro.
            // O bit 0x02 indica que a entrada é um diretório.
            bool isDirectory = (record[25] & 0x02) != 0;

            // O byte 32 informa o comprimento do identificador.
            int identifierLength = record[32];

            // Confere se o nome cabe dentro do registro.
            if (33 + identifierLength > recordLength)
            {
                throw new InvalidDataException(
                    "O nome de uma entrada ultrapassa os limites do registro.");
            }

            // Obtém os bytes que representam o nome da entrada.
            ReadOnlySpan<byte> identifier = record.Slice(
                33,
                identifierLength);

            // Os identificadores especiais 0 e 1 representam,
            // respectivamente, o diretório atual e o diretório pai.
            bool isSpecialEntry =
                identifierLength == 1 &&
                (identifier[0] == 0 || identifier[0] == 1);

            // Não adicionamos as entradas especiais à lista.
            if (!isSpecialEntry)
            {
                // Converte o identificador ISO 9660 em texto.
                string name = Encoding.ASCII.GetString(identifier);

                // Remove o sufixo de versão, como ";1", quando existir.
                // Isso deixa os nomes mais legíveis no relatório.
                int versionSeparator = name.LastIndexOf(';');

                if (versionSeparator >= 0)
                {
                    name = name[..versionSeparator];
                }

                // Lê o setor inicial dos dados desta entrada.
                uint extent = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(2, 4));

                // Lê o tamanho da entrada, em bytes.
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(10, 4));

                // Converte o setor inicial para um deslocamento em bytes.
                long offset = (long)extent * SectorSize;

                // Monta um caminho relativo à raiz do disco.
                string entryPath = "/" + name;

                // Cria o modelo compartilhado que representa a entrada.
                entries.Add(new DiscFileEntry(
                    name,
                    entryPath,
                    offset,
                    size,
                    isDirectory));
            }

            // Avança para o próximo registro dentro do diretório.
            position += recordLength;
        }

        // Retorna todas as entradas encontradas no diretório raiz.
        return entries;
    }
}
