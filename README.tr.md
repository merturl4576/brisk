# brisk

**Windows için ücretsiz, açık kaynak CCleaner alternatifi: bilgisayarının *neden* yavaş olduğunu kanıtıyla söyler — ölçmediği hiçbir şeyi iddia etmez.**

*Windows bilgisayarın hakkında sana doğruyu söyleyen araç.*

[![CI](https://github.com/merturl4576/brisk/actions/workflows/ci.yml/badge.svg)](https://github.com/merturl4576/brisk/actions/workflows/ci.yml)
[![Sürüm](https://img.shields.io/github/v/release/merturl4576/brisk?include_prereleases)](https://github.com/merturl4576/brisk/releases)
[![Lisans: GPL v3](https://img.shields.io/badge/lisans-GPL--3.0-blue)](LICENSE)
![Windows 10 / 11 x64](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078d4)

Kurulum yok. Hesap yok. Telemetri yok. Yapay zekâ yok. Her teşhis, kaynağında okuyabileceğin deterministik bir kural — ve her bulgu, dayandığı kanıtı yanında taşıyor.

[English README](README.md)

<!-- EKRAN GÖRÜNTÜSÜ: Health sayfası bulgular görünürken, artı tek tıkla
     düzeltme ve geri alma GIF'i. İkisi de gerçek makinede uygulama açmayı
     gerektirdiği için Mert'in kararı. -->

---

## Kurulum

[Releases](https://github.com/merturl4576/brisk/releases) sayfasından **`brisk-app.exe`** dosyasını indir ve çalıştır. Tek dosya. Hiçbir şey kurulmuyor, sen "düzelt" demeden makinene hiçbir şey yazılmıyor ve değiştirdiği her şey geri alınabilsin diye kaydediliyor.

Terminali seviyorsan **`brisk.exe`** indir — aynı teşhisler, yönetici sorusu yok, `--json` dahil.

```
brisk scan
```

İki dosya da .NET kurulumu istemiyor ve hiçbiri diğerine muhtaç değil. İndirdiğini aynı sürümdeki `SHA256SUMS.txt` ile doğrulayabilirsin.

> brisk henüz imzalı değil, bu yüzden SmartScreen ilk açılışta uyaracak. O uyarı dürüst bir uyarı — bu cümleye değil, checksum'a güven.

---

## Bir tarama gerçekte neye benziyor

Mockup değil, geliştiricinin kendi makinesinden gerçek çıktı:

```
[! ] Too many programs start with Windows (impact ***)
    13 programs start with Windows. Heavy ones that can be started manually
    instead: WhatsAppDesktop, MSTeams.
[! ] Disk space fragmented across system folders (impact **)
    AppData\Local: 53.9 GB (over threshold); AppData\Roaming: 28.6 GB (over
    threshold); Desktop: 57.7 GB (over threshold); Downloads: 8.7 GB
[i ] temperature: CPU not read — GPU only. Memory integrity is on here, and the
     driver that reads CPU temperature is on Microsoft's vulnerable-driver
     blocklist, so Windows will not load it at any privilege level. brisk does
     not switch that off, and cannot prove it is the only reason here.
Reclaimable — Safe: 2.3 GB, Developer: 3.6 GB, Deep: 5.1 GB (run 'brisk clean')
```

Üçüncü satır projenin tamamının özeti. brisk bir GPU sıcaklığı basıp ona "bilgisayarının sıcaklığı" diyebilirdi. Bunun yerine neyi okuyamadığını, muhtemelen neden okuyamadığını ve **bu sebebi kanıtlayamadığını** söyledi.

---

## brisk'in yapmayı reddettikleri

Bu kategori büyük ölçüde kimsenin kontrol etmediği iddialar üzerine kurulu. brisk'te asıl ürün, **reddettiklerinin listesi**:

- **Registry temizliği yok.** brisk'in alternatifi olduğu araçların en çok eleştirilen özelliği. Bir Windows makinesini hızlandırdığı hiçbir zaman gösterilemedi, buna karşılık kurulu yazılımları bozabiliyor.
- **Ölçülmemiş hız vaadi yok.** brisk, bir düzeltmenin bilgisayarını hızlandırdığını ancak bunu söyleyen bir sayı okuyabiliyorsa söyler. Windows kendi açılış ölçümlerini tutuyor; brisk onları okuyor ve elinde ölçüm yoksa "yok" diyor.
- **"Windows'un veri toplamasını durdurduk" yok.** brisk'in söyleyebileceği en fazla şey şu: "bu ayar şu an kapalı okunuyor, en son şu tarihte baktım."
- **Telemetri, hesap, bulut, yapay zekâ yok.** Makinenden hiçbir şey çıkmıyor. Gönderilecek bir sunucu yok.
- **Sessiz işlem yok.** Her düzeltme eylem günlüğüne yazılıyor; geri alınabilir olanlar da uygulandıkları ekrandan geri alınabiliyor.

---

## Ne yapıyor

- **16 deterministik teşhis kuralı** — güç planı, başlangıç yükü, Windows'un kendi ölçtüğü açılış süresi, monitörün desteklediğinin altında çalışan ekran yenileme hızı, kendi hız değerinin altında çalışan bellek, sıcaklıklar, disk baskısı ve dahası. Her bulgu kanıtını taşıyor.
- **Tek tıkla düzeltme ve geri alma.** Kurallar Auto, Confirm ve Advise diye ayrılıyor; brisk bir Confirm kuralını sormadan uygulamıyor, yalnızca gözlemlediği bir şey için düzeltme düğmesi göstermiyor.
- **Beyaz listeyle çalışan temizleyici** — desenle değil. Yalnızca Windows'un ve uygulamaların kendiliğinden yeniden ürettiği önbellek ve geçici dosyalara dokunuyor. Üç seviye: Safe, Developer, Deep.
- **Gözle görülen bir düzeltme.** 144 Hz monitörün 60 Hz'de çalışması çok yaygın; brisk bu değişikliği yapıp 15 saniye içinde "görüyorum" onayı gelmezse kendiliğinden geri alıyor.
- **İngilizce ve Türkçe** — hem uygulamada hem komut satırında.

### Güvenlik modeli

Temizleyici yalnızca kendi beyaz listesine dokunuyor. Arayüzdeki tek tıkla temizlik alanı hemen boşaltıyor: geri dönüşüm kutusuna atıyor, sonra **sadece az önce attığı öğeleri** kalıcı siliyor — kutundaki başka hiçbir şeye dokunmuyor. Bunun geri alması yok ve bunu çalışmadan önce söylüyor. Seviye bazlı temizlikler ve CLI ise öğeleri geri dönüşüm kutusuna taşıyıp kutuyu boşaltmayı sana bırakıyor. Ayar ve başlangıç düzeltmeleri her zaman geri alınabilir.

---

## Komutlar

```
brisk — Windows performance diagnostics and cleanup

Usage: brisk <command> [options]

Commands:
  scan                       run diagnostics + cleaner scan
    --json                   emit JSON instead of text
  fix                        apply diagnostic rule fixes
    --all                    apply every Auto rule with a finding
    --rule <id>              apply/undo a single rule
    --undo                   undo the named rule's last fix
    --yes                    actually mutate (otherwise dry-run)
  clean                      reclaim disk space
    --level <safe|developer|deep>  which cleanup level to run
    --yes                    actually delete (otherwise print plan)
  targets                    list cleanup targets
  rules                      list diagnostic rules
  version                    print the engine version
```

`--yes` olmadan `fix` ve `clean` sadece ne yapacaklarını yazar, hiçbir şeyi değiştirmez.

---

## Kaynaktan derleme

.NET 8 SDK gerekiyor. Yalnızca Windows — brisk registry, WMI, Windows olay günlüğü ve donanım sensörlerini okuyor; burada başka bir yerde dürüstçe koşabilecek hiçbir şey yok.

```
dotnet test brisk.sln -c Release      # 702 test
dotnet run --project src/Brisk.Cli -- scan
pwsh -File scripts/publish.ps1        # iki tek dosyalık exe -> artifacts/
```

---

## Durum

Ön sürüm ve bunu saklamıyor: brisk şu ana kadar yalnızca geliştiricisinin sahip olduğu makinelerde gerçek donanıma karşı doğrulandı. Bir kural senin makineni yanlış okuyorsa, açılmaya en değer hata kaydı tam olarak odur — yanında `brisk scan --json` çıktısıyla gel.

## Lisans

[GPL-3.0](LICENSE). brisk açık kalır: isteyen kullanır, inceler, değiştirir; ama değiştirilmiş bir sürümü dağıtan herkes kendi kaynağını da açmak zorundadır.
