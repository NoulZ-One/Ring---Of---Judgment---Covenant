using System.Buffers.Binary;
using System.Text;

namespace SHC.Runtime.Overlays;

/// <summary>
/// Lê o cabeçalho de 0x80 bytes dos overlays FIELD.BIN, BATTLE.BIN
/// e OPENING.BIN.
/// </summary>
public static class OverlayHeaderReader
{
    /// <summary>Tamanho do cabeçalho, em bytes.</summary>
    public const int HeaderSize = 0x80;

    // Bytes 'M', 'W', 'o', '3' lidos como inteiro little-endian.
    private const uint Magic = 0x336F574D;

    // O nome começa no offset 0x20 e ocupa no máximo 32 bytes.
    private const int NameOffset = 0x20;
    private const int NameMaxLength = 32;

    /// <summary>Lê o cabeçalho a partir de um arquivo.</summary>
    public static OverlayHeaderInfo Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "O arquivo de overlay não foi encontrado.",
                filePath);
        }

        using var stream = File.OpenRead(filePath);

        if (stream.Length < HeaderSize)
        {
            throw new InvalidDataException(
                "O arquivo é pequeno demais para conter um cabeçalho de overlay.");
        }

        byte[] header = new byte[HeaderSize];
        stream.ReadExactly(header);

        return Parse(header);
    }

    /// <summary>Interpreta o cabeçalho a partir de bytes já carregados.</summary>
    public static OverlayHeaderInfo Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException(
                "Dados insuficientes para um cabeçalho de overlay.");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data);

        if (magic != Magic)
        {
            throw new InvalidDataException(
                "A assinatura 'MWo3' do overlay não foi encontrada.");
        }

        var id = (OverlayId)BinaryPrimitives.ReadUInt32LittleEndian(
            data.Slice(0x04, 4));

        if (!Enum.IsDefined(id))
        {
            throw new InvalidDataException(
                $"Identificador de overlay desconhecido: {(uint)id}.");
        }

        ReadOnlySpan<byte> nameBytes = data.Slice(NameOffset, NameMaxLength);

        int nameLength = nameBytes.IndexOf((byte)0);

        if (nameLength < 0)
        {
            nameLength = nameBytes.Length;
        }

        return new OverlayHeaderInfo(
            id,
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x08, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x0C, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x10, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x14, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x18, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x1C, 4)),
            Encoding.ASCII.GetString(nameBytes[..nameLength]));
    }
}
