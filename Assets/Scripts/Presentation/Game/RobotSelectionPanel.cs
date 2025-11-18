// RobotSelectionPanel.cs — selection + video + motors on/off on select/deselect.
// Starts with no selection. Exposes CurrentRobotId and SelectionChanged.

using System;                                            // Bring in basic system types (Action, etc.)
using System.Collections.Generic;                        // Bring in List<T> and HashSet<T>
using TMPro;                                             // Bring in TextMeshProUGUI
using UnityEngine;                                       // Bring in MonoBehaviour and Unity basics
using UnityEngine.UI;                                    // Bring in Button

public class RobotSelectionPanel : MonoBehaviour
{
    [Header("Buttons")]                                  // Header label for button fields in the inspector
    [SerializeField] private Button prevButton;          // Button to move to the previous robot
    [SerializeField] private Button nextButton;          // Button to move to the next robot
    [SerializeField] private Button clearButton;         // Button to clear the current selection

    [Header("Info Labels")]                              // Header label for info label fields
    [SerializeField] private TextMeshProUGUI nameLabel;  // Label showing the robot name / callsign
    [SerializeField] private TextMeshProUGUI ipLabel;    // Label showing the robot IP address
    [SerializeField] private TextMeshProUGUI playerLabel;// Label showing which player owns the robot
    [SerializeField] private TextMeshProUGUI allianceLabel; // Label showing alliance (placeholder)
    [SerializeField] private TextMeshProUGUI clientLabel;   // Label showing client info (placeholder)

    [Header("Video")]                                    // Header label for video-related fields
    [SerializeField] private ESP32VideoReceiver video;   // Reference to the shared video receiver

    private IRobotDirectory _dir;                        // Reference to the robot directory service
    private RobotWebSocketServer _ws;                    // Reference to the WebSocket server for commands
    private readonly List<RobotInfo> _list = new();      // Local list of robots for indexing / cycling
    private int _index = -1;                             // Current index in _list, -1 means "no selection"
    private string _selectedRobotId;                     // Id of the currently selected robot (or null)

    // NEW: set of robot IDs that are allowed for the current player/turn (null => no filter).
    private HashSet<string> _allowedSet;                 // When null or empty => all robots are allowed

    public string CurrentRobotId => _selectedRobotId;    // Public getter so other panels can read selection

    public event Action<string> SelectionChanged;        // Event raised whenever the selection changes

    private void Awake()
    {
        _dir = ServiceLocator.RobotDirectory;            // Get the global robot directory
        _ws = ServiceLocator.RobotServer;                // Get the global websocket server
        if (video == null)                               // If video reference not set in inspector
            video = ESP32VideoReceiver.Instance;         //   -> fallback to the singleton instance
    }

    private void OnEnable()
    {
        WireButtons();                                   // Hook up button click handlers
        SubscribeDirectory();                            // Subscribe to directory change events
        RebuildList();                                   // Populate local robot list from directory

        _index = -1;                                     // Start with no selection
        _selectedRobotId = null;                         // Clear selected robot id
        if (video) video.ClearActiveRobot();             // Tell video receiver to clear active robot
        RefreshUI();                                     // Update labels to reflect "no selection"
        SelectionChanged?.Invoke(null);                  // Notify listeners that selection is now null
    }

    private void OnDisable()
    {
        UnsubscribeDirectory();                          // Detach from directory events when disabled
    }

    private void WireButtons()
    {
        if (prevButton != null)                          // If a "previous" button is assigned
        {
            prevButton.onClick.RemoveAllListeners();     //   -> clear any existing listeners
            prevButton.onClick.AddListener(Prev);        //   -> hook up the Prev() method
        }

        if (nextButton != null)                          // If a "next" button is assigned
        {
            nextButton.onClick.RemoveAllListeners();     //   -> clear any existing listeners
            nextButton.onClick.AddListener(Next);        //   -> hook up the Next() method
        }

        if (clearButton != null)                         // If a "clear selection" button is assigned
        {
            clearButton.onClick.RemoveAllListeners();    //   -> clear existing listeners
            clearButton.onClick.AddListener(ClearSelection); // -> hook up the ClearSelection() method
        }
    }

