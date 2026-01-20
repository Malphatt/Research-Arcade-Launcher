using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArcademiaGameLauncher.Models;
using Microsoft.Extensions.Logging;

namespace ArcademiaGameLauncher.Services
{
    public interface IApiClient
    {
        HttpClient Http { get; }

        Task<ControllerMapping> GetControllerMappingAsync(CancellationToken cancellationToken);
        Task<Stream> GetSiteLogoAsync(CancellationToken cancellationToken);
        Task<string> GetLatestUpdaterVersionAsync(ILogger<UpdaterService> _logger);
        Task<Stream> GetUpdaterDownloadAsync(
            string versionNumber,
            CancellationToken cancellationToken
        );
        Task<bool> UpdateRemoteUpdaterVersionAsync(
            string newVersion,
            ILogger<UpdaterService> _logger
        );
        Task<IEnumerable<GameInfo>> GetMachineGamesAsync(
            ILogger<UpdaterService> _logger,
            CancellationToken cancellationToken
        );
        Task<Stream> GetGameDownloadAsync(
            int gameId,
            string versionNumber,
            CancellationToken cancellationToken
        );
        Task<bool> UpdateRemoteGameVersionAsync(
            int gameId,
            string newVersion,
            ILogger<UpdaterService> _logger
        );
    }

    public class ApiClient(HttpClient http) : IApiClient
    {
        private readonly HttpClient _http = http;
        public HttpClient Http => _http;

        public async Task<ControllerMapping> GetControllerMappingAsync(
            CancellationToken cancellationToken
        )
        {
            var response = await _http.GetAsync("/api/Assets/ControllerMapping", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception(
                    $"Failed to download controller mapping: {response.StatusCode} - {error}"
                );
            }
            return await response.Content.ReadFromJsonAsync<ControllerMapping>(cancellationToken);
        }

        public async Task<Stream> GetSiteLogoAsync(CancellationToken cancellationToken)
        {
            var response = await _http.GetAsync("/api/Assets/Logo", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception(
                    $"Failed to download site logo: {response.StatusCode} - {error}"
                );
            }
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }

        public async Task<string> GetLatestUpdaterVersionAsync(ILogger<UpdaterService> _logger)
        {
            var response = await _http.GetAsync("/api/UpdaterVersions/Latest");

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await response.Content.ReadAsStringAsync();

                if (
                    response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    || response.StatusCode == System.Net.HttpStatusCode.NotFound
                )
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                        _logger.LogWarning(
                            "[ApiClient] Warning whilst executing GetLatestUpdaterVersionAsync: {message}",
                            errorMessage
                        );
                }
                else if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "[ApiClient] Unexpected error whilst executing GetLatestUpdaterVersionAsync: {StatusCode}",
                        response.StatusCode
                    );

                throw new InvalidOperationException("Failed to retrieve UpdaterInfo.");
            }
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<Stream> GetUpdaterDownloadAsync(
            string versionNumber,
            CancellationToken cancellationToken
        )
        {
            var response = await _http.GetAsync(
                $"/api/UpdaterVersions/Download?versionNumber={versionNumber}",
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception($"Failed to download updater: {response.StatusCode} - {error}");
            }

            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }

        public async Task<bool> UpdateRemoteUpdaterVersionAsync(
            string newVersion,
            ILogger<UpdaterService> _logger
        )
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { VersionNumber = newVersion }),
                System.Text.Encoding.UTF8,
                "application/json"
            );
            var response = await _http.PutAsync("/api/UpdaterVersions/UpdateVersion", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await response.Content.ReadAsStringAsync();

                if (
                    response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    || response.StatusCode == System.Net.HttpStatusCode.NotFound
                )
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                        _logger.LogWarning(
                            "[ApiClient] Warning whilst executing UpdateRemoteUpdaterVersionAsync: {message}",
                            errorMessage
                        );
                }
                else if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "[ApiClient] Unexpected error whilst executing UpdateRemoteUpdaterVersionAsync: {StatusCode}",
                        response.StatusCode
                    );
            }
            response.EnsureSuccessStatusCode();

            return response.IsSuccessStatusCode;
        }

        public async Task<IEnumerable<GameInfo>> GetMachineGamesAsync(
            ILogger<UpdaterService> _logger,
            CancellationToken cancellationToken
        )
        {
            _logger.LogInformation("[ApiClient] Fetching machine games from API...");

            var response = await _http.GetAsync($"/api/GameAssignments/Machine", cancellationToken);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "[ApiClient] Received response with status code: {StatusCode}",
                    response.StatusCode
                );

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await response.Content.ReadAsStringAsync(cancellationToken);

                if (
                    response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    || response.StatusCode == System.Net.HttpStatusCode.NotFound
                )
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                        _logger.LogWarning(
                            "[ApiClient] Warning whilst executing GetMachineGamesAsync: {message}",
                            errorMessage
                        );
                }
                else if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "[ApiClient] Unexpected error whilst executing GetMachineGamesAsync: {StatusCode}",
                        response.StatusCode
                    );

                _logger.LogInformation("[ApiClient] Returning empty game list due to error.");

                return [];
            }
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var games = await JsonSerializer.DeserializeAsync<IEnumerable<GameInfo>>(
                stream,
                options
            );

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "[ApiClient] Retrieved {GameCount} games from API.",
                    games?.Count() ?? 0
                );

            return games ?? [];
        }

        public async Task<Stream> GetGameDownloadAsync(
            int gameId,
            string versionNumber,
            CancellationToken cancellationToken
        )
        {
            var response = await _http.GetAsync(
                $"/api/GameAssignments/{gameId}/Download?versionNumber={versionNumber ?? "0.0.0"}",
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception($"Failed to download game: {response.StatusCode} - {error}");
            }

            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }

        public async Task<bool> UpdateRemoteGameVersionAsync(
            int gameId,
            string newVersion,
            ILogger<UpdaterService> _logger
        )
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { VersionNumber = newVersion }),
                System.Text.Encoding.UTF8,
                "application/json"
            );
            var response = await _http.PutAsync(
                $"/api/GameAssignments/{gameId}/UpdateVersion",
                content
            );

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await response.Content.ReadAsStringAsync();

                if (
                    response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    || response.StatusCode == System.Net.HttpStatusCode.NotFound
                )
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                        _logger.LogWarning(
                            "[ApiClient] Warning whilst executing UpdateRemoteGameVersionAsync: {message}",
                            errorMessage
                        );
                }
                else if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        "[ApiClient] Unexpected error whilst executing UpdateRemoteGameVersionAsync: {StatusCode}",
                        response.StatusCode
                    );
            }
            response.EnsureSuccessStatusCode();

            return response.IsSuccessStatusCode;
        }
    }
}
