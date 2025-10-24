using UnityEngine;
using UnityEngine.UI;

public class GameFlowPresenter : MonoBehaviour
{
    [Header("Top-level screens/panels")]
    [SerializeField] private GameObject mainMenuPanel;  // your Main Menu root (Server/Client choices)
    [SerializeField] private GameObject lobbyPanel;     // your Server setup (Alliances/Players/Robots/Clients)
    [SerializeField] private GameObject playingPanel;   // your in-game HUD root
    [SerializeField] private GameObject endedPanel;     // winner / end screen (optional)

    [Header("Buttons")]
    [SerializeField] private Button toLobbyButton;      // on Main Menu: “Server” (goes to Lobby)
    [SerializeField] private Button startGameButton;    // on Lobby: “Start Game”
    [SerializeField] private Button backToMenuButton;   // anywhere appropriate
    [SerializeField] private Button endGameButton;      // in-game: “End Game” (for testing)

    private GameFlow _flow;

    void OnEnable()
    {
        Debug.Log("We're in the GameGlowPresenter OnEnable now");

        _flow = ServiceLocator.GameFlow;

        // Hook UI buttons
        if (toLobbyButton)    toLobbyButton.onClick.AddListener(() => _flow.GoToLobby());
        if (startGameButton)  startGameButton.onClick.AddListener(() => _flow.StartGame());
        if (backToMenuButton) backToMenuButton.onClick.AddListener(() => _flow.BackToMenu());
        if (endGameButton)    endGameButton.onClick.AddListener(() => _flow.EndGame());

        // React to phase changes
        _flow.OnPhaseChanged += HandlePhaseChanged;

        // Initialize UI to current phase
        HandlePhaseChanged(_flow.Phase);
    }

    void OnDisable()
    {

        Debug.Log("We're in the GameGlowPresenter OnDisable now");
        if (_flow != null) _flow.OnPhaseChanged -= HandlePhaseChanged;

        // Unhook buttons (optional for now)
        if (toLobbyButton)    toLobbyButton.onClick.RemoveAllListeners();
        if (startGameButton)  startGameButton.onClick.RemoveAllListeners();
        if (backToMenuButton) backToMenuButton.onClick.RemoveAllListeners();
        if (endGameButton)    endGameButton.onClick.RemoveAllListeners();
    }

    void HandlePhaseChanged(GamePhase p)
    {

        Debug.Log("We're in the GameGlowPresenter HandlePhaseChange now");
        // Show exactly one panel; hide the others
        if (mainMenuPanel) mainMenuPanel.SetActive(p == GamePhase.MainMenu);
        if (lobbyPanel)    lobbyPanel.SetActive(p == GamePhase.Lobby);
        if (playingPanel)  playingPanel.SetActive(p == GamePhase.Playing);
        if (endedPanel)    endedPanel.SetActive(p == GamePhase.Ended);

        // Enable/disable critical buttons safely
        if (startGameButton) startGameButton.interactable = _flow.CanStartGame();
    }
}