    private void SubscribeDirectory()
    {
        if (_dir == null) return;                        // If no directory available, nothing to subscribe to

        _dir.OnRobotAdded += OnRobotAdded;             // Subscribe to robot added event
        _dir.OnRobotUpdated += OnRobotUpdated;           // Subscribe to robot updated event
        _dir.OnRobotRemoved += OnRobotRemoved;           // Subscribe to robot removed event
    }

    private void UnsubscribeDirectory()
    {
        if (_dir == null) return;                        // If no directory available, nothing to unsubscribe

        _dir.OnRobotAdded -= OnRobotAdded;             // Unsubscribe from robot added event
        _dir.OnRobotUpdated -= OnRobotUpdated;           // Unsubscribe from robot updated event
        _dir.OnRobotRemoved -= OnRobotRemoved;           // Unsubscribe from robot removed event
    }

    private void RebuildList()
    {
        _list.Clear();                                   // Remove all existing entries in local list
        if (_dir != null)                                // If we have a directory
            _list.AddRange(_dir.GetAll());               //   -> copy all robots into the local list

        ClampIndexAfterListChange();                     // Ensure the index is still valid after rebuild
    }

    private void ClampIndexAfterListChange()
    {
        if (_list.Count == 0)                            // If there are no robots at all
        {
            _index = -1;                                 //   -> ensure index is "no selection"
            return;                                      //   -> and exit
        }

        if (_index >= _list.Count)                       // If index is past the last entry
            _index = -1;                                 //   -> reset to "no selection"

        if (_index < -1)                                 // If index somehow went below -1
            _index = -1;                                 //   -> clamp it up to -1
    }

    private void OnRobotAdded(RobotInfo r)
    {
        _list.Add(r);                                    // Add the new robot to the local list
        ClampIndexAfterListChange();                     // Make sure index is still valid
        RefreshUI();                                     // Update labels in case we were empty before
    }

    private void OnRobotUpdated(RobotInfo r)
    {
        for (int i = 0; i < _list.Count; i++)            // Loop through existing robots
        {
            if (_list[i].RobotId == r.RobotId)           //   -> if we find the matching id
            {
                _list[i] = r;                            //   -> replace with updated info
                break;                                   //   -> and stop searching
            }
        }
        RefreshUI();                                     // Robot details may have changed, so refresh labels
    }

    private void OnRobotRemoved(string robotId)
    {
        int idx = _list.FindIndex(x => x.RobotId == robotId); // Find index of robot with this id
        if (idx >= 0)                                   // If we actually found it
            _list.RemoveAt(idx);                       //   -> remove it from the local list

        if (_selectedRobotId == robotId)               // If the removed robot was currently selected
        {
            _index = -1;                               //   -> clear index
            _selectedRobotId = null;                   //   -> clear selected id
            if (video) video.ClearActiveRobot();       //   -> tell video receiver to clear active robot
        }

        ClampIndexAfterListChange();                   // Ensure index is still within range
        RefreshUI();                                   // Update labels to reflect removal

        _ws = ServiceLocator.RobotServer;              // Refresh websocket reference in case it changed
        _ws?.SendMotorsOff(robotId);                   // As a safety measure, turn off motors on removed robot

        SelectionChanged?.Invoke(null);                // Notify listeners that selection may now be null
    }

    // NEW: helper to check if a robot id is allowed under the current filter.
    private bool IsAllowed(string robotId)
    {
        if (string.IsNullOrEmpty(robotId))             // If id is null/empty
            return false;                              //   -> treat as not allowed

        if (_allowedSet == null || _allowedSet.Count == 0)
            return true;                               // If there is no filter, all robots are allowed

        return _allowedSet.Contains(robotId);          // Otherwise only ids in the set are allowed
    }

