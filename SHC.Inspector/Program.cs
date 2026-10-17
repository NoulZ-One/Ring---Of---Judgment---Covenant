using System.Buffers.Binary; // Leitura de palavras de 32 bits.
using System.Globalization; // Interpretação de números hexadecimais.
using SHC.ELF; // Leitores do formato ELF.
using SHC.ISO; // Leitura e extração de imagens ISO.
using SHC.Recompiler; // Unidades de recompilação.
using SHC.Recompiler.Analysis; // Descoberta de funções e blocos básicos.
using SHC.Recompiler.CodeGeneration; // Geração de código C#.
using SHC.Recompiler.Decoding; // Decodificador do R5900.
using SHC.Runtime.Loading; // Carregamento do ELF na memória.
using SHC.Runtime.Machine; // Memória do PS2.
using SHC.Runtime.Overlays; // Overlays do jogo.

// Ring Of Judgment: Covenant - Inspector
//
// Uso:
//   SHC.Inspector elf [caminho do ELF]
//   SHC.Inspector iso-list <iso>
//   SHC.Inspector iso-extract <iso> <pasta de saída> [--max-mb N]
//   SHC.Inspector elf-load <elf>
//   SHC.Inspector overlay <arquivo .BIN>
//   SHC.Inspector units <pasta do disco extraído>
//   SHC.Inspector decode-stats <pasta do disco extraído>
//   SHC.Inspector functions <pasta do disco extraído>
//   SHC.Inspector function <pasta do disco extraído> <unidade> <endereço hex>
//   SHC.Inspector emit <pasta do disco extraído> <unidade> <endereço hex>
//   SHC.Inspector generate <pasta do disco extraído> <unidade> <pasta da solução> [--no-comments]
//   SHC.Inspector disasm <pasta do disco extraído> <unidade> <endereço hex> [quantidade]
//
// Sem argumentos, analisa output/Disc1/SLUS_210.41 (comportamento anterior).

// Garante acentos corretos no console e nos arquivos redirecionados.
Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("Ring Of Judgment: Covenant");
Console.WriteLine("================================");

if (args.Length == 0)
{
    InspectElf(Path.Combine("output", "Disc1", "SLUS_210.41"));

    Console.WriteLine();
    Console.WriteLine("Pressione Enter para encerrar.");
    Console.ReadLine();
    return 0;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "elf":
            InspectElf(args.Length > 1
                ? args[1]
                : Path.Combine("output", "Disc1", "SLUS_210.41"));
            return 0;

        case "iso-list" when args.Length >= 2:
            ListIso(args[1]);
            return 0;

        case "iso-extract" when args.Length >= 3:
            long? maxBytes = null;

            if (args.Length >= 5 && args[3] == "--max-mb")
            {
                maxBytes = long.Parse(args[4]) * 1024 * 1024;
            }

            ExtractIso(args[1], args[2], maxBytes);
            return 0;

        case "elf-load" when args.Length >= 2:
            LoadElfIntoMemory(args[1]);
            return 0;

        case "overlay" when args.Length >= 2:
            InspectOverlay(args[1]);
            return 0;

        case "units" when args.Length >= 2:
            ListUnits(args[1]);
            return 0;

        case "decode-stats" when args.Length >= 2:
            DecodeStats(args[1]);
            return 0;

        case "functions" when args.Length >= 2:
            AnalyzeFunctions(args[1]);
            return 0;

        case "function" when args.Length >= 4:
            ShowFunction(args[1], args[2], args[3]);
            return 0;

        case "emit" when args.Length >= 4:
            EmitFunction(args[1], args[2], args[3]);
            return 0;

        case "generate" when args.Length >= 4:
            GenerateUnit(args[1], args[2], args[3], args.Skip(4).Contains("--no-comments"));
            return 0;

        case "disasm" when args.Length >= 4:
            Disassemble(args[1], args[2], args[3], args.Length >= 5 ? args[4] : "32");
            return 0;

        default:
            Console.WriteLine("Comando inválido ou argumentos ausentes.");
            Console.WriteLine("  elf [caminho do ELF]");
            Console.WriteLine("  iso-list <iso>");
            Console.WriteLine("  iso-extract <iso> <pasta de saída> [--max-mb N]");
            Console.WriteLine("  elf-load <elf>");
            Console.WriteLine("  overlay <arquivo .BIN>");
            Console.WriteLine("  units <pasta do disco extraído>");
            Console.WriteLine("  decode-stats <pasta do disco extraído>");
            Console.WriteLine("  functions <pasta do disco extraído>");
            Console.WriteLine("  function <pasta do disco extraído> <unidade> <endereço hex>");
            Console.WriteLine("  emit <pasta do disco extraído> <unidade> <endereço hex>");
            Console.WriteLine("  generate <pasta do disco extraído> <unidade> <pasta da solução> [--no-comments]");
            Console.WriteLine("  disasm <pasta do disco extraído> <unidade> <endereço hex> [quantidade]");
            return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"Erro: {ex.Message}");
    return 1;
}

