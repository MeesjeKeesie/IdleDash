# IdleDash

Een dashboard voor je extra scherm. Het verschijnt vanzelf zodra daar geen vensters meer staan, en verdwijnt weer als je er iets naartoe sleept. Het neemt nooit je toetsenbord over.

*English below.*

<!-- Tip: maak een screenshot van je dashboard, zet hem in docs/screenshot.png en haal dan de eerste en laatste regel van dit commentaar weg.
![IdleDash](docs/screenshot.png)
-->

## Wat staat erop

- Klok en datum (24- of 12-uurs)
- Weer en regenradar: Buienradar in Nederland en België, daarbuiten Open-Meteo
- Je afspraken uit Google Agenda, Apple iCloud en agenda-links (.ics), en afspraken toevoegen met de plusknop
- Je taken uit Google Taken, met wat het eerst af moet bovenaan
- Nieuws en RSS: NOS, BBC, of elke website met een feed (ook YouTube-kanalen en Reddit)
- Foto's als diavoorstelling uit een map op je pc
- Een aftelklok, bijvoorbeeld tot je vakantie of een verjaardag
- Smarthome: Home Assistant, Philips Hue en Shelly bedienen, ook in groepen, met kleur, wittint en helderheid
- Een melding als de deurbel gaat (via ntfy): je muziek pauzeert en er klinkt een geluid
- Aandelen, indexen en crypto, met een grafiekje van de dag
- Snelkoppelingen naar je apps en websites
- Wat er speelt in Spotify of je browser, met vorige, pauze, volgende en shuffle
- Je Spotify-playlists starten met één klik (Spotify Premium), en tegels voor playlists van Apple Music, YouTube Music en andere diensten
- PC-stats (processor, videokaart, geheugen) en een focustimer
- Thema's: kant-en-klaar of zelf gemaakt, met je eigen foto (zelf de uitsnede kiezen) of een wisselende fotomap als achtergrond, ook per widget
- Een nachtmodus die het scherm 's avonds dimt
- Back-ups van al je instellingen, ook elke dag automatisch
- Nederlands en Engels

Je kiest zelf welke onderdelen je ziet, waar ze staan en hoe groot ze zijn.

## Downloaden en installeren

