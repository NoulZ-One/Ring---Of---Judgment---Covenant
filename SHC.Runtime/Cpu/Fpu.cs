namespace SHC.Runtime.Cpu;

/// <summary>
/// Aritmética do FPU do Emotion Engine (COP1, precisão simples).
///
/// O FPU do PS2 não segue o IEEE 754 por completo:
/// - Não há infinito nem NaN: expoente 255 é um número normal, e o maior
///   valor representável é 0x7FFFFFFF (positivo) ou 0xFFFFFFFF (negativo).
/// - Resultados que estouram o limite são "travados" nesse máximo.
/// - Valores denormais valem zero.
/// - O arredondamento é para zero (truncamento).
///
/// Os valores ficam como bits (uint) e as contas são feitas em double,
/// que representa exatamente o produto de dois floats; o resultado é
/// truncado para 23 bits de mantissa.
/// </summary>
public static class Fpu
{
    private const uint SignBit = 0x80000000u;
    private const uint MaxMagnitude = 0x7FFFFFFFu;

    /// <summary>Converte um float do PS2 (bits) em double.</summary>
    public static double ToDouble(uint bits)
    {
        int exponent = (int)((bits >> 23) & 0xFF);

        // Expoente zero: zero ou denormal, ambos tratados como zero.
        if (exponent == 0)
        {
            return (bits & SignBit) != 0 ? -0.0 : 0.0;
        }

        double value = Math.ScaleB(
            1.0 + (bits & 0x7FFFFFu) / 8388608.0,
            exponent - 127);

        return (bits & SignBit) != 0 ? -value : value;
    }

    /// <summary>
    /// Converte um double em float do PS2: trunca a mantissa, zera os
    /// resultados muito pequenos e trava os muito grandes no máximo.
    /// </summary>
    public static uint FromDouble(double value)
    {
        long raw = BitConverter.DoubleToInt64Bits(value);

        uint sign = raw < 0 ? SignBit : 0u;
        int exponent = (int)((raw >> 52) & 0x7FF);

        if (exponent == 0x7FF)
        {
            return sign | MaxMagnitude;
        }

        if (exponent == 0)
        {
            return sign;
        }

        int ps2Exponent = exponent - 1023 + 127;

        if (ps2Exponent <= 0)
        {
            return sign;
        }

        if (ps2Exponent > 255)
        {
            return sign | MaxMagnitude;
        }

        return sign | ((uint)ps2Exponent << 23) | (uint)((raw >> 29) & 0x7FFFFF);
    }

    public static uint Add(uint a, uint b) => FromDouble(ToDouble(a) + ToDouble(b));

    public static uint Sub(uint a, uint b) => FromDouble(ToDouble(a) - ToDouble(b));

    public static uint Mul(uint a, uint b) => FromDouble(ToDouble(a) * ToDouble(b));

    /// <summary>Divisão. Dividir por zero dá o valor máximo, com o sinal do quociente.</summary>
    public static uint Div(uint a, uint b)
    {
        double divisor = ToDouble(b);

        if (divisor == 0.0)
        {
            return ((a ^ b) & SignBit) | MaxMagnitude;
        }

        return FromDouble(ToDouble(a) / divisor);
    }

    /// <summary>Raiz quadrada do valor absoluto (o FPU não gera NaN para negativos).</summary>
    public static uint Sqrt(uint a) =>
        FromDouble(Math.Sqrt(Math.Abs(ToDouble(a))));

    /// <summary>a / raiz quadrada do valor absoluto de b.</summary>
    public static uint Rsqrt(uint a, uint b)
    {
        double root = Math.Sqrt(Math.Abs(ToDouble(b)));

        if (root == 0.0)
        {
            return (a & SignBit) | MaxMagnitude;
        }

        return FromDouble(ToDouble(a) / root);
    }

    public static uint Abs(uint a) => a & ~SignBit;

    public static uint Neg(uint a) => a ^ SignBit;

    public static uint Max(uint a, uint b) => ToDouble(a) >= ToDouble(b) ? a : b;

    public static uint Min(uint a, uint b) => ToDouble(a) <= ToDouble(b) ? a : b;

    /// <summary>acumulador + a * b, com um único arredondamento.</summary>
    public static uint Madd(uint accumulator, uint a, uint b) =>
        FromDouble(ToDouble(accumulator) + ToDouble(a) * ToDouble(b));

    /// <summary>acumulador - a * b, com um único arredondamento.</summary>
    public static uint Msub(uint accumulator, uint a, uint b) =>
        FromDouble(ToDouble(accumulator) - ToDouble(a) * ToDouble(b));

    /// <summary>CVT.S.W: inteiro de 32 bits para float.</summary>
    public static uint FromInt(int value) => FromDouble(value);

    /// <summary>CVT.W.S: float para inteiro de 32 bits, truncando e travando nos limites.</summary>
    public static uint ToInt(uint bits)
    {
        double value = ToDouble(bits);

        if (value >= 2147483648.0)
        {
            return 0x7FFFFFFFu;
        }

        if (value <= -2147483648.0)
        {
            return 0x80000000u;
        }

        return (uint)(int)value;
    }

    public static bool Eq(uint a, uint b) => ToDouble(a) == ToDouble(b);

    public static bool Lt(uint a, uint b) => ToDouble(a) < ToDouble(b);

    public static bool Le(uint a, uint b) => ToDouble(a) <= ToDouble(b);
}