// Lista todos os arquivos e pastas da ISO.
static void ListIso(string isoPath)
{
    var info = IsoImageReader.Inspect(isoPath);
    var entries = IsoTreeReader.ListAll(isoPath);

    Console.WriteLine($"Imagem: {info.FilePath}");
    Console.WriteLine($"Rótulo: {info.DiscLabel}");
    Console.WriteLine();

    foreach (var entry in entries)
    {
        string kind = entry.IsDirectory ? "<DIR>" : $"{entry.Size,12:N0}";

        Console.WriteLine(
            $"{kind} | LBA={entry.Offset / 2048,8} | {entry.Path}");
    }

    int files = entries.Count(e => !e.IsDirectory);
    long totalBytes = entries.Where(e => !e.IsDirectory).Sum(e => e.Size);

    Console.WriteLine();
    Console.WriteLine(
        $"{files:N0} arquivos, {entries.Count - files:N0} pastas, " +
        $"{totalBytes:N0} bytes.");
}

// Extrai toda a árvore da ISO, mantendo as pastas.
static void ExtractIso(
    string isoPath,
    string outputDirectory,
    long? maxBytes)
{
    var entries = IsoTreeReader.ListAll(isoPath);

    Console.WriteLine($"Extraindo para: {Path.GetFullPath(outputDirectory)}");

    int count = IsoTreeExtractor.ExtractAll(
        isoPath,
        entries,
        outputDirectory,
        entry => Console.WriteLine($"  {entry.Path}"),
        maxBytes);

    int total = entries.Count(e => !e.IsDirectory);

    Console.WriteLine();
    Console.WriteLine(
        $"{count:N0} arquivos extraídos, {total - count:N0} ignorados por tamanho.");
}

// Analisa a estrutura de um executável ELF.
static void InspectElf(string relativePath)
{
    string executablePath = Path.GetFullPath(relativePath);

    if (!File.Exists(executablePath))
    {
        Console.WriteLine("Executável não encontrado:");
        Console.WriteLine(executablePath);
        return;
    }

    try
    {
        var header = ElfHeaderReader.Read(executablePath);
        var segments = ElfProgramHeaderReader.Read(executablePath, header);
        var sections = ElfSectionHeaderReader.Read(executablePath, header);
        var names = ElfSectionNameReader.ReadNames(
            executablePath,
            header,
            sections);

        Console.WriteLine();
        Console.WriteLine($"Arquivo: {executablePath}");
        Console.WriteLine($"Tamanho: {new FileInfo(executablePath).Length:N0} bytes");
        Console.WriteLine($"Entry point: 0x{header.EntryPoint:X8}");

        Console.WriteLine();
        Console.WriteLine("--- Segmentos ---");

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];

            string typeName = segment.Type switch
            {
                0 => "NULL",
                1 => "LOAD",
                2 => "DYNAMIC",
                3 => "INTERP",
                4 => "NOTE",
                5 => "SHLIB",
                6 => "PHDR",
                7 => "TLS",
                _ => "OUTRO"
            };

            string permissions =
                $"{((segment.Flags & 4) != 0 ? 'R' : '-')}" +
                $"{((segment.Flags & 2) != 0 ? 'W' : '-')}" +
                $"{((segment.Flags & 1) != 0 ? 'X' : '-')}";

            Console.WriteLine(
                $"#{i} {typeName} | Offset=0x{segment.Offset:X8} | " +
                $"VA=0x{segment.VirtualAddress:X8} | " +
                $"FileSize={segment.FileSize} | MemSize={segment.MemorySize} | " +
                $"Flags={permissions}");
        }

        Console.WriteLine();
        Console.WriteLine("--- Seções ELF ---");

        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];

            string name = string.IsNullOrEmpty(names[i]) ? "(sem nome)" : names[i];

            Console.WriteLine(
                $"#{i} {name} | Type={section.Type} (0x{section.Type:X}) | " +
                $"Flags=0x{section.Flags:X8} | " +
                $"Address=0x{section.Address:X8} | " +
                $"Offset=0x{section.Offset:X8} | " +
                $"Size={section.Size} | " +
                $"Link={section.Link} | Info={section.Info} | " +
                $"Align={section.AddressAlignment} | " +
                $"EntrySize={section.EntrySize}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"Erro na análise: {ex.Message}");
    }
}