    private void Prev()
    {
        if (_list.Count == 0)                          // If there are no robots to select
            return;                                    //   -> nothing to do

        // We want to find the previous robot that is allowed.
        int startIndex = _index;                       // Remember the starting index
        if (startIndex < 0)                            // If we currently have no selection
            startIndex = 0;                            //   -> start searching from the first entry

        for (int i = 0; i < _list.Count; i++)          // Loop at most once through the list
        {
            int next = (startIndex - 1 - i + _list.Count) % _list.Count; // Step backwards with wrap-around
            string id = _list[next].RobotId;           //   -> get the id at that position

            if (IsAllowed(id))                         // If this robot is allowed under filter
            {
                SelectIndex(next);                     //   -> select it
                return;                                //   -> and stop searching
            }
        }

        // If we got here, no robots are allowed under the current filter.
        ClearSelection();                              //   -> clear any current selection
    }

    private void Next()
    {
        if (_list.Count == 0)                          // If there are no robots to select
            return;                                    //   -> nothing to do

        // We want to find the next robot that is allowed.
        int startIndex = _index;                       // Remember the starting index
        if (startIndex < 0)                            // If we currently have no selection
            startIndex = 0;                            //   -> start searching from the first entry

        for (int i = 0; i < _list.Count; i++)          // Loop at most once through the list
        {
            int next = (startIndex + 1 + i) % _list.Count; // Step forwards with wrap-around
            string id = _list[next].RobotId;           //   -> get the id at that position

            if (IsAllowed(id))                         // If this robot is allowed under filter
            {
                SelectIndex(next);                     //   -> select it
                return;                                //   -> and stop searching
            }
        }

        // If we got here, no robots are allowed under the current filter.
        ClearSelection();                              //   -> clear any current selection
    }

    private void ClearSelection()
    {
        _ws = ServiceLocator.RobotServer;              // Refresh websocket reference

        if (!string.IsNullOrEmpty(_selectedRobotId))   // If we previously had a selected robot
        {
            _ws?.SendStreamOff(_selectedRobotId);      //   -> stop its video stream
            _ws?.SendMotorsOff(_selectedRobotId);      //   -> and stop its motors
        }

        _index = -1;                                   // Set index to "no selection"
        _selectedRobotId = null;                       // Clear selected robot id

        if (video) video.ClearActiveRobot();           // Tell video receiver to clear active robot
        RefreshUI();                                   // Update labels to show "no selection"
        SelectionChanged?.Invoke(null);                // Notify listeners that selection is now null
    }

    private void SelectIndex(int newIndex)
    {
        if (newIndex < 0 || newIndex >= _list.Count)   // If given index is out of range
        {
            ClearSelection();                          //   -> treat as clear request
            return;                                    //   -> and exit
        }

        var r = _list[newIndex];                       // Get the robot at the requested index

        if (!IsAllowed(r.RobotId))                     // If this robot is not allowed by filter
        {
            ClearSelection();                          //   -> clear selection instead
            return;                                    //   -> and exit
        }

        _ws = ServiceLocator.RobotServer;              // Refresh websocket reference

        if (!string.IsNullOrEmpty(_selectedRobotId))   // If there was a previously selected robot
        {
            _ws?.SendStreamOff(_selectedRobotId);      //   -> stop its video stream
            _ws?.SendMotorsOff(_selectedRobotId);      //   -> and stop its motors
        }

        _index = newIndex;                             // Store the new index
        _selectedRobotId = r.RobotId;                  // Store the new selected robot id

        if (video)                                     // If we have a video receiver reference
            video.SetActiveRobot(_selectedRobotId);    //   -> tell it to display this robot

        _ws?.SendStreamOn(_selectedRobotId);           // Ask robot to start sending video
        _ws?.SendMotorsOn(_selectedRobotId);           // Ask robot to enable motors

        RefreshUI();                                   // Update labels with new robot info
        SelectionChanged?.Invoke(_selectedRobotId);    // Notify listeners about new selection
    }

