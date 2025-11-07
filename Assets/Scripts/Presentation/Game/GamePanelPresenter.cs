// GamePanelPresenter.cs — shows the in-game robot list and switches the active camera on click
using System;                                   // For basic types
using System.Collections.Generic;               // For List<>
using TMPro;                                    // For TextMeshProUGUI
using UnityEngine;                              // For MonoBehaviour, GameObject, Debug
using UnityEngine.UI;                           // For Button

public class GamePanelPresenter : MonoBehaviour // Attach this to your Playing panel root
{
    [Header("UI wiring")]                        // Inspector header
    [SerializeField] private RectTransform content;  // Parent under a VerticalLayoutGroup
    [SerializeField] private GameObject rowPrefab;   // Prefab with "Name"(TMP) + "FlashButton"(Button)

    [Header("Video")]
    [SerializeField] private ESP32VideoReceiver video; // Drag the ESP32VideoReceiver in here (or leave null and it will find Instance)

    private GameService _game;                   // Reference to GameService for state
    private RobotWebSocketServer _ws;            // Reference to WS server to send commands
    private string _activeRobotId;               // Tracks which robot is currently “on camera”

    private void OnEnable()                      // Called when panel becomes active
    {
        _game = ServiceLocator.Game;             // Get the game service
        _ws = ServiceLocator.RobotServer;      // Get the running WS server (set at start)

        if (video == null) video = ESP32VideoReceiver.Instance; // Fill from singleton if not wired

        if (_game == null || _game.State == null) // If no game is running yet
        {
            Debug.LogWarning("[GamePanel] No game state yet."); // Warn for awareness
            return;                               // Nothing to render
        }

        if (content == null || rowPrefab == null) // Ensure UI is wired
        {
            Debug.LogError("[GamePanel] Missing content or rowPrefab."); // Log error
            return;                               // Bail out
        }

        Rebuild();                                // Build the list for the current state
    }

    private void OnDisable()                      // Called when panel is hidden
    {
        Clear();                                  // Clean up child rows
    }

    private void Rebuild()                        // Build rows from GameService.State.Robots
    {
        Clear();                                  // Start fresh

        List<RobotInfo> robots = _game.State.Robots; // Get the snapshot list
        for (int i = 0; i < robots.Count; i++)       // Loop through each robot
        {
            var r = robots[i];                       // Current robot
            var row = Instantiate(rowPrefab, content, false); // Make a new row under the content
            row.name = r.RobotId;                    // Name the row with the robot id

            var nameText = row.transform.Find("Name").GetComponent<TextMeshProUGUI>(); // Find label
            var button = row.transform.Find("FlashButton").GetComponent<Button>();    // Find button

            nameText.text = string.IsNullOrEmpty(r.Callsign) ? r.RobotId : r.Callsign;  // Show name/id

            button.onClick.RemoveAllListeners();     // Clear previous listeners (safety)
            button.onClick.AddListener(() =>         // Add a click handler
            {
                if (_ws == null)                     // Ensure WS server is available
                {
                    Debug.LogWarning("[GamePanel] WS server not ready."); // Warn if missing
                    return;                          // Do nothing
                }

                // 1) If we were viewing another robot, tell it to stop streaming
                if (!string.IsNullOrEmpty(_activeRobotId) && _activeRobotId != r.RobotId) // Switching sources?
                {
                    _ws.SendStreamOff(_activeRobotId);            // Stop the previous camera (best effort)
                }

                // 2) Switch the UI receiver to this robot
                _activeRobotId = r.RobotId;                       // Remember active robot id
                if (video != null) video.SetActiveRobot(_activeRobotId); // Tell the receiver which frames to show

                // 3) Send the flash command (your ESP32 treats this as “flash + start stream”)
                bool ok = _ws.SendFlashCommand(r.RobotId, 48, 2000); // Pin 48, 2 seconds
                if (!ok) Debug.LogWarning("[GamePanel] Send failed (robot offline?)");   // Warn if send failed
            });
        }
    }

    private void Clear()                           // Destroy all generated rows under 'content'
    {
        for (int i = content.childCount - 1; i >= 0; i--) // Loop backwards for safety
        {
            Destroy(content.GetChild(i).gameObject);      // Destroy each row GameObject
        }
    }
}
