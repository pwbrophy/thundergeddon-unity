using System;
using System.Collections.Generic;

public class RobotDirectory : IRobotDirectory
{
    // store the robots by id (fast lookup)
    private readonly Dictionary<string, RobotInfo> _byId = new();

    // NEW: track insertion order so we know which was "last added"
    private readonly List<string> _order = new();

    public event Action<RobotInfo> OnRobotAdded;
    public event Action<RobotInfo> OnRobotUpdated;
    public event Action<string> OnRobotRemoved;

    // Return robots in insertion order
    public IReadOnlyList<RobotInfo> GetAll()
    {
        List<RobotInfo> list = new List<RobotInfo>(_order.Count);
        for (int i = 0; i < _order.Count; i++)
        {
            string id = _order[i];
            if (_byId.TryGetValue(id, out var info))
            {
                list.Add(info);
            }
        }
        return list;
    }

    public bool TryGet(string robotId, out RobotInfo info)
    {
        return _byId.TryGetValue(robotId, out info);
    }

    // Treat this as "insert or update". We ignore online/offline entirely.
    public void Upsert(string robotId, string callsign, string ip)
    {
        if (string.IsNullOrWhiteSpace(robotId)) return;

        if (!_byId.TryGetValue(robotId, out var r))
        {
            // New robot → add to dictionary and to the insertion order list
            r = new RobotInfo
            {
                RobotId = robotId,
                Callsign = string.IsNullOrWhiteSpace(callsign) ? GenerateGenericName() : callsign.Trim(),
                Ip = ip ?? "",
                AssignedPlayer = "Unassigned",
                // You can delete IsOnline from RobotInfo if you want; we just won't set it here.
            };

            _byId.Add(robotId, r);
            _order.Add(robotId);                // track insertion order
            OnRobotAdded?.Invoke(r);
        }
        else
        {
            // Existing robot → update fields if changed
            bool changed = false;

            if (!string.IsNullOrWhiteSpace(callsign))
            {
                string newName = callsign.Trim();
                if (r.Callsign != newName)
                {
                    r.Callsign = newName;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(ip) && r.Ip != ip)
            {
                r.Ip = ip;
                changed = true;
            }

            if (changed)
            {
                OnRobotUpdated?.Invoke(r);
            }
        }
    }

    public void SetCallsign(string robotId, string newCallsign)
    {
        if (string.IsNullOrWhiteSpace(newCallsign)) return;
        if (_byId.TryGetValue(robotId, out var r))
        {
            string trimmed = newCallsign.Trim();
            if (r.Callsign != trimmed)
            {
                r.Callsign = trimmed;
                OnRobotUpdated?.Invoke(r);
            }
        }
    }

    public void SetAssignedPlayer(string robotId, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName)) return;
        if (_byId.TryGetValue(robotId, out var r))
        {
            if (r.AssignedPlayer != playerName)
            {
                r.AssignedPlayer = playerName;
                OnRobotUpdated?.Invoke(r);
            }
        }
    }

    public bool Remove(string robotId)
    {
        // Remove from dictionary
        bool removedDict = _byId.Remove(robotId);

        if (removedDict)
        {
            // Also remove from insertion order list
            int idx = _order.IndexOf(robotId);
            if (idx >= 0) _order.RemoveAt(idx);

            OnRobotRemoved?.Invoke(robotId);
            return true;
        }
        return false;
    }

    // NEW: remove the most recently added robot (by insertion order)
    public bool RemoveLast()
    {
        if (_order.Count == 0) return false;

        // last id that was inserted
        string lastId = _order[_order.Count - 1];

        // defer to existing Remove logic to keep everything in sync + raise events
        return Remove(lastId);
    }

    // Simple generator for "robot-01", "robot-02", ...
    private int _genericCounter = 0;
    private string GenerateGenericName()
    {
        _genericCounter = _genericCounter + 1;
        return $"robot-{_genericCounter:00}";
    }
}
