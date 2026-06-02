namespace SuperSocketLite.LoadTest.Client;

public enum ClientState
{
    Created,
    Connecting,
    Connected,
    Login,
    Active,
    Idle,
    Closing,
    Closed,
    Reconnecting
}
