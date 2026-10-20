<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/media/logo-dark.svg">
    <img src="docs/media/logo-light.svg" width="128" height="128" alt="brisk: bir gösterge yayı ve tek ölçüm çentiği">
  </picture>
</p>

# brisk

**Yavaş bir Windows bilgisayar için tek tık. brisk yer açar, programları açılıştan çıkarır, telemetriyi kapatır; sonra her birini Windows'un kendi verdiği sayılarla kanıtlar.**

*Ücretsiz, açık kaynak, satacak hiçbir şeyi olmayan bir CCleaner alternatifi.*

[![CI](https://github.com/merturl4576/brisk/actions/workflows/ci.yml/badge.svg)](https://github.com/merturl4576/brisk/actions/workflows/ci.yml)
[![Sürüm](https://img.shields.io/github/v/release/merturl4576/brisk?include_prereleases)](https://github.com/merturl4576/brisk/releases)
[![Lisans: GPL v3](https://img.shields.io/badge/lisans-GPL--3.0-blue)](LICENSE)
![Windows 10 / 11 x64](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078d4)

Kurulum yok. Hesap yok. Telemetri yok; brisk'in kendisi hiçbir ağ bağlantısı açmaz. Ürünün içinde yapay zekâ yok: her satır, kaynağında okuyabileceğin deterministik bir kural.

[English README](README.md)

<!-- TODO-RUN: docs/media/demo.gif, geliştiricinin kayıtlı denemesinden: aç, Hepsini yap, yeniden başlat, tekrar aç. -->

**Geliştiricinin dizüstünde tek tık:** TODO-RUN GB açıldı (Gezgin'in gösterdiği boş alan farkı) · açılışta başlayan program TODO-RUN 14 → n · TODO-RUN telemetri ayarı kapandı · açılış TODO-RUN sn → n sn, Windows'un kendi açılış ölçümüyle.

---

## Kurulum

Tek satır, güveniyorsan. Güvenmek zorunda kalma diye betik [63 okunur satır](get.ps1): son sürümü indirir, **SHA-256 özeti yayımlananla tutmuyorsa çalıştırmayı reddeder** ve tek klasöre açar. Kaldırmak, klasörü silmek demek.

```powershell
irm https://raw.githubusercontent.com/merturl4576/brisk/main/get.ps1 | iex
```

Ya da elle: [Releases](https://github.com/merturl4576/brisk/releases) sayfasından **`brisk-app.exe`** dosyasını indir ve çalıştır. Tek dosya. Hiçbir şey kurulmuyor ve sen **Hepsini yap**'a basana kadar bilgisayarında hiçbir şey değişmiyor.

Terminali seviyorsan **`brisk.exe`** indir. `brisk run` aynı planı yazar ve hiçbir şeyi değiştirmez; `brisk run --yes` uygular.

İki dosya da .NET kurulumu istemiyor ve hiçbiri diğerine muhtaç değil. İndirdiğini aynı sürümdeki `SHA256SUMS.txt` ile doğrulayabilirsin.

> brisk henüz imzalı değil, bu yüzden SmartScreen ilk açılışta uyaracak. O uyarı haklı. Bu cümleye değil, checksum'a güven.
>
> Windows 11'inde **Smart App Control** açıksa imzasız bir programı "yine de çalıştır" seçeneği bile vermeden durdurur: kurulum betiği olanın bu olduğunu söyler ve brisk imzalanana kadar açılmaz. Smart App Control'ü kapatmak tek yönlü (Ayarlar › Gizlilik ve güvenlik › Windows Güvenliği › Uygulama ve tarayıcı denetimi), brisk için kapatma.

---

## Tek tık ne yapıyor

Pencere açıldığı anda bakmaya başlar. Birkaç saniye içinde Genel ekranı ne kadar yer bulduğunu, Windows ile kaç programın başladığını, şu an kaç uygulamanın internete bağlı olduğunu ve bilgisayarı şu an neyin kastığını gösterir. **Hepsini yap** işaretli her satırı çalıştırır. **Yer aç**, **Hızlandır** ve **Gizlilik** sayfaları aynı satırları listeler; istemediğinin işaretini önceden kaldırabilirsin.

### Yer aç

| Satır | İşaretli | Neden |
|---|---|---|
| Önbellekler ve geçici dosyalar: tüm tarayıcı profilleri, uygulama önbellekleri, küçük resimler, çökme dökümleri, Windows Update ve Teslim İyileştirme indirmeleri, güncelleme artıkları | evet | kendiliğinden yeniden oluşurlar |
| Geliştirici önbellekleri: npm, pnpm, pip, NuGet, Yarn, Cargo, Gradle | evet | gerektiğinde yeniden iner |
| İndirilenler'deki kurulum dosyaları (`.exe` `.msi` `.iso`), her dosya kendi kutusuyla listelenir | evet | önce sorulur, aşağıya bak |
| Geri Dönüşüm Kutusu | evet | önce sorulur |
| Eski Windows kopyası | evet | önce sorulur; eski Windows'a dönülemez |
| Hazırda bekletme dosyası | yalnızca masaüstünde | dizüstü pil biterken hazırda bekletmeye geçer; dosya yoksa açık işler kaybolur |
| Oyun shader önbellekleri | hayır | oyunlar onları yeniden kurarken takılır |
| Docker'ın kullanılmayan verisi | hayır | durmuş container'lar silinir |

Önbellekler Geri Dönüşüm Kutusu'na taşınmaz, gerçekten silinir; böylece boş alan gerçekten değişir. Uygulaması açık olan bir önbellek, örneğin Chrome ya da Discord, görev çubuğunda bekler ve o uygulama kapanınca temizlenir. Masaüstü, Belgeler ve büyük dosyalarına hiç dokunulmaz: brisk en büyüklerini adıyla gösterir ve olduğu gibi bırakır.

### Hızlandır

- **Şu an kasanlar.** En çok işlemci ya da bellek kullanan uygulamalar; on saniye boyunca ölçülür ve uygulamaya göre gruplanır, yani Chrome'un otuz işlemi tek satırdır. **Kapat** uygulamadan kapanmasını ister, böylece sana kaydetmeyi sorabilir. **Zorla kapat** ancak beş saniye sonra hâlâ çalışıyorsa çıkar. Windows'un kendi işlemlerinde düğme yerine tek satırlık açıklama vardır ("Windows güncelleme kuruyor").
- **Açılış programları.** Görev Yöneticisi'nin listelediği her şey, Görev Yöneticisi'nin kullandığı yolla kapatılır; istediğini oradan geri açabilirsin. Özleyeceklerin dışında hepsi işaretli: antivirüs, ses, touchpad ve ekran kartı yardımcıları, OneDrive ve diğer eşitleme programları, şifre yöneticileri, VPN'ler ve klavye araçları. Bunlar işaretsiz kalır, her birinin yanında nedeni yazar.
- **Ayarlar.** Şarjdayken tam güç, oyunda arka planda video kaydı kapalı, oyunlarda gecikmeyi azaltma, pencere modundaki oyunları hızlandırma, pencere açılış animasyonları kapalı. Ekstra çekirdek koruması ve Sanal makine desteği, Microsoft'un kendi oyun rehberi bunları andığı için listede; ama işaretsiz kalır: ilki güvenliği bir kademe düşürür, ikincisi WSL ve Docker'ı bozar.

### Gizlilik

- **Ayarlar.** Kullanım bilgilerini Microsoft'a göndermesi, arka planda bilgi toplaması, hata raporları, reklam kimliği ve sana göre öneriler, etkinlik geçmişi, Başlat menüsündeki reklamlar, Başlat'ta arama yapınca internete gitmesi, sesini ve yazdıklarını göndermesi, "Windows'u nasıl buldun?" soruları, Recall, Copilot, güncellemeleri internetteki yabancılara dağıtması (kendi ağındaki paylaşım kalır) ve programların sormadan yönetici olması. Konum listede ama işaretsiz, çünkü Cihazımı bul ona muhtaç.
- **Şu an konuştukları.** Hangi uygulama şu an hangi şirkete bağlı; iki saniyede bir yenilenir, Microsoft'un telemetri sunucuları etiketlenir. Bağlantı, veri gönderildiği anlamına gelmez; sayfa da bunu söyler.
- **30 günde gönderilenler.** Windows'un uygulama başına tuttuğu gönderilen ve alınan veri kaydı. Sadece miktarlar, asla içerik yok.

### Sorduğu tek soru

Hepsini yap hiçbir şey sormaz; geri gelmeyecek dosyalar silinecekse sorar: İndirilenler'deki kurulum dosyaları, Geri Dönüşüm Kutusu, eski Windows kopyası. O zaman bir pencere bunları boyutlarıyla listeler, her seferinde: **Devam**, **Bunları atla** ya da **Listeyi gör**. Kimse görmediği bir dosyayı kaybetmez.

---

## Kanıt

Bu türdeki her araç "tamam" der. brisk uydurmadığı sayıları gösterir:

- **Yer:** çalıştırmadan önce ve sonra sistem diskindeki boş alan. Gezgin'in gösterdiği sayı, "seçilen öğe" değil.
- **Açılış programları:** önce ve sonra açılışta başlayan program sayısı.
- **Açılış süresi:** Windows'un olay günlüğüne kendi yazdığı açılış ölçümü; son açılışlar ile çalıştırmadan sonraki ilk açılış karşılaştırılır. Yeniden başlattıktan sonra brisk'i açınca Genel ekranı bununla başlar: *Son sefer: TODO-RUN GB açıldı · açılış TODO-RUN sn → n sn · TODO-RUN GB RAM boşaldı*, geliştiricinin dizüstünden.
- **RAM:** açılıştan çıkarılan programların kullandığı bellek. Ancak sonraki açılış hiçbirinin başlamadığını doğruladıktan sonra sayılır; yine de başlayan olursa adı yazılır ve sayıya katılmaz.
- **Gizlilik:** her ayar çalıştırmadan sonra yeniden okunur ve **Kapandı ✓**, **Windows yok saydı** (bazı sürümler bir ilkeyi yazıp sonra yok sayar) ya da **Geri açıldı** der.

Ters yöne giden bir sayı da gösterilir. Sana sadece iyi sayıyı gösteren araç ölçmüyordur.

### Geri al

**Geri al**, o çalıştırmadaki her ayar ve açılış değişikliğini en yeniden başlayarak geri çevirir ve silinen dosyaların geri gelmediğini açıkça söyler. Her ayar değiştirilmeden önce bir deftere yazılır; yarıda kalan bir satır bile geri alınabilir. Geri alma başarısız olursa brisk bunu söyler ve çalıştırma geri alınabilir durumda kalır.

---

## brisk'in yapmayı reddettikleri

- **Kayıt defteri temizliği yok.** Windows'u hızlandırdığı hiç gösterilmedi ve kurulu programları bozabiliyor.
- **Windows'un kendi uygulamalarını kaldırmak yok.** Debloat betikleri geri getirmesi zor şeyleri bozar; brisk uygulamaları yerinde bırakır.
- **Önce listelemeden dosya silmek yok.** Önbellekler kendiliğinden yeniden oluşur; senin yaptığın ya da indirdiğin dosyalar gitmeden önce adıyla gösterilir.
- **Kaynağını gösteremediği hız iddiası yok.** Açılış süresi Windows'un kendi kaydı. brisk'in ölçümü olmayan yerde sayı da yoktur.
- **Telemetri yok, hesap yok, bulut yok, ürünün içinde yapay zekâ yok.** brisk hiçbir ağ bağlantısı açmaz. Bir şey gönderebileceği bir sunucu yok.
- **Sahte aciliyet yok.** Kırmızı "1.247 sorun bulundu!" yok, geri sayım yok, Pro sürüm yok. Satılacak bir şey yok.

---

## Komut satırı

```
brisk — Windows performance diagnostics and cleanup

Usage: brisk <command> [options]

Commands:
  scan                       run diagnostics + cleaner scan
    --json                   emit JSON instead of text
  fix                        apply diagnostic rule fixes
    --all                    apply every Auto rule with a finding
    --rule <id>               apply/undo a single rule
    --undo                   undo the named rule's last fix
    --keep                   commit the display mode currently on screen
    --yes                    actually mutate (otherwise dry-run)
  clean                      reclaim disk space
    --level <safe|developer|deep>  which cleanup level to run
    --target <id>            clean a single target by id (see 'brisk targets')
    --yes                    actually delete (otherwise print plan)
  run                        one click: free space, speed up, privacy
    --group <space|speed|privacy>  only that group
    --yes                    apply (without it: show the plan, change nothing)
  undo                       undo the latest run
  receipt                    before/after numbers of the latest run
  live                       what is slowing you down, who you talk to, who sent what
  targets                    list cleanup targets
  rules                      list diagnostic rules
  report                     save the scan as a shareable PNG (brisk-app.exe only)
  version                    print the engine version
```

`--yes` olmadan `run`, `fix` ve `clean` ne yapacaklarını yazar ve hiçbir şeyi değiştirmez.

---

## Bu türün hak ettiği sorular

Bilgisayar "hızlandırıcıları" internetin en az güvenilen yazılımları arasında, haklı nedenlerle. O nedenler, kimse sormadan cevabı hak ediyor.

**"Yılan yağı. Bu araçlar ölçülebilir hiçbir fark yaratmaz."**
Çoğu zaman doğru. brisk bu yüzden ölçüyor: Windows'un bildirdiği boş alan önce ve sonra, Windows'un kendi açılış ölçümleri önce ve sonra, ve ancak sonraki açılış doğruladığında saydığı bir RAM rakamı. Ölçümü olmayan yerde bunu söylüyor.

**"Kayıt defteri temizleyicileri makineleri bozar."**
Bozar. brisk'te yok ve hiç olmayacak.

**"Temizleyici değer verdiğim dosyaları yer."**
Bir desenden değil, önbelleklerden oluşan bir izin listesinden çalışır. Her yol, bağlantı noktaları çözülerek kayıtlı bir şablona karşı denetlenir; korunan klasörler (Belgeler, Masaüstü, Resimler, OneDrive, sistem kökleri) her şablonu yener. İndirilenler'deki bir dosya ancak sana gösterilen dosyanın ta kendisiyse silinir: o zamandan beri boyutu ya da tarihi değiştiyse atlanır.

**"Kapalı kaynak, kendisi de eve telefon eden bir exe."**
GPL-3.0 ve ağ kodu yok. Bağlantı listesi ve 30 günlük kullanım senin makinende Windows'tan okunur; hiçbir şey dışarı çıkmaz.

**"Arkamdan ayarlarımı değiştirir."**
Tek tık ayar değiştirir; amacı da bu. Her satır, işaretli ya da değil, sen bir şeye basmadan önce ekranda; sen basmadan hiçbir şey çalışmaz; her değişiklik deftere yazılır ve aynı düğmeden geri alınabilir.

**"İmzasız exe. Asla."**
Haklısın. Sürüm SHA-256 özetleriyle geliyor, kurulum betiği tutmayan indirmeyi reddediyor ve kod imzası için başvuru yapılıyor. O zamana kadar SmartScreen'in uyarısı dürüst, bu paragraf da.

**"Bu yapay zekâyla yapılmış. Neden güveneyim?"**
Kısmen öyle; bir sonraki bölüm tam olarak hangi kısmın öyle olduğunu söylüyor. Yazarın sözüne değil, testlere, açık spesifikasyonlara ve kaynağa güven.

---

## Nasıl yapıldı

brisk 2026'da tek bir kişi tarafından, yapay zekâ yardımıyla yazıldı. Bunu açıkça söylemek gerekiyor, çünkü yukarıdaki "yapay zekâ yok" ürünün içinde yok demek, yapımında yok demek değil.

0.10'a kadar brisk sadece teşhis koyuyordu. Dışarıdan deneyen herkes, aralarında geliştiricinin babası ve arkadaşları da var, kurdu, tıkladı ve hiçbir şey hissetmedi: en fazla bir gigabayt açıldı. 0.11 o cevabın istediği yeniden yazım. Ölçütü şuydu: dikkatli bir teknisyenin bu makinede yapacağını yap, gözle görülür şekilde, sonra kanıtla.

Geliştiricinin sahip oldukları: ürün kararları, güvenlik kuralları, mimari (arayüz kodu içermeyen saf bir motor ve üstünde bir WPF penceresi), bu sayfadaki her ret ve brisk'in neyi iddia etmesine izin verildiğine dair her karar. Yapay zekânın (Claude Code) yaptığı: yazılı spesifikasyonlarla, incelenen dallarda, yazımın çoğu.

Bunu dürüst tutan şeyler:

- Her kural, ayar ve kanıt, her birinin ne yaptığını ve neyi reddettiğini sabitleyen 1.604 testte sahte ölçümlere karşı çalışıyor.
- Her sürümü yönlendiren spesifikasyonlar ve uygulama planları [`docs/superpowers`](docs/superpowers) altında açık.
- Her değişiklik birleştirilmeden önce incelendi; commit geçmişi kendi düzeltmelerini yeniden yazmak yerine kaydediyor.
- Her sürüm, yayımlanmadan önce geliştiricinin kendi makinelerinde çalıştı.

Kodun kanıtladığından fazlasını iddia ettiği bir yer bulursan, bu projenin en çok istediği hata raporu odur.

---

## Kaynaktan derleme

.NET 8 SDK gerekir. Sadece Windows: brisk kayıt defterini, WMI'ı, Windows olay günlüğünü ve donanım sensörlerini okuyor; başka bir yerde dürüstçe çalışabilecek bir şey yok.

```
dotnet test brisk.sln -c Release      # 1.604 test
dotnet run --project src/Brisk.Cli -- run
pwsh -File scripts/publish.ps1        # iki tek dosyalık exe artifacts/ içine
```

---

## Durum

Ön sürüm ve bunu saklamıyor: brisk geliştiricinin kendi makinelerinde doğrulandı. Bir satır makineni yanlış okuyorsa, açılmaya değer hata raporu tam olarak budur. `brisk run` çıktısını getir (`--yes` olmadan hiçbir şeyi değiştirmez).

## Lisans

[GPL-3.0](LICENSE). brisk açık kalır: herkes kullanabilir, inceleyebilir ve değiştirebilir; değiştirilmiş bir sürümü dağıtan da kaynağını yayımlamak zorundadır.
