// RobotSelectionPanel.cs — selection + video + motors on/off on select/deselect.
// Starts with no selection. Exposes CurrentRobotId and SelectionChanged.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RobotSelectionPanel : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button prevButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button clearButton;

    [Header("Info Labels")]
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI ipLabel;
    [SerializeField] private TextMeshProUGUI playerLabel;
    [SerializeField] private TextMeshProUGUI allianceLabel;
    [SerializeField] private TextMeshProUGUI clientLabel;

    [Header("Video")]
    [SerializeField] private ESP32VideoReceiver video;

    private IRobotDirectory _dir;
    private RobotWebSocketServer _ws;
    private readonly List<RobotInfo> _list = new();
    private int _index = -1;
    private string _selectedRobotId;

    public string CurrentRobotId => _selectedRobotId;               // <— other panels read this
    public event Action<string> SelectionChanged;                    // <— raised on any change

    private void Awake()
    {
        _dir = ServiceLocator.RobotDirectory;
        _ws = ServiceLocator.RobotServer;
        if (video == null) video = ESP32VideoReceiver.Instance;
    }

    private void OnEnable()
    {
        WireButtons();
        SubscribeDirectory();
        RebuildList();

        // Start with no selection
        _index = -1;
        _selectedRobotId = null;
        if (video) video.ClearActiveRobot();
        RefreshUI();
        SelectionChanged?.Invoke(null);
    }

    private void OnDisable() => UnsubscribeDirectory();

    private void WireButtons()
    {
        if (prevButton) { prevButton.onClick.RemoveAllListeners(); prevButton.onClick.AddListener(Prev); }
        if (nextButton) { nextButton.onClick.RemoveAllListeners(); nextButton.onClick.AddListener(Next); }
        if (clearButton) { clearButton.onClick.RemoveAllListeners(); clearButton.onClick.AddListener(ClearSelection); }
    }

    private void SubscribeDirectory()
    {
        if (_dir == null) return;
        _dir.OnRobotAdded += OnRobotAdded;
        _dir.OnRobotUpdated += OnRobotUpdated;
        _dir.OnRobotRemoved += OnRobotRemoved;
    }
    private void UnsubscribeDirectory()
    {
        if (_dir == null) return;
        _dir.OnRobotAdded -= OnRobotAdded;
        _dir.OnRobotUpdated -= OnRobotUpdated;
        _dir.OnRobotRemoved -= OnRobotRemoved;
    }

    private void RebuildList()
    {
        _list.Clear();
        if (_dir != null) _list.AddRange(_dir.GetAll());
        ClampIndexAfterListChange();
    }

    private void ClampIndexAfterListChange()
    {
        if (_list.Count == 0) { _index = -1; return; }
        if (_index >= _list.Count) _index = -1;
        if (_index < -1) _index = -1;
    }

    private void OnRobotAdded(RobotInfo r) { _list.Add(r); RefreshUI(); }
    private void OnRobotUpdated(RobotInfo r)
    {
        for (int i = 0; i < _list.Count; i++) if (_list[i].RobotId == r.RobotId) { _list[i] = r; break; }
        RefreshUI();
    }
    private void OnRobotRemoved(string robotId)
    {
        int idx = _list.FindIndex(x => x.RobotId == robotId);
        if (idx >= 0) _list.RemoveAt(idx);
        if (_selectedRobotId == robotId) { _index = -1; _selectedRobotId = null; if (video) video.ClearActiveRobot(); }
        ClampIndexAfterListChange();
        RefreshUI();
        _ws = ServiceLocator.RobotServer;
        _ws?.SendMotorsOff(robotId);                      // safety: make sure removed robot is off
        SelectionChanged?.Invoke(null);
    }

    private void Prev()
    {
        if (_list.Count == 0) return;
        int next = (_index < 0) ? 0 : (_index - 1 + _list.Count) % _list.Count;
        SelectIndex(next);
    }
    private void Next()
    {
        if (_list.Count == 0) return;
        int next = (_index < 0) ? 0 : (_index + 1) % _list.Count;
        SelectIndex(next);
    }

    private void ClearSelection()
    {
        _ws = ServiceLocator.RobotServer;
        if (!string.IsNullOrEmpty(_selectedRobotId))
        {
            _ws?.SendStreamOff(_selectedRobotId);
            _ws?.SendMotorsOff(_selectedRobotId);         // <— turn off motors on clear
        }

        _index = -1;
        _selectedRobotId = null;
        if (video) video.ClearActiveRobot();
        RefreshUI();
        SelectionChanged?.Invoke(null);
    }

    private void SelectIndex(int newIndex)
    {
        if (newIndex < 0 || newIndex >= _list.Count) { ClearSelection(); return; }

        _ws = ServiceLocator.RobotServer;

        if (!string.IsNullOrEmpty(_selectedRobotId))
        {
            _ws?.SendStreamOff(_selectedRobotId);
            _ws?.SendMotorsOff(_selectedRobotId);         // <— stop old robot’s motors
        }

        _index = newIndex;
        var r = _list[_index];
        _selectedRobotId = r.RobotId;

        if (video) video.SetActiveRobot(_selectedRobotId);
        _ws?.SendStreamOn(_selectedRobotId);
        _ws?.SendMotorsOn(_selectedRobotId);              // <— enable new robot’s motors

        RefreshUI();
        SelectionChanged?.Invoke(_selectedRobotId);
    }

    private void RefreshUI()
    {
        if (_index < 0 || _index >= _list.Count)
        {
            if (nameLabel) nameLabel.text = "(no robot selected)";
            if (ipLabel) ipLabel.text = "";
            if (playerLabel) playerLabel.text = "Player: —";
            if (allianceLabel) allianceLabel.text = "Alliance: —";
            if (clientLabel) clientLabel.text = "Client: —";
            return;
        }

        var r = _list[_index];
        string display = string.IsNullOrEmpty(r.Callsign) ? r.RobotId : r.Callsign;
        if (nameLabel) nameLabel.text = display;
        if (ipLabel) ipLabel.text = string.IsNullOrEmpty(r.Ip) ? "(no ip)" : r.Ip;
        if (playerLabel) playerLabel.text = "Player: " + (string.IsNullOrEmpty(r.AssignedPlayer) ? "Unassigned" : r.AssignedPlayer);
        if (allianceLabel) allianceLabel.text = "Alliance: TBD";
        if (clientLabel) clientLabel.text = "Client:   TBD";
    }
}
