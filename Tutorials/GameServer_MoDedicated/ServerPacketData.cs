using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CSBaseLib;
using MemoryPack;


namespace GameServer;

public class ServerPacketData
{
    public UInt16 PacketSize;
    public string SessionID; 
    public int SessionIndex;
    public UInt16 PacketID;        
    public SByte Type;
    public byte[] BodyData;
            
    
    public void Assign(string sessionID, int sessionIndex, UInt16 packetID, byte[] packetBodyData)
    {
        SessionIndex = sessionIndex;
        SessionID = sessionID;

        PacketID = packetID;
        
        if (packetBodyData.Length > 0)
        {
            BodyData = packetBodyData;
        }
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
