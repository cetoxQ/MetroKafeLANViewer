using System.Text.RegularExpressions;
using MetroKafeLANViewer.Net;

namespace MetroKafeLANViewer;

/// <summary>
/// Cihaz türünü bulur. Sıra: elle seçilen tür → kamera izleri (üretici, RTSP/kamera portları, web başlığı) →
/// Windows adı (NetBIOS) → ağ cihazı / yazıcı / rastgele MAC'li telefon → bilinmiyor.
/// </summary>
public static class Classifier
{
    static readonly string[] CameraVendors =
    [
        "hikvision", "dahua", "uniview", "ezviz", "imou", "reolink", "foscam", "axis communications", "vivotek",
        "hanwha", "samsung techwin", "avtech", "tiandy", "milesight", "provision", "xiongmai", "longse", "jovision",
        "shenzhen tvt", "sunell", "kedacom", "geovision", "mobotix", "amcrest", "annke", "vstarcam", "wyze", "arlo",
        "hangzhou ezviz", "zhejiang dahua", "hikvisiondig", "huaanzhi", "bosch security", "honeywell security", "idis",
    ];

    static readonly string[] CameraBanners =
    [
        "hikvision", "dahua", "dvr", "nvr", "ipc", "ip camera", "ipcam", "webcam", "netsurveillance", "xmeye", "uniview",
        "app-webs", "dnvrs-webs", "web service", "camera", "ezviz", "imou", "reolink", "web client", "network video",
    ];

    static readonly string[] NetworkVendors =
    [
        "mikrotik", "routerboard", "ubiquiti", "cisco", "zyxel", "ruijie", "cambium", "aruba", "d-link", "netgear",
        "tenda", "tp-link", "juniper", "fortinet", "huawei technologies", "zte", "keenetic", "mercusys",
        "grandstream", "edimax", "linksys", "airties", "sagemcom", "technicolor", "arcadyan", "hp enterprise", "aruba networks",
    ];

    // Ev/kafe tipi Wi-Fi router markaları
    static readonly string[] RouterVendors = ["tp-link", "tenda", "mercusys", "keenetic", "netgear", "d-link", "linksys", "airties", "totolink", "xiaomi communications"];

    static readonly string[] PrinterVendors = ["epson", "brother", "canon", "kyocera", "ricoh", "xerox", "lexmark", "sharp", "konica", "zebra"];

    static readonly string[] MobileVendors = ["apple", "samsung electronics", "xiaomi", "oppo", "vivo", "realme", "oneplus", "honor", "huawei device", "motorola", "google"];

    static readonly string[] CameraNames = ["camera", "cam_", "-cam", "ipc", "ipcam", "nvr", "dvr", "hikvision", "dahua", "ezviz", "imou",
        "chuangmi", "imilab", "mijia", "xiaovv", "isa-", "tapo", "yi-", "wyze"];

    /// <param name="inClientRange">Kalıp boşken Windows bilgisayarlar yalnızca istemci aralığındaysa istemci sayılır.</param>
    /// <param name="clientLike">Kalıp boşken istemci aralığındaki adlardan çıkarılan kalıp (ör. ^MASA-?\d+); aralık dışına düşmüş istemciler için.</param>
    public static (DeviceKind Kind, string Reason) Classify(Device d, Regex? clientPattern, bool inClientRange, Regex? clientLike)
    {
        if (d.ManualKind is { } manual) return (manual, "Elle seçildi");

        string vendor = (d.Vendor ?? "").ToLowerInvariant();
        string banner = (d.Banner ?? "").ToLowerInvariant();
        var ports = d.OpenPorts;

        if (d.IsSelf) return (DeviceKind.Bilgisayar, "Bu bilgisayar");

        // Kamera izleri, Windows adından önce bakılır: istemci IP'sine oturan kamera asıl aranan şey
        if (CameraVendors.FirstOrDefault(vendor.Contains) is { } cv) return (DeviceKind.Kamera, $"Üretici: {d.Vendor}");
        if (!d.NetBios)
        {
            string host = (d.Hostname ?? "").ToLowerInvariant();
            if (CameraNames.FirstOrDefault(host.Contains) is { }) return (DeviceKind.Kamera, $"Cihaz adı: {d.Hostname}");
            if (ports.Contains(37777)) return (DeviceKind.Kamera, "Dahua portu (37777) açık");
            if (ports.Contains(34567)) return (DeviceKind.Kamera, "XMEye portu (34567) açık");
            if (ports.Contains(554)) return (DeviceKind.Kamera, "RTSP (554) açık — görüntü akışı");
            if (CameraBanners.FirstOrDefault(banner.Contains) is { } cb) return (DeviceKind.Kamera, $"Web arayüzü: {d.Banner}");
            if (ports.Contains(8000) && ports.Contains(80)) return (DeviceKind.Kamera, "Hikvision portu (8000) açık");
        }

        if (d.NetBios || d.WasClient)
        {
            // NetBIOS adı olan Windows bilgisayar: ad kalıbına uyuyorsa (ya da kalıp boşsa) istemci
            if (!d.NetBios || d.Hostname == null) return (DeviceKind.Istemci, "Daha önce istemci olarak görüldü");
            if (clientPattern != null)
                return clientPattern.IsMatch(d.Hostname)
                    ? (DeviceKind.Istemci, $"Windows adı: {d.Hostname}")
                    : (DeviceKind.Bilgisayar, $"Windows adı istemci kalıbına uymuyor: {d.Hostname}");
            if (inClientRange) return (DeviceKind.Istemci, $"Windows adı: {d.Hostname}");
            if (clientLike?.IsMatch(d.Hostname) == true) return (DeviceKind.Istemci, $"İstemci adı ({d.Hostname}), ama istemci aralığı dışında");
            return (DeviceKind.Bilgisayar, $"Windows bilgisayar (istemci aralığı dışında): {d.Hostname}");
        }

        if (d.IsGateway) return (DeviceKind.Modem, "Ağ geçidi (internet modemi)");
        if (ports.Contains(9100) || PrinterVendors.Any(vendor.Contains)) return (DeviceKind.Yazici, ports.Contains(9100) ? "Yazıcı portu (9100) açık" : $"Üretici: {d.Vendor}");
        // DNS (53) sunan cihaz kendi ağını dağıtıyordur: Wi-Fi router
        if (ports.Contains(53)) return (DeviceKind.Router, "DNS (53) açık — yönlendirici" + (d.Vendor != null ? $", {d.Vendor}" : ""));
        if (RouterVendors.FirstOrDefault(vendor.Contains) is { }) return (DeviceKind.Router, $"Üretici: {d.Vendor}");
        if (NetworkVendors.FirstOrDefault(vendor.Contains) is { }) return (DeviceKind.AgCihazi, $"Üretici: {d.Vendor}");
        if (ports.Contains(445) || ports.Contains(3389)) return (DeviceKind.Bilgisayar, "Windows paylaşım/RDP portu açık");
        if (Oui.IsRandomized(d.Mac)) return (DeviceKind.Mobil, "Rastgele (gizli) MAC — telefon/tablet Wi-Fi'si");
        if (MobileVendors.Any(vendor.Contains)) return (DeviceKind.Mobil, $"Üretici: {d.Vendor}");
        if (d.Hostname != null) return (DeviceKind.Bilgisayar, $"Ad: {d.Hostname}");
        return (DeviceKind.Bilinmiyor, d.Vendor != null ? $"Üretici: {d.Vendor}" : "Tanınmadı");
    }
}
