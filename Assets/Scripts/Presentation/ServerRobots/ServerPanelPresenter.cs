using System.Collections.Generic;      // Lists
using TMPro;                           // TextMeshPro
using UnityEngine;                     // Unity types (MonoBehaviour, GameObject)

public class ServerPanelPresenter : MonoBehaviour
{
    [Header("Robots Number Text")]
    [SerializeField] private GameObject NumRobots;   // Text which displays number of robots

    // === Private fields ===

    private IRobotDirectory _dir;                     // The shared robot registry (from ServiceLocator)
    private bool _isSubscribed = false;               // Tracks if we subscribed to events already (safety)


    private void OnEnable()
    {
        _dir = ServiceLocator.RobotDirectory;


        UpdateRobotsText();

        if (!_isSubscribed)
        {
            _dir.OnRobotAdded += HandleRobotAdded;
            _dir.OnRobotRemoved += HandleRobotRemoved;
            _isSubscribed = true;
        }
    }

    private void OnDisable()
    {

            _dir.OnRobotAdded -= HandleRobotAdded;
            _dir.OnRobotRemoved -= HandleRobotRemoved;
            _isSubscribed = false;

    }

    private void HandleRobotAdded(RobotInfo r)
    {
        UpdateRobotsText();
    }

    private void HandleRobotRemoved(string robotId)
    {
        UpdateRobotsText();
    }

    private void UpdateRobotsText()
    {
        List<RobotInfo> robots = new List<RobotInfo>(_dir.GetAll());
        TextMeshProUGUI NumRobotsText = NumRobots.GetComponent<TextMeshProUGUI>();
        NumRobotsText.text = robots.Count.ToString();
    }
}
