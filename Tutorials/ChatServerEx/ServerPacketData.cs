using System;

using MemoryPack;

using DB;
using CSBaseLib;


namespace ChatServer;

public class RawPacketData
{
    public short Size;
    public short PacketID;
    public sbyte Type;
    public byte[] Body;
}

public class ServerPacketData
{
    public Int16 PacketSize;
    public string SessionID; 
    public int SessionIndex;
    public Int16 PacketID;        
    public SByte Type;
    public byte[] BodyData;
            
    
    public void Assign(string sessionID, int sessionIndex, Int16 packetID, byte[] packetBodyData)
    {
        SessionIndex = sessionIndex;
        SessionID = sessionID;

        PacketID = packetID;
        
        if (packetBodyData.Length > 0)
        {
            BodyData = packetBodyData;
        }
    }

    public void Assign(DBResultQueue DBResult)
    {
        SessionIndex = DBResult.SessionIndex;
        SessionID = DBResult.SessionID;

        PacketID = (short)DBResult.PacketID;
        BodyData = DBResult.Datas;
    }

    public static ServerPacketData MakeNTFInConnectOrDisConnectClientPacket(bool isConnect, string sessionID, int sessionIndex)
    {
        var packet = new ServerPacketData();
        
        if (isConnect)
        {
            packet.PacketID = (Int32)PacketId.NtfInConnectClient;
        }
        else
        {
            packet.PacketID = (Int32)PacketId.NtfInDisconnectClient;
        }

        packet.SessionIndex = sessionIndex;
        packet.SessionID = sessionID;
        return packet;
    }               
    
}



[MemoryPackable]
public partial class PKTInternalReqRoomEnter
{
    public int RoomNumber;

    public string UserID;        
}

[MemoryPackable]
public partial class PKTInternalResRoomEnter
{
    public ErrorCode Result;

    public int RoomNumber;

    public string UserID;
}


[MemoryPackable]
public partial class PKTInternalNtfRoomLeave
{
    public int RoomNumber;

    public string UserID;
}
