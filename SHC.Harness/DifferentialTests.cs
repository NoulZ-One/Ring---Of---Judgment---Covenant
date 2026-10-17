using System.Diagnostics;
using SHC.Generated.Main;
using SHC.Recompiler.Decoding;
using SHC.Runtime.Cpu;
using SHC.Runtime.Loading;
using SHC.Runtime.Machine;

/// <summary>
/// Teste diferencial: para cada função do executável, sorteia um estado inicial
/// (registradores e memória) e executa a função de duas formas, a partir do mesmo
/// estado: com o interpretador de referência e com o código recompilado.
/// Depois compara todos os registradores e toda a RAM.
/// </summary>
internal sealed class DifferentialTests
{
    // Valor de $gp definido na inicialização do jogo (seção .reginfo do ELF).
    private const uint Gp = 0x004479F0;

    // Áreas da RAM preenchidas com dados aleatórios.
    private const uint ArenaStart = 0x00500000;
    private const int ArenaLength = 0x40000;
    private const uint StackStart = 0x01EC0000;
    private const uint StackEnd = 0x01F00000;
    private const uint StackPointer = 0x01EFFF00;

    // Endereço de retorno do nível mais alto: a execução termina ao chegar nele.
    private const uint Sentinel = 0x0000FFF0;

    private readonly Ps2Memory _memory;
    private readonly R5900Context _ctx;

    private readonly byte[] _baseline = new byte[Ps2Memory.Size];
    private readonly byte[] _reference = new byte[Ps2Memory.Size];

    private readonly int _trials;
    private readonly int _seed;
    private readonly uint? _only;
    private readonly long _budget;

    private int _total;
    private int _compared;
    private int _identicalExceptions;
    private int _unsupported;
    private int _longLoops;
    private int _inconclusiveCalls;
    private int _failed;

    private readonly List<string> _failures = new();
    private readonly HashSet<uint> _verified = new();

    public DifferentialTests(string elfPath, int trials, int seed, uint? only, long budget)
    {
        _trials = trials;
        _seed = seed;
        _only = only;
        _budget = budget;

        _memory = new Ps2Memory();

        ElfImageLoader.Load(Path.GetFullPath(elfPath), _memory);

        _ctx = new R5900Context(_memory);
        MainUnit.Register(_ctx);

        _memory.GetSpan(0, Ps2Memory.Size).CopyTo(_baseline);
    }

