# IdleDash ontwikkelen

Uitleg voor jezelf als maker: bouwen, vertalen, de Google-sleutel, en nieuwe versies uitbrengen via GitHub.
Wat gebruikers moeten weten staat in [README.md](README.md).

## Starten in Visual Studio

Open `IdleDash.csproj` en druk op F5. De eerste keer haalt Visual Studio de onderdelen van Google en Microsoft op (NuGet-pakketten), dat duurt even.

- Er kan maar één IdleDash tegelijk draaien. Staat de geïnstalleerde versie aan, sluit die dan eerst via het icoon bij de klok, anders sluit je F5-versie zich meteen.
- Een zelf gebouwde versie heeft versienummer 0.0.0, zoekt niet naar updates en heeft geen ingebouwde Google-sleutel. Gebruik daarvoor je eigen sleutelbestand (zie hieronder).

## Waar staat wat in het project

| Map of bestand | Wat |
|---|---|
| `Core/` | Koppeling met Windows, instellingen, systeemvak, nachtmodus, autostart, taal (`Loc.cs`, `Strings.cs`), thema's (`Theme.cs`, `ThemeManager.cs`), versleuteling (`Secrets.cs`) en bouwstenen voor instellingenschermen (`Ui.cs`) |
| `Services/` | Weer en regen (Buienradar, Open-Meteo), RSS-feeds, Google, Apple iCloud (`CalDav.cs`), agenda-links (`IcsParser.cs`), alle agenda's samen (`CalendarHub.cs`), smarthome, foto's, updates |
| `Widgets/` | Alle widgets. Een nieuwe widget toevoegen = één regel in `WidgetCatalog.cs`. Instellingen per widget staan in `CreateSettings` van die widget |
| `Styles/` | Kleuren en stijlen. `Theme.xaml` is voor het dashboard (kleuren wisselen mee met het thema), `Controls.xaml` voor de instellingenvensters |
| `CHANGELOG.md` | Wat er in elke versie nieuw is (de patch notes) |
| `installer/IdleDash.iss` | Het installatieprogramma (Inno Setup) |
| `docs/` | De website op GitHub Pages: homepage en privacybeleid |
| `.github/workflows/release.yml` | Bouwt op GitHub de installer bij elke release |

Je instellingen tijdens het gebruik staan in `%AppData%\IdleDash`: `settings.json`, `google-sleutel.json` (alleen als je zelf een sleutel kiest) en `google-login\`. Het iCloud-wachtwoord en de smarthome-tokens staan versleuteld in `settings.json` (Windows-gegevensbescherming): ze werken alleen voor jouw Windows-account op die pc. Zet je `settings.json` over naar een andere pc, dan moet je iCloud en smarthome daar opnieuw koppelen. Deel die Google-bestanden met niemand. De `.gitignore` houdt sleutelbestanden buiten GitHub, maar zet ze sowieso nooit in je projectmap.

## Vertalen (Nederlands en Engels)

IdleDash is geschreven in het Nederlands; de Engelse teksten staan in `Core/Strings.cs`.

- **In de code** zet je een tekst altijd zo neer: `Loc.T("Geen open taken.")`. Met iets erin: `Loc.T("Versie {0} is beschikbaar.", versie)`.
- **In XAML** schrijf je gewoon Nederlands (`Text="Weer ophalen…"`). IdleDash vertaalt die teksten vanzelf als een venster opent.
- **De vertaling** zet je in `Core/Strings.cs`: `["Geen open taken."] = "No open tasks.",`. De Nederlandse tekst moet precies gelijk zijn, tot en met de punt, en `{0}` moet in beide versies staan.
- Vergeet je een vertaling, dan crasht er niets: die tekst blijft dan Nederlands in de Engelse versie.
- Datums, tijden (12/24 uur), graden en windsnelheid gaan via `Loc.Date`, `Loc.Time`, `Loc.Degrees` en `Loc.Wind`, zodat ze de instellingen van de gebruiker volgen.

## Google Cloud-project (eenmalig)

Deze sleutel gebruik je zelf in Visual Studio, en dezelfde sleutel gaat (via GitHub-secrets) in de officiële download.

1. Ga naar [console.cloud.google.com](https://console.cloud.google.com) en maak een project `IdleDash`.
2. Zet **Google Calendar API** en **Google Tasks API** aan (zoeken, dan *Inschakelen*).
3. **Google Auth Platform** > *Aan de slag*: app-naam `IdleDash`, je e-mailadres, doelgroep *Extern*. Upload (nog) geen logo: dan wil Google de app eerst verifiëren.
4. Zet de website aan, want Google laat je alleen publiceren met een homepage en privacybeleid: GitHub > je repository > *Settings* > *Pages* > *Deploy from a branch* > branch `main`, map `/docs` > *Save*. Controleer na een paar minuten dat `https://meesjekeesie.github.io/IdleDash/` opent.
5. *Branding* (Huisstijl):
   - *Authorized domains* > *Add domain*: `meesjekeesie.github.io`
   - *Application home page*: `https://meesjekeesie.github.io/IdleDash/`
   - *Application privacy policy link*: `https://meesjekeesie.github.io/IdleDash/privacy.html`
   - *Terms of service* leeg laten, dan *Save*.
