// Ring Of Judgment: Covenant - Harness
//
// Executa o código recompilado e confere o resultado.
//
// Uso:
//   SHC.Harness [smoke] [--elf <SLUS_210.41>]
//   SHC.Harness diff [--trials N] [--seed N] [--function 0xENDEREÇO] [--budget N] [--elf <SLUS_210.41>]
//
// smoke: funções escolhidas, com resultado esperado escrito à mão.
// diff:  compara cada função recompilada com um interpretador de referência,
//        usando estados iniciais aleatórios.

using System.Globalization;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("Ring Of Judgment: Covenant - Harness");
Console.WriteLine("====================================");

string command = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "smoke";

string elfPath = Option("--elf") ?? Path.Combine("output", "Disc1", "SLUS_210.41");

if (!File.Exists(elfPath))
{
    Console.WriteLine($"Executável não encontrado: {Path.GetFullPath(elfPath)}");
    return 1;
}

switch (command)
{
    case "smoke":
        return new SmokeTests(elfPath).Run();

    case "diff":
        return new DifferentialTests(
            elfPath,
            trials: IntOption("--trials", 3),
            seed: IntOption("--seed", 1),
            only: AddressOption("--function"),
            budget: IntOption("--budget", 100000)).Run();

    default:
        Console.WriteLine("Comando inválido. Use: smoke ou diff.");
        return 1;
}

string? Option(string name)
{
    int index = Array.IndexOf(args, name);

    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

int IntOption(string name, int fallback) =>
    Option(name) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

uint? AddressOption(string name) =>
    Option(name) is { } text
        ? uint.Parse(
            text.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase),
            NumberStyles.HexNumber)
        : null;