1. Ga naar [Releases](https://github.com/MeesjeKeesie/IdleDash/releases/latest) en download **IdleDash-Setup-….exe**.
2. Start het bestand. Zegt Windows "Windows heeft uw pc beschermd", klik dan op **Meer info** en **Toch uitvoeren**. Die melding krijg je bij programma's van kleine makers die geen betaald certificaat hebben.
3. Klaar. IdleDash staat nu bij de klok rechtsonder (een icoontje met een maan).

Liever niets installeren? Download dan **IdleDash-…-portable.zip**, pak hem uit en start IdleDash.exe.

Voor Windows 10 (versie 2004 of nieuwer) en Windows 11. Je hebt verder niets nodig.

## Eerste keer

- Heb je meerdere schermen, dan kiest IdleDash vanzelf je bovenste extra scherm (nooit je hoofdscherm). Een ander scherm kies je in de instellingen.
- Klik op het IdleDash-icoon bij de klok voor de instellingen: taal, thema, je plaats voor het weer, agenda's, smarthome en meer.
- Beweeg je muis over het dashboard en klik op het potlood om widgets te verplaatsen, groter te maken of toe te voegen. Het tandwieltje op een widget opent de instellingen van alleen die widget (bijvoorbeeld welke nieuwsbronnen of welke fotomap).

Windows 11 verstopt nieuwe icoontjes achter het pijltje (^) bij de klok. Sleep het icoon naar de taakbalk om hem altijd te zien.

## Agenda's

**Google:** klik in de instellingen op **Koppelen met Google** en log in. Google laat eerst een waarschuwing zien dat de app niet door Google is geverifieerd. Klik op **Geavanceerd** en daarna op **Ga naar IdleDash**. Had je Google al gekoppeld in een oudere versie? Klik dan één keer op **Opnieuw koppelen** om ook afspraken te kunnen toevoegen.

**Apple iCloud:** je hebt een *app-specifiek wachtwoord* nodig (niet je gewone wachtwoord). Maak dat aan op [account.apple.com](https://account.apple.com) bij Inloggen en beveiliging, App-specifieke wachtwoorden, en vul het samen met je Apple ID in bij de instellingen.

**Agenda-links:** plak een gedeelde .ics- of webcal-link, bijvoorbeeld van Outlook, de afvalkalender van je gemeente of school.

IdleDash leest je agenda's en taken. Afspraken toevoegen, taken afvinken of toevoegen gebeurt alleen als jij daarop klikt.

## Smarthome

- **Home Assistant:** vul het adres in (meestal `http://homeassistant.local:8123`) en een token. Een token maak je in Home Assistant: klik linksonder op je naam, ga naar Beveiliging en maak een *langlevend toegangstoken* aan. Via Home Assistant kun je ook apparaten van de meeste andere merken en Zigbee-apparaten bedienen.
- **Philips Hue:** klik op Zoeken, druk op de ronde knop van je Hue-bridge en klik binnen 30 seconden op Koppelen.
- **Shelly:** vul het IP-adres van je Shelly in (staat in de Shelly-app).

Voeg daarna de widget Smarthome toe en kies met het tandwieltje welke apparaten je wilt zien. Een klik op een tegel zet hem aan of uit; met het kleurknopje op een lamp kies je kleur, wittint en helderheid. Bij Instellingen > Smarthome maak je groepen van lampen en schakelaars, ook van verschillende merken. Sloten, alarm en garagedeuren vragen altijd om een bevestiging, want iedereen bij je scherm kan klikken. Alles gaat rechtstreeks binnen je eigen netwerk.

## Foto's

Kies met het tandwieltje van de widget een map, bijvoorbeeld een map die met OneDrive of Google Drive synchroniseert. Voor iPhone-foto's (HEIC) heeft Windows twee gratis uitbreidingen uit de Microsoft Store nodig; de widget geeft je de knoppen als het nodig is.

## Spotify

Met de widget Playlists start je een playlist met één klik. Dit werkt alleen met Spotify Premium. Spotify vraagt sinds 2026 dat iedereen daarvoor eenmalig een eigen "Spotify-app" aanmaakt:

1. Ga naar [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) en log in.
2. Klik op **Create app** en vul een naam en korte beschrijving in, bijvoorbeeld IdleDash.
3. Vul bij **Redirect URIs** precies `http://127.0.0.1:45631/callback` in en klik op **Add**.
4. Vink **Web API** aan, ga akkoord met de voorwaarden en klik op **Save**.
5. Kopieer de **Client ID**, plak hem in IdleDash bij Instellingen > Spotify en klik op **Koppelen met Spotify**.

Voeg daarna de widget Playlists toe en kies met het tandwieltje welke playlists erop komen. Daar kun je ook een link plakken naar een playlist uit Apple Music, YouTube Music, Deezer, Tidal of SoundCloud. Die opent IdleDash dan in de app of je browser; YouTube Music begint meestal meteen, bij Apple Music druk je zelf op play.

## Deurbel

IdleDash kan meeluisteren met [ntfy](https://ntfy.sh), een gratis dienst voor meldingen. Laat je deurbel (bijvoorbeeld een ESP32) een bericht sturen naar een onderwerp op ntfy; je telefoon met de ntfy-app en IdleDash krijgen het dan tegelijk. Stel het in bij Instellingen > Deurbel: vul hetzelfde onderwerp in en kies of je muziek moet pauzeren en welk geluid er klinkt (de ingebouwde dingdong of je eigen geluid). Kies een lange, moeilijk te raden naam voor het onderwerp, want op ntfy.sh kan iedereen meelezen die de naam kent.

Je deurbel stuurt een gewoon webverzoek: een POST naar `https://ntfy.sh/jouw-onderwerp`, met als tekst bijvoorbeeld "Er wordt aangebeld".

## Back-up

Bij Instellingen > Back-up bewaar je al je instellingen in één bestand (indeling, widgets, thema's en koppelingen) en zet je ze later terug. IdleDash maakt daarnaast elke dag automatisch een back-up en bewaart de laatste 10. Wachtwoorden en tokens werken alleen op dezelfde pc met hetzelfde Windows-account; op een andere pc koppel je die opnieuw.

## Updates

IdleDash werkt zichzelf bij. Een nieuwe versie wordt op de achtergrond gedownload en gecontroleerd, en jij kiest wanneer IdleDash even herstart: klik op de melding, of ga naar Instellingen > Over IdleDash. Automatisch bijwerken kun je daar ook uitzetten. Je instellingen blijven altijd bewaard.

Gebruik je de portable-versie, dan krijg je alleen een melding en download je de nieuwe versie zelf.

Wat er in elke versie veranderd is, staat in [CHANGELOG.md](CHANGELOG.md).

## Privacy

Alles blijft op je eigen pc: er is geen server van IdleDash die je gegevens ziet. Je iCloud-wachtwoord en smarthome-tokens worden versleuteld met je Windows-account opgeslagen. Lees het [privacybeleid](https://meesjekeesie.github.io/IdleDash/privacy.html).

## Verwijderen

Via Windows-instellingen > Apps > Geïnstalleerde apps > IdleDash > Verwijderen. Je Google-login wordt dan ook gewist. Je instellingen staan nog in `%AppData%\IdleDash`; die map kun je zelf weggooien.

## Zelf bouwen

Open `IdleDash.csproj` in Visual Studio 2022 en druk op F5. Alles over ontwikkelen, vertalen, de Google-sleutel en nieuwe versies uitbrengen staat in [ONTWIKKELEN.md](ONTWIKKELEN.md).

## Bronnen

Weer, plaatsen en regen buiten Nederland: [Open-Meteo](https://open-meteo.com). Regen in Nederland en België: [Buienradar](https://www.buienradar.nl). Nieuws: de feeds die je zelf kiest, zoals [NOS](https://nos.nl) en [BBC](https://www.bbc.co.uk/news). Koersen: [Yahoo Finance](https://finance.yahoo.com), alleen voor persoonlijk gebruik. Muziek: [Spotify](https://www.spotify.com). Deurbel: [ntfy](https://ntfy.sh). IdleDash is niet verbonden aan Google, Apple, Buienradar, de NOS, de BBC, Yahoo, ntfy, Spotify, Signify (Philips Hue), Shelly of Home Assistant.

## Licentie

MIT, zie [LICENSE](LICENSE).

---

## English

IdleDash is a dashboard for your extra screen. It appears by itself as soon as no windows are left on that screen, and disappears again when you drag something onto it. It never takes over your keyboard.

**What's on it:** clock, weather and rain radar, your events from Google Calendar, Apple iCloud and calendar links (and adding events), Google Tasks, news and RSS (BBC, NOS or any site with a feed), a photo slideshow, countdowns, smart home control for Home Assistant, Philips Hue and Shelly (with groups, colors and white tones), doorbell notifications via ntfy (pausing your music and playing a sound), one-click Spotify playlists (Premium), stocks and crypto, shortcuts to your apps and websites, backups, what's playing in Spotify or your browser, PC stats, a focus timer, themes (including your own photo as a background, also per widget) and a night mode. The app is available in English and Dutch.

**Install:** download **IdleDash-Setup-….exe** from [Releases](https://github.com/MeesjeKeesie/IdleDash/releases/latest). If Windows says "Windows protected your PC", click **More info** and **Run anyway**. Then click the IdleDash icon next to the clock, open the settings and choose **English** under Language and display (by default IdleDash follows your Windows language).

**Google:** Google shows a warning that the app isn't verified. Click **Advanced** and then **Go to IdleDash**. **Apple iCloud** needs an app-specific password from [account.apple.com](https://account.apple.com).

**Privacy:** everything stays on your own PC; there is no IdleDash server. See the [privacy policy](https://meesjekeesie.github.io/IdleDash/privacy.html). IdleDash updates itself; see [CHANGELOG.md](CHANGELOG.md) for what's new.
