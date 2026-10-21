using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;

namespace MCG.Launcher.Core;

public sealed class SteamGridDbException(string message) : Exception(message);

// Last-resort cover source for games without any local image. Each user supplies their own API key.
public sealed class SteamGridDb(HttpClient http, string apiKey)
{
    public static readonly Uri ApiBase = new("https://www.steamgriddb.com/api/v2/");
    public const string KeyPage = "https://www.steamgriddb.com/profile/preferences/api";
    private const string PosterQuery = "?dimensions=600x900,342x482,660x930&types=static&nsfw=false&humor=false";
    private const long MaxImageBytes = 10 * 1024 * 1024;
    private const int Attempts = 3;

    // Some home networks have a flaky IPv6 route to Cloudflare (which hosts SteamGridDB): the TLS handshake
    // gets reset while IPv4 works. Connect over IPv4 first and fall back to IPv6 only when IPv4 is unavailable.
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        ConnectCallback = async (context, cancellation) =>
        {
            var addresses = (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellation))
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToList();
            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellation);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException error) { socket.Dispose(); last = error; }
            }
            throw last ?? new SocketException((int)SocketError.HostNotFound);
        },
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = TimeSpan.FromSeconds(20) };

    // Full message chain, because HttpClient hides the useful part ("connection reset") in inner exceptions.
    public static string Describe(Exception error)
    {
        var messages = new List<string>();
        for (var current = error; current is not null; current = current.InnerException)
            if (!messages.Contains(current.Message)) messages.Add(current.Message);
        return string.Join(" → ", messages);
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> create, HttpClient http,
        HttpCompletionOption completion, CancellationToken cancellation)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = create();
            try { return await http.SendAsync(request, completion, cancellation); }
            catch (HttpRequestException) when (attempt < Attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellation);
            }
        }
    }

    public async Task<string?> FindPosterUrlAsync(GameEntry game, CancellationToken cancellation = default)
    {
        if (game.Source == GameSource.Steam && ulong.TryParse(game.SourceId, out var appId) && appId > 0
            && await FirstGridAsync($"grids/steam/{appId}{PosterQuery}", cancellation) is { } steamUrl)
            return steamUrl;
        using var search = await GetJsonAsync($"search/autocomplete/{Uri.EscapeDataString(game.Name.Trim())}", cancellation);
        if (search is null || !search.RootElement.TryGetProperty("data", out var results) || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0 || !results[0].TryGetProperty("id", out var id)) return null;
        return await FirstGridAsync($"grids/game/{id.GetInt64()}{PosterQuery}", cancellation);
    }

    // Downloads to <directory>/<game id>.<ext>. Returns null when SteamGridDB has no vertical cover.
    public async Task<string?> DownloadPosterAsync(GameEntry game, string directory, CancellationToken cancellation = default)
    {
        if (await FindPosterUrlAsync(game, cancellation) is not { } url) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), http,
            HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode) return null;
        var extension = response.Content.Headers.ContentType?.MediaType switch
        {
            "image/jpeg" => ".jpg", "image/png" => ".png", "image/webp" => ".webp", _ => null
        };
        if (extension is null || response.Content.Headers.ContentLength > MaxImageBytes) return null;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, game.Id.ToString("N") + extension);
        var temporary = path + ".pending";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
        await using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
            {
                total += read;
                if (total > MaxImageBytes) break;
                await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
            }
            if (total > MaxImageBytes) { target.Close(); File.Delete(temporary); return null; }
        }
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    private async Task<string?> FirstGridAsync(string path, CancellationToken cancellation)
    {
        using var json = await GetJsonAsync(path, cancellation);
        if (json is null || !json.RootElement.TryGetProperty("data", out var grids) || grids.ValueKind != JsonValueKind.Array) return null;
        foreach (var grid in grids.EnumerateArray())
            if (grid.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String) return url.GetString();
        return null;
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken cancellation)
    {
        using var response = await SendWithRetryAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBase, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            return request;
        }, http, HttpCompletionOption.ResponseContentRead, cancellation);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new SteamGridDbException("A chave do SteamGridDB foi recusada. Confira a chave nas configurações.");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new SteamGridDbException($"O SteamGridDB respondeu {(int)response.StatusCode}. Tente novamente mais tarde.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
    }
}
