namespace FireLink119.Network
{
    public enum LobbyRoomRole
    {
        // Host는 방을 만들면서 동시에 자기 입력도 보내는 플레이어다.
        Host,

        // Client는 로비에서 같은 4자리 코드를 입력해 Host가 만든 방에 들어간다.
        Client
    }
}
