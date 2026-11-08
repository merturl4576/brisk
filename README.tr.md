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

<!-- TODO-RUN: **Geliştiricinin dizüstünde tek tık:** n GB açıldı (Gezgin'in gösterdiği boş alan farkı) · açılışta başlayan program 14 → n · n telemetri ayarı kapandı · açılış n sn → n sn, Windows'un kendi açılış ölçümüyle. -->

---

## Kurulum

Tek satır, güveniyorsan. Güvenmek zorunda kalma diye betik [63 okunur satır](get.ps1): son sürümü indirir, **SHA-256 özeti yayımlananla tutmuyorsa çalıştırmayı reddeder** ve tek klasöre açar. Kaldırmak, klasörü silmek demek.

```powershell
irm https://raw.githubusercontent.com/merturl4576/brisk/main/get.ps1 | iex
```

Ya da elle: [Releases](https://github.com/merturl4576/brisk/releases) sayfasından **`brisk-app.exe`** dosyasını indir ve çalıştır. Tek dosya. Hiçbir şey kurulmuyor ve sen **Hepsini yap**'a ya da bir sayfanın kendi düğmesine basana kadar bilgisayarında hiçbir şey değişmiyor.

Terminali seviyorsan **`brisk.exe`** indir. `brisk run` aynı planı yazar ve hiçbir şeyi değiştirmez; `brisk run --yes` uygular. `--level mid` ya da `--level max`, Yer aç'ı aşağıda anlatıldığı gibi daha ileri götürür.

İki dosya da .NET kurulumu istemiyor ve hiçbiri diğerine muhtaç değil. İndirdiğini aynı sürümdeki `SHA256SUMS.txt` ile doğrulayabilirsin.

> brisk henüz imzalı değil, bu yüzden SmartScreen ilk açılışta uyaracak. O uyarı haklı. Bu cümleye değil, checksum'a güven.
>
> Windows 11'inde **Smart App Control** açıksa imzasız bir programı "yine de çalıştır" seçeneği bile vermeden durdurur: kurulum betiği olanın bu olduğunu söyler ve brisk imzalanana kadar açılmaz. Smart App Control'ü kapatmak tek yönlü (Ayarlar › Gizlilik ve güvenlik › Windows Güvenliği › Uygulama ve tarayıcı denetimi), brisk için kapatma.

---

## Tek tık ne yapıyor

Genel ekranı makinenin canlı görüntüsüdür: işlemci, ekran kartı ve bellek göstergeleri, sıcaklık, ağ, pil (dizüstünde), makinenin özellikleri ve şu an en çok kullanan uygulamalar. Alttaki şerit brisk'in ne bulduğunu söyler, örneğin *brisk hazır: 12 GB · 9 uygulama · 14 ayar*, yanında da **Hepsini yap** durur. Hepsini yap MIN seviyesinde yer açar, listelenen programları açılıştan çıkarır ve listelenen ayarları kapatır. **Yer aç**, **Hızlandır** ve **Gizlilik** sayfaları aynı satırları listeler ve her biri yalnızca kendi grubunu çalıştırır. Bir satıra dokunursan atlanır.

### Yer aç

Üç seviye var; her biri öncekileri de kapsar. **Hepsini yap** her zaman MIN çalıştırır. Yer aç sayfasında seviyeyi sen seçersin, sayfanın düğmesi o seviyeyi çalıştırır.

| Seviye | Satır | Ne gider, bedeli ne |
|---|---|---|
| MIN | Önbellekler | tüm tarayıcı profillerinin önbellekleri, uygulama önbellekleri, küçük resimler, uygulama çökme dökümleri ve hata raporları, Windows'un geçici dosyaları, Windows Update ve Teslim İyileştirme indirmeleri, güncelleme artıkları. Kendiliğinden yeniden oluşurlar. |
| MIN | Geliştirici önbellekleri | npm, pnpm, pip, NuGet, Yarn, Cargo, Gradle. Gerektiğinde yeniden iner. |
| MIN | Eski çökme raporu | Windows'un 7 günden eski çekirdek dökümleri (LiveKernelReports, Minidump, MEMORY.DMP) |
| MIN | Windows kurulum kayıtları | Windows kurulum klasöründeki (Panther) 7 günden eski `.log` ve `.etl` dosyaları; yükseltme sürerken hiçbiri |
| MIN | Eski sürücü kurulumu | geride kalan, açılmış, 7 günden eski NVIDIA ve AMD kurulum dosyaları |
| MID | Hazırda bekletme dosyası | masaüstünde Hızlı başlatma kapanır, geri açılabilir; dizüstünde pil biterse açık işler kaybolur |
| MID | Oyun gölgelendirici önbelleği | oyunların ilk açılışı biraz yavaşlar |
| MID | İndirilenler: kurulum dosyaları | en az 30 gün önce yazılmış `.exe` ve `.msi` dosyaları, her dosya kendi kutusuyla. Geri alınamaz. |
| MID | Geri dönüşüm kutusu | geri alınamaz |
| MID | Eski projelerin indirme klasörleri | bir `package.json` yanındaki `node_modules` ya da `.next`, veya bir Python venv; projenin diğer üst düzey dosya ve klasörlerinin hepsi 90 gün ya da daha eskiyse. Gerekince `npm install` ile gelir. |
| MAX | Eski Windows kopyası | eski Windows'a dönülemez |
| MAX | Docker'ın kullanılmayan verisi | `docker system prune`: durmuş container'lar silinir |
| MAX | Ev klasöründeki git deposu | doğrudan kullanıcı klasöründeki `.git` klasörü, o deponun geçmişiyle birlikte. Geri alınamaz. |
| MAX | Arşiv (açılmış hali yanında) | 1 GiB ya da daha büyük bir `.7z`, `.zip` veya `.rar`; yanında aynı adı taşıyan ve en az onun kadar bayt tutan bir klasör varsa. Yalnızca arşiv gider. |
| MAX | Eski geri yükleme noktaları | en yenisi dışındaki tüm geri yükleme noktaları. Windows boyutlarını bildirmez, bu yüzden satırda "—" yazar. |

Önbellekler Geri Dönüşüm Kutusu'na taşınmaz, gerçekten silinir; böylece boş alan gerçekten değişir. Uygulaması açık olan bir önbellek, örneğin Chrome ya da Discord, görev çubuğunda bekler ve o uygulama kapanınca temizlenir.

İki satır senin klasörlerinin içine bakar: eski projelerin indirme klasörleri ve açılmış arşivler. Masaüstü, Belgeler, İndirilenler ve alışılmış proje klasörlerinde (`source\repos`, `Projects`, `dev`, `code`, `repos`) dört seviye derine iner ve bir bağlantı noktasının (junction) içine asla girmez. Her yol, silineceği anda diskte kendi kuralına göre yeniden denetlenir.

Sana ait büyük dosyalar *Senin dosyaların · brisk dokunmaz* başlığı altında listelenir: sanal makine diskleri (`.vdi`, `.vhdx`, `.vmdk`; WSL ve Docker'ın kendi depoları hariç), `.iso` kalıpları, Android emülatörleri ve BlueStacks, her biri 1 GiB ya da daha büyük. Hiçbir sayıya katılmazlar ve asla silinmezler.

### Hızlandır

- **Açılış programları.** Görev Yöneticisi'nin listelediği her şey, Görev Yöneticisi'nin kullandığı yolla kapatılır; istediğini oradan geri açabilirsin. Özleyeceklerin dışında hepsi gider: antivirüs, ses, touchpad ve ekran kartı yardımcıları, OneDrive ve diğer eşitleme programları, şifre yöneticileri, VPN'ler ve klavye araçları. Bunlar *Bırakıyorum* altında listelenir, her birinin yanında nedeni yazar.
- **Ayarlar.** Şarjdayken tam güç, oyunda arka planda video kaydı kapalı, oyunlarda gecikmeyi azaltma, pencere modundaki oyunları hızlandırma, pencere açılış animasyonları kapalı. Ekstra çekirdek koruması ve Sanal makine desteği, Microsoft'un kendi oyun rehberi bunları andığı için *İstersen ekle* altında listede; ama ancak sen eklersen çalışırlar: ilki güvenliği bir kademe düşürür, ikincisi WSL ve Docker'ı bozar.

### Gizlilik

- **Ayarlar.** Kullanım bilgilerini Microsoft'a göndermesi, arka planda bilgi toplaması, hata raporları, reklam kimliği ve sana göre öneriler, etkinlik geçmişi, Başlat menüsündeki reklamlar, Başlat'ta arama yapınca internete gitmesi, sesini ve yazdıklarını göndermesi, "Windows'u nasıl buldun?" soruları, Recall, Copilot, güncellemeleri internetteki yabancılara dağıtması (kendi ağındaki paylaşım kalır) ve programların sormadan yönetici olması. Konum *İstersen ekle* altında listede, çünkü Cihazımı bul ona muhtaç.
- **Terminalde.** `brisk live` bir kez yazar: en çok işlemci ve bellek kullanan uygulamaları, hangi uygulamanın şu an hangi şirkete bağlı olduğunu (yönetici olarak çalışırsa Microsoft'un telemetri sunucuları etiketlenir; sunucu adlarını okumak bunu ister) ve Windows'un uygulama başına tuttuğu 30 günlük gönderilen ve alınan veri kaydını. Bağlantı, veri gönderildiği anlamına gelmez; miktarlar sadece miktardır, asla içerik yok.

### Silmeden önce soru sormaz

Pencere çıkmaz. Hepsini yap MIN çalıştırır ve MIN'de hiçbir satır turuncuyla geri gelmez diye işaretli değil: MIN, kendini yeniden kuran önbellekler ve geçici dosyalar, Windows Update indirmeleri ve güncelleme artıkları ile bir daha gerekmeyecek eski kurulum kayıtları, çökme raporları ve sürücü kurulum dosyalarıdır. Geri gelmeyecek bir satır bunu satırın üstünde, turuncuyla söyler ("Geri alınamaz." ya da tam olarak neyin kaybolduğu); bu satırlar MID ve MAX'ta durur ve ancak Yer aç sayfasında o seviyeyi seçersen çalışır. MAX ekranda uyarılır, sorulmaz. Her satır sen bir şeye basmadan önce ekranda ve dokunursan atlanır.

---

## Kanıt

Bu türdeki her araç "tamam" der. brisk uydurmadığı sayıları gösterir. Çalıştırmanın hemen ardından Genel'deki şerit özetler (*Son işlem: …*); `brisk receipt` bu sayıları ne zaman istersen yazar:

- **Yer:** çalıştırmadan önce ve sonra sistem diskindeki boş alan. Gezgin'in gösterdiği sayı, "seçilen öğe" değil.
- **Açılış programları:** önce ve sonra açılışta başlayan program sayısı.
- **Açılış süresi:** Windows'un olay günlüğüne kendi yazdığı açılış ölçümü; son açılışlar ile çalıştırmadan sonraki ilk açılış karşılaştırılır. Yeniden başlattıktan sonra `brisk receipt` bunu yazar.<!-- TODO-RUN: *Son sefer: n GB açıldı · açılış n sn → n sn · n GB RAM boşaldı*, geliştiricinin dizüstünden. -->
- **RAM:** açılıştan çıkarılan programların kullandığı bellek. Ancak sonraki açılış hiçbirinin başlamadığını doğruladıktan sonra sayılır; yine de başlayan olursa adı yazılır ve sayıya katılmaz.
- **Gizlilik:** her ayar çalıştırmadan sonra yeniden okunur ve **Kapandı ✓**, **Windows yok saydı** (bazı sürümler bir ilkeyi yazıp sonra yok sayar) ya da **Geri açıldı** der.

Ters yöne giden bir sayı da gösterilir. Sana sadece iyi sayıyı gösteren araç ölçmüyordur.

### Geri al

**Geri al**, bir çalıştırmadan sonra Genel'de ve Hızlandır ile Gizlilik sayfalarında, o çalıştırmadaki her ayar ve açılış değişikliğini en yeniden başlayarak geri çevirir ve silinen dosyaların geri gelmediğini açıkça söyler. Yer aç'ın geri alması yok: silinen dosyalar gitmiştir ve Geri al hazırda bekletmeyi geri açmaz. Her ayar değiştirilmeden önce bir deftere yazılır; yarıda kalan bir satır bile geri alınabilir. Geri alma başarısız olursa brisk bunu söyler ve çalıştırma geri alınabilir durumda kalır.

---

## brisk'in yapmayı reddettikleri

- **Kayıt defteri temizliği yok.** Windows'u hızlandırdığı hiç gösterilmedi ve kurulu programları bozabiliyor.
- **Windows'un kendi uygulamalarını kaldırmak yok.** Debloat betikleri geri getirmesi zor şeyleri bozar; brisk uygulamaları yerinde bırakır.
- **Geri gelmeyecek bir şeyi önce göstermeden silmek yok.** Önbellekler kendiliğinden yeniden oluşur. Geri gelmeyecek bir satır, bir şey çalışmadan önce ekranda ve turuncuyla işaretli; Hepsini yap hiçbirini içermez.
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
    --level <min|mid|max>    how far Free up space goes (default min)
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

`brisk run`, Hepsini yap gibi MIN seviyesinde yer açar. `brisk clean --level deep` adıyla istenmesi gereken hedefleri dışarıda bırakır: eski Windows kopyası, hazırda bekletme dosyası, bileşen deposu, gölgelendirici önbellekleri, Docker'ın kullanılmayan verisi, ev klasöründeki git deposu, eski projelerin indirme klasörleri, açılmış arşivler ve geri yükleme noktaları. Birini `--target <id>` ile adlandır; kimlikleri `brisk targets` listeler.

`brisk run`, yalnızca bu hedeflerin üstüne kurulu satırları (gölgelendirici önbellekleri, Docker, ev klasöründeki git deposu, indirme klasörleri, açılmış arşivler, eski Windows kopyası, geri yükleme noktaları) hiçbir `--level`'da işaretlemez: planı her birini işaretsiz gösterir, yanında onu bilerek alan `brisk clean --target <id>` yazar.

---

## Bu türün hak ettiği sorular

Bilgisayar "hızlandırıcıları" internetin en az güvenilen yazılımları arasında, haklı nedenlerle. O nedenler, kimse sormadan cevabı hak ediyor.

**"Yılan yağı. Bu araçlar ölçülebilir hiçbir fark yaratmaz."**
Çoğu zaman doğru. brisk bu yüzden ölçüyor: Windows'un bildirdiği boş alan önce ve sonra, Windows'un kendi açılış ölçümleri önce ve sonra, ve ancak sonraki açılış doğruladığında saydığı bir RAM rakamı. Ölçümü olmayan yerde bunu söylüyor.

**"Kayıt defteri temizleyicileri makineleri bozar."**
Bozar. brisk'te yok ve hiç olmayacak.

**"Temizleyici değer verdiğim dosyaları yer."**
Bir desenden değil, bir izin listesinden çalışır. Her yol, bağlantı noktaları çözülerek kayıtlı bir şablona karşı denetlenir; korunan klasörler (Belgeler, Masaüstü, Resimler, OneDrive, sistem kökleri) her şablonu yener. Masaüstü ve Belgeler'in içine bakan iki satırın, eski projelerin indirme klasörleri ile açılmış arşivlerin, şablonu yok: her yol taradıkları bir klasörün içinde olmalı ve silineceği anda diskten yeniden okunarak hâlâ kuralına uymalı. Sanal makine disklerin, `.iso` kalıpların ve emülatörlerin listelenir ama asla silinmez. İndirilenler'deki bir dosya ancak sana gösterilen dosyanın ta kendisiyse silinir: o zamandan beri boyutu ya da tarihi değiştiyse atlanır.

**"Kapalı kaynak, kendisi de eve telefon eden bir exe."**
GPL-3.0 ve ağ kodu yok. `brisk live`'ın yazdığı bağlantı listesi ve 30 günlük kullanım senin makinende Windows'tan okunur; hiçbir şey dışarı çıkmaz.

**"Arkamdan ayarlarımı değiştirir."**
Tek tık ayar değiştirir; amacı da bu. Her satır, çalışmayacak olanlar dahil, sen bir şeye basmadan önce ekranda; sen basmadan hiçbir şey çalışmaz; her ayar değişikliği deftere yazılır ve Geri al ile geri alınabilir.

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

- Her kural, ayar ve kanıt, her birinin ne yaptığını ve neyi reddettiğini sabitleyen 1.782 testte sahte ölçümlere karşı çalışıyor.
- Her sürümü yönlendiren spesifikasyonlar ve uygulama planları [`docs/superpowers`](docs/superpowers) altında açık.
- Her değişiklik birleştirilmeden önce incelendi; commit geçmişi kendi düzeltmelerini yeniden yazmak yerine kaydediyor.
- Her sürüm, yayımlanmadan önce geliştiricinin kendi makinelerinde çalıştı.

Kodun kanıtladığından fazlasını iddia ettiği bir yer bulursan, bu projenin en çok istediği hata raporu odur.

---

## Kaynaktan derleme

.NET 8 SDK gerekir. Sadece Windows: brisk kayıt defterini, WMI'ı, Windows olay günlüğünü ve donanım sensörlerini okuyor; başka bir yerde dürüstçe çalışabilecek bir şey yok.

```
dotnet test brisk.sln -c Release      # 1.782 test
dotnet run --project src/Brisk.Cli -- run
pwsh -File scripts/publish.ps1        # iki tek dosyalık exe artifacts/ içine
```

---

## Durum

Ön sürüm ve bunu saklamıyor: brisk geliştiricinin kendi makinelerinde doğrulandı. Bir satır makineni yanlış okuyorsa, açılmaya değer hata raporu tam olarak budur. `brisk run` çıktısını getir (`--yes` olmadan hiçbir şeyi değiştirmez).

## Lisans

[GPL-3.0](LICENSE). brisk açık kalır: herkes kullanabilir, inceleyebilir ve değiştirebilir; değiştirilmiş bir sürümü dağıtan da kaynağını yayımlamak zorundadır.
