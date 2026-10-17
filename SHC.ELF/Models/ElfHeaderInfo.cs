namespace SHC.ELF.Models;

/// <summary>
/// Armazena os dados principais encontrados no cabeçalho de um arquivo ELF.
/// Esses dados permitem identificar a arquitetura e localizar a entrada do programa.
/// </summary>
public sealed record ElfHeaderInfo(
    // Tipo de arquivo ELF: executável, objeto relocável ou outro tipo.
    ushort FileType,

    // Identificador da arquitetura da máquina de destino.
    ushort Machine,

    // Versão do formato ELF.
    uint Version,

    // Endereço virtual em que a execução do programa deve começar.
    uint EntryPoint,

    // Posição, em bytes, da tabela de cabeçalhos de programa dentro do arquivo.
    uint ProgramHeaderOffset,

    // Posição, em bytes, da tabela de cabeçalhos de seção dentro do arquivo.
    uint SectionHeaderOffset,

    // Flags específicas da arquitetura de destino.
    uint Flags,

    // Tamanho do cabeçalho ELF, em bytes.
    ushort HeaderSize,

    // Tamanho de cada entrada da tabela de cabeçalhos de programa.
    ushort ProgramHeaderEntrySize,

    // Quantidade de entradas na tabela de cabeçalhos de programa.
    ushort ProgramHeaderCount,

    // Tamanho de cada entrada da tabela de cabeçalhos de seção.
    ushort SectionHeaderEntrySize,

    // Quantidade de entradas na tabela de cabeçalhos de seção.
    ushort SectionHeaderCount,

    // Índice da seção que contém os nomes das seções, quando disponível.
    ushort SectionNameStringTableIndex);