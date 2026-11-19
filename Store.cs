using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetroKafeLANViewer.Net;

namespace MetroKafeLANViewer;

public sealed class AppSettings
{
    /// <summary>Taranacak aralıklar, virgülle ayrılır: "192.168.2.1-254".</summary>
    public string ScanRanges { get; set; } = IpRange.DefaultScanRange();
    /// <summary>İstemcilerin DHCP'den aldığı aralık; buradaki istemci olmayan her cihaz çakışmadır.</summary>
    public string ClientRanges { get; set; } = IpRange.DefaultClientRange();
    /// <summary>İstemci bilgisayar adı kalıbı (düzenli ifade). Boşsa Windows adı olan her bilgisayar istemci sayılır.</summary>
    public string ClientNamePattern { get; set; } = "";
    public bool AutoScan { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public int PingTimeoutMs { get; set; } = 1000;
    public bool Sound { get; set; } = true;
    public bool Notify { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public string Repo { get; set; } = Updater.DefaultRepo;
    /// <summary>Gizli depodan güncelleme için GitHub anahtarı (bu bilgisayara özel şifreli). Depo herkese açıksa gerekmez.</summary>
    public string? Token { get; set; }
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Liste görünümündeki son seçilen sekme ve süzgeç.</summary>
    public int Tab { get; set; }
}

/// <summary>Cihaz kayıtları: cihazlar ve her istemci IP'sinde en son görülen istemci.</summary>
public sealed class DeviceFile
{
    public List<Device> Devices { get; set; } = [];
    /// <summary>IP → o IP'de en son görülen istemcinin anahtarı (çakışmada "MASA-045'in IP'si" diyebilmek için).</summary>
    public Dictionary<string, string> ClientIps { get; set; } = [];
}

/// <summary>
/// Ayarlar ve kayıtlar exe'nin yanındaki "veri" klasöründe durur (taşınabilir); oraya yazılamıyorsa
/// %AppData%\MetroKafeLANViewer kullanılır.
/// </summary>
public static class Store
{
    public static readonly string Folder = PickFolder();
    static string SettingsFile => Path.Combine(Folder, "ayarlar.json");
    static string DevicesFile => Path.Combine(Folder, "cihazlar.json");
    public static string LogFolder => Path.Combine(Folder, "olaylar");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static string PickFolder()
    {
        string exeDir = AppContext.BaseDirectory;
        string local = Path.Combine(exeDir, "veri");
        try
        {
            Directory.CreateDirectory(local);
            string probe = Path.Combine(local, ".yazma");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return local;
        }
        catch
        {
            string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroKafeLANViewer");
            Directory.CreateDirectory(app);
            return app;
        }
    }

    public static AppSettings LoadSettings() => Read<AppSettings>(SettingsFile) ?? new();
    public static void SaveSettings(AppSettings s) => Write(SettingsFile, s);
    public static DeviceFile LoadDevices() => Read<DeviceFile>(DevicesFile) ?? new();
    public static void SaveDevices(DeviceFile f) => Write(DevicesFile, f);

    static T? Read<T>(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) : default;
        }
        catch
        {
            // Bozuk dosya silinmez, kenara alınır
            try { File.Copy(file, file + ".bozuk", true); } catch { }
            return default;
        }
    }

    static void Write<T>(string file, T value)
    {
        try
        {
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, file, true);
        }
        catch { }
    }

    // ---------------------------------------------------------------- anahtar

    static readonly byte[] Entropy = "MetroKafe.LANViewer"u8.ToArray();

    public static string Protect(string text) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text.Trim()), Entropy, DataProtectionScope.LocalMachine));

    public static string? Unprotect(string? data)
    {
        if (string.IsNullOrEmpty(data)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(data), Entropy, DataProtectionScope.LocalMachine));
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- olay günlüğü

    public static void AppendLog(EventEntry e)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            // UTF8Encoding(true): yeni dosyanın başına BOM yazılır, Not Defteri Türkçe karakterleri doğru gösterir
            File.AppendAllText(Path.Combine(LogFolder, $"{e.Time:yyyy-MM-dd}.txt"), $"{e.Time:HH:mm:ss}\t{e.LevelText}\t{e.Text}\r\n", new UTF8Encoding(true));
        }
        catch { }
    }
}

public enum EventLevel { Bilgi, Uyari, Alarm, Duzeldi }

public sealed record EventEntry(DateTime Time, EventLevel Level, string Text)
{
    public string TimeText => Time.ToString("dd.MM HH:mm:ss");
    public string LevelText => Level switch
    {
        EventLevel.Alarm => "ALARM",
        EventLevel.Uyari => "Uyarı",
        EventLevel.Duzeldi => "Düzeldi",
        _ => "Bilgi",
    };
}
