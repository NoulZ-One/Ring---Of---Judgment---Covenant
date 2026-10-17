namespace SHC.ELF.Models;

/// <summary>
/// Armazena os campos de uma entrada da tabela de seções ELF32.
/// </summary>
public sealed record ElfSectionHeaderInfo(
    // Índice do nome da seção na tabela de strings.
    uint NameIndex,

    // Tipo da seção, como PROGBITS, NOBITS ou STRTAB.
    uint Type,

    // Atributos da seção, incluindo permissões e características.
    uint Flags,

    // Endereço virtual da seção, quando aplicável.
    uint Address,

    // Posição inicial da seção dentro do arquivo.
    uint Offset,

    // Tamanho da seção em bytes.
    uint Size,

    // Índice da seção relacionada, quando aplicável.
    uint Link,

    // Informação adicional específica do tipo da seção.
    uint Info,

    // Requisito de alinhamento da seção.
    uint AddressAlignment,

    // Tamanho de cada entrada, para seções que contêm tabelas.
    uint EntrySize);