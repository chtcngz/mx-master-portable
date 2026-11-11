# MX Master Portable v0.2 — MX Master 3S ve MX Master 4

**Deneysel kaynak proje.** C#/.NET 8 WinForms · Windows 10/11 x64 · Bluetooth ve Logi Bolt.

**ÖNEMLİ:** Buradaki kod Linux ortamında güncellendi; Windows'ta **derlenmiş ve gerçek fareyle test edilmiş değildir**. ZIP kaynak kod içerir, EXE içermez. Kaynak kodun bulunması ve model ID eşleşmesi, şirket bilgisayarında sorunsuz HID erişimi garantilemez.

## Desteklenen modeller

| Model | Bluetooth HID++ PID | Logi Bolt üzerinden cihaz PID'si |
|---|---|---|
| MX Master 3S | `B034` | `B043` |
| MX Master 3S for Business | `B035` | `B044` |
| MX Master 4 | `B042` | `B042` (cihazın yanıtıyla doğrulanır) |
| MX Master 4 for Business | `B048` | `B048` (cihazın yanıtıyla doğrulanır) |

**Not:** Bolt USB alıcısının Windows HID arayüz PID'si tipik olarak `C548` olur. Bu, mouse model PID'si değildir. Uygulama her Bolt slotundan HID++ `0x0003` cihaz bilgisini sorgular ve yanıtın **7..12. baytlarındaki** model ID'leri doğrular; alıcının kendi PID'sini model yerine kullanmaz. Bazı firmware/bağlantı farklılıklarında cihazın bu tabloda bildirilen ID'si değişebilir; tanılama günlüğünü inceleyin.

Model tanımada kendi cihazını gerçekten yanıtlayan bir fiziksel mouse doğrulanmadan **tuş yönlendirme yazma komutu gönderilmez**.

## v0.2 özellikleri

- Bluetooth HID++ cihaz indeksini `0xFF`, Bolt alıcı slotlarını `1..6` tarar.
- Aynı Bolt alıcısında bir **MX Master 3S ve MX Master 4'ü birlikte** listeleyebilir; her slot tek HID okuyucusu üzerinden çağrı ve bildirim gönderir.
- Modelin gerçek kontrol tablosunu `0x1B04` ile okur; `divert + reprog` bildirilen kontrolleri yapılandırılabilir gösterir.
- Desteklenen fiziksel tuşu geçici olarak yazılıma yönlendirir, tuşa basıldığında ön tanımlı Windows kısayolunu yollar; durdurulunca eski geçici yönlendirme durumunu geri yükler.
- **Fiziksel mouse bazlı ayrı profiller:** Bluetooth veya Bolt üzerinden aynı `unit_id` algılanırsa aynı tuş ataması kullanılır. İki ayrı fiziksel mouse birbirinin ayarlarını değiştirmez.
- İsteğe bağlı, **bütün fareleri etkileyen** Windows geri/ileri/orta tuş fallback'i mevcuttur. Seçeneği açmadan kullanılmaz.
- Tek kullanıcı klasöründe `recovery.json` (önce kaydedilen HID++ reporting durumları) ve `profiles/<sha256-id>.json` (her cihazın profili) tutulur.
- v0.1'in `profile.json` dosyası **yalnızca MX Master 4 için** geriye dönük okunabilir. Eski `recovery.json` içindeki ham unit ID de MX Master 4 için tanınır.

**Bu sürümde YOK:** DPI ayarı, SmartShift hassasiyet ayarı, yatay teker özelleştirme, gesture swipe algılama ve MX Master 4'ün haptic ayarı. 3S'de Haptic Sense paneli bulunmaz. Bu özellikler ayrı aşama.

## Derleme (SDK olan herhangi bir Windows 10/11 x64 bilgisayarda)

