using System.Buffers.Binary;
using SHC.Recompiler.Decoding;

namespace SHC.Recompiler.Analysis;

/// <summary>
/// Resolve JR indiretos que são tabelas de switch, lendo para trás
/// as instruções que calculam o endereço do destino.
///
/// Padrão reconhecido (os registradores variam):
///   sltiu $t, $idx, N        ; limite do índice
///   beq   $t, $zero, padrão
///   sll   $x, $idx, 2        ; índice x 4
///   lui   $b, %hi(tabela)
///   addiu $b, $b, %lo(tabela)  (opcional)
///   addu  $b, $x, $b
///   lw    $d, deslocamento($b)
///   jr    $d
///
/// Uma tabela só é aceita se todas as entradas apontarem para código.
/// </summary>
public sealed class JumpTableResolver
{
    // Quantas instruções para trás a busca olha.
    private const int Window = 40;

    // Limite de entradas aceito para uma tabela.
    private const int MaxEntries = 1024;

    private readonly RecompilationUnit _unit;
    private readonly CodeRange _range;

    public JumpTableResolver(RecompilationUnit unit, CodeRange range)
    {
        _unit = unit;
        _range = range;
    }

    /// <summary>
    /// Tenta resolver o JR em <paramref name="jumpAddress"/>.
    /// Devolve nulo se o padrão não for reconhecido ou a tabela não for válida.
    /// </summary>
    public JumpTableInfo? TryResolve(uint jumpAddress)
    {
        Instruction jump = Fetch(jumpAddress);

        // O registrador do JR deve vir de um LW.
        uint? loadAddress = FindDefinition(jump.Rs, jumpAddress);

        if (loadAddress is null)
        {
            return null;
        }

        Instruction load = Fetch(loadAddress.Value);

        if (load.Opcode != Opcode.Lw)
        {
            return null;
        }

        // A base do LW deve ser uma soma: tabela + índice x 4.
        uint? addAddress = FindDefinition(load.Rs, loadAddress.Value);

        if (addAddress is null)
        {
            return null;
        }

        Instruction add = Fetch(addAddress.Value);

        if (add.Opcode is not (Opcode.Addu or Opcode.Add or Opcode.Daddu))
        {
            return null;
        }

        // Um dos operandos é o índice deslocado (SLL 2); o outro, a constante da tabela.
        foreach ((int scaled, int constant) in new[]
        {
            (add.Rs, add.Rt),
            (add.Rt, add.Rs)
        })
        {
            uint? shiftAddress = FindDefinition(scaled, addAddress.Value);

            if (shiftAddress is null)
            {
                continue;
            }

            Instruction shift = Fetch(shiftAddress.Value);

            if (shift.Opcode != Opcode.Sll || shift.Sa != 2)
            {
                continue;
            }

            int index = shift.Rt;

            uint? baseAddress = ConstantValue(constant, addAddress.Value, 0);

            if (baseAddress is null)
            {
                continue;
            }

            uint table = baseAddress.Value + (uint)load.ImmediateSigned;

            int? count = FindBound(index, shiftAddress.Value);

            if (count is null || count.Value == 0 || count.Value > MaxEntries)
            {
                return null;
            }

            return ReadTable(jumpAddress, table, count.Value);
        }

        return null;
    }

    // Lê a tabela e confere que todas as entradas são endereços de código.
    private JumpTableInfo? ReadTable(uint jumpAddress, uint table, int count)
    {
        if (table < _unit.BaseAddress ||
            (table & 3) != 0 ||
            (long)table + 4L * count > _unit.EndAddress)
        {
            return null;
        }

        var targets = new List<uint>(count);

        for (int i = 0; i < count; i++)
        {
            uint target = BinaryPrimitives.ReadUInt32LittleEndian(
                _unit.Data.AsSpan((int)(table - _unit.BaseAddress) + 4 * i, 4));

            if (!_range.Contains(target))
            {
                return null;
            }

            targets.Add(target);
        }

        return new JumpTableInfo(jumpAddress, table, count, targets);
    }

