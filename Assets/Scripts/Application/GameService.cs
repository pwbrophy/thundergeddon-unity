public sealed class GameService
{
    // Later: inject whatever you need (TurnEngine, RobotGateway, etc.)

    public bool CanStart()
    {
        // TODO: real checks: ≥2 alliances, ≥1 army/robot per alliance, devices assigned, etc.
        return true;
    }

    public void StartGame(int startAP)
    {
        // TODO: refill AP, reset turn order, broadcast state to clients, etc.
    }
}
