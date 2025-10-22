using UnityEngine;




[DefaultExecutionOrder(-1000)]  // runs very early
public class AppBootstrap : MonoBehaviour
{
    [SerializeField] private bool startInMainMenu = true;
    private static bool _made;

    void Awake()
    {
        if (_made) { Destroy(gameObject); return; }
        _made = true;

        DontDestroyOnLoad(gameObject);

        // Create services
        var lobby = new LobbyService();
        var game = new GameService();
        var flow = new GameFlow(lobby, game);

        // Register for global access
        ServiceLocator.Lobby = lobby;
        ServiceLocator.Game = game;
        ServiceLocator.GameFlow = flow;

        // Optional: force a starting phase
        if (startInMainMenu)
            flow.BackToMenu();

        ServiceLocator.RobotDirectory = new RobotDirectory();

        Debug.Log("[AppBootstrap] RobotDirectory created");

        // Optional debug
        Debug.Log("[AppBootstrap] GameFlow created");
    }
}
