// Minimal robot data stored by the registry.
// In real life you'll add more fields (battery, firmware, etc.)
public class RobotInfo
{
    public string RobotId;         // stable unique id (e.g., MAC-based) - the "key"
    public string Callsign;        // nice display name (editable)
    public string Ip;              // last-known local IP
    public string AssignedPlayer;  // "Player1" or "Player2" for now

}
