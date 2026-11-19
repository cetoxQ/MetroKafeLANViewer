# Metro Kafe LAN Viewer

İnternet kafe ağını sürekli tarayan, istemci bilgisayarların IP aralığına başka bir cihaz (ör. elektrik kesintisinden sonra açılıp istemci IP'sini kapan kamera) girdiğinde uyaran küçük bir Windows programı. MyLanViewer benzeri.

## Ne yapar

- Ayarlardaki aralığı (varsayılan: bilgisayarın ağı, ör. `192.168.2.1-254`) paralel tarar: ARP + ping, Windows adı (NetBIOS), DNS adı, açık portlar (RTSP 554, Hikvision 8000, Dahua 37777, XMEye 34567, HTTP, SMB, RDP, VNC, yazıcı) ve web arayüzü başlığı. 254 adres yaklaşık 10 saniye sürer.
- MAC adresinden üreticiyi bulur (Wireshark üretici listesi programın içinde).
- Cihaz türünü tahmin eder: İstemci, Kamera, Ağ cihazı, Yazıcı, Telefon/tablet, Bilgisayar. Tahmin yanlışsa sağ tık ile düzeltilir ve MAC'e göre hatırlanır.
- **İstemci aralığında** (varsayılan `x.x.x.1-110`) istemci olmayan her açık cihazı **ÇAKIŞMA** olarak gösterir: kırmızı şerit, haritada kırmızı kutu, sesli uyarı ve saat yanında bildirim.
- Otomatik tarama (varsayılan dakikada bir); pencere kapatılınca bildirim alanında çalışmaya devam eder.
- Olaylar (yeni cihaz, IP değişikliği, çakışma başladı/bitti) her gün ayrı dosyaya yazılır: `veri\olaylar\yyyy-MM-dd.txt`.
- Liste CSV olarak dışa aktarılabilir (Excel).

## İstemciler nasıl tanınır

Windows adı (NetBIOS) veren ve istemci aralığında olan her bilgisayar istemci sayılır ve MAC'i hatırlanır. Aralıktaki adların çoğu `MASA-012` gibi aynı önekle başlıyorsa, aralık dışında IP almış `MASA-067` gibi bilgisayarlar da istemci olarak tanınır. Kasa gibi başka bilgisayarlar da aralıktaysa Ayarlar'da ad kalıbı yazılabilir (ör. `^MASA`). Bilerek aralıkta duran bir cihaz için sağ tık → "Bu cihaz için uyarma".

## Kayıtlar

Ayarlar ve cihaz kayıtları exe'nin yanındaki `veri` klasöründedir (yazılamıyorsa `%AppData%\MetroKafeLANViewer`).

## Güncelleme

Program GitHub sürümlerini 6 saatte bir denetler; yeni sürüm varsa üstte "Şimdi güncelle" çıkar. Depo gizliyken Ayarlar → Güncelleme'ye bu depoyu okuyabilen bir GitHub anahtarı (fine-grained token, Contents: Read) girilmelidir; depo herkese açılınca anahtar gerekmez.

`MetroKafeLANViewer.exe --guncelle` pencere açmadan günceller (sonuç `veri\guncelleme.txt`). `--gizli` bildirim alanında başlatır.

## Yayınlama (geliştirici)

`yayinla.bat` sürümü artırır, derler, kodu gönderir ve `MetroKafeLANViewer.zip`'i GitHub sürümü olarak yükler. Gerekenler: .NET 10 SDK, `gh` (giriş yapılmış).