// Carrega o ELF na memória do PS2 e mostra o resultado.
static void LoadElfIntoMemory(string path)
{
    var memory = new Ps2Memory();
    var result = ElfImageLoader.Load(Path.GetFullPath(path), memory);

    Console.WriteLine($"Entry point: 0x{result.EntryPoint:X8}");
    Console.WriteLine();

    foreach (var segment in result.Segments)
    {
        Console.WriteLine(
            $"LOAD VA=0x{segment.VirtualAddress:X8} | " +
            $"FileSize=0x{segment.FileSize:X} | " +
            $"MemSize=0x{segment.MemorySize:X}");
    }

    Console.WriteLine();
    Console.WriteLine("Primeiras palavras a partir do entry point:");

    for (uint i = 0; i < 4; i++)
    {
        uint address = result.EntryPoint + i * 4;

        Console.WriteLine($"  0x{address:X8}: 0x{memory.Read32(address):X8}");
    }
}

// Mostra o cabeçalho de um overlay e valida o carregamento na memória.
static void InspectOverlay(string path)
{
    var header = OverlayHeaderReader.Read(path);

    Console.WriteLine($"Arquivo: {Path.GetFullPath(path)}");
    Console.WriteLine($"Nome interno: {header.Name}");
    Console.WriteLine($"Overlay: {header.Id} ({(uint)header.Id})");
    Console.WriteLine($"Endereço de carga: 0x{header.LoadAddress:X8}");
    Console.WriteLine($"Word 0x0C: 0x{header.Word0C:X8}");
    Console.WriteLine($"Word 0x10: 0x{header.Word10:X8}");
    Console.WriteLine($"Cauda zerada (0x14): 0x{header.ZeroTailSize:X}");
    Console.WriteLine($"Word 0x18: 0x{header.Word18:X8}");
    Console.WriteLine($"Word 0x1C: 0x{header.Word1C:X8}");

    var memory = new Ps2Memory();
    var result = OverlayLoader.Load(path, memory);

    Console.WriteLine();
    Console.WriteLine(
        $"Arquivo: {result.FileSize:N0} bytes | " +
        $"Em memória: {result.MemorySize:N0} bytes");
    Console.WriteLine(
        $"Faixa: 0x{header.LoadAddress:X8} - " +
        $"0x{header.LoadAddress + (uint)result.MemorySize:X8}");
}

// Lista as unidades de recompilação de um disco extraído.
static void ListUnits(string discDirectory)
{
    var units = RecompilationUnitLoader.LoadAll(discDirectory);

    foreach (var unit in units)
    {
        Console.WriteLine(
            $"{unit.Name,-14} Base=0x{unit.BaseAddress:X8} | " +
            $"Fim=0x{unit.EndAddress:X8} | " +
            $"Tamanho={unit.Data.Length,10:N0} | " +
            $"Palavras={unit.Data.Length / 4:N0}");
    }
}

