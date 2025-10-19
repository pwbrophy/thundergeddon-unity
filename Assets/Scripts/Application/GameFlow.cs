using System;

public enum GamePhase { MainMenu, Lobby, Playing, Ended }

public sealed class GameFlow
{
    public GamePhase Phase { get; private set; } = GamePhase.MainMenu;

    // Services the flow will call (we’ll plug real ones later)
    private readonly LobbyService _lobby;
    private readonly GameService _game;

    // Let UI subscribe to phase changes
    public event Action<GamePhase> OnPhaseChanged;

    public GameFlow(LobbyService lobby, GameService game)
    {
        _lobby = lobby;
        _game = game;
    }

    private void SetPhase(GamePhase p)
    {
        if (Phase == p) return;
        Phase = p;
        OnPhaseChanged?.Invoke(Phase);
    }

    public bool CanGoToLobby() => Phase == GamePhase.MainMenu;
    public void GoToLobby()
    {
        if (!CanGoToLobby()) return;
        SetPhase(GamePhase.Lobby);
    }

    public bool CanStartGame() => Phase == GamePhase.Lobby && _game.CanStart();
    public void StartGame()
    {
        if (!CanStartGame()) return;
        _game.StartGame(startAP: 5);
        SetPhase(GamePhase.Playing);
    }

    public bool CanEndGame() => Phase == GamePhase.Playing;
    public void EndGame()
    {
        if (!CanEndGame()) return;
        SetPhase(GamePhase.Ended);
    }

    public void BackToMenu()
    {
        // From anywhere, go back to main menu (adjust as you like)
        SetPhase(GamePhase.MainMenu);
    }
}
