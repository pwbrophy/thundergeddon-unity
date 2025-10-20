using UnityEngine;




[DefaultExecutionOrder(-1000)]  // runs very early
public class AppBootstrap : MonoBehaviour
{
    [SerializeField] private bool startInMainMenu = true;

    void Awake()
    {
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

        // Optional debug
        Debug.Log("[AppBootstrap] GameFlow created");
    }
}
