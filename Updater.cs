using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MetroKafeLANViewer;

public sealed record UpdateInfo(Version Latest, string Tag, string AssetUrl, string? Notes);

public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// GitHub'daki sürümlerden kendini günceller. yayinla.bat her yayında MetroKafeLANViewer.zip'i yükler.
/// Depo gizliyken anahtar gerekir (Ayarlar → Güncelleme); depo herkese açılınca anahtarsız çalışır.
/// Çalışan exe'nin adı değiştirilebilir: eskisi ".eski" yapılır, yenisi yerine konur, program yeniden açılır.
/// </summary>
public static class Updater
{
    public const string DefaultRepo = "cetoxQ/MetroKafeLANViewer";
    const string Asset = "MetroKafeLANViewer.zip";

    static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = true, ConnectTimeout = TimeSpan.FromSeconds(15) })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MetroKafeLANViewer.exe");

    public static Version CurrentVersion
    {
        get
        {
            string? text = FileVersionInfo.GetVersionInfo(ExePath).ProductVersion?.Split('+')[0];
            return Version.TryParse(text, out var v) ? v : new Version(0, 0, 0);
        }
    }

    /// <summary>Daha yeni sürüm varsa bilgisini, yoksa null döner.</summary>
    public static async Task<UpdateInfo?> CheckAsync(AppSettings s)
    {
        string? token = Store.Unprotect(s.Token);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{s.Repo}/releases/latest");
        Authorize(req, token);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        HttpResponseMessage res;
        try
        {
            res = await Http.SendAsync(req);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new UpdateException("GitHub'a ulaşılamadı. İnternet bağlantısını kontrol edin.");
        }
        using (res)
        {
            if (res.StatusCode == HttpStatusCode.Unauthorized)
                throw new UpdateException("GitHub anahtarı geçersiz veya süresi dolmuş.");
            if (res.StatusCode == HttpStatusCode.NotFound)
                throw new UpdateException(token == null
                    ? "Sürüm bulunamadı. Depo gizliyse Ayarlar'dan GitHub anahtarı girin."
                    : $"{s.Repo} deposunda sürüm yok ya da anahtarın bu depoya erişimi yok.");
            if (!res.IsSuccessStatusCode)
                throw new UpdateException($"GitHub yanıtı: {(int)res.StatusCode} {res.ReasonPhrase}");

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                throw new UpdateException($"Sürüm etiketi anlaşılamadı: {tag}");
            if (latest <= CurrentVersion) return null;

            string? url = doc.RootElement.GetProperty("assets").EnumerateArray()
                .Where(a => string.Equals(a.GetProperty("name").GetString(), Asset, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.GetProperty("url").GetString())
                .FirstOrDefault();
            if (url == null) throw new UpdateException($"{tag} sürümünde {Asset} yok.");
            string? notes = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() : null;
            return new(latest, tag, url, notes);
        }
    }

    static void Authorize(HttpRequestMessage req, string? token)
    {
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.UserAgent.ParseAdd("MetroKafeLANViewer/" + CurrentVersion);
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <summary>İndirir, doğrular ve exe'yi değiştirir. Dönüşte çağıran yeni exe'yi başlatıp kapanmalıdır.</summary>
    public static async Task InstallAsync(AppSettings s, UpdateInfo info, Action<string> progress)
    {
        string stage = Path.Combine(Path.GetTempPath(), "MetroKafeLANViewer-" + info.Tag);
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        Directory.CreateDirectory(stage);
        string zip = Path.Combine(stage, Asset);

        using (var req = new HttpRequestMessage(HttpMethod.Get, info.AssetUrl))
        {
            Authorize(req, Store.Unprotect(s.Token));
            req.Headers.Accept.ParseAdd("application/octet-stream");
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) throw new UpdateException($"İndirilemedi: {(int)res.StatusCode}");
            long total = res.Content.Headers.ContentLength ?? 0;
            await using var input = await res.Content.ReadAsStreamAsync();
            await using var output = File.Create(zip);
            var buffer = new byte[1 << 16];
            long done = 0;
            int last = -1, read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                int p = total > 0 ? (int)(done * 100 / total) : 0;
                if (p != last) progress($"İndiriliyor... %{last = p}");
            }
        }

        progress("Kuruluyor...");
        string dir = Path.Combine(stage, "acik");
        try
        {
            ZipFile.ExtractToDirectory(zip, dir);
        }
        catch (InvalidDataException)
        {
            throw new UpdateException("İndirilen dosya bozuk. Tekrar deneyin.");
        }
        string newExe = Path.Combine(dir, "MetroKafeLANViewer.exe");
        string? ver = File.Exists(newExe) ? FileVersionInfo.GetVersionInfo(newExe).ProductVersion?.Split('+')[0] : null;
        if (!Version.TryParse(ver, out var v) || v != info.Latest)
            throw new UpdateException($"Paketteki exe beklenen sürüm değil ({ver ?? "yok"}).");

        string target = ExePath;
        string staged = target + ".yeni", old = target + ".eski";
        File.Copy(newExe, staged, true);
        TryDelete(old);
        if (File.Exists(old)) old = $"{target}.eski-{DateTime.Now:yyyyMMddHHmmss}";
        File.Move(target, old);
        try
        {
            File.Move(staged, target);
        }
        catch
        {
            File.Move(old, target);
            throw;
        }
        try { Directory.Delete(stage, true); } catch { }
    }

    /// <summary>Önceki güncellemeden kalan ".eski" dosyalarını siler.</summary>
    public static void CleanupOldFiles()
    {
        try
        {
            foreach (string f in Directory.EnumerateFiles(Path.GetDirectoryName(ExePath)!, "MetroKafeLANViewer.exe.eski*")) TryDelete(f);
        }
        catch { }
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }
}