6. *Doelgroep* (Audience) > **App publiceren** (Publish app). Zo verloopt de login niet elke 7 dagen. Blijft de knop grijs, beweeg je muis erover: Google zegt dan wat er nog mist.
7. *Gegevenstoegang* (Data access) > *Scopes toevoegen of verwijderen*: vink `.../auth/calendar.readonly`, `.../auth/calendar.events` en `.../auth/tasks` aan > *Bijwerken* > *Save*. Staan ze er niet tussen, controleer dan of stap 2 gelukt is.
8. *Clients* > *Client maken* > type **Desktop-app** > *Maken* > JSON downloaden.
9. In een zelf gebouwde IdleDash: instellingen > *Sleutelbestand kiezen…* > kies het JSON-bestand > *Koppelen met Google*.
10. Bij "Google heeft deze app niet geverifieerd": *Geavanceerd* > *Ga naar IdleDash*.

Sinds versie 1.2.0 vraagt IdleDash ook toestemming om afspraken toe te voegen (`calendar.events`). Mensen die al gekoppeld waren, zien in de instellingen een knop *Opnieuw koppelen*; tot ze die gebruiken, werkt het lezen gewoon door.

Was je al gekoppeld toen de app nog in testmodus stond? Klik dan na het publiceren één keer op *Ontkoppelen* en koppel opnieuw. Een login uit de testmodus blijft anders elke 7 dagen verlopen.

## De ingebouwde sleutel voor de download

Zodat andere mensen niet zelf een Google Cloud-project hoeven te maken, bouwt GitHub jouw sleutel in de officiële download. Hij staat nooit in de broncode.

1. Open het gedownloade JSON-bestand met Kladblok. Je hebt `client_id` en `client_secret` nodig (binnen `"installed"`).
2. Op GitHub: je repository > *Settings* > *Secrets and variables* > *Actions* > *New repository secret*.
3. Maak `GOOGLE_CLIENT_ID` met de waarde van `client_id`, en `GOOGLE_CLIENT_SECRET` met de waarde van `client_secret`.

Voor desktop-apps is het normaal dat deze sleutel in het programma zit: Google behandelt de "secret" van een desktop-app niet als geheim. Iedereen logt in met zijn eigen Google-account en krijgt zijn eigen toegang.

Zolang de app niet door Google is geverifieerd, zien gebruikers een waarschuwing en kunnen er in totaal maximaal 100 mensen koppelen, voor altijd. Hoeveel het er zijn, zie je bij Google Auth Platform > *Doelgroep*. Gebruikers kunnen altijd nog een eigen sleutel kiezen (*Eigen sleutel (geavanceerd)…* in de instellingen).

## Nieuwe versie uitbrengen

1. Pas de code aan en test met F5.
2. Schrijf in `CHANGELOG.md` bovenaan een kopje met het nieuwe nummer en wat er nieuw is.
3. In Visual Studio: *Git Changes* > typ wat je hebt veranderd > *Commit All* > *Push*.
4. Op GitHub: *Releases* > *Draft a new release* > *Choose a tag* > typ een nieuw nummer, bijvoorbeeld `v1.2.1` > *Create new tag*.
5. Titel, bijvoorbeeld `IdleDash 1.2.1`, en plak als beschrijving het stuk uit `CHANGELOG.md`. Klik *Publish release*.
6. GitHub bouwt nu de installer (volg het in het tabblad *Actions*). Na 5 à 10 minuten staan drie bestanden onder *Assets*: `IdleDash-Setup-1.2.1.exe`, `IdleDash-Setup-1.2.1.exe.sha256` en de portable zip.

Versienummers altijd als `vX.Y.Z` en steeds hoger, anders krijgen gebruikers geen update. Kleine reparatie: laatste cijfer omhoog (1.2.0 naar 1.2.1). Nieuwe functies: middelste cijfer (1.2.0 naar 1.3.0).

