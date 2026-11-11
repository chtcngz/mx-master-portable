# v0.2 değişiklik özeti

- `SupportedModels.cs`: MX Master 3S / 3S for Business / 4 / 4 for Business için x0003 model ID tanıma eklendi.
- `Mx4Session` → `MxMasterSession`: model adı ve model PID'leri cihaz keşfine dahil edildi.
- `HidppConnection`: Bolt alıcısında aynı reader üzerinden birden çok slot ve refcount ile güvenli kapatma.
- `MainForm`: çoklu mouse listesini gösterir; seçimde fiziksel mouse'a ait profil yüklenir.
- `ProfileStore`: cihaz kimliği bazlı ayrı profil dosyaları, v0.1 MX4 profil migrasyonu.
- `HidNative`: MX Master 3S Bluetooth PID tanıma.
- `tests/SupportedModelsTests.csproj`: bilgisayardaki .NET SDK ile çalıştırılabilir model tanıma testi.

Bu değişiklikler Windows'ta henüz derlenip fiziksel MX Master 3S/4 üzerinde doğrulanmadı.
