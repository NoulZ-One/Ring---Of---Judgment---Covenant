using SHC.Generated.Main;
using SHC.Runtime.Cpu;
using SHC.Runtime.Loading;
using SHC.Runtime.Machine;

/// <summary>
/// Testes de execução de funções puras do executável recompilado.
/// Cada resultado esperado foi escrito a partir da leitura do assembly,
/// sem usar o código gerado.
/// </summary>
internal sealed class SmokeTests
{
    // Endereços de trabalho na RAM, em áreas livres (BSS e heap).
    private const uint Object1 = 0x00500000;
    private const uint Object2 = 0x00510000;
    private const uint Object3 = 0x00520000;
    private const uint StackTop = 0x01F00000;

    private readonly Ps2Memory _memory;
    private readonly R5900Context _ctx;

    private int _passed;
    private int _failed;

    public SmokeTests(string elfPath)
    {
        _memory = new Ps2Memory();

        ElfImageLoader.Load(Path.GetFullPath(elfPath), _memory);

        _ctx = new R5900Context(_memory);
        MainUnit.Register(_ctx);
    }

    public int Run()
    {
        Group("Cópia byte a byte (F_00213978)", CopyBytes);
        Group("Consulta em tabela de bytes (F_003F5470)", TableLookup);
        Group("Troca dos 2 bits baixos de um byte (F_00225A70)", SetLowBits);
        Group("Tamanho por campos do objeto (F_003FD920)", ObjectSize);
        Group("Gravação condicional (F_00245A80)", ConditionalStore);
        Group("Flag global com $gp (F_00331340)", GlobalFlag);
        Group("Endereço de elemento com switch (F_00297D10)", ElementAddress);

        Console.WriteLine();
        Console.WriteLine($"{_passed} verificações passaram, {_failed} falharam.");

        return _failed == 0 ? 0 : 1;
    }

    // ----- Infraestrutura -----

    private void Group(string name, Action test)
    {
        Console.WriteLine(name);

        int passedBefore = _passed;
        int failedBefore = _failed;

        try
        {
            test();
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"    EXCEÇÃO: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine(
            _failed == failedBefore
                ? $"  OK ({_passed - passedBefore} verificações)"
                : "  COM FALHAS");
    }

    private void Expect(string what, ulong expected, ulong actual)
    {
        if (expected == actual)
        {
            _passed++;
            return;
        }

        _failed++;
        Console.WriteLine($"    FALHA: {what}: esperado 0x{expected:X}, obtido 0x{actual:X}");
    }

    // Zera os registradores e deixa um pilha válida.
    private void ResetRegisters()
    {
        Array.Clear(_ctx.R);
        Array.Clear(_ctx.RHigh);

        _ctx.R[29] = StackTop;
    }

    // ----- Testes -----

    // F_00213978: copia $a2 bytes de $a1 para $a0 e devolve 0.
    private void CopyBytes()
    {
        const uint source = Object2;
        const uint destination = Object1;

        foreach (int count in new[] { 0, 1, 5, 100 })
        {
            ResetRegisters();

            for (int i = 0; i < 128; i++)
            {
                _memory.Write8(source + (uint)i, (byte)(i * 7 + 3));
                _memory.Write8(destination + (uint)i, 0xEE);
            }

            _ctx.R[4] = destination;
            _ctx.R[5] = source;
            _ctx.R[6] = (ulong)count;

            MainUnit.F_00213978(_ctx);

            for (int i = 0; i < 128; i++)
            {
                byte expected = i < count ? (byte)(i * 7 + 3) : (byte)0xEE;

                Expect($"count={count}, byte {i}", expected, _memory.Read8(destination + (uint)i));
            }

            Expect($"count={count}, retorno", 0, _ctx.R[2]);
            Expect($"count={count}, $a0 avançou", destination + (uint)count, _ctx.R[4]);
            Expect($"count={count}, $a1 avançou", source + (uint)count, _ctx.R[5]);
        }
    }

    // F_003F5470: se $a0 (com sinal) < 12, devolve tabela[$a0]; senão devolve $a0.
    private void TableLookup()
    {
        const uint table = 0x00459EF0;

        for (int i = 0; i < 12; i++)
        {
            _memory.Write8(table + (uint)i, (byte)(10 * (i + 1)));
        }

        // Byte logo antes da tabela: lido quando o índice é -1.
        _memory.Write8(table - 1, 0xAB);

        for (int index = 0; index < 12; index++)
        {
            ResetRegisters();
            _ctx.R[4] = (ulong)index;

            MainUnit.F_003F5470(_ctx);

            Expect($"índice {index}", (ulong)(10 * (index + 1)), _ctx.R[2]);
        }

        foreach (long value in new long[] { 12, 13, 100, 0x7FFFFFFF })
        {
            ResetRegisters();
            _ctx.R[4] = (ulong)value;

            MainUnit.F_003F5470(_ctx);

            Expect($"valor {value} fora da tabela", (ulong)value, _ctx.R[2]);
        }

        // Comparação com sinal: -1 é menor que 12 e lê o byte antes da tabela.
        ResetRegisters();
        _ctx.R[4] = ulong.MaxValue;

        MainUnit.F_003F5470(_ctx);

        Expect("índice -1", 0xAB, _ctx.R[2]);
    }

