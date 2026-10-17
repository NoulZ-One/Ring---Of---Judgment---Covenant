
using System;
using System.IO;
using SHC.ELF.Models;

namespace SHC.ELF;

/// <summary>
/// Lê e interpreta o cabeçalho de um arquivo ELF32.
/// </summary>
public static class ElfHeaderReader
{
    // Tamanho padrão do cabeçalho ELF32, em bytes.
    private const int Elf32HeaderSize = 52;

    // Identifica o formato ELF pelos quatro primeiros bytes.
    private static readonly byte[] ElfMagic =
    {
        0x7F, (byte)'E', (byte)'L', (byte)'F'
    };

    /// <summary>
    /// Abre um executável ELF e extrai os campos do cabeçalho.
    /// </summary>
    /// <param name="filePath">Caminho do arquivo ELF.</param>
    /// <returns>Modelo contendo os campos interpretados do cabeçalho.</returns>
    public static ElfHeaderInfo Read(string filePath)
    {
        // Verifica se o caminho foi informado corretamente.
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "O caminho do arquivo não pode estar vazio.",
                nameof(filePath));
        }

        // Verifica se o arquivo existe antes de tentar abri-lo.
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "O arquivo ELF não foi encontrado.",
                filePath);
        }

        // Abre o arquivo somente para leitura.
        using var stream = File.OpenRead(filePath);

        // Um cabeçalho ELF32 precisa ter pelo menos 52 bytes.
        if (stream.Length < Elf32HeaderSize)
        {
            throw new InvalidDataException(
                "O arquivo é pequeno demais para conter um cabeçalho ELF32 completo.");
        }

        // Lê os 16 primeiros bytes, que formam o campo e_ident.
        byte[] identification = new byte[16];

        using var reader = new BinaryReader(stream);

        int bytesRead = reader.Read(identification, 0, identification.Length);

        // Confirma que todos os bytes de identificação foram lidos.
        if (bytesRead != identification.Length)
        {
            throw new InvalidDataException(
                "Não foi possível ler a identificação completa do ELF.");
        }

        // Confere a assinatura: 0x7F seguido pelos caracteres E, L e F.
        for (int i = 0; i < ElfMagic.Length; i++)
        {
            if (identification[i] != ElfMagic[i])
            {
                throw new InvalidDataException(
                    "A assinatura ELF não foi encontrada no arquivo.");
            }
        }

        // EI_CLASS = 1 indica ELF32; EI_DATA = 1 indica little-endian.
        if (identification[4] != 1)
        {
            throw new InvalidDataException(
                $"Classe ELF não suportada: {identification[4]}. Esperado: ELF32.");
        }

        if (identification[5] != 1)
        {
            throw new InvalidDataException(
                $"Ordem de bytes não suportada: {identification[5]}. Esperado: little-endian.");
        }

        // EI_VERSION deve indicar a versão inicial do formato ELF.
        if (identification[6] != 1)
        {
            throw new InvalidDataException(
                $"Versão ELF inválida: {identification[6]}.");
        }

        // Posiciona a leitura no início dos campos principais do cabeçalho,
        // imediatamente depois dos 16 bytes de identificação.
        stream.Position = 16;

        // Lê os campos do ELF32. BinaryReader interpreta inteiros
        // no formato little-endian, adequado para este cabeçalho.
        ushort fileType = reader.ReadUInt16();
        ushort machine = reader.ReadUInt16();
        uint version = reader.ReadUInt32();
        uint entryPoint = reader.ReadUInt32();
        uint programHeaderOffset = reader.ReadUInt32();
        uint sectionHeaderOffset = reader.ReadUInt32();
        uint flags = reader.ReadUInt32();
        ushort headerSize = reader.ReadUInt16();
        ushort programHeaderEntrySize = reader.ReadUInt16();
        ushort programHeaderCount = reader.ReadUInt16();
        ushort sectionHeaderEntrySize = reader.ReadUInt16();
        ushort sectionHeaderCount = reader.ReadUInt16();
        ushort sectionNameStringTableIndex = reader.ReadUInt16();

        // Confere se o tamanho declarado do cabeçalho corresponde ao ELF32.
        if (headerSize != Elf32HeaderSize)
        {
            throw new InvalidDataException(
                $"Tamanho de cabeçalho inesperado: {headerSize} bytes. Esperado: {Elf32HeaderSize}.");
        }

        // Devolve os campos lidos em um objeto organizado.
        return new ElfHeaderInfo(
            fileType,
            machine,
            version,
            entryPoint,
            programHeaderOffset,
            sectionHeaderOffset,
            flags,
            headerSize,
            programHeaderEntrySize,
            programHeaderCount,
            sectionHeaderEntrySize,
            sectionHeaderCount,
            sectionNameStringTableIndex);
    }
}
