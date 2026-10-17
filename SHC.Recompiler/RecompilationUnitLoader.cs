using SHC.ELF;
using SHC.Runtime.Overlays;

namespace SHC.Recompiler;

/// <summary>
/// Monta as unidades de recompilação de um disco já extraído:
/// o executável principal (SLUS) e os overlays FIELD, BATTLE e OPENING.
/// </summary>
public static class RecompilationUnitLoader
{
    // Tipo de segmento carregável.
    private const uint SegmentTypeLoad = 1;

    private static readonly string[] OverlayFileNames =
    {
        "FIELD.BIN",
        "BATTLE.BIN",
        "OPENING.BIN"
    };

    /// <summary>
    /// Carrega as quatro unidades a partir da pasta do disco extraído,
    /// por exemplo output\Disc1.
    /// </summary>
    public static IReadOnlyList<RecompilationUnit> LoadAll(string discDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discDirectory);

        if (!Directory.Exists(discDirectory))
        {
            throw new DirectoryNotFoundException(
                $"A pasta do disco não foi encontrada: {discDirectory}");
        }

        var units = new List<RecompilationUnit>
        {
            LoadExecutable(FindExecutable(discDirectory))
        };

        foreach (string fileName in OverlayFileNames)
        {
            units.Add(LoadOverlay(Path.Combine(discDirectory, fileName)));
        }

        return units;
    }

    // Localiza o executável, cujo nome segue o padrão SLUS_xxx.xx.
    private static string FindExecutable(string discDirectory)
    {
        string[] matches = Directory.GetFiles(discDirectory, "SLUS_*");

        if (matches.Length != 1)
        {
            throw new FileNotFoundException(
                $"Esperado um único SLUS_* em '{discDirectory}', " +
                $"encontrado(s): {matches.Length}.");
        }

        return matches[0];
    }

    // Lê o único segmento PT_LOAD que possui dados no arquivo.
    private static RecompilationUnit LoadExecutable(string path)
    {
        var header = ElfHeaderReader.Read(path);
        var segments = ElfProgramHeaderReader.Read(path, header);

        var fileBacked = segments
            .Where(s => s.Type == SegmentTypeLoad && s.FileSize > 0)
            .ToList();

        if (fileBacked.Count != 1)
        {
            throw new InvalidDataException(
                "Esperado exatamente um segmento LOAD com dados no arquivo, " +
                $"encontrado(s): {fileBacked.Count}.");
        }

        var segment = fileBacked[0];

        byte[] data = new byte[checked((int)segment.FileSize)];

        using (var stream = File.OpenRead(path))
        {
            stream.Position = segment.Offset;
            stream.ReadExactly(data);
        }

        return new RecompilationUnit(
            Path.GetFileName(path),
            segment.VirtualAddress,
            data,
            header.EntryPoint);
    }

    // O overlay inteiro é uma imagem de memória, cabeçalho incluído.
    private static RecompilationUnit LoadOverlay(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "O arquivo de overlay não foi encontrado.",
                path);
        }

        byte[] data = File.ReadAllBytes(path);

        OverlayHeaderInfo header = OverlayHeaderReader.Parse(data);

        return new RecompilationUnit(header.Name, header.LoadAddress, data);
    }
}
