using System.Runtime.CompilerServices;

namespace SHC.Runtime.Cpu;

/// <summary>
/// Operações pequenas usadas pelo código recompilado.
/// O código gerado as importa com "using static".
/// </summary>
public static class Ops
{
    /// <summary>Estende com sinal um valor de 32 bits para 64 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Sx(uint value) => (ulong)(long)(int)value;

    /// <summary>Estende com sinal um byte para 64 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Sx8(byte value) => (ulong)(long)(sbyte)value;

    /// <summary>Estende com sinal um valor de 16 bits para 64 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Sx16(ushort value) => (ulong)(long)(short)value;
}
