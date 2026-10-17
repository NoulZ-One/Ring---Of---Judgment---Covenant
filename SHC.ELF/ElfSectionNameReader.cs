
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SHC.ELF.Models;

namespace SHC.ELF;

/// <summary>
/// Resolve os nomes das seções ELF usando a tabela de strings indicada
/// pelo cabeçalho principal.
/// </summary>
public static class ElfSectionNameReader
{
    /// <summary>
    /// Obtém o nome de cada seção a partir de sua tabela de strings.
    /// </summary>
    /// <param name="filePath">Caminho do executável ELF.</param>
    /// <param name="header">Cabeçalho ELF previamente interpretado.</param>
    /// <param name="sections">Seções lidas do arquivo.</param>
    /// <returns>Lista de nomes na mesma ordem das seções.</returns>
    public static List<string> ReadNames(
        string filePath,
        ElfHeaderInfo header,
        IReadOnlyList<ElfSectionHeaderInfo> sections)
    {
        // Valida os argumentos antes de acessar o arquivo.
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

        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(sections);

        // O índice especial 0 indica que não existe tabela de nomes de seções.
        if (header.SectionNameStringTableIndex == 0)
        {
            return CreateEmptyNames(sections.Count);
        }

        // Confirma que o índice da tabela aponta para uma seção existente.
        if (header.SectionNameStringTableIndex >= sections.Count)
        {
            throw new InvalidDataException(
                "O índice da tabela de nomes aponta para uma seção inexistente.");
        }

        // Obtém a seção que contém os nomes das outras seções.
        var stringTableSection =
            sections[header.SectionNameStringTableIndex];

        // SHT_STRTAB, tipo 3, identifica uma tabela de strings ELF.
        if (stringTableSection.Type != 3)
        {
            throw new InvalidDataException(
                "A seção indicada como tabela de nomes não é uma SHT_STRTAB.");
        }

        // Lê os bytes da tabela de strings.
        byte[] stringTable;

        using (var stream = File.OpenRead(filePath))
        {
            // Verifica se a tabela de strings está dentro do arquivo.
            if ((long)stringTableSection.Offset > stream.Length ||
                (long)stringTableSection.Size >
                stream.Length - stringTableSection.Offset)
            {
                throw new InvalidDataException(
                    "A tabela de strings ultrapassa os limites do arquivo.");
            }

            // Posiciona o arquivo no início da tabela.
            stream.Position = stringTableSection.Offset;

            // Lê exatamente o tamanho declarado para a tabela.
            stringTable = new byte[checked((int)stringTableSection.Size)];

            int totalRead = 0;

            // Continua lendo até obter todos os bytes solicitados.
            while (totalRead < stringTable.Length)
            {
                int bytesRead = stream.Read(
                    stringTable,
                    totalRead,
                    stringTable.Length - totalRead);

                // Uma leitura de zero bytes indica fim inesperado do arquivo.
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException(
                        "A tabela de strings terminou antes do esperado.");
                }

                totalRead += bytesRead;
            }
        }

        // Cria a lista de nomes preservando a ordem das seções.
        var names = new List<string>(sections.Count);

        foreach (var section in sections)
        {
            // O índice zero representa um nome vazio.
            if (section.NameIndex == 0)
            {
                names.Add(string.Empty);
                continue;
            }

            // Verifica se o índice do nome cabe dentro da tabela de strings.
            if (section.NameIndex >= stringTable.Length)
            {
                throw new InvalidDataException(
                    $"O índice de nome {section.NameIndex} está fora da tabela.");
            }

            // Procura o byte zero que encerra a string.
            int start = (int)section.NameIndex;
            int end = start;

            while (end < stringTable.Length && stringTable[end] != 0)
            {
                end++;
            }

            // Converte os bytes do nome em texto ASCII.
            string name = Encoding.ASCII.GetString(
                stringTable,
                start,
                end - start);

            names.Add(name);
        }

        // Retorna os nomes na mesma ordem da lista de seções.
        return names;
    }

    /// <summary>
    /// Cria uma lista de nomes vazios quando não existe tabela de nomes.
    /// </summary>
    private static List<string> CreateEmptyNames(int count)
    {
        // Mantém a quantidade de elementos igual à quantidade de seções.
        var names = new List<string>(count);

        for (int i = 0; i < count; i++)
        {
            names.Add(string.Empty);
        }

        return names;
    }
}
