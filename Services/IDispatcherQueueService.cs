using System;

namespace ArcademiaGameLauncher.Services
{
    public interface IDispatcherQueueService
    {
        void Enqueue(Action action);
        void EnqueueUnique(string key, Action action);
    }
}
