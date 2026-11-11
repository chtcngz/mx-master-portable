# v0.2 geliştirme listesi

1. Windows üzerinde `dotnet publish` derleme raporu ve gerçek HID++ cihaz tanılama çıktısı ile doğrulama.
2. BLE ve Bolt eşzamanlı bağlantılar için stabil cihaz kimliği ve slot/arayüz hata toleransı.
3. Hotplug / cihaz uykusu / reconnect takibi, otomatik profil yeniden uygulama.
4. PID 0xB042 `0x2201/0x2202` DPI sorgusu ve aralık-güvenli DPI ayarı.
5. `0x2111` SmartShift okuma/değiştirme, eski değerleri kaydetme.
6. `0x1B04` raw XY eventleri, gesture up/down/left/right ve Haptic Sense paneli.
7. Windows per-device Raw Input ilişkilendirmesi; genel hook sınırlamasını azaltma.
8. Tray icon, cihaz arayüzü overlay, çoklu profiller, özelleştirilebilir kısayollar.

**Güvenlik:** Her HID yazma işlemi öncesi readback + snapshot + rollback ve açık kullanıcı eylemi korunmalı. Haptic veya firmware özellikleri doğrudan deneysel yazılmamalı.