// Decodifica todas as palavras de cada unidade e conta as não reconhecidas.
static void DecodeStats(string discDirectory)
{
    var units = RecompilationUnitLoader.LoadAll(discDirectory);

    foreach (var unit in units)
    {
        int words = unit.Data.Length / 4;
        int unknown = 0;
        int vu0Macro = 0;

        for (int i = 0; i < words; i++)
        {
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(
                unit.Data.AsSpan(i * 4, 4));

            var instruction = R5900Decoder.Decode(
                unit.BaseAddress + (uint)(i * 4),
                word);

            if (instruction.Opcode == Opcode.Unknown)
            {
                unknown++;
            }
            else if (instruction.Opcode == Opcode.Vu0_Macro)
            {
                vu0Macro++;
            }
        }

        Console.WriteLine(
            $"{unit.Name,-14} Palavras={words,9:N0} | " +
            $"Desconhecidas={unknown,7:N0} ({unknown * 100.0 / words:F2}%) | " +
            $"VU0 macro={vu0Macro,6:N0}");
    }
}

// Desmonta um trecho de uma unidade, a partir de um endereço.
static void Disassemble(
    string discDirectory,
    string unitName,
    string addressText,
    string countText)
{
    var units = RecompilationUnitLoader.LoadAll(discDirectory);

    var unit = units.FirstOrDefault(u =>
        u.Name.Equals(unitName, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(
            $"Unidade '{unitName}' não encontrada. Disponíveis: " +
            string.Join(", ", units.Select(u => u.Name)));

    uint address = uint.Parse(
        addressText.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase),
        NumberStyles.HexNumber);

    int count = int.Parse(countText);

    if (address < unit.BaseAddress || address >= unit.EndAddress)
    {
        throw new ArgumentException(
            $"O endereço 0x{address:X8} está fora de {unit.Name} " +
            $"(0x{unit.BaseAddress:X8} - 0x{unit.EndAddress:X8}).");
    }

    for (int i = 0; i < count; i++)
    {
        uint current = address + (uint)(i * 4);

        if (current + 4 > unit.EndAddress)
        {
            break;
        }

        uint word = BinaryPrimitives.ReadUInt32LittleEndian(
            unit.Data.AsSpan((int)(current - unit.BaseAddress), 4));

        var instruction = R5900Decoder.Decode(current, word);

        Console.WriteLine(
            $"{current:X8}: {word:X8}  {R5900Disassembler.Format(instruction)}");
    }
}

// Descobre as funções de cada unidade e resume o resultado.
static void AnalyzeFunctions(string discDirectory)
{
    foreach (var unit in RecompilationUnitLoader.LoadAll(discDirectory))
    {
        var result = FunctionFinder.Analyze(unit);

        int blocks = result.Functions.Values.Sum(f => f.Blocks.Count);
        int indirectJumps = result.Functions.Values.Sum(f => f.IndirectJumps.Count);
        int tables = result.Functions.Values.Sum(f => f.JumpTables.Count);
        int warnings = result.Functions.Values.Sum(f => f.OutOfRangeAddresses.Count);

        int rangeWords = result.CodeRange.WordCount;
        double coverage = result.CoveredAddresses.Count * 100.0 / rangeWords;

        Console.WriteLine(
            $"{unit.Name,-12} faixa=0x{result.CodeRange.Start:X8}-0x{result.CodeRange.End:X8} | " +
            $"raízes={result.BaseRootCount}+{result.DataPointerRootCount} | " +
            $"funções={result.Functions.Count} blocos={blocks} | " +
            $"cobertas={result.CoveredAddresses.Count}/{rangeWords} ({coverage:F1}%) | " +
            $"tabelas={tables} jr não resolvidos={indirectJumps} avisos={warnings} " +
            $"externas={result.ExternalTargets.Count}");
    }
}

// Mostra os blocos básicos de uma função, com o código desmontado.
static void ShowFunction(string discDirectory, string unitName, string addressText)
{
    var units = RecompilationUnitLoader.LoadAll(discDirectory);

    var unit = units.FirstOrDefault(u =>
        u.Name.Equals(unitName, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(
            $"Unidade '{unitName}' não encontrada. Disponíveis: " +
            string.Join(", ", units.Select(u => u.Name)));

    uint address = uint.Parse(
        addressText.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase),
        NumberStyles.HexNumber);

    var result = FunctionFinder.Analyze(unit);

    // Aceita tanto a entrada da função quanto qualquer endereço dentro dela.
    var function = result.Functions.TryGetValue(address, out var exact)
        ? exact
        : result.Functions.Values.FirstOrDefault(f => f.Contains(address))
          ?? throw new ArgumentException(
              $"O endereço 0x{address:X8} não pertence a nenhuma função encontrada.");

    string Targets(IEnumerable<uint> values) =>
        values.Any() ? string.Join(", ", values.Select(v => $"0x{v:X8}")) : "-";

    Console.WriteLine(
        $"Função 0x{function.Entry:X8}: {function.Blocks.Count} bloco(s), " +
        $"{function.Addresses.Length} instrução(ões)");
    Console.WriteLine($"Chamadas: {Targets(function.Calls)}");
    Console.WriteLine($"Chamadas em cauda: {Targets(function.TailCalls)}");
    Console.WriteLine($"Destinos externos: {Targets(function.ExternalTargets)}");
    Console.WriteLine($"Saltos indiretos não resolvidos: {Targets(function.IndirectJumps)}");

    foreach (var table in function.JumpTables)
    {
        Console.WriteLine(
            $"Tabela de saltos: jr em 0x{table.JumpAddress:X8}, " +
            $"tabela em 0x{table.TableAddress:X8}, {table.EntryCount} entrada(s)");
    }

    foreach (var block in function.Blocks)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"bloco 0x{block.Start:X8}-0x{block.End:X8} [{block.Terminator}] " +
            $"-> {Targets(block.Successors)}");

        for (uint current = block.Start; current < block.End; current += 4)
        {
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(
                unit.Data.AsSpan((int)(current - unit.BaseAddress), 4));

            var instruction = R5900Decoder.Decode(current, word);

            Console.WriteLine(
                $"  {current:X8}: {word:X8}  {R5900Disassembler.Format(instruction)}");
        }
    }
}

