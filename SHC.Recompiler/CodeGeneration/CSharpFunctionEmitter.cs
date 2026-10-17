using System.Buffers.Binary;
using System.Text;
using SHC.Recompiler.Analysis;
using SHC.Recompiler.Decoding;

namespace SHC.Recompiler.CodeGeneration;

/// <summary>
/// Gera o código C# de uma função recompilada.
///
/// Cada bloco básico vira um trecho do método, com um rótulo quando é destino
/// de um desvio. O delay slot é emitido antes do desvio ou da chamada.
/// Instruções ainda não suportadas viram uma chamada a ctx.Unsupported.
///
/// O código gerado usa dois atalhos locais: "r" (registradores, ulong[]) e
/// "m" (memória), e as funções Sx, Sx8 e Sx16 de SHC.Runtime.Cpu.Ops.
/// </summary>
public static class CSharpFunctionEmitter
{
    /// <summary>Nome do método gerado para a função que começa em <paramref name="entry"/>.</summary>
    public static string MethodName(uint entry) => $"F_{entry:X8}";

    /// <summary>Gera o método C# da função.</summary>
    public static string Emit(
        AnalysisResult analysis,
        FunctionInfo function,
        EmitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(function);

        return new FunctionEmitter(
            analysis,
            function,
            options ?? new EmitOptions()).Emit();
    }

    private sealed class FunctionEmitter
    {
        private readonly AnalysisResult _analysis;
        private readonly FunctionInfo _function;
        private readonly bool _comments;
        private readonly HashSet<uint> _labels = new();

        // Blocos que precisam de goto para o bloco seguinte no fluxo, por não ser
        // o próximo no arquivo: bloco -> destino.
        private readonly Dictionary<uint, uint> _explicitFall = new();

        public FunctionEmitter(
            AnalysisResult analysis,
            FunctionInfo function,
            EmitOptions options)
        {
            _analysis = analysis;
            _function = function;
            _comments = options.IncludeComments;
        }

        public string Emit()
        {
            // Só os destinos de goto precisam de rótulo; o fluxo normal
            // entre blocos vizinhos segue em sequência.
            foreach (BasicBlock block in _function.Blocks)
            {
                switch (block.Terminator)
                {
                    case BlockTerminator.Branch:
                    case BlockTerminator.BranchLikely:
                    case BlockTerminator.Jump:
                        _labels.Add(block.Successors[0]);
                        break;

                    case BlockTerminator.JumpTable:
                        foreach (uint successor in block.Successors)
                        {
                            _labels.Add(successor);
                        }

                        break;
                }
            }

            // Um bloco que não desvia segue para o bloco seguinte do fluxo. Se esse
            // não for o próximo no arquivo, é preciso um goto. Acontece quando um
            // salto cai no delay slot de outro desvio.
            IReadOnlyList<BasicBlock> ordered = _function.Blocks;

            for (int i = 0; i < ordered.Count; i++)
            {
                uint? fall = FallThroughTarget(ordered[i]);
                uint? following = i + 1 < ordered.Count ? ordered[i + 1].Start : null;

                if (fall is not null && fall != following)
                {
                    _labels.Add(fall.Value);
                    _explicitFall[ordered[i].Start] = fall.Value;
                }
            }

            // A execução começa na entrada. Os blocos saem em ordem crescente de
            // endereço; se algum fica abaixo da entrada (alcançado por um desvio
            // para trás), o primeiro bloco do arquivo não é a entrada e é preciso
            // saltar para ela no início do método.
            bool entryFirst =
                _function.Blocks.Count > 0 &&
                _function.Blocks[0].Start == _function.Entry;

            if (!entryFirst)
            {
                _labels.Add(_function.Entry);
            }

            var text = new StringBuilder();

            if (_comments)
            {
                text.AppendLine(
                    $"// Função 0x{_function.Entry:X8}: " +
                    $"{_function.Blocks.Count} bloco(s), {_function.Addresses.Length} instrução(ões)");
            }

            text.AppendLine(
                $"public static void {MethodName(_function.Entry)}(R5900Context ctx)");
            text.AppendLine("{");
            text.AppendLine("    ulong[] r = ctx.R;");
            text.AppendLine("    uint[] f = ctx.F;");
            text.AppendLine("    Ps2Memory m = ctx.Memory;");

            if (!entryFirst)
            {
                text.AppendLine($"    goto L_{_function.Entry:X8};");
            }

            foreach (BasicBlock block in _function.Blocks)
            {
                List<string> lines = EmitBlock(block);

                if (_comments)
                {
                    text.AppendLine();
                    text.AppendLine(
                        $"    // bloco 0x{block.Start:X8}-0x{block.End:X8} [{block.Terminator}]");
                }

                if (_labels.Contains(block.Start))
                {
                    // O rótulo precisa de um comando depois dele.
                    bool hasStatement = lines.Any(l => !l.TrimStart().StartsWith("//"));

                    text.AppendLine($"  L_{block.Start:X8}:{(hasStatement ? string.Empty : " ;")}");
                }

                foreach (string line in lines)
                {
                    text.AppendLine("    " + line);
                }
            }

            text.AppendLine("}");

            return text.ToString();
        }

