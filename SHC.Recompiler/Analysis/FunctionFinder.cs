using System.Buffers.Binary;
using SHC.Recompiler.Decoding;

namespace SHC.Recompiler.Analysis;

/// <summary>
/// Descobre as funções de uma unidade e divide cada uma em blocos básicos.
///
/// Estratégia:
/// 1. Raízes iniciais: ponto de entrada e todo prólogo ADDIU $sp, $sp, -N
///    dentro da região de código.
/// 2. Cada raiz é explorada seguindo desvios e saltos; os alvos de JAL
///    viram novas funções.
/// 3. JR indiretos que são tabelas de switch são resolvidos (JumpTableResolver)
///    e seus destinos continuam a exploração.
/// 4. Endereços de código apontados por dados (tabelas de ponteiros) que
///    ainda não foram alcançados entram como raízes adicionais.
/// </summary>
public sealed class FunctionFinder
{
    // JR $ra.
    private const uint JrRa = 0x03E00008;

    private readonly RecompilationUnit _unit;
    private readonly CodeRange _range;
    private readonly JumpTableResolver _resolver;

    private readonly record struct Terminator(
        BlockTerminator Kind,
        List<uint> Successors,
        bool HasDelaySlot);

    private FunctionFinder(RecompilationUnit unit, CodeRange range)
    {
        _unit = unit;
        _range = range;
        _resolver = new JumpTableResolver(unit, range);
    }

    /// <summary>Analisa a unidade inteira.</summary>
    public static AnalysisResult Analyze(RecompilationUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var finder = new FunctionFinder(unit, UnitCodeRanges.For(unit));

        return finder.Run();
    }

    private AnalysisResult Run()
    {
        uint mainRoot = _unit.EntryPoint ?? _range.Start;

        // Fase 1: ponto de entrada e prólogos de função.
        var baseRoots = new List<uint> { mainRoot };

        for (uint address = _range.Start; address < _range.End; address += 4)
        {
            if (address != mainRoot && IsPrologue(ReadWord(address)))
            {
                baseRoots.Add(address);
            }
        }

        var firstPass = AnalyzeRoots(baseRoots);
        var covered = CollectCovered(firstPass.Functions);

        // Fase 2: ponteiros em dados que apontam para código ainda não alcançado.
        var baseSet = new HashSet<uint>(baseRoots);
        var dataRoots = new SortedSet<uint>();

        int wordCount = _unit.Data.Length / 4;

        for (int i = 0; i < wordCount; i++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(
                _unit.Data.AsSpan(i * 4, 4));

            if (!_range.Contains(value) ||
                covered.Contains(value) ||
                baseSet.Contains(value))
            {
                continue;
            }

            if (Fetch(value).Opcode == Opcode.Unknown ||
                !IsPlausibleFunctionStart(value))
            {
                continue;
            }

            dataRoots.Add(value);
        }

        var allRoots = new List<uint>(baseRoots);
        allRoots.AddRange(dataRoots);

        var (functions, external) = AnalyzeRoots(allRoots);

        return new AnalysisResult
        {
            Unit = _unit,
            CodeRange = _range,
            BaseRootCount = baseRoots.Count,
            DataPointerRootCount = dataRoots.Count,
            Functions = functions,
            ExternalTargets = external,
            CoveredAddresses = CollectCovered(functions)
        };
    }

    // Explora as raízes e todas as funções alcançadas a partir delas.
    private (SortedDictionary<uint, FunctionInfo> Functions, SortedSet<uint> External)
        AnalyzeRoots(List<uint> roots)
    {
        var functions = new SortedDictionary<uint, FunctionInfo>();
        var external = new SortedSet<uint>();

        // A lista funciona como pilha; "seen" guarda tudo o que já foi enfileirado.
        var queue = new List<uint>(roots);
        var seen = new HashSet<uint>(roots);

        while (queue.Count > 0)
        {
            uint entry = queue[^1];
            queue.RemoveAt(queue.Count - 1);

            if (functions.ContainsKey(entry))
            {
                continue;
            }

            FunctionInfo function = Explore(entry, seen);

            functions[entry] = function;
            external.UnionWith(function.ExternalTargets);

            // Em ordem crescente, para que o resultado seja determinístico.
            var discovered = new SortedSet<uint>(function.Calls);
            discovered.UnionWith(function.TailCalls);

            foreach (uint target in discovered)
            {
                if (seen.Add(target))
                {
                    queue.Add(target);
                }
            }
        }

        return (functions, external);
    }

