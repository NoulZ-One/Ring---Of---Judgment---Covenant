namespace SHC.ELF.Models;

/// <summary>
/// Representa uma entrada da tabela de cabeçalhos de programa de um ELF32.
/// Descreve um segmento e como ele deve ser interpretado ou carregado.
/// </summary>
public sealed record ElfProgramHeaderInfo(
    // Tipo do segmento, como PT_LOAD para uma região carregável.
    uint Type,

    // Posição inicial dos dados do segmento dentro do arquivo ELF.
    uint Offset,

    // Endereço virtual em que o segmento deve ser colocado na memória.
    uint VirtualAddress,

    // Endereço físico declarado, quando aplicável.
    uint PhysicalAddress,

    // Quantidade de bytes ocupados pelo segmento dentro do arquivo.
    uint FileSize,

    // Quantidade de bytes que o segmento ocupa na memória.
    // Pode ser maior que FileSize, por exemplo, em regiões de dados zerados.
    uint MemorySize,

    // Flags que indicam permissões como leitura, escrita e execução.
    uint Flags,

    // Alinhamento requerido para o segmento, quando aplicável.
    uint Alignment);