    private void RefreshUI()
    {
        if (_index < 0 || _index >= _list.Count)       // If there is no valid selection
        {
            if (nameLabel) nameLabel.text = "(no robot selected)"; // Show placeholder name
            if (ipLabel) ipLabel.text = "";           // Clear IP label
            if (playerLabel) playerLabel.text = "Player: —";      // Show placeholder player
            if (allianceLabel) allianceLabel.text = "Alliance: —"; // Show placeholder alliance
            if (clientLabel) clientLabel.text = "Client: —";      // Show placeholder client
            return;                                   // Exit after clearing placeholders
        }

        var r = _list[_index];                        // Get the currently selected robot info
        string display = string.IsNullOrEmpty(r.Callsign)
            ? r.RobotId                               // If no callsign, fall back to robot id
            : r.Callsign;                             // Otherwise show callsign

        if (nameLabel) nameLabel.text = display;      // Update name label
        if (ipLabel)                                  // If IP label is assigned
            ipLabel.text = string.IsNullOrEmpty(r.Ip) //   -> if ip is empty
                ? "(no ip)"                           //      show placeholder
                : r.Ip;                               //      otherwise show IP string

        // For player label, treat null/empty as "Unassigned" for now.
        if (playerLabel)
            playerLabel.text = "Player: " +
                (string.IsNullOrEmpty(r.AssignedPlayer) ? "Unassigned" : r.AssignedPlayer);

        if (allianceLabel) allianceLabel.text = "Alliance: TBD"; // Alliance info not wired yet
        if (clientLabel) clientLabel.text = "Client:   TBD"; // Client info not wired yet
    }

    // NEW: called by GamePanelPresenter to restrict which robots are selectable this turn.
    public void SetAllowedFilter(IReadOnlyList<string> allowedIds)
    {
        if (allowedIds == null || allowedIds.Count == 0) // If no list or empty list is provided
        {
            _allowedSet = null;                          //   -> treat as "no filter, all robots allowed"
            return;                                      //   -> and exit
        }

        if (_allowedSet == null)                         // If we do not yet have a HashSet
            _allowedSet = new HashSet<string>();         //   -> allocate one

        _allowedSet.Clear();                             // Clear previous contents

        for (int i = 0; i < allowedIds.Count; i++)       // Loop through provided allowed ids
        {
            string id = allowedIds[i];                   //   -> read current id
            if (!string.IsNullOrEmpty(id))               //   -> ignore null/empty ids
                _allowedSet.Add(id);                     //   -> add valid id to the set
        }
    }

    // NEW: called after SetAllowedFilter to fix selection if it is now invalid.
    public void EnsureValidSelectionAfterFilter(bool autoSelectFirstAllowed)
    {
        // If we currently have a selected robot but it is no longer allowed, clear it.
        if (!string.IsNullOrEmpty(_selectedRobotId)      // If we have some selection
            && !IsAllowed(_selectedRobotId))             //   -> and it is not allowed anymore
        {
            ClearSelection();                            //   -> clear selection (and notify listeners)
        }

        // If we have no selection and caller wants auto-select behaviour, choose the first allowed.
        if (autoSelectFirstAllowed                      // If caller requested auto-selection
            && string.IsNullOrEmpty(_selectedRobotId))   //   -> and we currently have no selection
        {
            for (int i = 0; i < _list.Count; i++)        // Loop through all robots
            {
                string id = _list[i].RobotId;            //   -> get id at this index
                if (IsAllowed(id))                       //   -> if this robot is allowed under filter
                {
                    SelectIndex(i);                      //      select it
                    return;                              //      and stop searching
                }
            }

            // If we reach here, no robots are allowed under the current filter.
            // Selection is already cleared, so nothing more to do.
        }
    }
}
