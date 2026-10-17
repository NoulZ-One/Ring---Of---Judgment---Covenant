using SHC.Recompiler.Decoding;
using SHC.Runtime.Cpu;
using SHC.Runtime.Machine;

/// <summary>O interpretador executou mais instruções do que o permitido.</summary>
internal sealed class BudgetExceededException : Exception
{
}

/// <summary>
/// Interpretador de referência do R5900, no ciclo clássico: busca uma instrução,
/// executa, e trata o delay slot logo depois do desvio. Não usa nada da
/// geração de código (blocos, rótulos, tabelas de switch): serve para comparar
/// com o código recompilado. As operações de aritmética foram escritas de forma
/// independente do emissor; auxiliares complexos (multiplicação, divisão, acessos
/// desalinhados, FPU) vêm do R5900Context e do Fpu, que o emissor também usa.
/// </summary>
internal sealed class Interpreter
{
    private readonly R5900Context _c;
    private readonly Ps2Memory _m;
    private readonly ulong[] _r;

    public Interpreter(R5900Context context)
    {
        _c = context;
        _m = context.Memory;
        _r = context.R;
    }

    /// <summary>Executa a função em <paramref name="entry"/> até voltar ao endereço sentinela.</summary>
    public void Run(uint entry, uint sentinel, long budget)
    {
        uint pc = entry;
        long steps = 0;

        while (pc != sentinel)
        {
            if (++steps > budget)
            {
                throw new BudgetExceededException();
            }

            pc = Step(pc);
        }
    }

    private Instruction Fetch(uint address) =>
        R5900Decoder.Decode(address, _m.Read32(address));

    // Executa a instrução em pc (e o delay slot, se houver) e devolve o próximo pc.
    private uint Step(uint pc)
    {
        Instruction i = Fetch(pc);
        int rs = i.Rs;
        int rt = i.Rt;

        switch (i.Opcode)
        {
            case Opcode.Beq: return Branch(pc, i, _r[rs] == _r[rt], false);
            case Opcode.Bne: return Branch(pc, i, _r[rs] != _r[rt], false);
            case Opcode.Blez: return Branch(pc, i, (long)_r[rs] <= 0, false);
            case Opcode.Bgtz: return Branch(pc, i, (long)_r[rs] > 0, false);
            case Opcode.Bltz: return Branch(pc, i, (long)_r[rs] < 0, false);
            case Opcode.Bgez: return Branch(pc, i, (long)_r[rs] >= 0, false);
            case Opcode.Bc1t: return Branch(pc, i, _c.Condition, false);
            case Opcode.Bc1f: return Branch(pc, i, !_c.Condition, false);

            case Opcode.Beql: return Branch(pc, i, _r[rs] == _r[rt], true);
            case Opcode.Bnel: return Branch(pc, i, _r[rs] != _r[rt], true);
            case Opcode.Blezl: return Branch(pc, i, (long)_r[rs] <= 0, true);
            case Opcode.Bgtzl: return Branch(pc, i, (long)_r[rs] > 0, true);
            case Opcode.Bltzl: return Branch(pc, i, (long)_r[rs] < 0, true);
            case Opcode.Bgezl: return Branch(pc, i, (long)_r[rs] >= 0, true);
            case Opcode.Bc1tl: return Branch(pc, i, _c.Condition, true);
            case Opcode.Bc1fl: return Branch(pc, i, !_c.Condition, true);

            case Opcode.Bgezal when rs == 0:
                _r[31] = pc + 8;
                Exec(Fetch(pc + 4));
                return i.BranchTarget;

            case Opcode.J:
                Exec(Fetch(pc + 4));
                return i.JumpTarget;

            case Opcode.Jal:
                _r[31] = pc + 8;
                Exec(Fetch(pc + 4));
                return i.JumpTarget;

            case Opcode.Jr:
            {
                uint target = (uint)_r[rs];

                Exec(Fetch(pc + 4));
                return target;
            }

            case Opcode.Jalr:
            {
                uint target = (uint)_r[rs];

                if (i.Rd != 0)
                {
                    _r[i.Rd] = pc + 8;
                }

                Exec(Fetch(pc + 4));
                return target;
            }

            default:
                Exec(i);
                return pc + 4;
        }
    }

