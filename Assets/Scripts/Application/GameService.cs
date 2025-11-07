// GameService.cs — super small first-pass game bootstrapper
using System;                                      // For basic types
using System.Collections.Generic;                  // For List<>

public sealed class GameService
{
    // Keep the current game state so the Playing UI can read it.
    public GameState State { get; private set; }   // Null when no game is running

    // Return true if we are allowed to start a game (very permissive for now).
    public bool CanStart()
    {
        return true;                               // You can add real checks later
    }

    // Create a new game using the current lobby robots and hard-coded counts.
    public void StartGame(int startAP)
    {
        // Grab the current robot list from the shared directory.
        var dir = ServiceLocator.RobotDirectory;   // Get the registry of connected robots
        var robotsNow = dir != null ? dir.GetAll() // Snapshot current robots (in insertion order)
                                    : Array.Empty<RobotInfo>();

        // Build a brand-new GameState (hard-coded alliances/players/clients as requested).
        var gs = new GameState
        {
            Alliances = 2,                         // Fixed for first pass
            Players = 2,                         // Fixed for first pass
            Clients = 2,                         // Fixed for first pass
            Robots = new List<RobotInfo>(robotsNow) // Copy the robots into game state
        };

        // Store it so the Playing UI can read from here.
        State = gs;                                 // Publish the state

        // Nothing else yet (no AP/turn order). Simple by design.
        // NOTE: Switching Phase to Playing is done by GameFlow.StartGame(). :contentReference[oaicite:4]{index=4}
        // NOTE: UDP listener stops automatically when leaving Lobby. :contentReference[oaicite:5]{index=5}
        // NOTE: WS "hello" is only accepted in Lobby, so no new robots join mid-game. :contentReference[oaicite:6]{index=6}
    }
}
