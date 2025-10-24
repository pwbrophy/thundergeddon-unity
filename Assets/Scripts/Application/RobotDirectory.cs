using System;
using System.Collections.Generic;



// Stores robots in memory and raises events when things change.
// No Unity types in here so it's easy to test.
public class RobotDirectory : IRobotDirectory
{
    private readonly Dictionary<string, RobotInfo> _byId = new();

    // Events for UI
    public event Action<RobotInfo> OnRobotAdded;
    public event Action<RobotInfo> OnRobotUpdated;
    public event Action<string> OnRobotRemoved;

    public IReadOnlyList<RobotInfo> GetAll() => new List<RobotInfo>(_byId.Values);

    public bool TryGet(string robotId, out RobotInfo info) => _byId.TryGetValue(robotId, out info);

    // Adds or updates a robot, marking it Online and updating callsign/ip if provided.
    public void UpsertOnline(string robotId, string callsign, string ip)
    {
        if (string.IsNullOrWhiteSpace(robotId)) return;

        if (!_byId.TryGetValue(robotId, out var r))
        {
            r = new RobotInfo
            {
                RobotId = robotId,
                Callsign = string.IsNullOrWhiteSpace(callsign) ? GenerateGenericName() : callsign.Trim(),
                Ip = ip ?? "",
                AssignedPlayer = "Unassigned",
                IsOnline = true
            };
            _byId.Add(robotId, r);
            OnRobotAdded?.Invoke(r);
        }
        else
        {
            bool changed = false;

            if (!string.IsNullOrWhiteSpace(callsign) && r.Callsign != callsign.Trim())
            { r.Callsign = callsign.Trim(); changed = true; }

            if (!string.IsNullOrWhiteSpace(ip) && r.Ip != ip)
            { r.Ip = ip; changed = true; }

            if (!r.IsOnline)
            { r.IsOnline = true; changed = true; }

            if (changed) OnRobotUpdated?.Invoke(r);
        }
    }

    public void SetOnline(string robotId, bool online)
    {
        if (_byId.TryGetValue(robotId, out var r) && r.IsOnline != online)
        {
            r.IsOnline = online;
            OnRobotUpdated?.Invoke(r);
        }
    }

    public void SetCallsign(string robotId, string newCallsign)
    {
        if (string.IsNullOrWhiteSpace(newCallsign)) return;
        if (_byId.TryGetValue(robotId, out var r) && r.Callsign != newCallsign.Trim())
        {
            r.Callsign = newCallsign.Trim();
            OnRobotUpdated?.Invoke(r);
        }
    }

    public void SetAssignedPlayer(string robotId, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName)) return;
        if (_byId.TryGetValue(robotId, out var r) && r.AssignedPlayer != playerName)
        {
            r.AssignedPlayer = playerName;
            OnRobotUpdated?.Invoke(r);
        }
    }

    public bool Remove(string robotId)
    {
        if (_byId.Remove(robotId))
        {
            OnRobotRemoved?.Invoke(robotId);
            return true;
        }
        return false;
    }

    // Simple generator for "robot-01", "robot-02", ...
    private int _genericCounter = 0;
    private string GenerateGenericName()
    {
        _genericCounter++;
        return $"robot-{_genericCounter:00}";
    }
}