1. ZIP dosyasını çıkarın.
2. `.NET 8 SDK` bulunan bir bilgisayarda `build-portable.cmd` çalıştırın.
3. `publish\MXMasterPortable.exe` oluşacaktır. Bu EXE kendi .NET çalışma zamanını taşır (*self-contained*), yönetici yetkisi veya .NET runtime kurulumu gerektirmeden açılacak şekilde tasarlanır.
4. İzin verilen yöntemle EXE'yi kurumsal bilgisayara aktarın ve normal kullanıcı hesabıyla başlatın.
5. **Cihazları tara** → 3S veya 4 cihazını seç → `HID++` ile etiketlenmiş tuşu yapılandır → **Atamaları uygula**.
6. **Durdur / geri al** düğmesi fiziksel mouse'un eski yönlendirme durumunu geri yükler.

Doğrudan komut:

```powershell
dotnet publish .\MXMasterPortable.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o .\publish
```

İstersen model-kimliği eşleştirme testlerini SDK bilgisayarında çalıştır:

```powershell
dotnet run --project .\tests\SupportedModelsTests.csproj
```

Bu testler yalnızca HID++ yanıtı çözümlemeyi test eder; Windows HID bağlantısını ve tuşları test etmez.

## Güvenli ilk test sırası

1. Mouse'u uyandır ve ilk denemede **yalnızca bir bağlantı üzerinden aktif** kullan; sonra diğer bağlantıyı dene.
2. Uygulamayı açıp **Cihazları tara**. Günlükte doğru model, transport, slot, model PID'leri, `0x1B04` index ve listelenen kontrol sayısını kontrol et.
3. **Windows genel tuşları yakala** kutusunu şimdilik boş bırak.
4. Listelenen ve değiştirilebilir bir fiziksel tuşa `F13` atayarak deneyebilirsin. F13 birçok programda görünür bir eyleme sahip olmadığı için testten sonra kendi kısayolunu seç.
5. **Durdur / geri al** ile değişikliğin kapandığını kontrol et.
6. İki farklı mouse'u aynı Bolt alıcısına bağlıyorsan, her birini sırayla seçip profilin birbirine karışmadığını dene.

## Uyarılar ve sınırlar

- Kurulum gerektirmemesi **Windows HID erişimi veya kurumsal AppLocker/WDAC/antivirüs kısıtlamalarını aşacağı anlamına gelmez**. Kurum politikasına uygun çalıştırın.
- Hiçbir sürücü, servis veya elevated süreç yüklenmez. HID vendor koleksiyonuna Windows erişimi engellenmişse uygulama o fareyi konfigüre edemez.
- Başka bir HID++ remapper `divert` etmişse çatışmayı önlemek için o tuşu yeniden yönlendirmez.
- Aynı mouse'ta hem Bluetooth hem Bolt görünen iki iletişim yolu olabilir. Aktif bağlantıdaki cihazın yanıt verdiğinden emin olun. Bağlantı değişirse yeniden tarama gerekebilir.
- Kısayollar yalnızca uygulama aktifken gerçekleşir. Firmware'e kalıcı programlama yapılmaz.
- Windows fallback `WH_MOUSE_LL` global hook kullanır, diğer fiziksel fareleri de etkiler. İsteğe bağlıdır; varsayılan **kapalıdır**.
- Kurtarma başarısız olursa `recovery.json` silinmez. Aynı mouse'u tekrar bağlayıp **Önceki ayarı kurtar** deneyin.
- Kaynak projenin statik gözden geçirilmiş olması derleme veya gerçek cihaz testinin yerini tutmaz.

## Kaynaklar

- [OpenLogi model ID bilgileri (B034, B043, B035, B044)](https://openlogi.org/hidpp/model-ids)
- [OpenLogi cihaz listesi](https://openlogi.org/docs/supported-devices)
- [HID++ deviceInformation 0x0003](https://openlogi.org/hidpp/features/x0003-device-information)
- [HID++ 0x1B04 tuş protokolü](https://lekensteyn.nl/files/logitech/x1b04_specialkeysmsebuttons.html)
- [Logitech HID++ 2.0](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/README.rst)