        // Endereço para onde o fluxo segue quando o bloco não desvia.
        private static uint? FallThroughTarget(BasicBlock block) => block.Terminator switch
        {
            BlockTerminator.FallThrough =>
                block.Successors.Count > 0 ? (uint?)block.Successors[0] : null,
            BlockTerminator.Branch or BlockTerminator.BranchLikely =>
                block.Successors.Count > 1 ? (uint?)block.Successors[1] : null,
            _ => null
        };

        // Comentário com o assembly original, quando habilitado.
        private string Tag(string asm) => _comments ? $"  // {asm}" : string.Empty;

        // ----- Blocos -----

        private List<string> EmitBlock(BasicBlock block)
        {
            var lines = new List<string>();

            // Fim das instruções "comuns": o desvio e o delay slot ficam de fora.
            uint stop = block.Terminator switch
            {
                BlockTerminator.FallThrough => block.End,
                BlockTerminator.Halt => block.End - 4,
                _ => block.End - 8
            };

            uint address = block.Start;

            while (address < stop)
            {
                Instruction instruction = Fetch(address);
                ControlFlowKind kind = ControlFlow.Classify(instruction);

                if (kind == ControlFlowKind.Call)
                {
                    EmitCall(instruction, lines);
                    address += 8;
                }
                else if (kind == ControlFlowKind.IndirectCall)
                {
                    EmitIndirectCall(instruction, lines);
                    address += 8;
                }
                else
                {
                    lines.AddRange(EmitSequential(instruction));
                    address += 4;
                }
            }

            if (block.Terminator != BlockTerminator.FallThrough)
            {
                EmitTerminator(block, lines);
            }

            if (_explicitFall.TryGetValue(block.Start, out uint fallTarget))
            {
                lines.Add($"goto L_{fallTarget:X8};");
            }

            return lines;
        }

        private void EmitTerminator(BasicBlock block, List<string> lines)
        {
            bool hasDelaySlot = block.Terminator != BlockTerminator.Halt;

            Instruction term = Fetch(hasDelaySlot ? block.End - 8 : block.End - 4);
            string tag = Tag(R5900Disassembler.Format(term));

            List<string> delayLines = hasDelaySlot
                ? EmitDelaySlot(Fetch(block.End - 4))
                : new List<string>();

            switch (block.Terminator)
            {
                case BlockTerminator.Return:
                    lines.AddRange(delayLines);
                    lines.Add($"return;{tag}");
                    break;

                case BlockTerminator.Jump:
                    lines.AddRange(delayLines);
                    lines.Add($"goto L_{block.Successors[0]:X8};{tag}");
                    break;

                case BlockTerminator.TailCall:
                    lines.AddRange(delayLines);
                    lines.Add($"{CallStatement(term.JumpTarget)}{tag}");
                    lines.Add("return;");
                    break;

                case BlockTerminator.IndirectJump:
                {
                    // Salto por registrador que não foi resolvido: tratado como chamada em cauda.
                    string target = $"t_{term.Address:X8}";

                    lines.Add($"uint {target} = {U32(term.Rs)};{tag}");
                    lines.AddRange(delayLines);
                    lines.Add($"ctx.Call({target});");
                    lines.Add("return;");
                    break;
                }

                case BlockTerminator.JumpTable:
                {
                    string target = $"t_{term.Address:X8}";

                    lines.Add($"uint {target} = {U32(term.Rs)};{tag}");
                    lines.AddRange(delayLines);
                    lines.Add($"switch ({target})");
                    lines.Add("{");

                    foreach (uint successor in block.Successors)
                    {
                        lines.Add($"    case 0x{successor:X8}u: goto L_{successor:X8};");
                    }

                    lines.Add($"    default: ctx.UnresolvedJump({target}); return;");
                    lines.Add("}");
                    break;
                }

                case BlockTerminator.Branch:
                case BlockTerminator.BranchLikely:
                    EmitBranch(block, term, tag, delayLines, lines);
                    break;

                default:
                    lines.Add(Unsupported(term));
                    break;
            }
        }

