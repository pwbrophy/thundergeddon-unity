using System;                                     // For Action
using System.Collections.Generic;                 // For List
using TMPro;                                      // For TMP_Dropdown, TMP_InputField
using UnityEngine;                                // For MonoBehaviour, GameObject, Mathf
using UnityEngine.UI;                             // For Button

// PlayersEditorPanel — minimal UI to manage players + their alliances.
// IMPORTANT: We suppress UI callbacks while we programmatically update controls to avoid recursion.
public class PlayersEditorPanel : MonoBehaviour
{
    [Header("Root Visibility")]                            // Group to toggle panel
    [SerializeField] private GameObject root;               // Root container of this panel

    [Header("Controls")]                                    // UI references (assign in Inspector)
    [SerializeField] private TMP_Dropdown playersDropdown;  // Combo box of players
    [SerializeField] private Button addButton;              // Add player
    [SerializeField] private Button removeButton;           // Remove selected player
    [SerializeField] private TMP_InputField nameField;      // Live-edit player name
    [SerializeField] private TMP_Dropdown allianceDropdown; // Combo box of alliances (numeric for now)

    // Service that stores the players list.
    private PlayersService _players;

    // Currently selected player index in the service list.
    private int _currentIndex = -1;

    // Flag to suppress event handlers while we update UI programmatically.
    private bool _updating = false;

    // Unity: called when this panel is enabled.
    private void OnEnable()
    {
        // If no explicit root was assigned in the inspector, use this GameObject as the root container.
        if (!root) root = this.gameObject;                                  // Fallback for root

        // Acquire the shared PlayersService (normally created in AppBootstrap).
        _players = ServiceLocator.Players;                                  // Cache global instance

        // Safety net: if the service is still missing, create it and register it once.
        if (_players == null)                                              // Did nothing create it yet?
        {
            Debug.LogWarning("[PlayersEditorPanel] ServiceLocator.Players was null; creating a new PlayersService."); // Warn
            _players = new PlayersService();                                // Allocate fresh service
            ServiceLocator.Players = _players;                              // Register globally for everyone else
        }

        // Make sure there is at least a basic set of players for the UI to edit.
        _players.EnsureDefaults();                                         // No-op if already populated

        // Subscribe to service change events so the UI updates when players change.
        _players.OnChanged -= HandlePlayersChanged;                         // Avoid double-subscribe
        _players.OnChanged += HandlePlayersChanged;                         // React to changes in players list

        // Wire up UI event handlers.
        if (addButton) addButton.onClick.AddListener(OnAddPlayerClicked);                 // Add player
        if (removeButton) removeButton.onClick.AddListener(OnRemovePlayerClicked);           // Remove player
        if (playersDropdown) playersDropdown.onValueChanged.AddListener(OnPlayerDropdownChanged); // Change selection
        if (nameField) nameField.onValueChanged.AddListener(OnNameEditedLive);            // Live rename
        if (allianceDropdown) allianceDropdown.onValueChanged.AddListener(OnAllianceChanged);   // Change alliance

        // Build initial UI once (suppress callbacks while we fill controls).
        _updating = true;                                                  // Begin suppression
        RebuildAllUI();                                                    // Fill dropdowns + text fields
        _updating = false;                                                 // End suppression
    }


    // Unity: called when this panel is disabled.
    private void OnDisable()
    {
        // Unsubscribe from service events to avoid leaks.
        if (_players != null) _players.OnChanged -= HandlePlayersChanged;                // Unhook

        // Unwire UI events.
        if (addButton) addButton.onClick.RemoveListener(OnAddPlayerClicked);         // Unhook
        if (removeButton) removeButton.onClick.RemoveListener(OnRemovePlayerClicked);   // Unhook
        if (playersDropdown) playersDropdown.onValueChanged.RemoveListener(OnPlayerDropdownChanged); // Unhook
        if (nameField) nameField.onValueChanged.RemoveListener(OnNameEditedLive);    // Unhook
        if (allianceDropdown) allianceDropdown.onValueChanged.RemoveListener(OnAllianceChanged);     // Unhook
    }

    // Handle any change from PlayersService (add/remove/rename/set-alliance).
    private void HandlePlayersChanged()
    {
        // Suppress callbacks while we rebuild UI in response to model changes.
        _updating = true;                                           // Begin suppression
        RebuildPlayersDropdown();                                   // Refresh players list
        RebuildAllianceDropdown();                                  // Refresh alliance list
        RefreshFieldsFromSelection();                               // Sync text + alliance value
        _updating = false;                                          // End suppression
    }

