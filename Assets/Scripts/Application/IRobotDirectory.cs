using System;
using System.Collections.Generic;

// The contract the UI talks to.
// "Events" are doorbells the UI can subscribe to, so the list auto-updates.
public interface IRobotDirectory
{
    IReadOnlyList<RobotInfo> GetAll();

    // Events (UI listens to these)
    event Action<RobotInfo> OnRobotAdded;
    event Action<RobotInfo> OnRobotUpdated;
    event Action<string> OnRobotRemoved;  // by RobotId

    // Mutating methods (UI calls these)
    void UpsertOnline(string robotId, string callsign, string ip);
    void SetOnline(string robotId, bool online);
    void SetCallsign(string robotId, string newCallsign);
    void SetAssignedPlayer(string robotId, string playerName);
    bool Remove(string robotId);

    // Helper: try get current info
    bool TryGet(string robotId, out RobotInfo info);
}
