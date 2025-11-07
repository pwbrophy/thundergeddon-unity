

public static class ServiceLocator
{
    public static IRobotDirectory RobotDirectory; 
    public static GameFlow GameFlow;
    public static LobbyService Lobby;
    public static GameService Game;
    public static RobotWebSocketServer RobotServer;  // Set by RobotWebSocketServer when it starts

}
