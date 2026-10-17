
using System;
using System.Collections.Generic;
using System.IO;
using SHC.ELF.Models;

namespace SHC.ELF;

/// <summary>
/// Lê a tabela de cabeçalhos de seção de um arquivo ELF32.
/// </summary>
public static class ElfSectionHeaderReader
{
    /// <summary>
    /// Lê as entradas da tabela de seções ELF.
    /// </summary>
    /// <param name="filePath">Caminho do executável ELF.</param>
    /// <param name="header">Cabeçalho ELF previamente interpretado.</param>
    /// <returns>Lista com os cabeçalhos de seção encontrados.</returns>
    public static List<ElfSectionHeaderInfo> Read(
        string filePath,
        ElfHeaderInfo header)
    {
        // Valida o caminho recebido.
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "O caminho do arquivo não pode estar vazio.",
                nameof(filePath));
        }

        // Confirma que o arquivo existe.
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "O arquivo ELF não foi encontrado.",
                filePath);
        }

        // Confirma que recebemos um cabeçalho válido.
        if (header is null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        // Abre o arquivo somente para leitura.
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);

        // Cada entrada ELF32 padrão possui 40 bytes.
        const ushort expectedEntrySize = 40;

        // Verifica se o tamanho das entradas corresponde ao ELF32.
        if (header.SectionHeaderEntrySize != expectedEntrySize)
        {
            throw new InvalidDataException(
                $"Tamanho inesperado de entrada: {header.SectionHeaderEntrySize}. " +
                $"Esperado: {expectedEntrySize} bytes.");
        }

        // Calcula o tamanho total da tabela.
        // O tipo long evita estouros durante as operações de validação.
        long tableOffset = header.SectionHeaderOffset;
        long tableSize = (long)header.SectionHeaderEntrySize *
                         header.SectionHeaderCount;

        // Verifica se a tabela inteira está dentro do arquivo.
        if (tableOffset < 0 ||
            tableOffset > stream.Length ||
            tableSize > stream.Length - tableOffset)
        {
            throw new InvalidDataException(
                "A tabela de seções ultrapassa os limites do arquivo.");
        }

        // Cria a lista que armazenará as entradas lidas.
        var sections = new List<ElfSectionHeaderInfo>(
            header.SectionHeaderCount);

        // Posiciona o fluxo no início da tabela de seções.
        stream.Position = tableOffset;

        // Lê cada entrada de acordo com a quantidade declarada no cabeçalho.
        for (int i = 0; i < header.SectionHeaderCount; i++)
        {
            // Lê os dez campos de 32 bits da entrada ELF32.
            uint nameIndex = reader.ReadUInt32();
            uint type = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();
            uint address = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            uint size = reader.ReadUInt32();
            uint link = reader.ReadUInt32();
            uint info = reader.ReadUInt32();
            uint addressAlignment = reader.ReadUInt32();
            uint entrySize = reader.ReadUInt32();

            // Alguns tipos de seção não ocupam espaço no arquivo.
            // SHT_NOBITS possui o valor 8 e não exige dados armazenados.
            // Para os outros tipos, verificamos se o intervalo é válido.
            if (type != 8 &&
                ((long)offset > stream.Length ||
                 (long)size > stream.Length - offset))
            {
                throw new InvalidDataException(
                    $"A seção {i} declara dados fora dos limites do arquivo.");
            }

            // Cria o modelo correspondente à entrada atual.
            sections.Add(
                new ElfSectionHeaderInfo(
                    nameIndex,
                    type,
                    flags,
                    address,
                    offset,
                    size,
                    link,
                    info,
                    addressAlignment,
                    entrySize));
        }

        // Retorna todas as seções encontradas.
        return sections;
    }
}
