using System;                                      // For Action
using System.Collections.Generic;                  // For List and Dictionary
using UnityEngine;                                 // For Debug.Log and Mathf

/// <summary>
/// TurnManager — rotates turns across players inside an alliance, then advances to the next alliance.
/// Data wiring:
///   - Alliances count comes from ServiceLocator.Game.State.Alliances.
///   - Players and their alliance indices come from ServiceLocator.Players.
///   - Robots filter by RobotInfo.AssignedPlayer (player name string).
/// Events:
///   - TurnChanged(allianceIndex, playerIndexInAlliance, allowedRobotIds)
///     * playerIndexInAlliance is -1 if the alliance has no players.
///     * Use CurrentPlayerName to show a label-friendly name.
/// </summary>
public sealed class TurnManager
{
    // Fired whenever the current turn changes (after model rebuilds or EndTurn).
    public event Action<int, int, List<string>> TurnChanged;

    // Reference to robot directory (to read robots + get change notifications).
    private IRobotDirectory _dir;

    // Reference to players service (to read players + get change notifications).
    private PlayersService _players;

    // Cached count of alliances from GameService.State (we recompute on rebuild).
    private int _alliances = 2;

    // Per-alliance list of player names (index = alliance; value = list of player names in that alliance).
    private readonly List<List<string>> _playersByAlliance = new List<List<string>>();

    // Map from player name → list of robot IDs currently assigned to that player.
    private readonly Dictionary<string, List<string>> _robotsByPlayer = new Dictionary<string, List<string>>();

    // Scratch list we reuse when publishing allowed robot IDs (to avoid heap churn).
    private readonly List<string> _scratch = new List<string>();

    // Current alliance index (0-based).
    private int _curAlliance = 0;

    // Current player index within the current alliance list (0-based, or -1 when no players).
    private int _curPlayer = -1;

    // Expose current alliance index for UI.
    public int CurrentAlliance => _curAlliance;

    // Expose current player index within alliance (can be -1).
    public int CurrentPlayerIndex => _curPlayer;

    // Expose current player name (null if the current alliance has no players).
    public string CurrentPlayerName
    {
        get
        {
            // If alliance has no players, return null.
            if (_curAlliance < 0 || _curAlliance >= _playersByAlliance.Count) return null;      // Guard
            var list = _playersByAlliance[_curAlliance];                                         // Get list
            if (_curPlayer < 0 || _curPlayer >= list.Count) return null;                        // Guard
            return list[_curPlayer];                                                            // Name
        }
    }


    public void Initialize(IRobotDirectory dir)
    {
        // Remember the robot directory so we can read robots and subscribe to its events.
        _dir = dir;                                                         // Store directory reference

        // Grab the shared PlayersService (normally created once in AppBootstrap).
        _players = ServiceLocator.Players;                                  // Cache players service

        // Safety net: if AppBootstrap was missing for some reason, create one on the fly.
        if (_players == null)                                              // No players service yet?
        {
            Debug.LogWarning("[TurnManager] ServiceLocator.Players was null; creating a new PlayersService."); // Warn
            _players = new PlayersService();                                // Allocate a fresh service
            _players.EnsureDefaults();                                      // Seed it with default players
            ServiceLocator.Players = _players;                              // Register globally so UI sees same instance
        }

        // Subscribe to robot directory changes so turns update when robots change.
        _dir.OnRobotAdded -= OnDirectoryChanged;                          // Ensure no duplicate subscriptions
        _dir.OnRobotUpdated -= OnDirectoryChanged;                          // "
        _dir.OnRobotRemoved -= OnDirectoryChanged;                          // "
        _dir.OnRobotAdded += OnDirectoryChanged;                          // Listen for added robots
        _dir.OnRobotUpdated += OnDirectoryChanged;                          // Listen for updated robots
        _dir.OnRobotRemoved += OnDirectoryChanged;                          // Listen for removed robots

        // Subscribe to player list changes so alliances / allowed robots stay in sync.
        _players.OnChanged -= OnPlayersChanged;                            // Clear any old subscription
        _players.OnChanged += OnPlayersChanged;                            // Listen for add/remove/rename/alliance

        // Build internal structures and publish the very first turn.
        RebuildModel();                                                     // Build players-by-alliance & robots-by-player
        PublishTurn();                                                      // Fire TurnChanged for the initial state
    }


    /// <summary>
    /// Advance the turn to the next player within the same alliance; if done, move to next alliance.
    /// </summary>
    public void EndTurn()
    {
        // If there are no alliances (should not happen), publish as-is.
        if (_alliances <= 0) { PublishTurn(); return; }                                         // Safety

        // Fetch current alliance's player list (or empty).
        var list = (_curAlliance >= 0 && _curAlliance < _playersByAlliance.Count)
            ? _playersByAlliance[_curAlliance]
            : new List<string>();                                                               // Guard to empty

        // If this alliance has at least one player, try next player.
        if (list.Count > 0)
        {
            // Move to next player (wrap around within alliance).
            _curPlayer = (_curPlayer + 1) % list.Count;                                         // Next player
            // If we wrapped back to zero, it means all players in this alliance have played → move to next alliance.
            if (_curPlayer == 0)
            {
                // Advance to next alliance (wrap across all alliances).
                _curAlliance = (_curAlliance + 1) % _alliances;                                 // Next alliance
                // Snap player index for the new alliance.
                _curPlayer = FirstValidPlayerIndex(_curAlliance);                                // Pick first or -1
            }
        }
        else
        {
            // No players in this alliance → go to the next alliance that has players (or keep -1 if none).
            int safety = _alliances;                                                             // Prevent infinite loop
            do
            {
                _curAlliance = (_curAlliance + 1) % _alliances;                                 // Next alliance
                _curPlayer = FirstValidPlayerIndex(_curAlliance);                              // First player or -1
                safety--;
            }
            while (_curPlayer == -1 && safety > 0);                                              // Skip empty alliances
        }

        // Publish the new turn.
        PublishTurn();                                                                           // Fire event
    }

