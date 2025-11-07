// GameState.cs — tiny data bag for the running match (first pass)
using System;                             // For basic types
using System.Collections.Generic;         // For List<>

public sealed class GameState             // Holds the snapshot we pass from Lobby -> Game
{
    public int Alliances = 2;             // Hard-coded as requested
    public int Players = 2;             // Hard-coded as requested
    public int Clients = 2;             // Hard-coded as requested

    public List<RobotInfo> Robots;        // The robots that are in the game (snapshot from Lobby)
}
