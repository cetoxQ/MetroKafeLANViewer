using System.IO;
using System.IO.Compression;

namespace MetroKafeLANViewer.Net;

/// <summary>MAC adresinin ilk baytlarından üretici adı (Wireshark manuf listesi, programın içinde).</summary>
public static class Oui
{
    // Anahtar: büyük harf onaltılık önek (6, 7 veya 9 hane: /24, /28, /36)
    static readonly Lazy<Dictionary<string, string>> Table = new(Load);

    static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(60000, StringComparer.Ordinal);
        try
        {
            using var s = typeof(Oui).Assembly.GetManifestResourceStream("oui.gz")!;
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            using var r = new StreamReader(gz);
            while (r.ReadLine() is { } line)
            {
                int tab = line.IndexOf('\t');
                if (tab > 0) map[line[..tab]] = line[(tab + 1)..];
            }
        }
        catch { }
        return map;
    }

    /// <summary>Program açılırken arka planda yüklenir; ilk taramayı geciktirmesin.</summary>
    public static void Preload() => Task.Run(() => _ = Table.Value);

    public static string? Lookup(string? mac)
    {
        if (string.IsNullOrEmpty(mac)) return null;
        string hex = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
        if (hex.Length < 6) return null;
        if (IsRandomized(mac)) return null;
        var t = Table.Value;
        // Önce en uzun önek (küçük bloklar), sonra /24
        foreach (int len in new[] { 9, 7, 6 })
            if (hex.Length >= len && t.TryGetValue(hex[..len], out var name)) return Shorten(name);
        return null;
    }

    static readonly string[] Suffixes =
    [
        " co., ltd.", " co.,ltd.", " co., ltd", " co.,ltd", " co. ltd.", " ltd.", " ltd", " inc.", " inc", " incorporation", " incorporated",
        " corporation", " corporate", " corp.", " corp", " limited", " gmbh", " s.a.", " llc", ",", " co.",
    ];

    /// <summary>"ASUSTek COMPUTER INC." → "ASUSTek COMPUTER": listede kısa görünsün.</summary>
    static string Shorten(string name)
    {
        string s = name.Trim();
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (string suffix in Suffixes)
            {
                if (s.Length > suffix.Length + 2 && s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    s = s[..^suffix.Length].TrimEnd();
                    changed = true;
                }
            }
        }
        return s;
    }

    /// <summary>
    /// Yerel yönetilen (rastgele) MAC: telefonlar ve tabletler Wi-Fi'de gizlilik için böyle adres kullanır.
    /// İlk baytın ikinci en düşük biti 1'dir (x2, x6, xA, xE).
    /// </summary>
    public static bool IsRandomized(string? mac)
    {
        if (string.IsNullOrEmpty(mac) || mac.Length < 2) return false;
        return byte.TryParse(mac[..2], System.Globalization.NumberStyles.HexNumber, null, out byte b) && (b & 0x02) != 0;
    }
}
