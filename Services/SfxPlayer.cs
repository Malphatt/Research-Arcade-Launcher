using Microsoft.Extensions.Logging;
using NAudio.Wave;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ArcademiaGameLauncher.Services
{
    public interface ISfxPlayer
    {
        Task PlayAsync(string fileUrl, CancellationToken ct = default);
    }

    public sealed class SfxPlayer : ISfxPlayer
    {
        private readonly HttpClient _http;
        private readonly ILogger<SfxPlayer> _log;

        public SfxPlayer(IApiClient apiClient, ILogger<SfxPlayer> log)
        {
            _http = apiClient.Http;
            _log = log;
        }

        public async Task PlayAsync(string fileUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
                return;

            try
            {
                var requestUri = ToRelativeIfSameHost(_http.BaseAddress, fileUrl);

                _log.LogDebug("[Audio] GET {Url}", requestUri);

                using var resp = await _http.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                // Handle unexpected redirect responses
                if (IsRedirect(resp.StatusCode))
                {
                    _log.LogWarning("[Audio] Unexpected redirect ({Status}) for {Url}", resp.StatusCode, requestUri);
                }

                resp.EnsureSuccessStatusCode();

                // Buffer to a seekable stream for NAudio
                await using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var ms = new MemoryStream(capacity: 64 * 1024);
                await net.CopyToAsync(ms, ct).ConfigureAwait(false);
                ms.Position = 0;

                var contentType = resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                _log.LogDebug("[Audio] Content-Type: {ContentType}", contentType);

                using var reader = CreateReaderFor(requestUri.ToString(), contentType, ms);
                using var output = new WaveOutEvent();

                output.Init(reader);
                output.Play();

                while (output.PlaybackState == PlaybackState.Playing && !ct.IsCancellationRequested)
                    await Task.Delay(100, ct).ConfigureAwait(false);

                // If cancelled mid-play, stop gracefully
                if (ct.IsCancellationRequested && output.PlaybackState == PlaybackState.Playing)
                    output.Stop();
            }
            catch (OperationCanceledException)
            {
                _log.LogInformation("[Audio] Playback cancelled for {Url}", fileUrl);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Audio] Failed to play '{Url}'", fileUrl);
            }
        }

        private static bool IsRedirect(HttpStatusCode code) =>
            code == HttpStatusCode.Moved ||
            code == HttpStatusCode.Redirect ||
            code == HttpStatusCode.TemporaryRedirect ||
            (int)code == 308;

        private static Uri ToRelativeIfSameHost(Uri baseAddress, string url)
        {
            if (baseAddress is null)
                return new Uri(url, UriKind.RelativeOrAbsolute);

            if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
            {
                if (abs.Host.Equals(baseAddress.Host, StringComparison.OrdinalIgnoreCase)
                    && abs.Scheme.Equals(baseAddress.Scheme, StringComparison.OrdinalIgnoreCase)
                    && abs.Port == baseAddress.Port)
                {
                    return new Uri(abs.PathAndQuery + abs.Fragment, UriKind.Relative);
                }

                return abs;
            }

            return new Uri(url, UriKind.Relative);
        }

        private static WaveStream CreateReaderFor(string url, string contentType, Stream stream)
        {
            if (url.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                || contentType == "audio/wav" || contentType == "audio/x-wav")
            {
                return new WaveFileReader(stream);
            }

            if (url.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                || contentType == "audio/mpeg")
            {
                return new Mp3FileReader(stream);
            }

            throw new NotSupportedException($"Unsupported audio type for '{url}' ({contentType ?? "unknown"}).");
        }
    }
}