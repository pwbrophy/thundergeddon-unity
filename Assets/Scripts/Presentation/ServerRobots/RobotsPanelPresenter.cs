using System;                          // Basic C# types like Action
using System.Collections.Generic;      // Lists
using UnityEngine;                     // Unity types (MonoBehaviour, GameObject)
using UnityEngine.UI;                  // UI Button
using TMPro;                           // TextMeshPro

// Attach this script to your "Server > Robots" panel.
// Drag the ScrollView's Content into "content" in the Inspector.
// Drag your row prefab into "rowPrefab".
// (Optional) Drag a RenamePopup into "renamePopup".
public class RobotsPanelPresenter : MonoBehaviour
{
    // === Inspector fields ===

    [Header("ScrollView wiring")]
    [SerializeField] private RectTransform content;   // Where the rows live under the ScrollView
    [SerializeField] private GameObject rowPrefab;    // Prefab for one robot row

    [Header("Optional: small modal to rename/assign")]
    [SerializeField] private RenamePopup renamePopup; // Can be null if you haven't made one yet

    // === Private fields ===

    private IRobotDirectory _dir;                     // The shared robot registry (from ServiceLocator)
    private bool _isSubscribed = false;               // Tracks if we subscribed to events already (safety)

    // Called automatically when this component becomes enabled/visible
    private void OnEnable()
    {
        // Grab the shared directory from the global shelf
        _dir = ServiceLocator.RobotDirectory;

        // Log which directory instance we are talking to (helps spot duplicate registries)
        // GetHashCode() just gives us a number to compare in the Console.
        Debug.Log($"[RobotsPanelPresenter:{name}] OnEnable - Using RobotDirectory hash={_dir?.GetHashCode()}");

        // If the directory is missing, we can't continue. Usually means AppBootstrap didn't run.
        if (_dir == null)
        {
            Debug.LogError("[RobotsPanelPresenter] RobotDirectory is null. Is AppBootstrap in the scene and enabled?");
            return; // Bail out early to avoid null errors
        }

        // (Safety) Make sure content and rowPrefab are assigned in the Inspector
        if (content == null)
        {
            Debug.LogError("[RobotsPanelPresenter] 'content' is not assigned in the Inspector.");
            return;
        }
        if (rowPrefab == null)
        {
            Debug.LogError("[RobotsPanelPresenter] 'rowPrefab' is not assigned in the Inspector.");
            return;
        }

        // Build the list from whatever is already in the directory
        RebuildFromDirectory();

        // Subscribe to directory events so the UI updates live when robots change
        // Guard with a flag so we don't double-subscribe if OnEnable runs again
        if (!_isSubscribed)
        {
            _dir.OnRobotAdded += HandleRobotAdded;
            _dir.OnRobotUpdated += HandleRobotUpdated;
            _dir.OnRobotRemoved += HandleRobotRemoved;
            _isSubscribed = true;
        }
    }

    // Called automatically when this component stops being enabled/visible
    private void OnDisable()
    {
        // If we had a directory and were subscribed, clean up event handlers
        if (_dir != null && _isSubscribed)
        {
            _dir.OnRobotAdded -= HandleRobotAdded;
            _dir.OnRobotUpdated -= HandleRobotUpdated;
            _dir.OnRobotRemoved -= HandleRobotRemoved;
            _isSubscribed = false;
        }
    }

    // Rebuild the whole list from the directory (used on first open)
    private void RebuildFromDirectory()
    {
        // 1) Clear any old rows. (Destroy is delayed until end-of-frame—so we won't reuse rows right now.)
        ClearAllRows();

        // 2) Get a snapshot of robots and sort them (optional).
        List<RobotInfo> robots = new List<RobotInfo>(_dir.GetAll());
        robots.Sort((a, b) => string.Compare(a.Callsign, b.Callsign, StringComparison.OrdinalIgnoreCase));

        // 3) Always create fresh rows during a full rebuild.
        for (int i = 0; i < robots.Count; i++)
        {
            CreateOrUpdateRow(robots[i], allowReuse: false);
        }

        Debug.Log($"[RobotsPanelPresenter:{name}] Rebuild - Rendered {robots.Count} robots.");
    }


    // Remove all child rows from the Content container
    private void ClearAllRows()
    {
        // Loop backwards when destroying children to be safe
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Transform child = content.GetChild(i);
            Destroy(child.gameObject);
        }
    }

    // === Event handlers from the directory ===

    // A robot was added to the directory (or first seen online)
    private void HandleRobotAdded(RobotInfo r)
    {
        // Create or update the row for this robot
        CreateOrUpdateRow(r);
    }

    // A robot's data changed (name/ip/player/online)
    private void HandleRobotUpdated(RobotInfo r)
    {
        // Create or update the row for this robot
        CreateOrUpdateRow(r);
    }

    // A robot was removed from the directory
    private void HandleRobotRemoved(string robotId)
    {
        // Try to find a row child whose name matches the robotId
        Transform row = content.Find(robotId);

        // If we found it, destroy it
        if (row != null)
        {
            Destroy(row.gameObject);
        }
    }

    // Create a row if it doesn't exist, or update it if it does
    private void CreateOrUpdateRow(RobotInfo r, bool allowReuse = true)
    {
        GameObject rowGO;

        if (allowReuse)
        {
            // Try to find an existing row by name.
            Transform existing = content.Find(r.RobotId);

            if (existing == null)
            {
                // No existing row → create one.
                rowGO = Instantiate(rowPrefab, content, false);
                rowGO.name = r.RobotId;
            }
            else
            {
                // Reuse the existing row.
                rowGO = existing.gameObject;
            }
        }
        else
        {
            // We are in a full rebuild right after ClearAllRows().
            // Do NOT attempt to reuse rows (they may still be pending-destroy this frame).
            rowGO = Instantiate(rowPrefab, content, false);
            rowGO.name = r.RobotId;
        }

        // ---- Fill UI fields (unchanged) ----
        TextMeshProUGUI nameText = rowGO.transform.Find("Name").GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI ipText = rowGO.transform.Find("Ip").GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI playerText = rowGO.transform.Find("Player").GetComponent<TextMeshProUGUI>();
        Button editButton = rowGO.transform.Find("EditButton").GetComponent<Button>();

        nameText.text = r.Callsign;
        ipText.text = string.IsNullOrEmpty(r.Ip) ? "IP: ?" : $"IP: {r.Ip}";
        playerText.text = string.IsNullOrEmpty(r.AssignedPlayer) ? "Unassigned" : r.AssignedPlayer;

        editButton.onClick.RemoveAllListeners();
        editButton.onClick.AddListener(() =>
        {
            if (renamePopup == null)
            {
                Debug.Log("[RobotsPanelPresenter] RenamePopup is not assigned. Skipping edit.");
                return;
            }

            renamePopup.Open(
                r.RobotId,
                r.Callsign,
                string.IsNullOrEmpty(r.AssignedPlayer) ? "Unassigned" : r.AssignedPlayer,
                (newName, newPlayer) =>
                {
                    _dir.SetCallsign(r.RobotId, newName);
                    _dir.SetAssignedPlayer(r.RobotId, newPlayer);
                }
            );
        });
    }

}