        private void EmitBranch(
            BasicBlock block,
            Instruction term,
            string tag,
            List<string> delayLines,
            List<string> lines)
        {
            string? condition = Condition(term);

            if (condition is null)
            {
                lines.Add(Unsupported(term));
                return;
            }

            uint target = block.Successors[0];

            if (block.Terminator == BlockTerminator.Branch &&
                ControlFlow.IsUnconditional(term))
            {
                lines.AddRange(delayLines);
                lines.Add($"goto L_{target:X8};{tag}");
                return;
            }

            if (block.Terminator == BlockTerminator.BranchLikely)
            {
                // O delay slot de um desvio "likely" só executa se o desvio for tomado.
                lines.Add($"if ({condition}){tag}");
                lines.Add("{");

                foreach (string line in delayLines)
                {
                    lines.Add("    " + line);
                }

                lines.Add($"    goto L_{target:X8};");
                lines.Add("}");
                return;
            }

            // A condição é avaliada antes do delay slot. Se o delay slot altera
            // um registrador da condição, o resultado precisa ser guardado antes.
            Instruction delay = Fetch(block.End - 4);

            if (DelayWritesConditionRegister(term, delay))
            {
                string flag = $"c_{term.Address:X8}";

                lines.Add($"bool {flag} = {condition};{tag}");
                lines.AddRange(delayLines);
                lines.Add($"if ({flag}) goto L_{target:X8};");
            }
            else
            {
                lines.AddRange(delayLines);
                lines.Add($"if ({condition}) goto L_{target:X8};{tag}");
            }
        }

        private static bool DelayWritesConditionRegister(Instruction branch, Instruction delay)
        {
            // Desvios do FPU testam a condição do FPU, não um registrador geral.
            if (branch.Opcode is Opcode.Bc1t or Opcode.Bc1f or Opcode.Bc1tl or Opcode.Bc1fl)
            {
                return delay.Opcode is Opcode.C_F_S or Opcode.C_Eq_S or
                                       Opcode.C_Lt_S or Opcode.C_Le_S or Opcode.Ctc1;
            }

            int? written = JumpTableResolver.GetDestination(delay);

            if (written is null || written == 0)
            {
                return false;
            }

            bool usesRt = branch.Opcode is Opcode.Beq or Opcode.Bne or
                                           Opcode.Beql or Opcode.Bnel;

            return written == branch.Rs || (usesRt && written == branch.Rt);
        }

        private static string? Condition(Instruction branch) => branch.Opcode switch
        {
            Opcode.Beq or Opcode.Beql => $"{Reg(branch.Rs)} == {Reg(branch.Rt)}",
            Opcode.Bne or Opcode.Bnel => $"{Reg(branch.Rs)} != {Reg(branch.Rt)}",
            Opcode.Blez or Opcode.Blezl => $"{S64(branch.Rs)} <= 0",
            Opcode.Bgtz or Opcode.Bgtzl => $"{S64(branch.Rs)} > 0",
            Opcode.Bltz or Opcode.Bltzl => $"{S64(branch.Rs)} < 0",
            Opcode.Bgez or Opcode.Bgezl => $"{S64(branch.Rs)} >= 0",
            Opcode.Bc1t or Opcode.Bc1tl => "ctx.Condition",
            Opcode.Bc1f or Opcode.Bc1fl => "!ctx.Condition",
            _ => null
        };

