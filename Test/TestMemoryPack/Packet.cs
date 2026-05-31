using MemoryPack;

using System.Buffers;
using System.Buffers.Binary;

namespace TestMemoryPack;


// MemoryPackt로 패킷의 헤더와 바디를 같이 인코딩/디코딩 할 때 사용한다.
public struct MemoryPackPacketHeadInfo
{
    const int PacketHeaderMemoryPackStartPos = 1;
    public const int HeadSize = 6;

    public UInt16 TotalSize;
    public UInt16 Id;
    public byte Type;

    public static UInt16 GetTotalSize(byte[] data, int startPos)
    {
        return FastBinaryRead.UInt16(data, startPos + PacketHeaderMemoryPackStartPos);
    }

    public static void WritePacketId(byte[] data, UInt16 packetId)
    {
        FastBinaryWrite.UInt16(data, PacketHeaderMemoryPackStartPos + 2, packetId);
    }

    public void Read(byte[] headerData)
    {
        var pos = PacketHeaderMemoryPackStartPos;

        TotalSize = FastBinaryRead.UInt16(headerData, pos);
        pos += 2;

        Id = FastBinaryRead.UInt16(headerData, pos);
        pos += 2;

        Type = headerData[pos];
        pos += 1;
    }

    public void Write(byte[] mqData)
    {
        var pos = PacketHeaderMemoryPackStartPos;

        FastBinaryWrite.UInt16(mqData, pos, TotalSize);
        pos += 2;

        FastBinaryWrite.UInt16(mqData, pos, Id);
        pos += 2;

        mqData[pos] = Type;
        pos += 1;
    }

    
    public void DebugConsolOutHeaderInfo()
    {
        Console.WriteLine("DebugConsolOutHeaderInfo");
        Console.WriteLine("TotalSize : " + TotalSize);
        Console.WriteLine("Id : " + Id);
        Console.WriteLine("Type : " + Type);
    }   
}


// MemoryPack으로 패킷의 헤더는 고정된 크기로 인코딩/디코딩 하고, 바디는 MemoryPack으로 인코딩/디코딩 할 때 사용한다.
public struct MemoryPackBodyPacketHeadInfo
{
    public const int HeadSize = 5;

    public UInt16 TotalSize;
    public UInt16 Id;
    public byte Type;

    public static MemoryPackBodyPacketHeadInfo Read(ArraySegment<byte> packetData)
    {
        if (packetData.Count < HeadSize)
        {
            throw new InvalidDataException("Packet data is smaller than header size.");
        }

        var span = packetData.AsSpan();
        return new MemoryPackBodyPacketHeadInfo
        {
            TotalSize = BinaryPrimitives.ReadUInt16LittleEndian(span),
            Id = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2)),
            Type = span[4],
        };
    }

    public void Write(Span<byte> packetData)
    {
        if (packetData.Length < HeadSize)
        {
            throw new InvalidDataException("Packet data is smaller than header size.");
        }

        BinaryPrimitives.WriteUInt16LittleEndian(packetData, TotalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(packetData.Slice(2), Id);
        packetData[4] = Type;
    }

    public void DebugConsolOutHeaderInfo()
    {
        Console.WriteLine("DebugConsolOutHeaderInfo");
        Console.WriteLine("TotalSize : " + TotalSize);
        Console.WriteLine("Id : " + Id);
        Console.WriteLine("Type : " + Type);
    }
}


public class MemoryPackBodyPacketToBytes
{
    public static ArraySegment<byte> Make<T>(UInt16 packetID, T bodyData, byte type = 0)
    {
        var writer = new PacketBufferWriter(256);

        var headerSpan = writer.GetSpan(MemoryPackBodyPacketHeadInfo.HeadSize);
        headerSpan.Slice(0, MemoryPackBodyPacketHeadInfo.HeadSize).Clear();
        writer.Advance(MemoryPackBodyPacketHeadInfo.HeadSize);

        MemoryPackSerializer.Serialize(writer, bodyData);

        if (writer.WrittenCount > UInt16.MaxValue)
        {
            throw new InvalidDataException($"Packet size is too large: {writer.WrittenCount}");
        }

        var header = new MemoryPackBodyPacketHeadInfo
        {
            TotalSize = (UInt16)writer.WrittenCount,
            Id = packetID,
            Type = type,
        };
        header.Write(writer.WrittenSpan.Slice(0, MemoryPackBodyPacketHeadInfo.HeadSize));

        return writer.WrittenSegment;
    }

    public static T? DeserializeBody<T>(ArraySegment<byte> packetData)
    {
        var header = MemoryPackBodyPacketHeadInfo.Read(packetData);
        if (header.TotalSize != packetData.Count)
        {
            throw new InvalidDataException($"Packet size mismatch. Header: {header.TotalSize}, Data: {packetData.Count}");
        }

        var bodySpan = packetData.AsSpan().Slice(MemoryPackBodyPacketHeadInfo.HeadSize);
        return MemoryPackSerializer.Deserialize<T>(bodySpan);
    }
}


public class PacketBufferWriter : IBufferWriter<byte>
{
    byte[] _buffer;

    public PacketBufferWriter(int initialCapacity)
    {
        if (initialCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        _buffer = new byte[initialCapacity];
    }

    public int WrittenCount { get; private set; }

    public Span<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    public ArraySegment<byte> WrittenSegment => new(_buffer, 0, WrittenCount);

    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (WrittenCount + count > _buffer.Length)
        {
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");
        }

        WrittenCount += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(WrittenCount);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(WrittenCount);
    }

    void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        var available = _buffer.Length - WrittenCount;
        if (available >= sizeHint)
        {
            return;
        }

        var growBy = Math.Max(sizeHint, _buffer.Length);
        Array.Resize(ref _buffer, checked(_buffer.Length + growBy));
    }
}


[MemoryPackable]
public partial class PkHeader
{
    public UInt16 TotalSize { get; set; } = 0;
    public UInt16 Id { get; set; } = 0;
    public byte Type { get; set; } = 0;
}

// 로그인 요청
[MemoryPackable]
public partial class PKTReqLogin : PkHeader
{
    public string UserID { get; set; } = default!;
    public string AuthToken { get; set; } = default!;
}

[MemoryPackable]
public partial class PKTResRoomEnter : PkHeader
{
    public Int16 ErrorCode { get; set; }
    public int RoomNumber { get; set; }
}


[MemoryPackable]
public partial class PKTReqLoginBody
{
    public string UserID { get; set; } = default!;
    public string AuthToken { get; set; } = default!;
}
