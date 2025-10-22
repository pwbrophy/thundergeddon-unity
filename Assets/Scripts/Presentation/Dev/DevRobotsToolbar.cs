using UnityEngine;
using UnityEngine.UI;

// Add this to a small "Dev Tools" UI panel.
// Hook the three Buttons in the Inspector.
public class DevRobotsToolbar : MonoBehaviour
{
    [SerializeField] private Button addFakeRobotButton;
    [SerializeField] private Button toggleOnlineButton;
    [SerializeField] private Button assignPlayerButton;

    private IRobotDirectory _dir;
    private string _lastRobotId; // remember the last created to make toggling easy
    private bool _toggle;

    void Start()
    {
        _dir = ServiceLocator.RobotDirectory;

        if (addFakeRobotButton) addFakeRobotButton.onClick.AddListener(AddFake);
        if (toggleOnlineButton) toggleOnlineButton.onClick.AddListener(Toggle);
        if (assignPlayerButton) assignPlayerButton.onClick.AddListener(AssignNextPlayer);
    }

    private void AddFake()
    {
        // Create a pretend robot with a made-up id/ip
        _lastRobotId = "esp32-" + Random.Range(100000, 999999);
        var ip = $"192.168.0.{Random.Range(20, 200)}";
        var name = ""; // blank -> directory will assign "robot-01", "robot-02", ...
        _dir.UpsertOnline(_lastRobotId, name, ip);
        Debug.Log($"[Dev] Added fake robot {_lastRobotId} @ {ip}");
    }

    private void Toggle()
    {
        if (string.IsNullOrEmpty(_lastRobotId)) return;
        _toggle = !_toggle;
        _dir.SetOnline(_lastRobotId, _toggle);
    }

    private void AssignNextPlayer()
    {
        if (string.IsNullOrEmpty(_lastRobotId)) return;
        if (_dir.TryGet(_lastRobotId, out var info))
        {
            var next = info.AssignedPlayer switch
            {
                "Player1" => "Player2",
                "Player2" => "Unassigned",
                _ => "Player1"
            };
            _dir.SetAssignedPlayer(_lastRobotId, next);
        }
    }
}
