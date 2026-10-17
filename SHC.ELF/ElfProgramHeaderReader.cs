
using System;
using System.Collections.Generic;
using System.IO;
using SHC.ELF.Models;

namespace SHC.ELF;

/// <summary>
/// Lê a tabela de cabeçalhos de programa de um arquivo ELF32.
/// </summary>
public static class ElfProgramHeaderReader
{
    /// <summary>
    /// Lê as entradas da tabela de cabeçalhos de programa.
    /// </summary>
    /// <param name="filePath">Caminho do executável ELF.</param>
    /// <param name="header">Cabeçalho ELF previamente interpretado.</param>
    /// <returns>Lista com os cabeçalhos de programa encontrados.</returns>
    public static List<ElfProgramHeaderInfo> Read(
        string filePath,
        ElfHeaderInfo header)
    {
        // Valida os argumentos recebidos.
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "O caminho do arquivo não pode estar vazio.",
                nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "O arquivo ELF não foi encontrado.",
                filePath);
        }

        if (header is null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        // Abre o arquivo exclusivamente para leitura.
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);

        // Cada entrada ELF32 padrão possui 32 bytes.
        const ushort expectedEntrySize = 32;

        // Rejeita tabelas com tamanho de entrada incompatível com ELF32.
        if (header.ProgramHeaderEntrySize != expectedEntrySize)
        {
            throw new InvalidDataException(
                $"Tamanho inesperado de entrada: {header.ProgramHeaderEntrySize}. " +
                $"Esperado: {expectedEntrySize} bytes.");
        }

        // Calcula o tamanho total da tabela usando long para evitar
        // estouro aritmético durante a validação dos limites.
        long tableOffset = header.ProgramHeaderOffset;
        long tableSize = (long)header.ProgramHeaderEntrySize *
                         header.ProgramHeaderCount;

        // Confirma que a tabela inteira está dentro do arquivo.
        if (tableOffset < 0 ||
            tableSize < 0 ||
            tableOffset > stream.Length ||
            tableSize > stream.Length - tableOffset)
        {
            throw new InvalidDataException(
                "A tabela de cabeçalhos de programa ultrapassa os limites do arquivo.");
        }

        // Prepara a lista que armazenará os segmentos encontrados.
        var programHeaders = new List<ElfProgramHeaderInfo>(
            header.ProgramHeaderCount);

        // Posiciona a leitura no início da tabela.
        stream.Position = tableOffset;

        // Percorre cada entrada conforme a quantidade declarada no ELF.
        for (int i = 0; i < header.ProgramHeaderCount; i++)
        {
            // Lê os campos da entrada ELF32 na ordem definida pelo formato.
            uint type = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            uint virtualAddress = reader.ReadUInt32();
            uint physicalAddress = reader.ReadUInt32();
            uint fileSize = reader.ReadUInt32();
            uint memorySize = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();
            uint alignment = reader.ReadUInt32();

            // Valida os limites do segmento dentro do arquivo.
            // Segmentos como PT_LOAD normalmente usam esses campos para
            // descrever os bytes que serão copiados para a memória.
            if (type == 1 &&
                ((long)offset > stream.Length ||
                 (long)fileSize > stream.Length - offset))
            {
                throw new InvalidDataException(
                    $"O segmento {i} declara dados fora dos limites do arquivo.");
            }

            // Armazena os campos interpretados em um modelo.
            programHeaders.Add(
                new ElfProgramHeaderInfo(
                    type,
                    offset,
                    virtualAddress,
                    physicalAddress,
                    fileSize,
                    memorySize,
                    flags,
                    alignment));
        }

        // Retorna todas as entradas lidas.
        return programHeaders;
    }
}
