using System.Text;             // Permite converter bytes em texto.
using SHC.Core.Models;         // Importa os modelos compartilhados.

// Agrupa os recursos responsáveis pela leitura de imagens ISO.
namespace SHC.ISO;

public static class IsoImageReader
{
    // Uma ISO 9660 utiliza setores lógicos de 2048 bytes.
    private const int SectorSize = 2048;

    // O descritor primário de volume fica no setor lógico 16.
    private const int PrimaryVolumeDescriptorSector = 16;

    // Analisa informações básicas da imagem, sem modificar o arquivo.
    public static DiscImageInfo Inspect(string filePath)
    {
        // Impede que um caminho vazio ou composto apenas por espaços seja usado.
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Confirma que o arquivo indicado realmente existe.
        if (!File.Exists(filePath))
        {
            // Interrompe a operação e informa qual arquivo não foi encontrado.
            throw new FileNotFoundException(
                "The ISO image was not found.",
                filePath);
        }

        // Abre a imagem somente para leitura e libera o arquivo ao sair do bloco.
        using var stream = File.OpenRead(filePath);

        // Calcula a posição, em bytes, do setor que contém o descritor.
        long descriptorOffset =
            (long)PrimaryVolumeDescriptorSector * SectorSize;

        // Confere se o arquivo possui espaço suficiente para ler o setor inteiro.
        if (stream.Length < descriptorOffset + SectorSize)
        {
            // Uma imagem menor que isso não pode conter o descritor esperado.
            throw new InvalidDataException(
                "The file is too small to contain an ISO 9660 volume descriptor.");
        }

        // Posiciona a leitura no início do setor 16.
        stream.Position = descriptorOffset;

        // Reserva memória para armazenar os 2048 bytes do setor.
        byte[] sector = new byte[SectorSize];

        // Lê o setor completo ou lança uma exceção se os dados forem insuficientes.
        stream.ReadExactly(sector);

        // O primeiro byte deve identificar um descritor primário de volume.
        bool isPrimaryVolumeDescriptor = sector[0] == 1;

        // Os cinco bytes seguintes devem conter a assinatura "CD001".
        bool hasIso9660Signature =
            Encoding.ASCII.GetString(sector, 1, 5) == "CD001";

        // O byte 6 indica a versão do descritor; esperamos a versão 1.
        bool hasSupportedVersion = sector[6] == 1;

        // Rejeita descritores que não correspondam ao formato esperado.
        if (!isPrimaryVolumeDescriptor ||
            !hasIso9660Signature ||
            !hasSupportedVersion)
        {
            throw new InvalidDataException(
                "The image does not contain a valid ISO 9660 primary volume descriptor.");
        }

        // Lê os 32 bytes reservados ao identificador do volume.
        // Remove espaços e caracteres nulos das extremidades.
        string volumeLabel = Encoding.ASCII
            .GetString(sector, 40, 32)
            .Trim(' ', '\0');

        // Retorna o caminho absoluto da imagem e o identificador encontrado.
        return new DiscImageInfo(
            Path.GetFullPath(filePath),
            volumeLabel);
    }
}