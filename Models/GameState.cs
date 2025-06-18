namespace ArcademiaGameLauncher.Models
{
    public enum GameState
    {
        checkingForUpdates,
        downloadingGame,
        downloadingUpdate,
        failed,
        loadingInfo,
        ready,
        launching,
        runningGame,
    }
}
