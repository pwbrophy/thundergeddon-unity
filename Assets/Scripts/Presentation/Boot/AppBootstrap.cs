
using UnityEngine;


[DefaultExecutionOrder(-1000)]  // runs very early
public class AppBootstrap : MonoBehaviour
{
    [SerializeField] private bool startInMainMenu = true;
    private static bool _made;

    void Awake()
    {
        Debug.Log("Starting the App Bootstrap");
        if (_made) { Destroy(gameObject); return; }
        _made = true;

        DontDestroyOnLoad(gameObject);

        // Create services
        Debug.Log("Creating Services");
        var lobby = new LobbyService();
        var game = new GameService();
        var flow = new GameFlow(lobby, game);

        // Register for global access
        Debug.Log("Registering services for Global Access in the Service Locator");
        ServiceLocator.Lobby = lobby;
        ServiceLocator.Game = game;
        ServiceLocator.GameFlow = flow;

        // Optional: force a starting phase
        if (startInMainMenu)
            flow.BackToMenu();
        Debug.Log("[AppBootstrap] GameFlow created");

        ServiceLocator.RobotDirectory = new RobotDirectory();
        Debug.Log($"[AppBootstrap] RobotDirectory created: {ServiceLocator.RobotDirectory.GetHashCode()}");

        // Optional debug
        
    }
}