    // Segue o fluxo a partir de "entry" e monta a função com seus blocos.
    private FunctionInfo Explore(uint entry, HashSet<uint> known)
    {
        // Endereços do fluxo de execução, delay slots (que só executam junto do
        // desvio) e instruções de chamada, que levam o delay slot junto.
        var visited = new HashSet<uint>();
        var delaySlots = new HashSet<uint>();
        var callSites = new HashSet<uint>();
        var leaders = new HashSet<uint> { entry };
        var terminators = new Dictionary<uint, Terminator>();

        var calls = new SortedSet<uint>();
        var tailCalls = new SortedSet<uint>();
        var external = new SortedSet<uint>();
        var indirectJumps = new List<uint>();
        var jumpTables = new List<JumpTableInfo>();
        var outOfRange = new List<uint>();

        var pending = new Stack<uint>();
        pending.Push(entry);

        while (pending.Count > 0)
        {
            uint pc = pending.Pop();

            while (true)
            {
                if (visited.Contains(pc))
                {
                    break;
                }

                if (!_range.Contains(pc))
                {
                    outOfRange.Add(pc);
                    break;
                }

                visited.Add(pc);

                Instruction instruction = Fetch(pc);
                ControlFlowKind kind = ControlFlow.Classify(instruction);

                if (kind == ControlFlowKind.Sequential)
                {
                    pc += 4;
                    continue;
                }

                if (kind is ControlFlowKind.Branch or ControlFlowKind.BranchLikely)
                {
                    delaySlots.Add(pc + 4);

                    uint target = ControlFlow.GetTarget(instruction);
                    var successors = new List<uint> { target };

                    leaders.Add(target);
                    pending.Push(target);

                    bool conditional =
                        kind == ControlFlowKind.BranchLikely ||
                        !ControlFlow.IsUnconditional(instruction);

                    if (conditional)
                    {
                        successors.Add(pc + 8);
                        leaders.Add(pc + 8);
                        pending.Push(pc + 8);
                    }

                    terminators[pc] = new Terminator(
                        kind == ControlFlowKind.BranchLikely
                            ? BlockTerminator.BranchLikely
                            : BlockTerminator.Branch,
                        successors,
                        true);

                    break;
                }

                if (kind == ControlFlowKind.Jump)
                {
                    delaySlots.Add(pc + 4);

                    uint target = ControlFlow.GetTarget(instruction);

                    // Salto para antes da função, para uma função conhecida ou
                    // para fora da região: é uma chamada em cauda.
                    if (target < entry ||
                        known.Contains(target) ||
                        !_range.Contains(target))
                    {
                        if (_range.Contains(target))
                        {
                            tailCalls.Add(target);
                        }
                        else
                        {
                            external.Add(target);
                        }

                        terminators[pc] = new Terminator(
                            BlockTerminator.TailCall,
                            new List<uint>(),
                            true);
                    }
                    else
                    {
                        leaders.Add(target);
                        pending.Push(target);

                        terminators[pc] = new Terminator(
                            BlockTerminator.Jump,
                            new List<uint> { target },
                            true);
                    }

                    break;
                }

                if (kind == ControlFlowKind.Call)
                {
                    callSites.Add(pc);
                    delaySlots.Add(pc + 4);

                    uint target = ControlFlow.GetTarget(instruction);

                    if (_range.Contains(target))
                    {
                        calls.Add(target);
                    }
                    else
                    {
                        external.Add(target);
                    }

                    // Assume que a função chamada retorna.
                    pc += 8;
                    continue;
                }

                if (kind == ControlFlowKind.IndirectCall)
                {
                    callSites.Add(pc);
                    delaySlots.Add(pc + 4);
                    pc += 8;
                    continue;
                }

                if (kind == ControlFlowKind.Return)
                {
                    delaySlots.Add(pc + 4);

                    terminators[pc] = new Terminator(
                        BlockTerminator.Return,
                        new List<uint>(),
                        true);

                    break;
                }

                if (kind == ControlFlowKind.IndirectJump)
                {
                    delaySlots.Add(pc + 4);

                    JumpTableInfo? table = _resolver.TryResolve(pc);

                    if (table is not null)
                    {
                        // Destinos distintos, na ordem em que aparecem na tabela.
                        var successors = new List<uint>();

                        foreach (uint target in table.Targets)
                        {
                            if (!successors.Contains(target))
                            {
                                successors.Add(target);
                                leaders.Add(target);
                                pending.Push(target);
                            }
                        }

                        jumpTables.Add(table);

                        terminators[pc] = new Terminator(
                            BlockTerminator.JumpTable,
                            successors,
                            true);
                    }
                    else
                    {
                        indirectJumps.Add(pc);

                        terminators[pc] = new Terminator(
                            BlockTerminator.IndirectJump,
                            new List<uint>(),
                            true);
                    }

                    break;
                }

                // Halt (ERET): sem delay slot.
                terminators[pc] = new Terminator(
                    BlockTerminator.Halt,
                    new List<uint>(),
                    false);

                break;
            }
        }

        // Todas as instruções da função, delay slots incluídos.
        var all = new HashSet<uint>(visited);
        all.UnionWith(delaySlots);

        uint[] addresses = all.ToArray();
        Array.Sort(addresses);

        return new FunctionInfo
        {
            Entry = entry,
            Blocks = BuildBlocks(visited, leaders, callSites, terminators),
            Addresses = addresses,
            Calls = calls,
            TailCalls = tailCalls,
            ExternalTargets = external,
            IndirectJumps = indirectJumps,
            JumpTables = jumpTables,
            OutOfRangeAddresses = outOfRange
        };
    }

