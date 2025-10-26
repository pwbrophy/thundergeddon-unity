using UnityEngine;
using UnityEngine.UI;

public class DevRobotsToolbar : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button addFakeRobotButton;
    [SerializeField] private Button removeLastButton;   // NEW: Remove Last button


    private IRobotDirectory _dir;
    private string _lastRobotId; // remember the last created robot so we can remove it easily

    private void Start()
    {
        _dir = ServiceLocator.RobotDirectory;

        if (addFakeRobotButton != null)
        {
            addFakeRobotButton.onClick.AddListener(AddFake);
        }

        if (removeLastButton != null)
        {
            removeLastButton.onClick.AddListener(RemoveLast);
        }
    }

    private void AddFake()
    {
        // Create a pretend robot with a made-up id & ip
        _lastRobotId = "esp32-" + Random.Range(100000, 999999);
        var ip = $"192.168.0.{Random.Range(20, 200)}";

        // Note: we’re just adding/upserting the robot. No online/offline concept anymore.
        _dir.Upsert(_lastRobotId, callsign: "", ip: ip); // still using existing method name for now
        Debug.Log($"[Dev] Added fake robot {_lastRobotId} @ {ip}");
    }

    private void RemoveLast()
    {
        bool removed = _dir.RemoveLast();
        if (!removed)
        {
            Debug.Log("[Dev] No robots to remove.");
        }
    }
}
