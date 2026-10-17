using SHC.Runtime.Machine;

namespace SHC.Runtime.Cpu;

/// <summary>
/// Estado do processador Emotion Engine (R5900) usado pelo código recompilado.
/// Cada registrador geral tem 128 bits, guardados em duas metades: a baixa
/// (R) e a alta (RHigh). Instruções comuns só escrevem a metade baixa;
/// LQ, SQ e as instruções MMI usam também a alta.
/// </summary>
public sealed class R5900Context
{
    private readonly Dictionary<uint, Action<R5900Context>> _functions = new();

    public R5900Context(Ps2Memory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        Memory = memory;
    }

    /// <summary>RAM do PS2.</summary>
    public Ps2Memory Memory { get; }

    /// <summary>64 bits baixos dos 32 registradores gerais. R[0] é sempre zero.</summary>
    public readonly ulong[] R = new ulong[32];

    /// <summary>64 bits altos dos 32 registradores gerais.</summary>
    public readonly ulong[] RHigh = new ulong[32];

    /// <summary>Registradores de multiplicação e divisão (pipeline 0).</summary>
    public ulong Lo, Hi;

    /// <summary>Registradores de multiplicação e divisão (pipeline 1, instruções MULT1/DIV1...).</summary>
    public ulong Lo1, Hi1;

    /// <summary>Registradores de ponto flutuante, como bits no formato do PS2.</summary>
    public readonly uint[] F = new uint[32];

    /// <summary>Acumulador do FPU (usado por ADDA.S, MULA.S, MADD.S...).</summary>
    public uint Acc;

    /// <summary>Registrador de controle e estado do FPU (FCR31). O bit 23 é a condição.</summary>
    public uint Fcr31;

    private const uint ConditionBit = 0x00800000u;

    /// <summary>Resultado da última comparação do FPU (testado por BC1T e BC1F).</summary>
    public bool Condition => (Fcr31 & ConditionBit) != 0;

    /// <summary>Define o resultado de uma comparação do FPU.</summary>
    public void SetCondition(bool value) =>
        Fcr31 = value ? Fcr31 | ConditionBit : Fcr31 & ~ConditionBit;

    /// <summary>CFC1: lê um registrador de controle do FPU (0 = revisão, 31 = estado).</summary>
    public uint ReadFcr(int index) =>
        index == 31 ? Fcr31 : index == 0 ? 0x00002E00u : 0u;

    /// <summary>CTC1: grava o registrador de controle 31 (só os bits graváveis).</summary>
    public void WriteFcr(int index, uint value)
    {
        if (index == 31)
        {
            Fcr31 = value & 0x0183FFFFu;
        }
    }

    // ----- Despacho de funções -----

    /// <summary>Endereços das funções registradas.</summary>
    public IReadOnlyCollection<uint> RegisteredFunctions => _functions.Keys;

    /// <summary>Registra uma função recompilada pelo endereço original.</summary>
    public void RegisterFunction(uint address, Action<R5900Context> function) =>
        _functions[address] = function;

    /// <summary>Chama a função recompilada que começa no endereço.</summary>
    public void Call(uint address)
    {
        if (!_functions.TryGetValue(address, out var function))
        {
            throw new InvalidOperationException(
                $"Não há função recompilada registrada em 0x{address:X8}.");
        }

        function(this);
    }

    /// <summary>Instrução que o recompilador ainda não sabe traduzir.</summary>
    public void Unsupported(uint address, uint word) =>
        throw new NotImplementedException(
            $"Instrução ainda não suportada: 0x{word:X8} em 0x{address:X8}.");

    /// <summary>Salto por tabela com um destino que não consta na tabela.</summary>
    public void UnresolvedJump(uint target) =>
        throw new InvalidOperationException(
            $"Salto indireto para 0x{target:X8}, que não é um destino conhecido.");

    // ----- Multiplicação e divisão -----

    public void Mult(int a, int b) => (Lo, Hi) = MultCore(a, b);
    public void Mult1(int a, int b) => (Lo1, Hi1) = MultCore(a, b);
    public void Multu(uint a, uint b) => (Lo, Hi) = MultuCore(a, b);
    public void Multu1(uint a, uint b) => (Lo1, Hi1) = MultuCore(a, b);
    public void Div(int a, int b) => (Lo, Hi) = DivCore(a, b);
    public void Div1(int a, int b) => (Lo1, Hi1) = DivCore(a, b);
    public void Divu(uint a, uint b) => (Lo, Hi) = DivuCore(a, b);
    public void Divu1(uint a, uint b) => (Lo1, Hi1) = DivuCore(a, b);

    private static (ulong Lo, ulong Hi) MultCore(int a, int b)
    {
        long product = (long)a * b;

        return (SignExtend((uint)product), SignExtend((uint)(product >> 32)));
    }

    private static (ulong Lo, ulong Hi) MultuCore(uint a, uint b)
    {
        ulong product = (ulong)a * b;

        return (SignExtend((uint)product), SignExtend((uint)(product >> 32)));
    }