    // Procura, antes do SLL, o SLTIU que limita o índice (valor sem sinal).
    private int? FindBound(int index, uint shiftAddress)
    {
        uint address = shiftAddress - 4;

        for (int i = 0; i < Window; i++)
        {
            if (address < _range.Start)
            {
                break;
            }

            Instruction instruction = Fetch(address);

            if (instruction.Opcode == Opcode.Sltiu && instruction.Rs == index)
            {
                return instruction.ImmediateUnsigned;
            }

            // O índice foi alterado depois do limite: o padrão não vale.
            if (GetDestination(instruction) == index)
            {
                break;
            }

            address -= 4;
        }

        return null;
    }

    // Calcula o valor constante de um registrador (LUI, ADDIU e ORI encadeados).
    private uint? ConstantValue(int register, uint beforeAddress, int depth)
    {
        if (register == 0)
        {
            return 0;
        }

        if (depth > 4)
        {
            return null;
        }

        uint? definition = FindDefinition(register, beforeAddress);

        if (definition is null)
        {
            return null;
        }

        Instruction instruction = Fetch(definition.Value);

        switch (instruction.Opcode)
        {
            case Opcode.Lui:
                return (uint)instruction.ImmediateUnsigned << 16;

            case Opcode.Addiu:
            case Opcode.Addi:
            {
                uint? value = ConstantValue(instruction.Rs, definition.Value, depth + 1);

                return value is null
                    ? null
                    : value.Value + (uint)instruction.ImmediateSigned;
            }

            case Opcode.Ori:
            {
                uint? value = ConstantValue(instruction.Rs, definition.Value, depth + 1);

                return value is null
                    ? null
                    : value.Value | instruction.ImmediateUnsigned;
            }

            default:
                return null;
        }
    }

    // Endereço da última instrução, antes de "beforeAddress", que escreve no registrador.
    // Uma chamada de função no meio apaga o valor dos registradores temporários.
    private uint? FindDefinition(int register, uint beforeAddress)
    {
        uint address = beforeAddress - 4;

        for (int i = 0; i < Window; i++)
        {
            if (address < _range.Start)
            {
                return null;
            }

            Instruction instruction = Fetch(address);

            if (GetDestination(instruction) == register)
            {
                return address;
            }

            if (ControlFlow.Classify(instruction) == ControlFlowKind.Call &&
                IsCallerSaved(register))
            {
                return null;
            }

            address -= 4;
        }

        return null;
    }

    // Registrador geral que a instrução escreve, ou nulo se não escreve nenhum.
    internal static int? GetDestination(Instruction instruction)
    {
        switch (instruction.Format)
        {
            case OperandFormat.RtRsImm:
            case OperandFormat.RtImm:
                return instruction.Rt;

            case OperandFormat.RtMem when IsLoad(instruction.Opcode):
                return instruction.Rt;

            case OperandFormat.RdRsRt:
            case OperandFormat.RdRtSa:
            case OperandFormat.RdRtRs:
            case OperandFormat.RdRsRtOpt:
            case OperandFormat.RdRt:
            case OperandFormat.RdRs:
            case OperandFormat.Rd:
            case OperandFormat.Jalr:
                return instruction.Rd;
        }

        return instruction.Opcode is Opcode.Mfc0 or Opcode.Mfc1 or
                                     Opcode.Cfc1 or Opcode.Cfc2
            ? instruction.Rt
            : null;
    }

    private static bool IsLoad(Opcode opcode) =>
        opcode is Opcode.Lb or Opcode.Lh or Opcode.Lw or Opcode.Lbu or
                  Opcode.Lhu or Opcode.Lwu or Opcode.Ld or Opcode.Ldl or
                  Opcode.Ldr or Opcode.Lwl or Opcode.Lwr or Opcode.Lq;

    // v0-v1, a0-a3, t0-t7, t8-t9 e ra são destruídos por uma chamada.
    private static bool IsCallerSaved(int register) =>
        register is (>= 2 and <= 15) or 24 or 25 or 31;

    private Instruction Fetch(uint address) =>
        R5900Decoder.Decode(
            address,
            BinaryPrimitives.ReadUInt32LittleEndian(
                _unit.Data.AsSpan((int)(address - _unit.BaseAddress), 4)));
}