        // ----- Chamadas -----

        private void EmitCall(Instruction call, List<string> lines)
        {
            string tag = Tag(R5900Disassembler.Format(call));

            // Suportados: JAL e BAL (BGEZAL $zero), que sempre chama.
            bool supported = call.Opcode == Opcode.Jal ||
                             (call.Opcode == Opcode.Bgezal && call.Rs == 0);

            if (!supported)
            {
                lines.Add(Unsupported(call));
                return;
            }

            // O endereço de retorno é gravado antes do delay slot, que executa antes da função.
            lines.Add($"r[31] = {Hex64(call.Address + 8)};{tag}");
            lines.AddRange(EmitDelaySlot(Fetch(call.Address + 4)));
            lines.Add(CallStatement(ControlFlow.GetTarget(call)));
        }

        private void EmitIndirectCall(Instruction call, List<string> lines)
        {
            string tag = Tag(R5900Disassembler.Format(call));
            string target = $"t_{call.Address:X8}";

            // O destino é lido antes de o registrador de retorno e o delay slot o alterarem.
            lines.Add($"uint {target} = {U32(call.Rs)};{tag}");

            if (call.Rd != 0)
            {
                lines.Add($"r[{call.Rd}] = {Hex64(call.Address + 8)};");
            }

            lines.AddRange(EmitDelaySlot(Fetch(call.Address + 4)));
            lines.Add($"ctx.Call({target});");
        }

        // Chamada direta quando a função foi recompilada; senão, pelo despacho em tempo de execução.
        private string CallStatement(uint target) =>
            _analysis.Functions.ContainsKey(target)
                ? $"{MethodName(target)}(ctx);"
                : $"ctx.Call(0x{target:X8}u);";

        private List<string> EmitDelaySlot(Instruction delay)
        {
            if (ControlFlow.Classify(delay) != ControlFlowKind.Sequential)
            {
                return new List<string> { Unsupported(delay) };
            }

            return EmitSequential(delay);
        }

        // ----- Instruções sequenciais -----