    // A condição já foi avaliada antes do delay slot. Em um desvio "likely",
    // o delay slot só executa se o desvio for tomado.
    private uint Branch(uint pc, Instruction branch, bool taken, bool likely)
    {
        Instruction delay = Fetch(pc + 4);

        if (likely)
        {
            if (!taken)
            {
                return pc + 8;
            }

            Exec(delay);
            return branch.BranchTarget;
        }

        Exec(delay);
        return taken ? branch.BranchTarget : pc + 8;
    }

    private void Set(int register, ulong value)
    {
        if (register != 0)
        {
            _r[register] = value;
        }
    }

    // Executa uma instrução que não é desvio nem salto.
    private void Exec(Instruction i)
    {
        int rs = i.Rs;
        int rt = i.Rt;
        int rd = i.Rd;
        int sa = i.Sa;
        short imm = i.ImmediateSigned;
        ushort immU = i.ImmediateUnsigned;
        ulong[] r = _r;

        // Endereço efetivo: soma em 64 bits, truncada para 32.
        uint address = unchecked((uint)(r[rs] + (ulong)(long)imm));

        switch (i.Opcode)
        {
            // --- Aritmética ---
            case Opcode.Addu:
            case Opcode.Add:
                Set(rd, (ulong)(long)unchecked((int)r[rs] + (int)r[rt]));
                break;
            case Opcode.Addiu:
            case Opcode.Addi:
                Set(rt, (ulong)(long)unchecked((int)r[rs] + imm));
                break;
            case Opcode.Subu:
            case Opcode.Sub:
                Set(rd, (ulong)(long)unchecked((int)r[rs] - (int)r[rt]));
                break;
            case Opcode.Daddu:
            case Opcode.Dadd:
                Set(rd, r[rs] + r[rt]);
                break;
            case Opcode.Daddiu:
            case Opcode.Daddi:
                Set(rt, r[rs] + (ulong)(long)imm);
                break;
            case Opcode.Dsubu:
            case Opcode.Dsub:
                Set(rd, r[rs] - r[rt]);
                break;

            // --- Lógica ---
            case Opcode.And: Set(rd, r[rs] & r[rt]); break;
            case Opcode.Or: Set(rd, r[rs] | r[rt]); break;
            case Opcode.Xor: Set(rd, r[rs] ^ r[rt]); break;
            case Opcode.Nor: Set(rd, ~(r[rs] | r[rt])); break;
            case Opcode.Andi: Set(rt, r[rs] & immU); break;
            case Opcode.Ori: Set(rt, r[rs] | immU); break;
            case Opcode.Xori: Set(rt, r[rs] ^ immU); break;
            case Opcode.Lui:
                Set(rt, (ulong)(long)(int)((uint)immU << 16));
                break;

            // --- Comparações ---
            case Opcode.Slt: Set(rd, (long)r[rs] < (long)r[rt] ? 1UL : 0UL); break;
            case Opcode.Sltu: Set(rd, r[rs] < r[rt] ? 1UL : 0UL); break;
            case Opcode.Slti: Set(rt, (long)r[rs] < imm ? 1UL : 0UL); break;
            case Opcode.Sltiu: Set(rt, r[rs] < (ulong)(long)imm ? 1UL : 0UL); break;

            // --- Deslocamentos ---
            case Opcode.Sll: Set(rd, (ulong)(long)(int)((uint)r[rt] << sa)); break;
            case Opcode.Srl: Set(rd, (ulong)(long)(int)((uint)r[rt] >> sa)); break;
            case Opcode.Sra: Set(rd, (ulong)(long)((int)r[rt] >> sa)); break;
            case Opcode.Sllv:
                Set(rd, (ulong)(long)(int)((uint)r[rt] << (int)(r[rs] & 31)));
                break;
            case Opcode.Srlv:
                Set(rd, (ulong)(long)(int)((uint)r[rt] >> (int)(r[rs] & 31)));
                break;
            case Opcode.Srav:
                Set(rd, (ulong)(long)((int)r[rt] >> (int)(r[rs] & 31)));
                break;
            case Opcode.Dsll: Set(rd, r[rt] << sa); break;
            case Opcode.Dsrl: Set(rd, r[rt] >> sa); break;
            case Opcode.Dsra: Set(rd, (ulong)((long)r[rt] >> sa)); break;
            case Opcode.Dsll32: Set(rd, r[rt] << (sa + 32)); break;
            case Opcode.Dsrl32: Set(rd, r[rt] >> (sa + 32)); break;
            case Opcode.Dsra32: Set(rd, (ulong)((long)r[rt] >> (sa + 32))); break;
            case Opcode.Dsllv: Set(rd, r[rt] << (int)(r[rs] & 63)); break;
            case Opcode.Dsrlv: Set(rd, r[rt] >> (int)(r[rs] & 63)); break;
            case Opcode.Dsrav: Set(rd, (ulong)((long)r[rt] >> (int)(r[rs] & 63))); break;

            // --- Movimentos condicionais ---
            case Opcode.Movz:
                if (r[rt] == 0) Set(rd, r[rs]);
                break;
            case Opcode.Movn:
                if (r[rt] != 0) Set(rd, r[rs]);
                break;

            // --- HI e LO ---
            case Opcode.Mfhi: Set(rd, _c.Hi); break;
            case Opcode.Mflo: Set(rd, _c.Lo); break;
            case Opcode.Mfhi1: Set(rd, _c.Hi1); break;
            case Opcode.Mflo1: Set(rd, _c.Lo1); break;
            case Opcode.Mthi: _c.Hi = r[rs]; break;
            case Opcode.Mtlo: _c.Lo = r[rs]; break;
            case Opcode.Mthi1: _c.Hi1 = r[rs]; break;
            case Opcode.Mtlo1: _c.Lo1 = r[rs]; break;

            case Opcode.Mult:
                _c.Mult((int)r[rs], (int)r[rt]);
                Set(rd, _c.Lo);
                break;
            case Opcode.Multu:
                _c.Multu((uint)r[rs], (uint)r[rt]);
                Set(rd, _c.Lo);
                break;
            case Opcode.Mult1:
                _c.Mult1((int)r[rs], (int)r[rt]);
                Set(rd, _c.Lo1);
                break;
            case Opcode.Multu1:
                _c.Multu1((uint)r[rs], (uint)r[rt]);
                Set(rd, _c.Lo1);
                break;
            case Opcode.Div: _c.Div((int)r[rs], (int)r[rt]); break;
            case Opcode.Divu: _c.Divu((uint)r[rs], (uint)r[rt]); break;
            case Opcode.Div1: _c.Div1((int)r[rs], (int)r[rt]); break;
            case Opcode.Divu1: _c.Divu1((uint)r[rs], (uint)r[rt]); break;

            // --- Leituras (um destino $zero não acessa a memória) ---
            case Opcode.Lb:
                if (rt != 0) r[rt] = (ulong)(long)(sbyte)_m.Read8(address);
                break;
            case Opcode.Lbu:
                if (rt != 0) r[rt] = _m.Read8(address);
                break;
            case Opcode.Lh:
                if (rt != 0) r[rt] = (ulong)(long)(short)_m.Read16(address);
                break;
            case Opcode.Lhu:
                if (rt != 0) r[rt] = _m.Read16(address);
                break;
            case Opcode.Lw:
                if (rt != 0) r[rt] = (ulong)(long)(int)_m.Read32(address);
                break;
            case Opcode.Lwu:
                if (rt != 0) r[rt] = _m.Read32(address);
                break;
            case Opcode.Ld:
                if (rt != 0) r[rt] = _m.Read64(address);
                break;
            case Opcode.Lq:
                if (rt != 0) _c.Lq(rt, address);
                break;
            case Opcode.Lwl:
                if (rt != 0) _c.Lwl(rt, address);
                break;
            case Opcode.Lwr:
                if (rt != 0) _c.Lwr(rt, address);
                break;
            case Opcode.Ldl:
                if (rt != 0) _c.Ldl(rt, address);
                break;
            case Opcode.Ldr:
                if (rt != 0) _c.Ldr(rt, address);
                break;

            // --- Gravações ---
            case Opcode.Sb: _m.Write8(address, (byte)r[rt]); break;
            case Opcode.Sh: _m.Write16(address, (ushort)r[rt]); break;
            case Opcode.Sw: _m.Write32(address, (uint)r[rt]); break;
            case Opcode.Sd: _m.Write64(address, r[rt]); break;
            case Opcode.Sq: _c.Sq(rt, address); break;
            case Opcode.Swl: _c.Swl(rt, address); break;
            case Opcode.Swr: _c.Swr(rt, address); break;
            case Opcode.Sdl: _c.Sdl(rt, address); break;
            case Opcode.Sdr: _c.Sdr(rt, address); break;

            // --- Sem efeito ---
            case Opcode.Sync:
            case Opcode.Cache:
            case Opcode.Pref:
                break;

            // --- FPU: fs = rd, ft = rt, fd = sa ---
            case Opcode.Mtc1: _c.F[rd] = (uint)r[rt]; break;
            case Opcode.Mfc1: Set(rt, (ulong)(long)(int)_c.F[rd]); break;
            case Opcode.Cfc1: Set(rt, (ulong)(long)(int)_c.ReadFcr(rd)); break;
            case Opcode.Ctc1: _c.WriteFcr(rd, (uint)r[rt]); break;
            case Opcode.Lwc1: _c.F[rt] = _m.Read32(address); break;
            case Opcode.Swc1: _m.Write32(address, _c.F[rt]); break;

            case Opcode.Add_S: _c.F[sa] = Fpu.Add(_c.F[rd], _c.F[rt]); break;
            case Opcode.Sub_S: _c.F[sa] = Fpu.Sub(_c.F[rd], _c.F[rt]); break;
            case Opcode.Mul_S: _c.F[sa] = Fpu.Mul(_c.F[rd], _c.F[rt]); break;
            case Opcode.Div_S: _c.F[sa] = Fpu.Div(_c.F[rd], _c.F[rt]); break;
            case Opcode.Sqrt_S: _c.F[sa] = Fpu.Sqrt(_c.F[rt]); break;
            case Opcode.Rsqrt_S: _c.F[sa] = Fpu.Rsqrt(_c.F[rd], _c.F[rt]); break;
            case Opcode.Abs_S: _c.F[sa] = Fpu.Abs(_c.F[rd]); break;
            case Opcode.Neg_S: _c.F[sa] = Fpu.Neg(_c.F[rd]); break;
            case Opcode.Mov_S: _c.F[sa] = _c.F[rd]; break;
            case Opcode.Max_S: _c.F[sa] = Fpu.Max(_c.F[rd], _c.F[rt]); break;
            case Opcode.Min_S: _c.F[sa] = Fpu.Min(_c.F[rd], _c.F[rt]); break;
            case Opcode.Cvt_S_W: _c.F[sa] = Fpu.FromInt((int)_c.F[rd]); break;
            case Opcode.Cvt_W_S: _c.F[sa] = Fpu.ToInt(_c.F[rd]); break;
            case Opcode.Adda_S: _c.Acc = Fpu.Add(_c.F[rd], _c.F[rt]); break;
            case Opcode.Suba_S: _c.Acc = Fpu.Sub(_c.F[rd], _c.F[rt]); break;
            case Opcode.Mula_S: _c.Acc = Fpu.Mul(_c.F[rd], _c.F[rt]); break;
            case Opcode.Madd_S: _c.F[sa] = Fpu.Madd(_c.Acc, _c.F[rd], _c.F[rt]); break;
            case Opcode.Msub_S: _c.F[sa] = Fpu.Msub(_c.Acc, _c.F[rd], _c.F[rt]); break;
            case Opcode.Madda_S: _c.Acc = Fpu.Madd(_c.Acc, _c.F[rd], _c.F[rt]); break;
            case Opcode.Msuba_S: _c.Acc = Fpu.Msub(_c.Acc, _c.F[rd], _c.F[rt]); break;
            case Opcode.C_F_S: _c.SetCondition(false); break;
            case Opcode.C_Eq_S: _c.SetCondition(Fpu.Eq(_c.F[rd], _c.F[rt])); break;
            case Opcode.C_Lt_S: _c.SetCondition(Fpu.Lt(_c.F[rd], _c.F[rt])); break;
            case Opcode.C_Le_S: _c.SetCondition(Fpu.Le(_c.F[rd], _c.F[rt])); break;

            // Qualquer outra coisa (inclusive desvio no delay slot) não é suportada,
            // com a mesma mensagem do código recompilado.
            default:
                throw new NotImplementedException(
                    $"Instrução ainda não suportada: 0x{i.Word:X8} em 0x{i.Address:X8}.");
        }
    }
}