// Gera o código C# de uma função e o exibe.
static void EmitFunction(string discDirectory, string unitName, string addressText)
{
    var units = RecompilationUnitLoader.LoadAll(discDirectory);

    var unit = units.FirstOrDefault(u =>
        u.Name.Equals(unitName, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(
            $"Unidade '{unitName}' não encontrada. Disponíveis: " +
            string.Join(", ", units.Select(u => u.Name)));

    uint address = uint.Parse(
        addressText.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase),
        NumberStyles.HexNumber);

    var result = FunctionFinder.Analyze(unit);

    var function = result.Functions.TryGetValue(address, out var exact)
        ? exact
        : result.Functions.Values.FirstOrDefault(f => f.Contains(address))
          ?? throw new ArgumentException(
              $"O endereço 0x{address:X8} não pertence a nenhuma função encontrada.");

    Console.WriteLine(CSharpFunctionEmitter.Emit(result, function));
}

// Gera o projeto C# de uma unidade inteira.
static void GenerateUnit(
    string discDirectory,
    string unitName,
    string solutionDirectory,
    bool noComments)
{
    var units = RecompilationUnitLoader.LoadAll(discDirectory);

    var unit = units.FirstOrDefault(u =>
        u.Name.Equals(unitName, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(
            $"Unidade '{unitName}' não encontrada. Disponíveis: " +
            string.Join(", ", units.Select(u => u.Name)));

    Console.WriteLine($"Analisando {unit.Name}...");

    var result = FunctionFinder.Analyze(unit);

    Console.WriteLine($"Gerando {result.Functions.Count:N0} funções...");

    var generation = UnitGenerator.Generate(
        result,
        Path.GetFullPath(solutionDirectory),
        new EmitOptions(IncludeComments: !noComments));

    Console.WriteLine($"Projeto: {generation.ProjectDirectory}");
    Console.WriteLine(
        $"{generation.FunctionCount:N0} funções em {generation.FileCount} arquivo(s), " +
        $"{generation.TotalBytes / 1024.0 / 1024.0:F1} MB.");
}
