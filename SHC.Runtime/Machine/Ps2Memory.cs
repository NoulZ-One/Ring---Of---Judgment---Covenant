using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SHC.Runtime.Machine;

/// <summary>
/// RAM principal de 32 MB do Emotion Engine.
/// Por enquanto trata apenas a RAM: scratchpad (0x70000000) e registradores
/// de hardware (0x10000000) serão adicionados depois.
/// Os acessos usam little-endian e não exigem alinhamento.
/// </summary>
public sealed class Ps2Memory
{
    /// <summary>Tamanho da RAM, em bytes.</summary>
    public const int Size = 32 * 1024 * 1024;

    // Os três bits altos escolhem o segmento: 0 = KUSEG, 4 = KSEG0 (cache),
    // 5 = KSEG1 (sem cache). Os três espelham a RAM física.
    private const uint PhysicalMask = 0x1FFFFFFF;

    private readonly byte[] _ram = new byte[Size];

    /// <summary>
    /// Converte um endereço virtual em posição na RAM e valida que
    /// o intervalo de <paramref name="length"/> bytes cabe nela.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Translate(uint address, int length)
    {
        uint segment = address >> 29;
        uint physical = address & PhysicalMask;

        if ((segment != 0 && segment != 4 && segment != 5) ||
            physical > (uint)(Size - length))
        {
            ThrowInvalidAddress(address, length);
        }

        return (int)physical;
    }

    [DoesNotReturn]
    private static void ThrowInvalidAddress(uint address, int length)
    {
        throw new ArgumentOutOfRangeException(
            nameof(address),
            $"Acesso de {length} byte(s) em 0x{address:X8} fora da RAM do EE.");
    }

    public byte Read8(uint address) => _ram[Translate(address, 1)];

    public ushort Read16(uint address) =>
        BinaryPrimitives.ReadUInt16LittleEndian(
            _ram.AsSpan(Translate(address, 2), 2));

    public uint Read32(uint address) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            _ram.AsSpan(Translate(address, 4), 4));

    public ulong Read64(uint address) =>
        BinaryPrimitives.ReadUInt64LittleEndian(
            _ram.AsSpan(Translate(address, 8), 8));

    public UInt128 Read128(uint address) =>
        BinaryPrimitives.ReadUInt128LittleEndian(
            _ram.AsSpan(Translate(address, 16), 16));

    public void Write8(uint address, byte value) =>
        _ram[Translate(address, 1)] = value;

    public void Write16(uint address, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(
            _ram.AsSpan(Translate(address, 2), 2), value);

    public void Write32(uint address, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(
            _ram.AsSpan(Translate(address, 4), 4), value);

    public void Write64(uint address, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(
            _ram.AsSpan(Translate(address, 8), 8), value);

    public void Write128(uint address, UInt128 value) =>
        BinaryPrimitives.WriteUInt128LittleEndian(
            _ram.AsSpan(Translate(address, 16), 16), value);

    /// <summary>
    /// Devolve uma janela sobre a RAM, útil para copiar blocos grandes.
    /// A janela vale enquanto esta instância existir.
    /// </summary>
    public Span<byte> GetSpan(uint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        return _ram.AsSpan(Translate(address, length), length);
    }

    /// <summary>Copia bytes para a RAM a partir de um endereço.</summary>
    public void Write(uint address, ReadOnlySpan<byte> data) =>
        data.CopyTo(GetSpan(address, data.Length));

    /// <summary>Preenche um intervalo da RAM com um valor.</summary>
    public void Fill(uint address, int length, byte value) =>
        GetSpan(address, length).Fill(value);

    /// <summary>Zera toda a RAM.</summary>
    public void Clear() => Array.Clear(_ram);
}