    // Add a new player and select it.
    // Called when the "Add Player" button is clicked.
    private void OnAddPlayerClicked()
    {
        // If we are already updating the UI programmatically, ignore this click.
        if (_updating) return;                                          // Prevent re-entrancy loops

        // Decide what alliance the new player should start in.
        // For now, we just default new players into alliance 0.
        int defaultAllianceIndex = 0;                                   // Start new players in alliance 0

        // Ask the PlayersService to add a new player.
        // Passing null for the name tells PlayersService to auto-generate a name like "Player3".
        _players.AddPlayer(null, defaultAllianceIndex);                 // Add a new auto-named player

        // After adding, the new player will be at the end of the list.
        var players = _players.GetAll();                                // Get the current read-only players list
        _currentIndex = players.Count - 1;                              // Select the last player (the one we just added)

        // Rebuild the UI to include the new player without causing extra callbacks.
        _updating = true;                                               // Begin suppressing UI change handlers
        RebuildPlayersDropdown();                                       // Rebuild the dropdown contents

        // If the dropdown exists and index is valid, select the new player visually.
        if (playersDropdown && _currentIndex >= 0)                      // Ensure dropdown and index are valid
            playersDropdown.SetValueWithoutNotify(_currentIndex);       // Change selection without firing OnValueChanged

        // Update the name field and alliance dropdown to reflect the newly selected player.
        RefreshFieldsFromSelection();                                   // Sync text field and alliance dropdown

        // Re-enable normal change handling.
        _updating = false;                                              // End suppression of UI callbacks
    }

    // Remove the selected player.
    private void OnRemovePlayerClicked()
    {
        // If we are already updating UI, ignore.
        if (_updating) return;                                      // Guard
        if (_currentIndex < 0) return;                              // Nothing selected

        // Remove from service (it will fire OnChanged).
        _players.RemovePlayerAt(_currentIndex);                     // Remove selection

        // Choose a valid new selection.
        var all = _players.GetAll();                                // Snapshot list
        if (all.Count == 0) _currentIndex = -1;                     // Nothing remains
        else _currentIndex = Mathf.Clamp(_currentIndex, 0, all.Count - 1); // Clamp index

        // Reflect selection in UI without triggering events.
        _updating = true;                                           // Begin suppression
        RebuildPlayersDropdown();                                   // Refresh names
        if (playersDropdown && _currentIndex >= 0)
            playersDropdown.SetValueWithoutNotify(_currentIndex);   // Select (no notify)
        RefreshFieldsFromSelection();                               // Update fields
        _updating = false;                                          // End suppression
    }

    // Player dropdown changed — switch selected index.
    private void OnPlayerDropdownChanged(int newIndex)
    {
        // Ignore if we are suppressing programmatic updates.
        if (_updating) return;                                      // Guard

        // Store the new selection.
        _currentIndex = newIndex;                                   // Track selection

        // Refresh the editable fields to match that player (without re-triggering).
        _updating = true;                                           // Begin suppression
        RefreshFieldsFromSelection();                               // Update text + alliance
        _updating = false;                                          // End suppression
    }

    // Name edited live — rename player and keep the dropdown caption in sync.
    private void OnNameEditedLive(string newText)
    {
        // Ignore if we are suppressing or nothing selected.
        if (_updating) return;                                      // Guard
        if (_currentIndex < 0) return;                              // No selection

        // Rename in service (fires OnChanged → we rebuild UI under suppression).
        _players.RenamePlayer(_currentIndex, newText);              // Rename selected
    }

    // Called when the alliance dropdown value changes.
    private void OnAllianceChanged(int newAllianceIndex)
    {
        // Ignore if we are in the middle of a programmatic UI update.
        if (_updating) return;                                      // Guard against re-entrancy

        // Ignore if no player is currently selected.
        if (_currentIndex < 0) return;                              // Nothing selected to update

        // Work out how many alliances currently exist in the game.
        // We ask GameService.State for this and fall back to 2 if it is missing.
        int maxAlliances = Mathf.Max(1,                             // At least one alliance must exist
            ServiceLocator.Game?.State?.Alliances ?? 2);            // Read configured alliances or default to 2

        // Apply the change through PlayersService.
        // PlayersService will clamp the alliance index into a valid range and save it.
        _players.SetPlayerAlliance(_currentIndex,                   // Index of player to modify
                                   newAllianceIndex,                // Alliance chosen in dropdown
                                   maxAlliances);                   // Total number of alliances available

        // PlayersService will fire OnChanged, which you already handle to rebuild the UI.
    }

