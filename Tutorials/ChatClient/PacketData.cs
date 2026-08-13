using MemoryPack; //https://github.com/neuecc/MemoryPack
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSBaseLib
{
    public class PacketDef
    {
        public const Int16 PACKET_HEADER_SIZE = 5;
        public const int MAX_USER_ID_BYTE_LENGTH = 16;
        public const int MAX_USER_PW_BYTE_LENGTH = 16;

        public const int INVALID_ROOM_NUMBER = -1;
    }

    public class PacketToBytes
    {
        public static byte[] Make(PACKETID packetID, byte[] bodyData)
        {
            byte type = 0;
            var pktID = (UInt16)packetID;
            UInt16 bodyDataSize = 0;
            if (bodyData != null)
            {
                bodyDataSize = (UInt16)bodyData.Length;
            }
            var packetSize = (UInt16)(bodyDataSize + PacketDef.PACKET_HEADER_SIZE);
                        
            var dataSource = new byte[packetSize];
            Buffer.BlockCopy(BitConverter.GetBytes(packetSize), 0, dataSource, 0, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(pktID), 0, dataSource, 2, 2);
            dataSource[4] = type;
            
            if (bodyData != null)
            {
                Buffer.BlockCopy(bodyData, 0, dataSource, 5, bodyDataSize);
            }

            return dataSource;
        }

        public static Tuple<int, byte[]> ClientReceiveData(int recvLength, byte[] recvData)
        {
            var packetSize = BitConverter.ToUInt16(recvData, 0);
            var packetID = BitConverter.ToUInt16(recvData, 2);
            var bodySize = packetSize - PacketDef.PACKET_HEADER_SIZE;

            var packetBody = new byte[bodySize];
            Buffer.BlockCopy(recvData, PacketDef.PACKET_HEADER_SIZE, packetBody,  0, bodySize);

            return new Tuple<int, byte[]>(packetID, packetBody);
        }
    }

    // 로그인 요청
    [MemoryPackable]
    public partial class PKTReqLogin
    {
        public string UserID;
        public string AuthToken;
    }

    [MemoryPackable]
    public partial class PKTResLogin
    {
        public short Result;
    }


    [MemoryPackable]
    public partial class PKNtfMustClose
    {
        public short Result;
    }



    [MemoryPackable]
    public partial class PKTReqRoomEnter
    {
        public int RoomNumber;
    }

    [MemoryPackable]
    public partial class PKTResRoomEnter
    {
        public short Result;
    }

    [MemoryPackable]
    public partial class PKTNtfRoomUserList
    {
        public List<string> UserIDList = new List<string>();
    }

    [MemoryPackable]
    public partial class PKTNtfRoomNewUser
    {
        public string UserID;
    }


    [MemoryPackable]
    public partial class PKTReqRoomLeave
    {
    }

    [MemoryPackable]
    public partial class PKTResRoomLeave
    {
        public short Result;
    }

    [MemoryPackable]
    public partial class PKTNtfRoomLeaveUser
    {
        public string UserID;
    }


    [MemoryPackable]
    public partial class PKTReqRoomChat
    {
        public string ChatMessage;
    }

    
    [MemoryPackable]
    public partial class PKTNtfRoomChat
    {
        public string UserID;

        public string ChatMessage;
    }
}
