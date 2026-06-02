using SuperSocketLite.LoadTest.Shared;

namespace SuperSocketLite.LoadTest.Client.Scenarios;

public sealed class GameLikeScenario
{
    private bool _loggedIn;
    private bool _inRoom;

    public BinaryPacket CreateLogin(int clientId)
    {
        return new BinaryPacket(201, 0, System.Text.Encoding.UTF8.GetBytes($"user-{clientId:D8}"));
    }

    public BinaryPacket CreateRoomEnter(int clientId)
    {
        return new BinaryPacket(207, 0, System.Text.Encoding.UTF8.GetBytes($"room-1 user-{clientId:D8}"));
    }

    public BinaryPacket CreateHeartbeat()
    {
        return new BinaryPacket(203, 0, []);
    }

    public BinaryPacket CreateChat(int clientId, int sequence, LoadTestOptions options)
    {
        return new BinaryPacket(205, 0, PayloadFactory.Create(clientId, sequence, options.Payload));
    }

    public BinaryPacket CreateRoomLeave(int clientId)
    {
        return new BinaryPacket(209, 0, System.Text.Encoding.UTF8.GetBytes($"room-1 user-{clientId:D8}"));
    }

    public GameLikeOperation NextOperation(int clientId, int sequence, LoadTestOptions options)
    {
        if (!_loggedIn)
        {
            _loggedIn = true;
            return new GameLikeOperation("login", CreateLogin(clientId));
        }

        if (!_inRoom)
        {
            _inRoom = true;
            return new GameLikeOperation("room-enter", CreateRoomEnter(clientId));
        }

        if (options.RoomCycleEvery > 0 && sequence % options.RoomCycleEvery == 0)
        {
            _inRoom = false;
            return new GameLikeOperation("room-leave", CreateRoomLeave(clientId));
        }

        if (sequence % 4 == 0)
            return new GameLikeOperation("chat", CreateChat(clientId, sequence, options));

        return new GameLikeOperation("heartbeat", CreateHeartbeat());
    }
}

public sealed record GameLikeOperation(string OperationType, BinaryPacket Packet);