        private List<string> EmitSequential(Instruction i)
        {
            int rs = i.Rs;
            int rt = i.Rt;
            int rd = i.Rd;
            int sa = i.Sa;
            short imm = i.ImmediateSigned;
            ushort immU = i.ImmediateUnsigned;

            string asm = R5900Disassembler.Format(i);
            var lines = new List<string>();

            // A primeira linha de cada instrução recebe o assembly original como comentário.
            void Add(string statement) =>
                lines.Add(lines.Count == 0 ? $"{statement}{Tag(asm)}" : statement);

            // Escrever em $zero não tem efeito.
            void Set(int destination, string expression)
            {
                if (destination == 0)
                {
                    if (_comments)
                    {
                        lines.Add($"// {asm}  (escreve em $zero: sem efeito)");
                    }
                }
                else
                {
                    Add($"r[{destination}] = {expression};");
                }
            }

            switch (i.Opcode)
            {
                case Opcode.Sll when i.Word == 0:
                    if (_comments)
                    {
                        lines.Add("// nop");
                    }

                    break;

                // --- Aritmética de 32 bits (resultado estendido com sinal) ---
                case Opcode.Addu:
                case Opcode.Add:
                    Set(rd, $"Sx({Add32(rs, rt)})");
                    break;
                case Opcode.Addiu:
                case Opcode.Addi:
                    Set(rt, AddImmediate32(rs, imm));
                    break;
                case Opcode.Subu:
                case Opcode.Sub:
                    Set(rd, rt == 0 ? $"Sx({U32(rs)})" : $"Sx({U32(rs)} - {U32(rt)})");
                    break;

                // --- Aritmética de 64 bits ---
                case Opcode.Daddu:
                case Opcode.Dadd:
                    Set(rd, Add64(rs, rt));
                    break;
                case Opcode.Daddiu:
                case Opcode.Daddi:
                    Set(rt, AddImmediate64(rs, imm));
                    break;
                case Opcode.Dsubu:
                case Opcode.Dsub:
                    Set(rd, rt == 0 ? Reg(rs) : $"{Reg(rs)} - {Reg(rt)}");
                    break;

                // --- Lógica ---
                case Opcode.And:
                    Set(rd, $"{Reg(rs)} & {Reg(rt)}");
                    break;
                case Opcode.Or:
                    Set(rd, Or64(rs, rt));
                    break;
                case Opcode.Xor:
                    Set(rd, rt == 0 ? Reg(rs) : rs == 0 ? Reg(rt) : $"{Reg(rs)} ^ {Reg(rt)}");
                    break;
                case Opcode.Nor:
                    Set(rd, $"~({Reg(rs)} | {Reg(rt)})");
                    break;
                case Opcode.Andi:
                    Set(rt, $"{Reg(rs)} & {Hex64(immU)}");
                    break;
                case Opcode.Ori:
                    Set(rt, rs == 0 ? Hex64(immU) : immU == 0 ? Reg(rs) : $"{Reg(rs)} | {Hex64(immU)}");
                    break;
                case Opcode.Xori:
                    Set(rt, $"{Reg(rs)} ^ {Hex64(immU)}");
                    break;
                case Opcode.Lui:
                    Set(rt, Hex64(SignExtend((uint)immU << 16)));
                    break;

                // --- Comparações ---
                case Opcode.Slt:
                    Set(rd, $"({S64(rs)} < {S64(rt)}) ? 1UL : 0UL");
                    break;
                case Opcode.Sltu:
                    Set(rd, $"({Reg(rs)} < {Reg(rt)}) ? 1UL : 0UL");
                    break;
                case Opcode.Slti:
                    Set(rt, $"({S64(rs)} < {imm}L) ? 1UL : 0UL");
                    break;
                case Opcode.Sltiu:
                    // O imediato é estendido com sinal para 64 bits e comparado sem sinal.
                    Set(rt, $"({Reg(rs)} < {Hex64(SignExtend64(imm))}) ? 1UL : 0UL");
                    break;

                // --- Deslocamentos de 32 bits (o C# já limita a contagem a 5 bits) ---
                case Opcode.Sll:
                    Set(rd, $"Sx({U32(rt)} << {sa})");
                    break;
                case Opcode.Srl:
                    Set(rd, $"Sx({U32(rt)} >> {sa})");
                    break;
                case Opcode.Sra:
                    Set(rd, $"Sx((uint)({S32(rt)} >> {sa}))");
                    break;
                case Opcode.Sllv:
                    Set(rd, $"Sx({U32(rt)} << (int){Reg(rs)})");
                    break;
                case Opcode.Srlv:
                    Set(rd, $"Sx({U32(rt)} >> (int){Reg(rs)})");
                    break;
                case Opcode.Srav:
                    Set(rd, $"Sx((uint)({S32(rt)} >> (int){Reg(rs)}))");
                    break;

                // --- Deslocamentos de 64 bits (o C# já limita a contagem a 6 bits) ---
                case Opcode.Dsll:
                    Set(rd, $"{Reg(rt)} << {sa}");
                    break;
                case Opcode.Dsrl:
                    Set(rd, $"{Reg(rt)} >> {sa}");
                    break;
                case Opcode.Dsra:
                    Set(rd, $"(ulong)({S64(rt)} >> {sa})");
                    break;
                case Opcode.Dsll32:
                    Set(rd, $"{Reg(rt)} << {sa + 32}");
                    break;
                case Opcode.Dsrl32:
                    Set(rd, $"{Reg(rt)} >> {sa + 32}");
                    break;
                case Opcode.Dsra32:
                    Set(rd, $"(ulong)({S64(rt)} >> {sa + 32})");
                    break;
                case Opcode.Dsllv:
                    Set(rd, $"{Reg(rt)} << (int){Reg(rs)}");
                    break;
                case Opcode.Dsrlv:
                    Set(rd, $"{Reg(rt)} >> (int){Reg(rs)}");
                    break;
                case Opcode.Dsrav:
                    Set(rd, $"(ulong)({S64(rt)} >> (int){Reg(rs)})");
                    break;

                // --- Movimentos condicionais ---
                case Opcode.Movz:
                    if (rd != 0)
                    {
                        Add($"if ({Reg(rt)} == 0UL) r[{rd}] = {Reg(rs)};");
                    }

                    break;
                case Opcode.Movn:
                    if (rd != 0)
                    {
                        Add($"if ({Reg(rt)} != 0UL) r[{rd}] = {Reg(rs)};");
                    }

                    break;

                // --- HI e LO ---
                case Opcode.Mfhi:
                    Set(rd, "ctx.Hi");
                    break;
                case Opcode.Mflo:
                    Set(rd, "ctx.Lo");
                    break;
                case Opcode.Mfhi1:
                    Set(rd, "ctx.Hi1");
                    break;
                case Opcode.Mflo1:
                    Set(rd, "ctx.Lo1");
                    break;
                case Opcode.Mthi:
                    Add($"ctx.Hi = {Reg(rs)};");
                    break;
                case Opcode.Mtlo:
                    Add($"ctx.Lo = {Reg(rs)};");
                    break;
                case Opcode.Mthi1:
                    Add($"ctx.Hi1 = {Reg(rs)};");
                    break;
                case Opcode.Mtlo1:
                    Add($"ctx.Lo1 = {Reg(rs)};");
                    break;

                // --- Multiplicação e divisão (no R5900, MULT também copia LO para rd) ---
                case Opcode.Mult:
                    Add($"ctx.Mult({S32(rs)}, {S32(rt)});");
                    if (rd != 0) lines.Add($"r[{rd}] = ctx.Lo;");
                    break;
                case Opcode.Multu:
                    Add($"ctx.Multu({U32(rs)}, {U32(rt)});");
                    if (rd != 0) lines.Add($"r[{rd}] = ctx.Lo;");
                    break;
                case Opcode.Mult1:
                    Add($"ctx.Mult1({S32(rs)}, {S32(rt)});");
                    if (rd != 0) lines.Add($"r[{rd}] = ctx.Lo1;");
                    break;
                case Opcode.Multu1:
                    Add($"ctx.Multu1({U32(rs)}, {U32(rt)});");
                    if (rd != 0) lines.Add($"r[{rd}] = ctx.Lo1;");
                    break;
                case Opcode.Div:
                    Add($"ctx.Div({S32(rs)}, {S32(rt)});");
                    break;
                case Opcode.Divu:
                    Add($"ctx.Divu({U32(rs)}, {U32(rt)});");
                    break;
                case Opcode.Div1:
                    Add($"ctx.Div1({S32(rs)}, {S32(rt)});");
                    break;
                case Opcode.Divu1:
                    Add($"ctx.Divu1({U32(rs)}, {U32(rt)});");
                    break;

                // --- Leituras da memória ---
                case Opcode.Lb:
                    Set(rt, $"Sx8(m.Read8({Address(rs, imm)}))");
                    break;
                case Opcode.Lbu:
                    Set(rt, $"m.Read8({Address(rs, imm)})");
                    break;
                case Opcode.Lh:
                    Set(rt, $"Sx16(m.Read16({Address(rs, imm)}))");
                    break;
                case Opcode.Lhu:
                    Set(rt, $"m.Read16({Address(rs, imm)})");
                    break;
                case Opcode.Lw:
                    Set(rt, $"Sx(m.Read32({Address(rs, imm)}))");
                    break;
                case Opcode.Lwu:
                    Set(rt, $"m.Read32({Address(rs, imm)})");
                    break;
                case Opcode.Ld:
                    Set(rt, $"m.Read64({Address(rs, imm)})");
                    break;
                case Opcode.Lq:
                    if (rt != 0)
                    {
                        Add($"ctx.Lq({rt}, {Address(rs, imm)});");
                    }

                    break;
                case Opcode.Lwl:
                case Opcode.Lwr:
                case Opcode.Ldl:
                case Opcode.Ldr:
                    if (rt != 0)
                    {
                        Add($"ctx.{i.Opcode}({rt}, {Address(rs, imm)});");
                    }

                    break;

                // --- Gravações na memória ---
                case Opcode.Sb:
                    Add($"m.Write8({Address(rs, imm)}, (byte){Reg(rt)});");
                    break;
                case Opcode.Sh:
                    Add($"m.Write16({Address(rs, imm)}, (ushort){Reg(rt)});");
                    break;
                case Opcode.Sw:
                    Add($"m.Write32({Address(rs, imm)}, (uint){Reg(rt)});");
                    break;
                case Opcode.Sd:
                    Add($"m.Write64({Address(rs, imm)}, {Reg(rt)});");
                    break;
                case Opcode.Sq:
                    Add($"ctx.Sq({rt}, {Address(rs, imm)});");
                    break;
                case Opcode.Swl:
                case Opcode.Swr:
                case Opcode.Sdl:
                case Opcode.Sdr:
                    Add($"ctx.{i.Opcode}({rt}, {Address(rs, imm)});");
                    break;

                // --- FPU (COP1): fs é o campo rd, ft é o campo rt e fd é o campo sa ---
                case Opcode.Mtc1:
                    Add($"f[{rd}] = (uint){Reg(rt)};");
                    break;
                case Opcode.Mfc1:
                    Set(rt, $"Sx(f[{rd}])");
                    break;
                case Opcode.Cfc1:
                    Set(rt, $"Sx(ctx.ReadFcr({rd}))");
                    break;
                case Opcode.Ctc1:
                    Add($"ctx.WriteFcr({rd}, (uint){Reg(rt)});");
                    break;
                case Opcode.Lwc1:
                    Add($"f[{rt}] = m.Read32({Address(rs, imm)});");
                    break;
                case Opcode.Swc1:
                    Add($"m.Write32({Address(rs, imm)}, f[{rt}]);");
                    break;

                case Opcode.Add_S:
                    Add($"f[{sa}] = Fpu.Add(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Sub_S:
                    Add($"f[{sa}] = Fpu.Sub(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Mul_S:
                    Add($"f[{sa}] = Fpu.Mul(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Div_S:
                    Add($"f[{sa}] = Fpu.Div(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Sqrt_S:
                    Add($"f[{sa}] = Fpu.Sqrt(f[{rt}]);");
                    break;
                case Opcode.Rsqrt_S:
                    Add($"f[{sa}] = Fpu.Rsqrt(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Abs_S:
                    Add($"f[{sa}] = Fpu.Abs(f[{rd}]);");
                    break;
                case Opcode.Neg_S:
                    Add($"f[{sa}] = Fpu.Neg(f[{rd}]);");
                    break;
                case Opcode.Mov_S:
                    Add($"f[{sa}] = f[{rd}];");
                    break;
                case Opcode.Max_S:
                    Add($"f[{sa}] = Fpu.Max(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Min_S:
                    Add($"f[{sa}] = Fpu.Min(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Cvt_S_W:
                    Add($"f[{sa}] = Fpu.FromInt((int)f[{rd}]);");
                    break;
                case Opcode.Cvt_W_S:
                    Add($"f[{sa}] = Fpu.ToInt(f[{rd}]);");
                    break;

                // Operações com o acumulador.
                case Opcode.Adda_S:
                    Add($"ctx.Acc = Fpu.Add(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Suba_S:
                    Add($"ctx.Acc = Fpu.Sub(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Mula_S:
                    Add($"ctx.Acc = Fpu.Mul(f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Madd_S:
                    Add($"f[{sa}] = Fpu.Madd(ctx.Acc, f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Msub_S:
                    Add($"f[{sa}] = Fpu.Msub(ctx.Acc, f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Madda_S:
                    Add($"ctx.Acc = Fpu.Madd(ctx.Acc, f[{rd}], f[{rt}]);");
                    break;
                case Opcode.Msuba_S:
                    Add($"ctx.Acc = Fpu.Msub(ctx.Acc, f[{rd}], f[{rt}]);");
                    break;

                // Comparações: o resultado vai para a condição do FPU.
                case Opcode.C_F_S:
                    Add("ctx.SetCondition(false);");
                    break;
                case Opcode.C_Eq_S:
                    Add($"ctx.SetCondition(Fpu.Eq(f[{rd}], f[{rt}]));");
                    break;
                case Opcode.C_Lt_S:
                    Add($"ctx.SetCondition(Fpu.Lt(f[{rd}], f[{rt}]));");
                    break;
                case Opcode.C_Le_S:
                    Add($"ctx.SetCondition(Fpu.Le(f[{rd}], f[{rt}]));");
                    break;

                // --- Sem efeito na recompilação ---
                case Opcode.Sync:
                case Opcode.Cache:
                case Opcode.Pref:
                    if (_comments)
                    {
                        lines.Add($"// {asm}  (sem efeito)");
                    }

                    break;

                default:
                    lines.Add(Unsupported(i));
                    break;
            }

            return lines;
        }

        // ----- Texto de expressões -----

        // 64 bits baixos do registrador; $zero vira a constante 0.
        private static string Reg(int n) => n == 0 ? "0UL" : $"r[{n}]";

        private static string U32(int n) => n == 0 ? "0u" : $"(uint)r[{n}]";

        private static string S32(int n) => n == 0 ? "0" : $"(int)r[{n}]";

        private static string S64(int n) => n == 0 ? "0L" : $"(long)r[{n}]";

        // Literais curtos: até 9 em decimal, acima disso em hexadecimal.
        private static string Hex32(uint value) => value <= 9 ? $"{value}u" : $"0x{value:X}u";

        private static string Hex64(ulong value) => value <= 9 ? $"{value}UL" : $"0x{value:X}UL";

        private static ulong SignExtend(uint value) => unchecked((ulong)(long)(int)value);

        private static ulong SignExtend64(short imm) => unchecked((ulong)(long)imm);

        // Soma de 32 bits, sem somar $zero.
        private static string Add32(int a, int b) =>
            a == 0 && b == 0 ? "0u" :
            a == 0 ? U32(b) :
            b == 0 ? U32(a) :
            $"{U32(a)} + {U32(b)}";

        // ADDIU: com $zero vira uma constante; com imediato zero, só a extensão de sinal.
        private static string AddImmediate32(int rs, short imm)
        {
            uint offset = unchecked((uint)(int)imm);

            if (rs == 0)
            {
                return Hex64(SignExtend(offset));
            }

            return imm == 0
                ? $"Sx({U32(rs)})"
                : $"Sx({U32(rs)} + {Hex32(offset)})";
        }

        // Soma de 64 bits, sem somar $zero.
        private static string Add64(int a, int b) =>
            a == 0 && b == 0 ? "0UL" :
            a == 0 ? Reg(b) :
            b == 0 ? Reg(a) :
            $"{Reg(a)} + {Reg(b)}";

        private static string AddImmediate64(int rs, short imm)
        {
            ulong value = SignExtend64(imm);

            if (rs == 0)
            {
                return Hex64(value);
            }

            return imm == 0 ? Reg(rs) : $"{Reg(rs)} + {Hex64(value)}";
        }

        private static string Or64(int a, int b) =>
            a == 0 && b == 0 ? "0UL" :
            a == 0 ? Reg(b) :
            b == 0 ? Reg(a) :
            $"{Reg(a)} | {Reg(b)}";

        // Endereço efetivo: base + deslocamento, em 32 bits.
        private static string Address(int baseRegister, short imm)
        {
            uint offset = unchecked((uint)(int)imm);

            if (baseRegister == 0)
            {
                return Hex32(offset);
            }

            return offset == 0
                ? U32(baseRegister)
                : $"{U32(baseRegister)} + {Hex32(offset)}";
        }

        private static string Unsupported(Instruction i) =>
            $"ctx.Unsupported(0x{i.Address:X8}u, 0x{i.Word:X8}u);";

        private Instruction Fetch(uint address) =>
            R5900Decoder.Decode(
                address,
                BinaryPrimitives.ReadUInt32LittleEndian(
                    _analysis.Unit.Data.AsSpan(
                        (int)(address - _analysis.Unit.BaseAddress),
                        4)));
    }
}