    /// <summary>
    /// Handle any robot directory change — rebuild and publish.
    /// </summary>
    private void OnDirectoryChanged(RobotInfo _)
    {
        // Rebuild model because assignments/names might have changed.
        RebuildModel();                                                                          // Recompute
        PublishTurn();                                                                           // Notify
    }

    /// <summary>
    /// Handle removal event variant (by id) — rebuild and publish.
    /// </summary>
    private void OnDirectoryChanged(string _)
    {
        // Same as above.
        RebuildModel();                                                                          // Recompute
        PublishTurn();                                                                           // Notify
    }

    /// <summary>
    /// Handle players list changes — rebuild and publish.
    /// </summary>
    private void OnPlayersChanged()
    {
        // Rebuild model in case names or alliances changed.
        RebuildModel();                                                                          // Recompute
        PublishTurn();                                                                           // Notify
    }

    /// <summary>
    /// Recompute alliances, their player lists, and the robots-per-player mapping.
    /// </summary>
    private void RebuildModel()
    {
        // Read alliances count from GameService.State (fallback to 2).
        _alliances = Mathf.Max(1, ServiceLocator.Game?.State?.Alliances ?? 2);                   // #Alliances

        // Clear and size the per-alliance player array.
        _playersByAlliance.Clear();                                                              // Reset list
        for (int a = 0; a < _alliances; a++) _playersByAlliance.Add(new List<string>());        // Allocate each bucket

        // Fill players into the correct alliance bucket by their Name.
        var plist = ServiceLocator.Players.GetAll();                                             // Snapshot players
        for (int i = 0; i < plist.Count; i++)                                                    // For every player
        {
            int a = Mathf.Clamp(plist[i].AllianceIndex, 0, _alliances - 1);                      // Clamp alliance
            string name = string.IsNullOrEmpty(plist[i].Name) ? $"Player{i + 1}" : plist[i].Name;// Safe name
            _playersByAlliance[a].Add(name);                                                     // Store name in alliance
        }

        // Rebuild robots-per-player mapping.
        _robotsByPlayer.Clear();                                                                 // Reset map
        var rlist = _dir.GetAll();                                                               // Snapshot robots
        for (int i = 0; i < rlist.Count; i++)                                                    // For every robot
        {
            string pn = rlist[i].AssignedPlayer;                                                 // Player name string
            if (string.IsNullOrEmpty(pn)) continue;                                              // Skip unassigned
            if (!_robotsByPlayer.TryGetValue(pn, out var ids))
            {
                ids = new List<string>();                                                        // Create new list
                _robotsByPlayer[pn] = ids;                                                       // Put in map
            }
            ids.Add(rlist[i].RobotId);                                                           // Add this robot id
        }

        // Clamp the current alliance index.
        _curAlliance = Mathf.Clamp(_curAlliance, 0, _alliances - 1);                             // Clamp alliance

        // Ensure current player index is valid for the current alliance.
        if (FirstValidPlayerIndex(_curAlliance) == -1)                                           // If no players here
            _curPlayer = -1;                                                                      // No player
        else
        {
            var list = _playersByAlliance[_curAlliance];                                         // Player list
            if (_curPlayer < 0 || _curPlayer >= list.Count) _curPlayer = 0;                      // Snap to 0
        }
    }

    /// <summary>
    /// Returns the first valid player index for the given alliance, or -1 if none.
    /// </summary>
    private int FirstValidPlayerIndex(int alliance)
    {
        // Guard out-of-range alliances.
        if (alliance < 0 || alliance >= _playersByAlliance.Count) return -1;                     // Invalid
        // If there are players, first index is 0; otherwise -1.
        return _playersByAlliance[alliance].Count > 0 ? 0 : -1;                                   // 0 or -1
    }

    /// <summary>
    /// Publish TurnChanged with the allowed robots for the current player.
    /// </summary>
    private void PublishTurn()
    {
        // Start fresh scratch list.
        _scratch.Clear();                                                                         // Empty

        // Figure out the current player's name (null if no players in this alliance).
        string playerName = CurrentPlayerName;                                                    // Resolve name

        // If we have a player, copy their robot IDs (if any) into scratch.
        if (!string.IsNullOrEmpty(playerName) && _robotsByPlayer.TryGetValue(playerName, out var ids))
        {
            for (int i = 0; i < ids.Count; i++) _scratch.Add(ids[i]);                            // Copy IDs
        }

        // Debug so you can see state transitions easily.
        var label = string.IsNullOrEmpty(playerName) ? "—" : playerName;                          // Friendly name
        Debug.Log($"[Turn] Alliance={_curAlliance + 1} Player={label} Robots={_scratch.Count}");  // Trace

        // Fire the event (player index can be -1 if no players in alliance).
        TurnChanged?.Invoke(_curAlliance, _curPlayer, _scratch);                                  // Notify
    }
}
