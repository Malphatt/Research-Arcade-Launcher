using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArcademiaGameLauncher.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ArcademiaGameLauncher.Services
{
    public interface ISessionTrackingService
    {
        void RegisterTransport(
            Func<string, int, string, Task> invokeStart,
            Func<string, string, string, Task> invokeEnd
        );
        Task StartSessionAsync(int gameAssignmentId, DateTime processStartTime);
        Task EndSessionAsync(string endReason);
        Task FlushQueueAsync();
        Task RecoverCrashAsync();
    }

    public class SessionTrackingService : ISessionTrackingService
    {
        private readonly string _queuePath;
        private readonly string _currentPath;
        private readonly ILogger<SessionTrackingService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private string? _currentExternalId;
        private Func<string, int, string, Task>? _invokeStart;
        private Func<string, string, string, Task>? _invokeEnd;

        public SessionTrackingService(
            string applicationPath,
            ILogger<SessionTrackingService> logger
        )
        {
            _queuePath = Path.Combine(applicationPath, "session_queue.json");
            _currentPath = Path.Combine(applicationPath, "session_current.json");
            _logger = logger;
        }

        public void RegisterTransport(
            Func<string, int, string, Task> invokeStart,
            Func<string, string, string, Task> invokeEnd
        )
        {
            _invokeStart = invokeStart;
            _invokeEnd = invokeEnd;
        }

        public async Task StartSessionAsync(int gameAssignmentId, DateTime processStartTime)
        {
            var externalId = Guid.NewGuid().ToString();
            _currentExternalId = externalId;

            var startedAtUtc =
                processStartTime.Kind == DateTimeKind.Utc
                    ? processStartTime
                    : processStartTime.ToUniversalTime();

            WriteCurrentFile(externalId, gameAssignmentId, startedAtUtc);
            _logger.LogInformation(
                "[Session] Started: {ExternalId} AssignmentId={AssignmentId}",
                externalId,
                gameAssignmentId
            );

            var sent = await TryInvokeStart(externalId, gameAssignmentId, startedAtUtc);
            if (!sent)
                await EnqueueAsync(
                    new SessionQueueItem
                    {
                        Type = "Start",
                        ExternalId = externalId,
                        GameAssignmentId = gameAssignmentId,
                        LauncherStartedAtUtc = startedAtUtc.ToString("o"),
                        QueuedAtUtc = DateTime.UtcNow.ToString("o"),
                    }
                );
        }

        public async Task EndSessionAsync(string endReason)
        {
            var externalId = _currentExternalId;
            if (externalId is null)
                return;

            _currentExternalId = null;
            DeleteCurrentFile();

            var endedAt = DateTime.UtcNow;
            _logger.LogInformation(
                "[Session] Ended: {ExternalId} Reason={Reason}",
                externalId,
                endReason
            );

            var sent = await TryInvokeEnd(externalId, endReason, endedAt);
            if (!sent)
                await EnqueueAsync(
                    new SessionQueueItem
                    {
                        Type = "End",
                        ExternalId = externalId,
                        EndReason = endReason,
                        EndedAtUtc = endedAt.ToString("o"),
                        QueuedAtUtc = DateTime.UtcNow.ToString("o"),
                    }
                );
        }

        public async Task FlushQueueAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var items = LoadQueue();
                if (items.Count == 0)
                    return;

                _logger.LogInformation("[Session] Flushing queue: {Count} item(s)", items.Count);

                var remaining = new List<SessionQueueItem>(items);
                for (int i = 0; i < remaining.Count; i++)
                {
                    var item = remaining[i];
                    bool sent;

                    try
                    {
                        if (
                            item.Type == "Start"
                            && item.GameAssignmentId.HasValue
                            && item.LauncherStartedAtUtc is not null
                        )
                        {
                            var startedAt = DateTime.Parse(item.LauncherStartedAtUtc);
                            await _invokeStart!(
                                item.ExternalId,
                                item.GameAssignmentId.Value,
                                startedAt.ToString("o")
                            );
                            sent = true;
                        }
                        else if (
                            item.Type == "End"
                            && item.EndReason is not null
                            && item.EndedAtUtc is not null
                        )
                        {
                            await _invokeEnd!(item.ExternalId, item.EndReason, item.EndedAtUtc);
                            sent = true;
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[Session] Skipping malformed queue item: {ExternalId} Type={Type}",
                                item.ExternalId,
                                item.Type
                            );
                            sent = true; // drop malformed items
                        }
                    }
                    catch (Exception ex)
                    {
                        item.AttemptCount++;
                        _logger.LogWarning(
                            ex,
                            "[Session] Flush failed for {ExternalId} (attempt {Attempt})",
                            item.ExternalId,
                            item.AttemptCount
                        );
                        sent = false;
                    }

                    if (sent)
                    {
                        remaining.RemoveAt(i);
                        i--;
                        SaveQueue(remaining);
                    }
                    else
                    {
                        SaveQueue(remaining);
                        break;
                    }
                }

                _logger.LogInformation(
                    "[Session] Queue flush complete. {Remaining} item(s) remaining",
                    remaining.Count
                );
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RecoverCrashAsync()
        {
            if (!File.Exists(_currentPath))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(_currentPath);
                var current = JsonConvert.DeserializeObject<CurrentSessionFile>(json);
                if (current is null)
                {
                    File.Delete(_currentPath);
                    return;
                }

                var crashTime = File.GetLastWriteTimeUtc(_currentPath);
                _logger.LogWarning(
                    "[Session] Recovering crashed session: {ExternalId} CrashTime={CrashTime}",
                    current.ExternalId,
                    crashTime
                );

                File.Delete(_currentPath);

                await EnqueueAsync(
                    new SessionQueueItem
                    {
                        Type = "End",
                        ExternalId = current.ExternalId,
                        EndReason = "Crash",
                        EndedAtUtc = crashTime.ToString("o"),
                        QueuedAtUtc = DateTime.UtcNow.ToString("o"),
                    }
                );

                await FlushQueueAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session] Failed to recover crashed session");
                try
                {
                    File.Delete(_currentPath);
                }
                catch { }
            }
        }

        private async Task<bool> TryInvokeStart(
            string externalId,
            int gameAssignmentId,
            DateTime startedAt
        )
        {
            if (_invokeStart is null)
                return false;
            try
            {
                await _invokeStart(externalId, gameAssignmentId, startedAt.ToString("o"));
                return true;
            }
            catch (Exception)
            {
                _logger.LogWarning("[Session] SessionStart invoke failed — will queue");
                return false;
            }
        }

        private async Task<bool> TryInvokeEnd(string externalId, string endReason, DateTime endedAt)
        {
            if (_invokeEnd is null)
                return false;
            try
            {
                await _invokeEnd(externalId, endReason, endedAt.ToString("o"));
                return true;
            }
            catch (Exception)
            {
                _logger.LogWarning("[Session] SessionEnd invoke failed — will queue");
                return false;
            }
        }

        private async Task EnqueueAsync(SessionQueueItem item)
        {
            await _lock.WaitAsync();
            try
            {
                var items = LoadQueue();
                items.Add(item);
                SaveQueue(items);
            }
            finally
            {
                _lock.Release();
            }
        }

        private List<SessionQueueItem> LoadQueue()
        {
            if (!File.Exists(_queuePath))
                return [];
            try
            {
                var json = File.ReadAllText(_queuePath);
                return JsonConvert.DeserializeObject<List<SessionQueueItem>>(json) ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session] Failed to load queue file; treating as empty");
                return [];
            }
        }

        private void SaveQueue(List<SessionQueueItem> items)
        {
            try
            {
                File.WriteAllText(
                    _queuePath,
                    JsonConvert.SerializeObject(items, Formatting.Indented)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session] Failed to save queue file");
            }
        }

        private void WriteCurrentFile(string externalId, int gameAssignmentId, DateTime startedAt)
        {
            try
            {
                var content = JsonConvert.SerializeObject(
                    new CurrentSessionFile
                    {
                        ExternalId = externalId,
                        GameAssignmentId = gameAssignmentId,
                        LauncherStartedAtUtc = startedAt.ToString("o"),
                    },
                    Formatting.Indented
                );
                File.WriteAllText(_currentPath, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session] Failed to write session_current.json");
            }
        }

        private void DeleteCurrentFile()
        {
            try
            {
                if (File.Exists(_currentPath))
                    File.Delete(_currentPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session] Failed to delete session_current.json");
            }
        }

        private sealed class CurrentSessionFile
        {
            public string ExternalId { get; set; } = null!;
            public int GameAssignmentId { get; set; }
            public string LauncherStartedAtUtc { get; set; } = null!;
        }
    }
}
