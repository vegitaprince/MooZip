using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MooZip;

internal sealed record UpdateRelease(Version Version, Uri Download, long Size, string Sha256);

internal sealed class UpdateClient : IDisposable
{
    internal const string Repository = "https://github.com/vegitaprince/MooZip";
    const string LatestApi = "https://api.github.com/repos/vegitaprince/MooZip/releases/latest";
    const int BufferSize = 128 * 1024;
    const long AssetLimit = 512L * 1024 * 1024;
    readonly HttpClient http;

    internal static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version!;
            return new(version.Major, version.Minor, version.Build);
        }
    }

    internal UpdateClient(HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MooZip/" + CurrentVersion);
    }

    internal static UpdateRelease? Parse(Stream json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;

        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"\Av(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\z") ||
            !Version.TryParse(tag.AsSpan(1), out var version) || version <= current)
            return null;

        string name = $"MooZip-Portable-{version}-x64.zip";
        string url = $"{Repository}/releases/download/{tag}/{name}";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name ||
                asset.GetProperty("state").GetString() != "uploaded") continue;
            string digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
            long size = asset.GetProperty("size").GetInt64();
            if (asset.GetProperty("browser_download_url").GetString() != url ||
                size is <= 0 or > AssetLimit || !Regex.IsMatch(digest, @"\Asha256:[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("GitHub에서 업데이트 파일의 검증 정보를 확인할 수 없습니다.");
            return new(version, new Uri(url), size, digest[7..]);
        }
        throw new InvalidDataException("새 버전의 포터블 ZIP을 GitHub에서 찾을 수 없습니다.");
    }

    internal async Task<UpdateRelease?> CheckAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var content = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var bounded = new MemoryStream();
        await CopyAsync(content, bounded, 1024 * 1024, null, null, timeout.Token);
        bounded.Position = 0;
        return Parse(bounded, CurrentVersion);
    }

    async Task<HttpResponseMessage> GetDownloadAsync(Uri address, CancellationToken token)
    {
        for (int redirect = 0; redirect < 6; redirect++)
        {
            if (address.Scheme != "https" || !address.IsDefaultPort || address.UserInfo.Length != 0 ||
                address.Host is not ("github.com" or "release-assets.githubusercontent.com" or
                                     "objects.githubusercontent.com" or "github-releases.githubusercontent.com"))
                throw new InvalidDataException("업데이트 다운로드 주소가 올바르지 않습니다.");
            var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location == null) break;
                address = new Uri(address, location);
                continue;
            }
            if (response.IsSuccessStatusCode) return response;
            using (response) response.EnsureSuccessStatusCode();
        }
        throw new InvalidDataException("GitHub에서 다운로드 주소를 확인할 수 없습니다.");
    }

    internal async Task DownloadAsync(UpdateRelease release, string destination, IProgress<double>? progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var response = await GetDownloadAsync(release.Download, timeout.Token);
        if (response.Content.Headers.ContentLength is long size && size != release.Size)
            throw new InvalidDataException("업데이트 ZIP 크기가 일치하지 않습니다.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long bytes = await CopyAsync(input, output, release.Size, hash, progress, timeout.Token);
        if (bytes != release.Size ||
            !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
            throw new InvalidDataException("업데이트 ZIP의 SHA-256 검증에 실패했습니다.");
    }

    internal static async Task<long> CopyAsync(Stream input, Stream output, long limit,
        IncrementalHash? hash, IProgress<double>? progress, CancellationToken token)
    {
        var buffer = new byte[BufferSize];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("업데이트 파일이 예상 크기를 초과합니다.");
            hash?.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            progress?.Report((double)total / limit);
        }
        return total;
    }

    public void Dispose() => http.Dispose();
}