    // F_00225A70: se 0 <= $a1 < 4, byte[$a0+14] = (byte & ~3) | $a1; senão não faz nada.
    private void SetLowBits()
    {
        foreach (long value in new long[] { -100, -1, 0, 1, 2, 3, 4, 100 })
        {
            ResetRegisters();

            _memory.Write8(Object1 + 14, 0xB7);

            _ctx.R[4] = Object1;
            _ctx.R[5] = (ulong)value;

            MainUnit.F_00225A70(_ctx);

            byte expected = value >= 0 && value < 4
                ? (byte)((0xB7 & ~3) | (int)value)
                : (byte)0xB7;

            Expect($"valor {value}", expected, _memory.Read8(Object1 + 14));
        }
    }

    // F_003FD920: devolve 8, ou 16 se byte[+4] < 9 e halfword[+6] != 0.
    private void ObjectSize()
    {
        foreach (byte kind in new byte[] { 0, 8, 9, 200 })
        {
            foreach (ushort flags in new ushort[] { 0, 1, 0xFFFF })
            {
                ResetRegisters();

                _memory.Write8(Object1 + 4, kind);
                _memory.Write16(Object1 + 6, flags);

                _ctx.R[4] = Object1;

                MainUnit.F_003FD920(_ctx);

                ulong expected = 8UL + (kind < 9 && flags != 0 ? 8UL : 0UL);

                Expect($"tipo={kind}, flags=0x{flags:X}", expected, _ctx.R[2]);
            }
        }
    }

    // F_00245A80: se byte[*(a0)+17] >= $a1 (sem sinal), grava $a1 em [a0+8].
    private void ConditionalStore()
    {
        var cases = new (byte Limit, ulong Value)[]
        {
            (5, 3), (3, 5), (5, 5), (0, 0), (255, 256), (10, ulong.MaxValue)
        };

        foreach ((byte limit, ulong value) in cases)
        {
            ResetRegisters();

            _memory.Write32(Object1, Object2);
            _memory.Write8(Object2 + 17, limit);
            _memory.Write32(Object1 + 8, 0xDEADBEEF);

            _ctx.R[4] = Object1;
            _ctx.R[5] = value;

            MainUnit.F_00245A80(_ctx);

            uint expected = limit >= value ? (uint)value : 0xDEADBEEF;

            Expect($"limite={limit}, valor=0x{value:X}", expected, _memory.Read32(Object1 + 8));
        }
    }

    // F_00331340: lê um byte em $gp-31912; devolve 0 se (byte & 3) == 1, senão 1.
    private void GlobalFlag()
    {
        const uint gp = 0x00440000;

        foreach (byte value in new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 0xFD })
        {
            ResetRegisters();

            _ctx.R[28] = gp;
            _memory.Write8(gp - 31912, value);

            MainUnit.F_00331340(_ctx);

            ulong expected = (value & 3) == 1 ? 0UL : 1UL;

            Expect($"byte=0x{value:X}", expected, _ctx.R[2]);
        }
    }

    // F_00297D10: base = *(*(*(gp-31940)+28)+68); devolve base + $a1 * tamanho($a0)
    // para $a0 < 6, ou 0 nos demais. O tamanho vem da tabela de saltos em 0x4338F0:
    // destino 0x297D58 = x1, 0x297D68 = x2, 0x297D80 = x4.
    private void ElementAddress()
    {
        const uint gp = 0x00440000;
        const uint tableAddress = 0x004338F0;
        const uint arrayBase = Object3;

        _memory.Write32(gp - 31940, Object1);
        _memory.Write32(Object1 + 28, Object2);
        _memory.Write32(Object2 + 68, arrayBase);

        const ulong s0Low = 0x1122334455667788UL;
        const ulong s0High = 0x99AABBCCDDEEFF00UL;

        for (uint selector = 0; selector < 6; selector++)
        {
            uint target = _memory.Read32(tableAddress + 4 * selector);

            uint size = target switch
            {
                0x00297D58 => 1,
                0x00297D68 => 2,
                0x00297D80 => 4,
                _ => throw new InvalidOperationException(
                    $"Destino inesperado na tabela: 0x{target:X8}.")
            };

            foreach (uint index in new uint[] { 0, 1, 10, 1000 })
            {
                ResetRegisters();

                _ctx.R[28] = gp;
                _ctx.R[16] = s0Low;
                _ctx.RHigh[16] = s0High;
                _ctx.R[4] = selector;
                _ctx.R[5] = index;

                MainUnit.F_00297D10(_ctx);

                uint address = arrayBase + index * size;
                ulong expected = unchecked((ulong)(long)(int)address);

                Expect($"seletor={selector}, índice={index}", expected, _ctx.R[2]);
                Expect($"seletor={selector}: $sp restaurado", StackTop, _ctx.R[29]);
                Expect($"seletor={selector}: $s0 (64 bits baixos) preservado", s0Low, _ctx.R[16]);
                Expect($"seletor={selector}: $s0 (64 bits altos) preservado", s0High, _ctx.RHigh[16]);
            }
        }

        // Seletores fora da faixa devolvem 0.
        foreach (uint selector in new uint[] { 6, 7, 100 })
        {
            ResetRegisters();

            _ctx.R[28] = gp;
            _ctx.R[4] = selector;
            _ctx.R[5] = 10;

            MainUnit.F_00297D10(_ctx);

            Expect($"seletor={selector} fora da faixa", 0, _ctx.R[2]);
        }
    }
}