    // Divide o fluxo em blocos básicos. Cada bloco começa em um líder e segue
    // até um desvio (com o delay slot) ou até o próximo líder. Um delay slot
    // que também é destino de salto aparece em dois blocos: no do desvio e
    // no bloco que começa nele.
    private static List<BasicBlock> BuildBlocks(
        HashSet<uint> flow,
        HashSet<uint> leaders,
        HashSet<uint> callSites,
        Dictionary<uint, Terminator> terminators)
    {
        var starts = leaders.Where(flow.Contains).ToList();
        starts.Sort();

        var blocks = new List<BasicBlock>();

        foreach (uint start in starts)
        {
            uint pc = start;

            while (true)
            {
                if (terminators.TryGetValue(pc, out Terminator terminator))
                {
                    uint end = pc + (terminator.HasDelaySlot ? 8u : 4u);

                    blocks.Add(new BasicBlock(
                        start,
                        end,
                        terminator.Kind,
                        terminator.Successors));

                    break;
                }

                // Uma chamada leva o delay slot junto: a próxima instrução fica 8 bytes à frente.
                uint next = pc + (callSites.Contains(pc) ? 8u : 4u);

                if (!flow.Contains(next) || leaders.Contains(next))
                {
                    var successors = new List<uint>();

                    if (flow.Contains(next))
                    {
                        successors.Add(next);
                    }

                    blocks.Add(new BasicBlock(
                        start,
                        next,
                        BlockTerminator.FallThrough,
                        successors));

                    break;
                }

                pc = next;
            }
        }

        return blocks;
    }

    private static HashSet<uint> CollectCovered(
        IReadOnlyDictionary<uint, FunctionInfo> functions)
    {
        var covered = new HashSet<uint>();

        foreach (FunctionInfo function in functions.Values)
        {
            covered.UnionWith(function.Addresses);
        }

        return covered;
    }

    // ADDIU $sp, $sp, -N: abertura típica de uma função com stack frame.
    private static bool IsPrologue(uint word) =>
        (word >> 16) == 0x27BD && (word & 0x8000) != 0;

    // Um início plausível de função vem logo após um retorno ou preenchimento.
    private bool IsPlausibleFunctionStart(uint address)
    {
        if (address - _unit.BaseAddress < 8)
        {
            return true;
        }

        uint twoBack = ReadWord(address - 8);
        uint oneBack = ReadWord(address - 4);

        return twoBack == JrRa || oneBack == 0 || oneBack == JrRa;
    }

    private uint ReadWord(uint address) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            _unit.Data.AsSpan((int)(address - _unit.BaseAddress), 4));

    private Instruction Fetch(uint address) =>
        R5900Decoder.Decode(address, ReadWord(address));
}
