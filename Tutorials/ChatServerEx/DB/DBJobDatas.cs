using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MemoryPack;
using CSBaseLib;


namespace DB;

public class DBQueue
{
    public PacketId PacketID;
    public int SessionIndex;
    public string SessionID;
    public byte[] Datas;
}

public class DBResultQueue
{
    public PacketId PacketID;
    public int SessionIndex;
    public string SessionID;
    public byte[] Datas;
}


[MemoryPackable]
public partial class DBReqLogin
{
    public string UserID;

    public string AuthToken;
}

[MemoryPackable]
public partial class DBResLogin
{
    public string UserID;
    public ErrorCode Result;
}
