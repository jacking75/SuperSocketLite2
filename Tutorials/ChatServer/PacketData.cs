using MemoryPack; //https://github.com/neuecc/MemoryPack
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// 지금과 달리 이 파일이 처음 만들어질 때는 별도의 프로젝트에 포함되어 있어서 별도의 네임스페이스를 사용하고 있다
namespace CSBaseLib;

// 패킷 관련 상수 정의
public class PacketDef
{
    public const Int16 HeaderSize = 5;
    public const int MaxUserIDByteLength = 16;
    public const int MaxUserPWByteLength = 16;

    public const int InvalidRoomNumber = -1;
}

// 패킷 클래스 객체를 바이트로 변환
public class PacketToBytes
{
    public static byte[] Make(PacketId packetID, byte[] bodyData)
    {
        byte type = 0;
        var pktID = (Int16)packetID;
        
        Int16 bodyDataSize = 0;
        if (bodyData != null)
        {
            bodyDataSize = (Int16)bodyData.Length;
        }
        
        var packetSize = (Int16)(bodyDataSize + PacketDef.HeaderSize);
        
        
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
