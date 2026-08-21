using System.Buffers.Binary;

namespace SuperSocketLite.SmokeClient;

/// <summary>옵션에 맞춰 패킷을 만들고 읽는다.</summary>
internal sealed class PacketCodec(Options options)
{
    private readonly Options _options = options;

    /// <summary>헤더 + 본문을 한 배열에 담아 돌려준다.</summary>
    public byte[] Encode(short packetId, ReadOnlySpan<byte> body)
    {
        var headerSize = _options.HeaderSize;
        var packet = new byte[headerSize + body.Length];

        var lengthValue = _options.LengthIncludesHeader
            ? headerSize + body.Length
            : body.Length;

        WriteInteger(packet.AsSpan(0, _options.LengthBytes), lengthValue);

        if (_options.IdBytes == 2)
        {
            WriteInteger(packet.AsSpan(_options.LengthBytes, 2), packetId);
        }

        body.CopyTo(packet.AsSpan(headerSize));

        return packet;
    }

    /// <summary>헤더에서 읽은 길이 값으로 "헤더 뒤에 더 읽어야 할 바이트 수"를 계산한다.</summary>
    public int GetRemainingBodyLength(ReadOnlySpan<byte> header)
    {
        var lengthValue = ReadInteger(header[.._options.LengthBytes]);

        return _options.LengthIncludesHeader
            ? lengthValue - _options.HeaderSize
            : lengthValue;
    }

    public short ReadPacketId(ReadOnlySpan<byte> header)
    {
        if (_options.IdBytes == 0)
        {
            return 0;
        }

        return (short)ReadInteger(header.Slice(_options.LengthBytes, 2));
    }

    private void WriteInteger(Span<byte> destination, int value)
    {
        if (destination.Length == 2)
        {
            if (_options.BigEndian)
            {
                BinaryPrimitives.WriteInt16BigEndian(destination, (short)value);
            }
            else
            {
                BinaryPrimitives.WriteInt16LittleEndian(destination, (short)value);
            }

            return;
        }

        if (_options.BigEndian)
        {
            BinaryPrimitives.WriteInt32BigEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        }
    }

    private int ReadInteger(ReadOnlySpan<byte> source)
    {
        if (source.Length == 2)
        {
            return _options.BigEndian
                ? BinaryPrimitives.ReadInt16BigEndian(source)
                : BinaryPrimitives.ReadInt16LittleEndian(source);
        }

        return _options.BigEndian
            ? BinaryPrimitives.ReadInt32BigEndian(source)
            : BinaryPrimitives.ReadInt32LittleEndian(source);
    }
}