    private static (ulong Lo, ulong Hi) DivCore(int a, int b)
    {
        // Divisão por zero e MinValue / -1 não geram exceção no R5900.
        if (b == 0)
        {
            return (a >= 0 ? ulong.MaxValue : 1UL, SignExtend((uint)a));
        }

        if (a == int.MinValue && b == -1)
        {
            return (SignExtend(0x80000000u), 0UL);
        }

        return (SignExtend((uint)(a / b)), SignExtend((uint)(a % b)));
    }

    private static (ulong Lo, ulong Hi) DivuCore(uint a, uint b)
    {
        if (b == 0)
        {
            return (ulong.MaxValue, SignExtend(a));
        }

        return (SignExtend(a / b), SignExtend(a % b));
    }

    private static ulong SignExtend(uint value) => (ulong)(long)(int)value;

    // ----- Acesso a 128 bits -----

    /// <summary>LQ: carrega 128 bits (endereço alinhado para baixo em 16 bytes).</summary>
    public void Lq(int rt, uint address)
    {
        uint aligned = address & ~0xFu;

        R[rt] = Memory.Read64(aligned);
        RHigh[rt] = Memory.Read64(aligned + 8);
    }

    /// <summary>SQ: grava 128 bits (endereço alinhado para baixo em 16 bytes).</summary>
    public void Sq(int rt, uint address)
    {
        uint aligned = address & ~0xFu;

        Memory.Write64(aligned, R[rt]);
        Memory.Write64(aligned + 8, RHigh[rt]);
    }

    // ----- Acessos desalinhados (little-endian) -----

    /// <summary>LWL: carrega os bytes mais altos do registrador a partir de um endereço desalinhado.</summary>
    public void Lwl(int rt, uint address)
    {
        int shift = 8 * (3 - (int)(address & 3));
        uint word = Memory.Read32(address & ~3u);
        uint kept = (uint)R[rt] & ((1u << shift) - 1);

        R[rt] = SignExtend(kept | (word << shift));
    }

    /// <summary>LWR: carrega os bytes mais baixos do registrador a partir de um endereço desalinhado.</summary>
    public void Lwr(int rt, uint address)
    {
        int bytes = (int)(address & 3);
        uint word = Memory.Read32(address & ~3u);
        uint kept = (uint)R[rt] & ~(0xFFFFFFFFu >> (8 * bytes));
        uint result = kept | (word >> (8 * bytes));

        // Com endereço alinhado a palavra toda é carregada e estendida com sinal.
        R[rt] = bytes == 0
            ? SignExtend(result)
            : (R[rt] & 0xFFFFFFFF00000000UL) | result;
    }

    /// <summary>SWL: grava os bytes mais altos do registrador em um endereço desalinhado.</summary>
    public void Swl(int rt, uint address)
    {
        int shift = 8 * (3 - (int)(address & 3));
        uint aligned = address & ~3u;
        uint kept = Memory.Read32(aligned) & ~(0xFFFFFFFFu >> shift);

        Memory.Write32(aligned, ((uint)R[rt] >> shift) | kept);
    }

    /// <summary>SWR: grava os bytes mais baixos do registrador em um endereço desalinhado.</summary>
    public void Swr(int rt, uint address)
    {
        int bytes = (int)(address & 3);
        uint aligned = address & ~3u;
        uint kept = Memory.Read32(aligned) & ((1u << (8 * bytes)) - 1);

        Memory.Write32(aligned, ((uint)R[rt] << (8 * bytes)) | kept);
    }

    /// <summary>LDL: versão de 64 bits do LWL.</summary>
    public void Ldl(int rt, uint address)
    {
        int shift = 8 * (7 - (int)(address & 7));
        ulong word = Memory.Read64(address & ~7u);

        R[rt] = (R[rt] & ((1UL << shift) - 1)) | (word << shift);
    }

    /// <summary>LDR: versão de 64 bits do LWR.</summary>
    public void Ldr(int rt, uint address)
    {
        int bytes = (int)(address & 7);
        ulong word = Memory.Read64(address & ~7u);

        R[rt] = (R[rt] & ~(ulong.MaxValue >> (8 * bytes))) | (word >> (8 * bytes));
    }

    /// <summary>SDL: versão de 64 bits do SWL.</summary>
    public void Sdl(int rt, uint address)
    {
        int shift = 8 * (7 - (int)(address & 7));
        uint aligned = address & ~7u;
        ulong kept = Memory.Read64(aligned) & ~(ulong.MaxValue >> shift);

        Memory.Write64(aligned, (R[rt] >> shift) | kept);
    }

    /// <summary>SDR: versão de 64 bits do SWR.</summary>
    public void Sdr(int rt, uint address)
    {
        int bytes = (int)(address & 7);
        uint aligned = address & ~7u;
        ulong kept = Memory.Read64(aligned) & ((1UL << (8 * bytes)) - 1);

        Memory.Write64(aligned, (R[rt] << (8 * bytes)) | kept);
    }
}