### Zo werkt automatisch bijwerken

- De geïnstalleerde IdleDash kijkt een minuut na het opstarten en daarna elke 6 uur op GitHub of er een nieuwere release is.
- Is die er, dan downloadt hij de installer naar `%LocalAppData%\IdleDash\updates` en controleert hem met het `.sha256`-bestand. Klopt de code niet, dan wordt het bestand weggegooid.
- De gebruiker krijgt een melding. Klikt hij erop (of op *Bijwerken* in het systeemvak-menu of de instellingen), dan sluit IdleDash af, installeert de nieuwe versie onzichtbaar en start zichzelf weer.
- Zonder `.sha256`-bestand bij de release, bij de portable-versie of met automatisch bijwerken uit: alleen een melding met een downloadlink.
- Een zelf gebouwde versie (F5 in Visual Studio) zoekt nooit naar updates. Testen doe je dus met de geïnstalleerde versie: installeer release A, breng release B uit en wacht maximaal een minuut na het opstarten.

Eerst testen zonder release: tabblad *Actions* > *IdleDash bouwen* > *Run workflow*. Als hij klaar is, staan de bestanden onderaan de run bij *Artifacts*.

## Google-verificatie (voor meer dan 100 gebruikers)

Pas nodig als je meer dan 100 gebruikers verwacht of de waarschuwing weg wilt hebben. Voor publiceren is `meesjekeesie.github.io` genoeg, maar voor verificatie heb je een eigen domein nodig: een `github.io`-adres kan Google niet als jouw domein verifiëren. Je verhuist de website dan naar je eigen domein en past de adressen bij *Branding* aan.

1. Laat een subdomein, bijvoorbeeld `idledash.minecraftgooners.nl`, via een CNAME-record wijzen naar `meesjekeesie.github.io`.
2. GitHub: *Settings* > *Pages* > *Deploy from a branch* > `main` en map `/docs`. Vul bij *Custom domain* je subdomein in en zet *Enforce HTTPS* aan.
3. [Google Search Console](https://search.google.com/search-console): voeg `minecraftgooners.nl` toe als domein en verifieer met het TXT-record dat Google geeft. Gebruik hetzelfde Google-account als voor je Cloud-project.
4. Google Auth Platform > *Branding*: homepage `https://idledash.minecraftgooners.nl/`, privacybeleid `https://idledash.minecraftgooners.nl/privacy.html`, authorized domain `minecraftgooners.nl`. Voorwaarden mag leeg blijven.
5. *Gegevenstoegang* (Data access): controleer dat de scopes `.../auth/calendar.readonly`, `.../auth/calendar.events` en `.../auth/tasks` erin staan, met een korte uitleg waarom (agenda tonen, afspraken toevoegen die de gebruiker zelf invult, taken tonen en afvinken).
6. Maak een demovideo (YouTube, niet vermeld) waarin je laat zien hoe je koppelt, waar de agenda en taken in IdleDash verschijnen en hoe je een afspraak toevoegt.
7. *Verificatiecentrum* > indienen.

## Problemen oplossen

- **Visual Studio geeft fouten**: kopieer de Foutenlijst (Error List) en zoek of vraag het na.
- **De build op GitHub mislukt**: open de rode run in het tabblad *Actions* en klik op de stap met het rode kruisje; daar staat de foutmelding.
- **Waarschuwing "De Google-secrets ontbreken"**: de secrets uit "De ingebouwde sleutel voor de download" zijn nog niet ingesteld. De download werkt, maar zonder Google-koppeling.
- **"Publish app" blijft grijs**: beweeg je muis over de knop. Meestal ontbreekt de homepage of het privacybeleid (stap 4 en 5 bij "Google Cloud-project"), of opent de website nog niet.
- **Google: "Toegang geblokkeerd" of fout 403**: de app staat nog in testmodus. Doe stap 4 tot en met 6 bij "Google Cloud-project".
- **Afspraak toevoegen in Google lukt niet**: klik bij de instellingen op *Opnieuw koppelen*, zodat Google toestemming geeft voor `calendar.events`. Controleer ook stap 7 bij "Google Cloud-project".
- **Een tekst blijft Nederlands in de Engelse versie**: die vertaling ontbreekt in `Core/Strings.cs` (zie "Vertalen").
- **iCloud of smarthome werkt niet meer na een nieuwe pc of Windows-account**: de versleutelde wachtwoorden en tokens horen bij het oude account. Koppel ze opnieuw in de instellingen.
