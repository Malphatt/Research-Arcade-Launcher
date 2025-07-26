using ArcademiaGameLauncher.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArcademiaGameLauncher.Services
{
    public interface IApiClient
    {
        Task<UpdaterInfo> GetUpdaterInfoAsync(ILogger<UpdaterService> _logger);
        Task<bool> UpdateRemoteUpdaterVersionAsync(
            string newVersion,
            ILogger<UpdaterService> _logger
        );
        Task<IEnumerable<GameInfo>> GetMachineGamesAsync(ILogger<UpdaterService> _logger);
        Task<Stream> GetGameDownloadAsync(int gameId, string versionNumber, CancellationToken cancellationToken);
        Task<bool> UpdateRemoteGameVersionAsync(
            int gameId,
            string newVersion,
            ILogger<UpdaterService> _logger
        );
    }

    public class ApiClient(HttpClient http) : IApiClient
    {
        private readonly HttpClient _http = http;

        public async Task<UpdaterInfo> GetUpdaterInfoAsync(ILogger<UpdaterService> _logger)
        {
            var response = await _http.GetAsync("/api/UpdaterVersions/Latest");

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await response.Content.ReadAsStringAsync();

                if (
                    response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    || response.StatusCode == System.Net.HttpStatusCode.NotFound
                )
                    _logger.LogWarning("Warning: {message}", errorMessage);
                else
                    _logger.LogError("Unexpected error: {StatusCode}", response.StatusCode);

                throw new InvalidOperationException("Failed to retrieve UpdaterInfo.");
            }
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var updaterInfo = await JsonSerializer.DeserializeAsync<UpdaterInfo>(stream, options);
            return updaterInfo
                ?? throw new InvalidOperationException("Failed to deserialize UpdaterInfo.");
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
            var response = await _http.PutAsync(
                "/api/MachineUpdaterAssignments/UpdateVersion",
                content
            );

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await response.Content.ReadAsStringAsync();

                if (
                    response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    || response.StatusCode == System.Net.HttpStatusCode.NotFound
                )
                    _logger.LogWarning("Warning: {message}", errorMessage);
                else
                    _logger.LogError("Unexpected error: {StatusCode}", response.StatusCode);
            }
            response.EnsureSuccessStatusCode();

            return response.IsSuccessStatusCode;
        }

        public async Task<IEnumerable<GameInfo>> GetMachineGamesAsync(
            ILogger<UpdaterService> _logger
        )
        {
            var response = await _http.GetAsync($"/api/GameAssignments/Machine");

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await response.Content.ReadAsStringAsync();

                if (
                    response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    || response.StatusCode == System.Net.HttpStatusCode.NotFound
                )
                    _logger.LogWarning("Warning: {message}", errorMessage);
                else
                    _logger.LogError("Unexpected error: {StatusCode}", response.StatusCode);

                return [];
            }
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var games = await JsonSerializer.DeserializeAsync<IEnumerable<GameInfo>>(
                stream,
                options
            );
            return games ?? [];
        }

        public async Task<Stream> GetGameDownloadAsync(int gameId, string versionNumber, CancellationToken cancellationToken)
        {
            var response = await _http.GetAsync($"/api/GameAssignments/{gameId}/Download?versionNumber={versionNumber}", cancellationToken);

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
                    _logger.LogWarning("Warning: {message}", errorMessage);
                else
                    _logger.LogError("Unexpected error: {StatusCode}", response.StatusCode);
            }
            response.EnsureSuccessStatusCode();

            return response.IsSuccessStatusCode;
        }
    }
}