    public int Run()
    {
        List<uint> entries = _ctx.RegisteredFunctions.OrderBy(e => e).ToList();

        if (_only is not null)
        {
            entries = entries.Where(e => e == _only.Value).ToList();

            if (entries.Count == 0)
            {
                Console.WriteLine($"Função 0x{_only.Value:X8} não está registrada.");
                return 1;
            }
        }

        Console.WriteLine(
            $"{entries.Count:N0} função(ões), {_trials} sorteio(s) cada, semente {_seed}.");

        var clock = Stopwatch.StartNew();
        int done = 0;

        foreach (uint entry in entries)
        {
            for (int trial = 0; trial < _trials; trial++)
            {
                RunTrial(entry, unchecked(_seed * 1000003 + (int)entry * 31 + trial));
            }

            done++;

            if (done % 500 == 0)
            {
                Console.WriteLine(
                    $"  {done:N0}/{entries.Count:N0} funções ({clock.Elapsed.TotalSeconds:F0} s)");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Sorteios executados:                  {_total:N0}");
        Console.WriteLine($"  Comparados, estado idêntico:        {_compared:N0}");
        Console.WriteLine($"  Mesma exceção nos dois lados:       {_identicalExceptions:N0}");
        Console.WriteLine($"  Instrução não suportada (ambos):    {_unsupported:N0}");
        Console.WriteLine($"  Descartados (laço longo):           {_longLoops:N0}");
        Console.WriteLine($"  Descartados (chamada não registrada): {_inconclusiveCalls:N0}");
        Console.WriteLine($"  DIVERGÊNCIAS:                       {_failed:N0}");
        Console.WriteLine(
            $"Funções com ao menos uma execução comparada: {_verified.Count:N0} de {entries.Count:N0}");
        Console.WriteLine($"Tempo: {clock.Elapsed.TotalSeconds:F0} s");

        foreach (string failure in _failures.Take(25))
        {
            Console.WriteLine(failure);
        }

        if (_failures.Count > 25)
        {
            Console.WriteLine($"... e mais {_failures.Count - 25:N0} divergência(s).");
        }

        return _failed == 0 ? 0 : 1;
    }

    // ----- Um sorteio -----

    private void RunTrial(uint entry, int seed)
    {
        _total++;

        // 1) Referência: o interpretador. Roda primeiro para descartar laços longos.
        Randomize(seed);

        Exception? referenceError = null;

        var interpreter = new Interpreter(_ctx);

        try
        {
            interpreter.Run(entry, Sentinel, _budget);
        }
        catch (BudgetExceededException)
        {
            _longLoops++;
            return;
        }
        catch (Exception ex)
        {
            referenceError = ex;
        }

        RegisterState referenceState = RegisterState.From(_ctx);

        _memory.GetSpan(0, Ps2Memory.Size).CopyTo(_reference);

        // 2) Código recompilado, a partir do mesmo estado inicial.
        Randomize(seed);

        Exception? actualError = RunRecompiled(entry);

        if (referenceError is null && actualError is null)
        {
            string? difference =
                referenceState.Compare(RegisterState.From(_ctx)) ?? MemoryDifference();

            if (difference is null)
            {
                _compared++;
                _verified.Add(entry);
            }
            else
            {
                Fail(entry, seed, difference);
            }

            return;
        }

        // Função não registrada ou salto sem destino conhecido: o interpretador
        // segue o código real e o recompilado não tem como acompanhar.
        if (actualError is InvalidOperationException)
        {
            _inconclusiveCalls++;
            return;
        }

        if (referenceError is not null && actualError is not null)
        {
            if (referenceError.GetType() == actualError.GetType() &&
                referenceError.Message == actualError.Message)
            {
                if (actualError is NotImplementedException)
                {
                    _unsupported++;
                }
                else
                {
                    _identicalExceptions++;
                }

                return;
            }

            // Dados para descobrir onde os dois lados se separaram.
            string recent = string.Join(
                ", ",
                interpreter.RecentAddresses().Select(a => $"0x{a:X8}"));

            string registers =
                referenceState.Compare(RegisterState.From(_ctx)) ?? "(registradores iguais)";

            Fail(
                entry,
                seed,
                $"exceções diferentes: recompilado={Describe(actualError)}; " +
                $"interpretador={Describe(referenceError)}\n" +
                $"      interpretador parou em 0x{interpreter.FaultAddress:X8}: " +
                $"{Disassemble(interpreter.FaultAddress)} | " +
                $"{Disassemble(interpreter.FaultAddress + 4)}\n" +
                $"      últimos endereços do interpretador: {recent}\n" +
                $"      primeira diferença de registrador no momento da exceção: {registers}\n" +
                $"      pilha do recompilado: {Frames(actualError)}");

            return;
        }

        Fail(
            entry,
            seed,
            actualError is not null
                ? $"só o código recompilado lançou exceção: {Describe(actualError)}\n" +
                  $"      pilha do recompilado: {Frames(actualError)}"
                : $"só o interpretador lançou exceção: {Describe(referenceError!)}\n" +
                  $"      interpretador parou em 0x{interpreter.FaultAddress:X8}: " +
                  $"{Disassemble(interpreter.FaultAddress)} | " +
                  $"{Disassemble(interpreter.FaultAddress + 4)}\n" +
                  $"      caminho recente do interpretador:\n{Trace(interpreter)}");
    }

    // Executa a função recompilada em uma thread com pilha grande e limite de tempo.
    private Exception? RunRecompiled(uint entry)
    {
        Exception? error = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    _ctx.Call(entry);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            },
            16 * 1024 * 1024);

        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(10)))
        {
            Console.WriteLine(
                $"TRAVOU: F_{entry:X8} não terminou em 10 s, embora o interpretador terminou.");
            Environment.Exit(2);
        }

        return error;
    }

    private string? MemoryDifference()
    {
        ReadOnlySpan<byte> now = _memory.GetSpan(0, Ps2Memory.Size);

        int at = now.CommonPrefixLength(_reference);

        return at == Ps2Memory.Size
            ? null
            : $"RAM diferente em 0x{at:X8}: recompilado=0x{now[at]:X2}, " +
              $"interpretador=0x{_reference[at]:X2}";
    }

    private void Fail(uint entry, int seed, string description)
    {
        _failed++;
        _failures.Add($"  DIVERGÊNCIA F_{entry:X8} (semente {seed}): {description}");
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    // Assembly da instrução no endereço, lida da memória atual.
    private string Disassemble(uint address)
    {
        try
        {
            return R5900Disassembler.Format(
                R5900Decoder.Decode(address, _memory.Read32(address)));
        }
        catch (Exception)
        {
            return "?";
        }
    }

    // Últimas instruções executadas pelo interpretador, uma por linha.
    private string Trace(Interpreter interpreter) =>
        string.Join(
            "\n",
            interpreter.RecentAddresses().Select(a => $"        0x{a:X8}: {Disassemble(a)}"));

    // Primeiros quadros da pilha de uma exceção (método e linha do código gerado).
    private static string Frames(Exception ex) =>
        string.Join(
            " <- ",
            (ex.StackTrace ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Take(3)
                .Select(line => line.Trim()));

    // ----- Estado inicial aleatório -----

    // Restaura a RAM do estado inicial e sorteia o conteúdo das áreas de trabalho
    // e todos os registradores. Para a mesma semente, o resultado é idêntico.
    private void Randomize(int seed)
    {
        _baseline.CopyTo(_memory.GetSpan(0, Ps2Memory.Size));

        var rng = new Random(seed);

        for (uint address = ArenaStart; address < ArenaStart + ArenaLength; address += 4)
        {
            _memory.Write32(address, RandomWord(rng));
        }

        for (uint address = StackStart; address < StackEnd; address += 4)
        {
            _memory.Write32(address, RandomWord(rng));
        }

        Array.Clear(_ctx.R);
        Array.Clear(_ctx.RHigh);
        Array.Clear(_ctx.F);

        for (int i = 1; i < 32; i++)
        {
            _ctx.R[i] = RandomRegister(rng);
            _ctx.RHigh[i] = (ulong)rng.NextInt64();
        }

        for (int i = 0; i < 32; i++)
        {
            _ctx.F[i] = RandomFloat(rng);
        }

        _ctx.Lo = (ulong)rng.NextInt64();
        _ctx.Hi = (ulong)rng.NextInt64();
        _ctx.Lo1 = (ulong)rng.NextInt64();
        _ctx.Hi1 = (ulong)rng.NextInt64();
        _ctx.Acc = RandomFloat(rng);
        _ctx.Fcr31 = (uint)rng.Next() & 0x0183FFFFu;

        _ctx.R[28] = Gp;
        _ctx.R[29] = StackPointer;
        _ctx.R[31] = Sentinel;
    }

    // Palavra de memória: mistura de números pequenos, negativos, ponteiros e valores soltos.
    private static uint RandomWord(Random rng)
    {
        int kind = rng.Next(100);

        if (kind < 45)
        {
            return (uint)rng.Next(0, 256);
        }

        if (kind < 60)
        {
            return (uint)(-rng.Next(1, 64));
        }

        if (kind < 85)
        {
            return ArenaStart + ((uint)rng.Next(0, ArenaLength) & ~3u);
        }

        return (uint)rng.NextInt64(0, 1L << 32);
    }

    private static ulong RandomRegister(Random rng)
    {
        int kind = rng.Next(100);

        if (kind < 35)
        {
            // Ponteiro para a área de trabalho, às vezes desalinhado.
            uint pointer = ArenaStart + (uint)rng.Next(0, ArenaLength);

            return rng.Next(100) < 70 ? pointer & ~3u : pointer;
        }

        if (kind < 60)
        {
            return (ulong)rng.Next(0, 65);
        }

        if (kind < 70)
        {
            return (ulong)(long)(-rng.Next(1, 65));
        }

        if (kind < 85)
        {
            return (ulong)(long)(int)(uint)rng.NextInt64(0, 1L << 32);
        }

        if (kind < 95)
        {
            return ((ulong)rng.NextInt64() << 1) | (uint)rng.Next(2);
        }

        ulong[] edges =
        {
            0x7FFFFFFFUL, 0x80000000UL, 0xFFFFFFFFUL, 0x100000000UL,
            ulong.MaxValue, 0x8000000000000000UL
        };

        return edges[rng.Next(edges.Length)];
    }

    private static uint RandomFloat(Random rng)
    {
        int kind = rng.Next(100);

        if (kind < 10)
        {
            return 0;
        }

        if (kind < 20)
        {
            return 0x3F800000;
        }

        if (kind < 90)
        {
            // Expoente em uma faixa comum, mantissa aleatória.
            uint sign = (uint)rng.Next(2) << 31;
            uint exponent = (uint)(100 + rng.Next(51)) << 23;

            return sign | exponent | (uint)rng.Next(1 << 23);
        }

        return (uint)rng.NextInt64(0, 1L << 32);
    }

    // ----- Estado dos registradores -----

    private sealed class RegisterState
    {
        private readonly ulong[] _r = new ulong[32];
        private readonly ulong[] _high = new ulong[32];
        private readonly uint[] _f = new uint[32];
        private ulong _lo, _hi, _lo1, _hi1;
        private uint _acc, _fcr31;

        public static RegisterState From(R5900Context c)
        {
            var state = new RegisterState();

            c.R.CopyTo(state._r, 0);
            c.RHigh.CopyTo(state._high, 0);
            c.F.CopyTo(state._f, 0);

            state._lo = c.Lo;
            state._hi = c.Hi;
            state._lo1 = c.Lo1;
            state._hi1 = c.Hi1;
            state._acc = c.Acc;
            state._fcr31 = c.Fcr31;

            return state;
        }

        // Descreve a primeira diferença; "this" é o interpretador e "actual" o recompilado.
        public string? Compare(RegisterState actual)
        {
            for (int i = 0; i < 32; i++)
            {
                if (_r[i] != actual._r[i])
                {
                    return $"$r{i}: recompilado=0x{actual._r[i]:X}, interpretador=0x{_r[i]:X}";
                }
            }

            for (int i = 0; i < 32; i++)
            {
                if (_high[i] != actual._high[i])
                {
                    return $"$r{i} (64 bits altos): recompilado=0x{actual._high[i]:X}, interpretador=0x{_high[i]:X}";
                }
            }

            for (int i = 0; i < 32; i++)
            {
                if (_f[i] != actual._f[i])
                {
                    return $"$f{i}: recompilado=0x{actual._f[i]:X8}, interpretador=0x{_f[i]:X8}";
                }
            }

            if (_lo != actual._lo) return $"LO: recompilado=0x{actual._lo:X}, interpretador=0x{_lo:X}";
            if (_hi != actual._hi) return $"HI: recompilado=0x{actual._hi:X}, interpretador=0x{_hi:X}";
            if (_lo1 != actual._lo1) return $"LO1: recompilado=0x{actual._lo1:X}, interpretador=0x{_lo1:X}";
            if (_hi1 != actual._hi1) return $"HI1: recompilado=0x{actual._hi1:X}, interpretador=0x{_hi1:X}";
            if (_acc != actual._acc) return $"ACC: recompilado=0x{actual._acc:X8}, interpretador=0x{_acc:X8}";
            if (_fcr31 != actual._fcr31) return $"FCR31: recompilado=0x{actual._fcr31:X8}, interpretador=0x{_fcr31:X8}";

            return null;
        }
    }
}
