# IdleDash

Een dashboard voor je extra scherm. Het verschijnt vanzelf zodra daar geen vensters meer staan, en verdwijnt weer als je er iets naartoe sleept. Het neemt nooit je toetsenbord over.

<!-- Tip: maak een screenshot van je dashboard, zet hem in docs/screenshot.png en haal dan de eerste en laatste regel van dit commentaar weg.
![IdleDash](docs/screenshot.png)
-->

## Wat staat erop

- Klok en datum
- Weer en Buienradar (regen voor de komende twee uur)
- Je afspraken uit Google Agenda en je taken uit Google Taken
- Wat er speelt in Spotify of je browser, met vorige, pauze en volgende
- Nieuwskoppen van de NOS
- PC-stats (processor, videokaart, geheugen)
- Een focustimer
- Een nachtmodus die het scherm 's avonds dimt

Je kiest zelf welke onderdelen je ziet, waar ze staan en hoe groot ze zijn.

## Downloaden en installeren

1. Ga naar [Releases](https://github.com/MeesjeKeesie/IdleDash/releases/latest) en download **IdleDash-Setup-….exe**.
2. Start het bestand. Zegt Windows "Windows heeft uw pc beschermd", klik dan op **Meer info** en **Toch uitvoeren**. Die melding krijg je bij programma's van kleine makers die geen betaald certificaat hebben.
3. Klaar. IdleDash staat nu bij de klok rechtsonder (een icoontje met een maan).

Liever niets installeren? Download dan **IdleDash-…-portable.zip**, pak hem uit en start IdleDash.exe.

Voor Windows 10 (versie 2004 of nieuwer) en Windows 11. Je hebt verder niets nodig.

## Eerste keer

- Heb je meerdere schermen, dan kiest IdleDash vanzelf je bovenste extra scherm (nooit je hoofdscherm). Een ander scherm kies je in de instellingen.
- Klik op het IdleDash-icoon bij de klok voor de instellingen: je plaats voor het weer, de nachtmodus, de nieuwsrubriek en meer.
- Beweeg je muis over het dashboard en klik op het potlood om widgets te verplaatsen, groter te maken of toe te voegen.

Windows 11 verstopt nieuwe icoontjes achter het pijltje (^) bij de klok. Sleep het icoon naar de taakbalk om hem altijd te zien.

## Google Agenda en Taken

Klik in de instellingen op **Koppelen met Google** en log in. Google laat eerst een waarschuwing zien dat de app niet door Google is geverifieerd. Klik op **Geavanceerd** en daarna op **Ga naar IdleDash** om verder te gaan.

IdleDash leest je agenda (alleen lezen) en je taken. Taken afvinken of toevoegen gebeurt alleen als jij daarop klikt. Alles blijft op je eigen pc: er is geen server van IdleDash die je gegevens ziet. Lees het [privacybeleid](https://meesjekeesie.github.io/IdleDash/privacy.html).

## Updates

IdleDash laat het weten als er een nieuwe versie is. Download hem via Instellingen > Over IdleDash en installeer hem over de oude heen. Je instellingen blijven bewaard.

## Verwijderen

Via Windows-instellingen > Apps > Geïnstalleerde apps > IdleDash > Verwijderen. Je Google-login wordt dan ook gewist. Je instellingen staan nog in `%AppData%\IdleDash`; die map kun je zelf weggooien.

## Zelf bouwen

Open `IdleDash.csproj` in Visual Studio 2022 en druk op F5. Alles over ontwikkelen, de Google-sleutel en nieuwe versies uitbrengen staat in [ONTWIKKELEN.md](ONTWIKKELEN.md).

## Bronnen

Weer en plaatsen: [Open-Meteo](https://open-meteo.com). Regen: [Buienradar](https://www.buienradar.nl). Nieuws: [NOS](https://nos.nl). IdleDash is niet verbonden aan Google, Buienradar of de NOS.

## Licentie

MIT, zie [LICENSE](LICENSE).
