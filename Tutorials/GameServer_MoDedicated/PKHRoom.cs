using System;
using System.Collections.Generic;
using System.Linq;

using MemoryPack;

using CSBaseLib;


namespace GameServer;

public class PKHRoom : PKHandler
{
    List<Room> RoomList = null;
    int StartRoomNumber;
    
    public void SetRooomList(List<Room> roomList)
    {
        RoomList = roomList;
        StartRoomNumber = roomList[0].Number;
    }

    public void RegistPacketHandler(Dictionary<UInt16, Action<ServerPacketData>> packetHandlerDict)
    {
        packetHandlerDict.Add((UInt16)PacketId.ReqRoomEnter, HandleRequestRoomEnter);
        packetHandlerDict.Add((UInt16)PacketId.ReqRoomLeave, HandleRequestLeave);
        packetHandlerDict.Add((UInt16)PacketId.NtfInRoomLeave, HandleNotifyLeaveInternal);
        packetHandlerDict.Add((UInt16)PacketId.ReqRoomChat, HandleRequestChat);

        packetHandlerDict.Add((UInt16)PacketId.ReqRoomDevAllRoomStartGame, HandleRequestDevAllRoomStartGame);
        packetHandlerDict.Add((UInt16)PacketId.ReqRoomDevAllRoomEndGame, HandleRequestDevAllRoomStopGame);
    }


    Room GetRoom(int roomNumber)
    {
        var index = roomNumber - StartRoomNumber;

        if( index < 0 || index >= RoomList.Count())
        {
            return null;
        }

        return RoomList[index];
    }
            
    (bool, Room, RoomUser) CheckRoomAndRoomUser(int userNetSessionIndex)
    {
        var user = _userMgr.GetUser(userNetSessionIndex);
        if (user == null)
        {
            return (false, null, null);
        }

        var roomNumber = user.RoomNumber;
        var room = GetRoom(roomNumber);

        if(room == null)
        {
            return (false, null, null);
        }

        var roomUser = room.GetUser(userNetSessionIndex);

        if (roomUser == null)
        {
            return (false, room, null);
        }

        return (true, room, roomUser);
    }



    public void HandleRequestRoomEnter(ServerPacketData packetData)
    {
        var sessionID = packetData.SessionID;
        var sessionIndex = packetData.SessionIndex;
        MainServer.MainLogger.Debug("RequestRoomEnter");

        try
        {
            var user = _userMgr.GetUser(sessionIndex);

            if (user == null || user.IsConfirm(sessionID) == false)
            {
                SendResponseEnterRoomToClient(ErrorCode.RoomEnterInvalidUser, sessionID);
                return;
            }

            if (user.IsStateRoom())
            {
                SendResponseEnterRoomToClient(ErrorCode.RoomEnterInvalidState, sessionID);
                return;
            }

            var reqData = MemoryPackSerializer.Deserialize<PKTReqRoomEnter>(packetData.BodyData);
            
            var room = GetRoom(reqData.RoomNumber);

            if (room == null)
            {
                SendResponseEnterRoomToClient(ErrorCode.RoomEnterInvalidRoomNumber, sessionID);
                return;
            }

            if (room.AddUser(user.ID(), sessionIndex, sessionID) == false)
            {
                SendResponseEnterRoomToClient(ErrorCode.RoomEnterFailAddUser, sessionID);
                return;
            }


            user.EnteredRoom(reqData.RoomNumber);

            room.NotifyPacketUserList(sessionID);
            room.NofifyPacketNewUser(sessionIndex, user.ID());

            SendResponseEnterRoomToClient(ErrorCode.None, sessionID);

            MainServer.MainLogger.Debug("RequestEnterInternal - Success");
        }
        catch (Exception ex)
        {
            MainServer.MainLogger.Error(ex.ToString());
        }
    }

    void SendResponseEnterRoomToClient(ErrorCode errorCode, string sessionID)
    {
        var resRoomEnter = new PKTResRoomEnter()
        {
            Result = (short)errorCode
        };

        var bodyData = MemoryPackSerializer.Serialize(resRoomEnter);
        var sendData = PacketToBytes.Make(PacketId.ResRoomEnter, bodyData);

        _serverNetwork.SendData(sessionID, sendData);
    }

