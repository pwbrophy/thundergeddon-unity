// PlayersService.cs
// Stores the list of players, raises OnChanged when they change,
// and now also saves / loads them to a JSON file in Application.persistentDataPath.

using System;                                      // For Action and Serializable
using System.Collections.Generic;                  // For List<T>
using System.IO;                                   // For File and Path
using UnityEngine;                                 // For Application.persistentDataPath and JsonUtility

// Simple data type representing a player in the game.
// I am assuming you already have something like this elsewhere:
// public class PlayerInfo { public string Name; public int AllianceIndex; }

public class PlayersService
{
    // This list holds our current players in memory.
    private readonly List<PlayerInfo> _players = new List<PlayerInfo>(); // In-memory players list

    // Raised whenever players are added/removed/renamed or alliances change.
    public event Action OnChanged;                                      // Event for UI / other systems

    // Small serializable DTO for saving a single player to JSON.
    [Serializable]
    private class PlayerData
    {
        public string name;                                             // Saved player name
        public int allianceIndex;                                       // Saved alliance index
    }

    // Wrapper for a list of players, because JsonUtility likes a single root object.
    [Serializable]
    private class PlayersSaveData
    {
        public List<PlayerData> players = new List<PlayerData>();       // List of saved players
    }

    // Returns the full list of players as a read-only view.
    public IReadOnlyList<PlayerInfo> GetAll()
    {
        return _players;                                                // Let callers read but not modify directly
    }

    // Computes the path of our save file in a cross-platform way.
    private string GetSavePath()
    {
        // Application.persistentDataPath is different per platform, but always valid.
        // We just drop a simple JSON file called "players.json" in there.
        return Path.Combine(Application.persistentDataPath, "players.json"); // Full path for save file
    }

    // Public entry point: try to load from disk; if that fails, create default players.
    public void LoadOrEnsureDefaults()
    {
        // Try to load players from the JSON file.
        bool loaded = LoadFromDisk();                                  // Attempt to read saved players

        if (!loaded)                                                   // If loading failed or file absent
        {
            EnsureDefaults();                                          //   -> create Player1 and Player2
            SaveToDisk();                                              //   -> immediately save them
        }
    }

    // Old behaviour: ensure at least Player1 and Player2 exist if list is empty.
    public void EnsureDefaults()
    {
        if (_players.Count > 0)                                        // If we already have players
            return;                                                    //   -> do nothing

        // Add two default players with alliances 0 and 1.
        _players.Add(new PlayerInfo { Name = "Player1", AllianceIndex = 0 }); // First default player
        _players.Add(new PlayerInfo { Name = "Player2", AllianceIndex = 1 }); // Second default player

        OnChanged?.Invoke();                                           // Notify listeners that players changed
    }

    // Add a new player with a name and alliance.
    public void AddPlayer(string name, int allianceIndex)
    {
        // If no name supplied, generate a simple default like "Player3".
        if (string.IsNullOrWhiteSpace(name))                           // If provided name is blank
        {
            name = "Player" + (_players.Count + 1);                    //   -> generate a simple default name
        }

        // Create the new player info object.
        var p = new PlayerInfo                                         // Allocate a new PlayerInfo
        {
            Name = name,                                               // Store the name
            AllianceIndex = allianceIndex                              // Store the alliance index
        };

        _players.Add(p);                                               // Add to the in-memory list
        OnChanged?.Invoke();                                           // Notify listeners
        SaveToDisk();                                                  // Persist updated list to disk
    }

    // Remove a player at a specific index.
    public void RemovePlayerAt(int index)
    {
        if (index < 0 || index >= _players.Count)                      // If index is out of range
            return;                                                    //   -> do nothing

        _players.RemoveAt(index);                                      // Remove the player from the list
        OnChanged?.Invoke();                                           // Notify listeners
        SaveToDisk();                                                  // Persist updated list to disk
    }

    // Rename an existing player.
    public void RenamePlayer(int index, string newName)
    {
        if (index < 0 || index >= _players.Count)                      // If index is out of range
            return;                                                    //   -> do nothing

        if (string.IsNullOrWhiteSpace(newName))                        // If the new name is blank
            return;                                                    //   -> ignore it to avoid empty names

        _players[index].Name = newName;                                // Store new name
        OnChanged?.Invoke();                                           // Notify listeners
        SaveToDisk();                                                  // Persist updated list to disk
    }

    // Change which alliance a player belongs to.
    public void SetPlayerAlliance(int index, int allianceIndex, int maxAlliances)
    {
        if (index < 0 || index >= _players.Count)                      // If index is out of range
            return;                                                    //   -> do nothing

        // Clamp alliance index into [0, maxAlliances-1] just like you already do.
        if (maxAlliances <= 0) maxAlliances = 1;                       // Safety: at least one alliance
        if (allianceIndex < 0) allianceIndex = 0;                      // Clamp lower bound
        if (allianceIndex >= maxAlliances) allianceIndex = maxAlliances - 1; // Clamp upper bound

        _players[index].AllianceIndex = allianceIndex;                 // Store new alliance index
        OnChanged?.Invoke();                                           // Notify listeners
        SaveToDisk();                                                  // Persist updated list to disk
    }

    // Try to load players from the JSON file.
    private bool LoadFromDisk()
    {
        string path = GetSavePath();                                   // Compute save file path

        if (!File.Exists(path))                                       // If the file does not exist
        {
            return false;                                              //   -> nothing to load
        }

        try
        {
            string json = File.ReadAllText(path);                      // Read full JSON text from file
            var data = JsonUtility.FromJson<PlayersSaveData>(json);    // Deserialize JSON into our save object

            _players.Clear();                                          // Clear current in-memory players

            if (data != null && data.players != null)                  // If we got valid data
            {
                foreach (var pd in data.players)                       //   -> loop over each saved player
                {
                    var p = new PlayerInfo                             //      Create a new PlayerInfo
                    {
                        Name = pd.name,                                //      Copy name from saved data
                        AllianceIndex = pd.allianceIndex               //      Copy alliance index
                    };
                    _players.Add(p);                                   //      Add to in-memory list
                }
            }

            bool hasAny = _players.Count > 0;                          // Check if we actually loaded anything
            if (hasAny) OnChanged?.Invoke();                           // If yes, notify listeners
            return hasAny;                                             // Return true only if list non-empty
        }
        catch (Exception e)
        {
            Debug.LogWarning("PlayersService.LoadFromDisk failed: " + e.Message); // Log a simple warning
            return false;                                              // On any error, report failure
        }
    }

    // Save current players list to the JSON file.
    private void SaveToDisk()
    {
        try
        {
            var data = new PlayersSaveData();                          // Create container for save
            data.players = new List<PlayerData>();                     // Allocate list for player entries

            foreach (var p in _players)                                // Loop through each in-memory player
            {
                var pd = new PlayerData                                //   Create a PlayerData record
                {
                    name = p.Name,                                     //   Copy name
                    allianceIndex = p.AllianceIndex                    //   Copy alliance index
                };
                data.players.Add(pd);                                  //   Add to save list
            }

            string json = JsonUtility.ToJson(data, true);              // Convert save data to pretty JSON
            string path = GetSavePath();                               // Compute save file path
            File.WriteAllText(path, json);                             // Write JSON text to disk
        }
        catch (Exception e)
        {
            Debug.LogWarning("PlayersService.SaveToDisk failed: " + e.Message); // Log warning on failure
        }
    }
}
