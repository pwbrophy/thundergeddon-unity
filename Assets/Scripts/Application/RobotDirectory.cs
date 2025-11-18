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

    public void Upsert(string robotId, string callsign, string ip)
    {
        // Ignore bad robot ids.
        if (string.IsNullOrWhiteSpace(robotId)) return;                    // If id is blank, do nothing

        // Try to get an existing robot entry.
        if (!_byId.TryGetValue(robotId, out var r))                        // If we do NOT already know this id
        {
            // Create a brand new RobotInfo for this robot.
            r = new RobotInfo                                              // Allocate a new RobotInfo
            {
                RobotId = robotId,                                         // Store the unique robot id
                Callsign = string.IsNullOrWhiteSpace(callsign)             // If no callsign was supplied
                    ? GenerateGenericName()                                //   -> auto-generate a name like "robot-01"
                    : callsign.Trim(),                                     //   -> otherwise use the trimmed callsign
                Ip = string.IsNullOrWhiteSpace(ip) ? "" : ip.Trim(),       // Store IP if we got one, else empty
                AssignedPlayer = null,                                     // Null means "unassigned" in our code
            };

            _byId.Add(robotId, r);                                         // Add to dictionary for fast lookup
            _order.Add(robotId);                                           // Track insertion order
            OnRobotAdded?.Invoke(r);                                       // Notify listeners that a robot was added
        }
        else                                                               // We already have this robot
        {
            bool changed = false;                                          // Track if anything actually changes

            // Update callsign only if we were given a non-empty value.
            if (!string.IsNullOrWhiteSpace(callsign))                      // If caller provided a callsign
            {
                string newName = callsign.Trim();                          //   -> trim the new name
                if (r.Callsign != newName)                                 //   -> only update if different
                {
                    r.Callsign = newName;                                  //   -> store the new name
                    changed = true;                                        //   -> remember that something changed
                }
            }

            // IMPORTANT: only update IP when the caller gives a non-empty IP.
            // This means the WS "hello" call with ip="" will NOT wipe the IP
            // that was already learned from UDP discovery.
            if (!string.IsNullOrWhiteSpace(ip))                            // If caller supplied a non-empty IP
            {
                string newIp = ip.Trim();                                  //   -> trim the IP string
                if (r.Ip != newIp)                                         //   -> only update if it actually changed
                {
                    r.Ip = newIp;                                          //   -> store the new IP
                    changed = true;                                        //   -> mark that we changed something
                }
            }

            // Only raise the update event if we actually changed something.
            if (changed)                                                   // If any field was updated
            {
                OnRobotUpdated?.Invoke(r);                                 //   -> notify listeners about the update
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

    // New logic: setting an empty / null player name now *clears* the assignment.
    public void SetAssignedPlayer(string robotId, string playerName)
    {
        // First make sure the robot exists; if not, quietly do nothing.
        if (!_byId.TryGetValue(robotId, out var r)) return;                  // Unknown id -> ignore

        // Normalise the input: null / empty / whitespace all become "no assignment".
        string normalized = string.IsNullOrWhiteSpace(playerName)            // Is the name blank?
            ? null                                                           //   -> store null for "unassigned"
            : playerName.Trim();                                             //   -> otherwise store trimmed name

        // Only update the record if the value really changed.
        if (r.AssignedPlayer != normalized)                                  // Different from existing?
        {
            r.AssignedPlayer = normalized;                                   // Store new (or cleared) assignment
            OnRobotUpdated?.Invoke(r);                                       // Notify listeners (UI, TurnManager)
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