    public void HandleRequestLeave(ServerPacketData packetData)
    {
        var sessionID = packetData.SessionID;
        var sessionIndex = packetData.SessionIndex;
        MainServer.MainLogger.Debug("로그인 요청 받음");

        try
        {
            var user = _userMgr.GetUser(sessionIndex);
            if(user == null)
            {
                return;
            }

            if(LeaveRoomUser(sessionIndex, user.RoomNumber) == false)
            {
                return;
            }

            user.LeaveRoom();

            SendResponseLeaveRoomToClient(sessionID);

            MainServer.MainLogger.Debug("Room RequestLeave - Success");
        }
        catch (Exception ex)
        {
            MainServer.MainLogger.Error(ex.ToString());
        }
    }

    bool LeaveRoomUser(int sessionIndex, int roomNumber)
    {
        MainServer.MainLogger.Debug($"LeaveRoomUser. SessionIndex:{sessionIndex}");

        var room = GetRoom(roomNumber);
        if (room == null)
        {
            return false;
        }

        var roomUser = room.GetUser(sessionIndex);
        if (roomUser == null)
        {
            return false;
        }
                    
        var userID = roomUser.UserID;
        room.RemoveUser(roomUser);

        room.NotifyPacketLeaveUser(userID);
        return true;
    }

    void SendResponseLeaveRoomToClient(string sessionID)
    {
        var resRoomLeave = new PKTResRoomLeave()
        {
            Result = (short)ErrorCode.None
        };

        var bodyData = MemoryPackSerializer.Serialize(resRoomLeave);
        var sendData = PacketToBytes.Make(PacketId.ResRoomLeave, bodyData);

        _serverNetwork.SendData(sessionID, sendData);
    }

    public void HandleNotifyLeaveInternal(ServerPacketData packetData)
    {
        var sessionIndex = packetData.SessionIndex;
        MainServer.MainLogger.Debug($"NotifyLeaveInternal. SessionIndex: {sessionIndex}");

        var reqData = MemoryPackSerializer.Deserialize<PKTInternalNtfRoomLeave>(packetData.BodyData);            
        LeaveRoomUser(sessionIndex, reqData.RoomNumber);
    }
            
    public void HandleRequestChat(ServerPacketData packetData)
    {
        var sessionID = packetData.SessionID;
        var sessionIndex = packetData.SessionIndex;
        MainServer.MainLogger.Debug("Room RequestChat");

        try
        {
            var roomObject = CheckRoomAndRoomUser(sessionIndex);

            if(roomObject.Item1 == false)
            {
                return;
            }


            var reqData = MemoryPackSerializer.Deserialize<PKTReqRoomChat>(packetData.BodyData);

            var notifyPacket = new PKTNtfRoomChat()
            {
                UserID = roomObject.Item3.UserID,
                ChatMessage = reqData.ChatMessage
            };

            var Body = MemoryPackSerializer.Serialize(notifyPacket);
            var sendData = PacketToBytes.Make(PacketId.NtfRoomChat, Body);

            roomObject.Item2.Broadcast(-1, sendData);

            MainServer.MainLogger.Debug("Room RequestChat - Success");
        }
        catch (Exception ex)
        {
            MainServer.MainLogger.Error(ex.ToString());
        }
    }

    public void HandleRequestDevAllRoomStartGame(ServerPacketData packetData)
    {
        var sessionID = packetData.SessionID;
        var sessionIndex = packetData.SessionIndex;
        MainServer.MainLogger.Debug("Room RequestDevAllRoomStartGame");

        try
        {
            foreach(var room in RoomList)
            {
                room.StartGame();
            }                
        }
        catch (Exception ex)
        {
            MainServer.MainLogger.Error(ex.ToString());
        }
    }

    public void HandleRequestDevAllRoomStopGame(ServerPacketData packetData)
    {
        var sessionID = packetData.SessionID;
        var sessionIndex = packetData.SessionIndex;
        MainServer.MainLogger.Debug("Room RequestDevAllRoomStopGame");

        try
        {
            foreach (var room in RoomList)
            {
                room.EndGame();
            }
        }
        catch (Exception ex)
        {
            MainServer.MainLogger.Error(ex.ToString());
        }
    }


}
