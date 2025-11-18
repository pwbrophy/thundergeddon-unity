using System.Collections.Generic;                 // For List<T>
using TMPro;                                      // For TextMeshProUGUI
using UnityEngine;                                // For MonoBehaviour, Debug
using UnityEngine.UI;                             // For Button

/// <summary>
/// GamePanelPresenter
/// - Wires the Playing screen to TurnManager.
/// - Shows "Alliance N • PlayerName".
/// - Filters RobotSelectionPanel to only the current player's robots.
/// - End Turn steps player→player→next alliance.
/// </summary>
public class GamePanelPresenter : MonoBehaviour
{
    [Header("UI")]                                                   // Inspector wiring
    [SerializeField] private TextMeshProUGUI turnLabel;              // "Alliance N • PlayerName"
    [SerializeField] private Button endTurnButton;                   // Ends current player's turn
    [SerializeField] private RobotSelectionPanel selectionPanel;     // Filters by allowed robots

    // References to shared services.
    private IRobotDirectory _dir;                                    // Robot directory
    private TurnManager _turns;                                      // Turn controller

    private void OnEnable()
    {
        // Resolve services.
        _dir = ServiceLocator.RobotDirectory;                        // Directory
        if (_dir == null) { Debug.LogError("[GamePanel] RobotDirectory missing"); return; } // Safety

        // Create the turn manager.
        _turns = new TurnManager();                                  // New manager

        // IMPORTANT: Subscribe BEFORE Initialize(), so we catch the initial PublishTurn().
        _turns.TurnChanged += OnTurnChanged;                         // Listen first

        // Now initialize (this will call PublishTurn() and we will receive it).
        _turns.Initialize(_dir);                                     // Wire + build + publish

        // Wire the end-turn button to advance the turn.
        if (endTurnButton) endTurnButton.onClick.AddListener(OnEndTurnClicked); // Hook
    }

    private void OnDisable()
    {
        // Unwire events to avoid leaks.
        if (_turns != null) _turns.TurnChanged -= OnTurnChanged;     // Unhook
        if (endTurnButton) endTurnButton.onClick.RemoveListener(OnEndTurnClicked); // Unhook
    }

    // Update UI when the current turn changes.
    private void OnTurnChanged(int allianceIndex, int playerIndexInAlliance, List<string> allowedIds)
    {
        // Compute a friendly label: "Alliance N • PlayerName" (or "—" if no players in that alliance).
        string playerName = _turns.CurrentPlayerName ?? "—";         // Name or dash
        if (turnLabel) turnLabel.text = $"Alliance {allianceIndex + 1} • {playerName}"; // Label

        // Apply the allowed filter to the selection panel.
        if (selectionPanel) selectionPanel.SetAllowedFilter(allowedIds); // Filter list

        // Ensure the current selection is valid for this turn.
        // - If selected robot is NOT in allowed list, we auto-deselect (and stop cam/motors).
        // - Then we auto-select the first allowed robot (if any) to keep things snappy.
        if (selectionPanel) selectionPanel.EnsureValidSelectionAfterFilter(autoSelectFirstAllowed: true); // Fix selection
    }

    // Button: advance to next player (and alliance when appropriate).
    private void OnEndTurnClicked()
    {
        // Ask TurnManager to step the turn (player→player→next alliance).
        _turns?.EndTurn();                                           // Advance
    }
}