    // Build all UI elements from current state.
    private void RebuildAllUI()
    {
        // Rebuild alliances first, then players, then fields (all under suppression by caller).
        RebuildAllianceDropdown();                                  // Fill alliance numbers
        RebuildPlayersDropdown();                                   // Fill player names

        // Choose a sensible default selection.
        var all = _players.GetAll();                                // Snapshot
        if (all.Count == 0) _currentIndex = -1;                     // Nothing to select
        else if (_currentIndex < 0) _currentIndex = 0;              // Pick first

        // Apply selection to UI WITHOUT NOTIFY to avoid loops.
        if (playersDropdown && _currentIndex >= 0)
            playersDropdown.SetValueWithoutNotify(_currentIndex);   // Select without notify

        // Refresh fields to reflect selection (use without-notify setters).
        RefreshFieldsFromSelection();                               // Set name + alliance
    }

    // Rebuild the players dropdown from service list (no notify here).
    private void RebuildPlayersDropdown()
    {
        // If control missing, nothing to do.
        if (!playersDropdown) return;

        // Snapshot players.
        var list = _players.GetAll();                               // Copy of players

        // Convert to TMP options using player names.
        var options = new List<TMP_Dropdown.OptionData>();          // Build new options
        for (int i = 0; i < list.Count; i++)                        // For each player
            options.Add(new TMP_Dropdown.OptionData(list[i].Name)); // Add name as label

        // Replace options and clamp selection (all under suppression by caller).
        playersDropdown.ClearOptions();                             // Wipe old
        playersDropdown.AddOptions(options);                        // Add new
        if (list.Count == 0) _currentIndex = -1;                    // Nothing to select
        else _currentIndex = Mathf.Clamp(_currentIndex, 0, list.Count - 1); // Clamp
        if (_currentIndex >= 0) playersDropdown.SetValueWithoutNotify(_currentIndex); // Select (no notify)
        playersDropdown.RefreshShownValue();                        // Refresh caption
    }

    // Rebuild the alliance dropdown using GameService.State.Alliances (no notify here).
    private void RebuildAllianceDropdown()
    {
        // If control missing, nothing to do.
        if (!allianceDropdown) return;

        // Read alliances count from GameService.State (fallback to 2).
        int alliances = Mathf.Max(1, ServiceLocator.Game?.State?.Alliances ?? 2); // How many alliances now

        // Build numeric options: "Alliance 1", "Alliance 2", ...
        var options = new List<TMP_Dropdown.OptionData>();          // New options list
        for (int i = 0; i < alliances; i++)                         // For each alliance index
            options.Add(new TMP_Dropdown.OptionData($"Alliance {i + 1}")); // Human-friendly

        // Replace options and refresh caption.
        allianceDropdown.ClearOptions();                            // Wipe old
        allianceDropdown.AddOptions(options);                       // Add new
        allianceDropdown.RefreshShownValue();                       // Refresh caption
    }

    // Update the editable fields to match the currently selected player (use without-notify setters).
    private void RefreshFieldsFromSelection()
    {
        // Snapshot players.
        var list = _players.GetAll();                               // Copy

        // If no selection, blank fields (without notify).
        if (_currentIndex < 0 || _currentIndex >= list.Count)
        {
            if (nameField) nameField.SetTextWithoutNotify("");      // Blank name
            if (allianceDropdown) allianceDropdown.SetValueWithoutNotify(0); // Default alliance
            return;                                                 // Done
        }

        // Get selected player.
        var p = list[_currentIndex];                                // Selected data

        // Sync name field without firing callbacks.
        if (nameField) nameField.SetTextWithoutNotify(p.Name);      // Show name

        // Clamp alliance to current GameService count and apply to dropdown without notify.
        int alliances = Mathf.Max(1, ServiceLocator.Game?.State?.Alliances ?? 2); // Current count
        int val = Mathf.Clamp(p.AllianceIndex, 0, alliances - 1);   // Clamp to valid
        if (allianceDropdown) allianceDropdown.SetValueWithoutNotify(val); // Sync dropdown
        if (allianceDropdown) allianceDropdown.RefreshShownValue(); // Refresh caption
    }

    // Helpers to show/hide this panel (optional convenience).
    public void Show() { if (root) root.SetActive(true); }          // Show panel
    public void Hide() { if (root) root.SetActive(false); }         // Hide panel
}
