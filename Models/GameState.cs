namespace ArcademiaGameLauncher.Models
{
    public enum GameState
    {
        fetchingInfo,
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
