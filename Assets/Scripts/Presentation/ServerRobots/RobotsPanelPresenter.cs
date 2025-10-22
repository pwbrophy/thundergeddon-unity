using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Attach this to your "Server > Robots" panel.
// Assign: content (the Content under your ScrollView) and rowPrefab (RobotRow prefab).
public class RobotsPanelPresenter : MonoBehaviour
{
    [Header("ScrollView wiring")]
    [SerializeField] private RectTransform content;   // Viewport/Content
    [SerializeField] private GameObject rowPrefab;    // RobotRow prefab

    [Header("Optional: a simple rename popup")]
    [SerializeField] private RenamePopup renamePopup; // drag if you create one (below)

    private IRobotDirectory _dir;

    void OnEnable()
    {
        _dir = ServiceLocator.RobotDirectory;
        if (_dir == null)
        {
            Debug.LogError("RobotsPanelPresenter: RobotDirectory is null. Is AppBootstrap in scene?");
            return;
        }

        // Build initial list
        Rebuild();

        // Listen for changes
        _dir.OnRobotAdded += HandleAdded;
        _dir.OnRobotUpdated += HandleUpdated;
        _dir.OnRobotRemoved += HandleRemoved;
    }

    void OnDisable()
    {
        if (_dir == null) return;
        _dir.OnRobotAdded -= HandleAdded;
        _dir.OnRobotUpdated -= HandleUpdated;
        _dir.OnRobotRemoved -= HandleRemoved;
    }

    private void Rebuild()
    {
        foreach (Transform c in content) Destroy(c.gameObject);
        foreach (var r in _dir.GetAll().OrderBy(x => x.Callsign))
            CreateOrUpdateRow(r);
    }

    private void HandleAdded(RobotInfo r) => CreateOrUpdateRow(r);
    private void HandleUpdated(RobotInfo r) => CreateOrUpdateRow(r);
    private void HandleRemoved(string id)
    {
        var t = content.Find(id);
        if (t) Destroy(t.gameObject);
    }

    private void CreateOrUpdateRow(RobotInfo r)
    {
        // Find existing row by name (we name rows with the RobotId)
        var existing = content.Find(r.RobotId);
        GameObject go = existing ? existing.gameObject : Instantiate(rowPrefab, content, false);
        go.name = r.RobotId;

        // Grab UI pieces
        var nameTxt = go.transform.Find("Name").GetComponent<TextMeshProUGUI>();
        var ipTxt = go.transform.Find("Ip").GetComponent<TextMeshProUGUI>();
        var playerTxt = go.transform.Find("Player").GetComponent<TextMeshProUGUI>();
        var editBtn = go.transform.Find("EditButton").GetComponent<Button>();

        // Fill labels
        var status = r.IsOnline ? "Online" : "Offline";
        nameTxt.text = $"{r.Callsign}  ({status})";
        ipTxt.text = string.IsNullOrEmpty(r.Ip) ? "IP: ?" : $"IP: {r.Ip}";
        playerTxt.text = string.IsNullOrEmpty(r.AssignedPlayer) ? "Unassigned" : r.AssignedPlayer;

        // Wire the Edit button once (clear first to avoid double listeners)
        editBtn.onClick.RemoveAllListeners();
        editBtn.onClick.AddListener(() =>
        {
            if (renamePopup != null)
            {
                // Open a tiny popup to edit name + player
                renamePopup.Open(r.RobotId, r.Callsign, r.AssignedPlayer,
                    onApply: (newName, newPlayer) =>
                    {
                        _dir.SetCallsign(r.RobotId, newName);
                        _dir.SetAssignedPlayer(r.RobotId, newPlayer);
                    });
            }
            else
            {
                Debug.Log("No RenamePopup hooked up. You can still change via code: _dir.SetCallsign(...)");
            }
        });
    }
}
