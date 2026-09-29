# IdleDash ontwikkelen

Uitleg voor jezelf als maker: bouwen, de Google-sleutel, en nieuwe versies uitbrengen via GitHub.
Wat gebruikers moeten weten staat in [README.md](README.md).

## Starten in Visual Studio

Open `IdleDash.csproj` en druk op F5. De eerste keer haalt Visual Studio de Google-onderdelen op, dat duurt even.

- Er kan maar één IdleDash tegelijk draaien. Staat de geïnstalleerde versie aan, sluit die dan eerst via het icoon bij de klok, anders sluit je F5-versie zich meteen.
- Een zelf gebouwde versie heeft versienummer 0.0.0, zoekt niet naar updates en heeft geen ingebouwde Google-sleutel. Gebruik daarvoor je eigen sleutelbestand (zie hieronder).

## Waar staat wat in het project

| Map of bestand | Wat |
|---|---|
| `Core/` | Koppeling met Windows, instellingen, systeemvak, nachtmodus, autostart |
| `Services/` | Weer, Buienradar, NOS, Google, updates |
| `Widgets/` | Alle widgets. Een nieuwe widget toevoegen = één regel in `WidgetCatalog.cs` |
| `Styles/` | Kleuren en stijlen |
| `installer/IdleDash.iss` | Het installatieprogramma (Inno Setup) |
| `docs/` | De website op GitHub Pages: homepage en privacybeleid |
| `.github/workflows/release.yml` | Bouwt op GitHub de installer bij elke release |

Je instellingen tijdens het gebruik staan in `%AppData%\IdleDash`: `settings.json`, `google-sleutel.json` (alleen als je zelf een sleutel kiest) en `google-login\`. Deel die Google-bestanden met niemand. De `.gitignore` houdt sleutelbestanden buiten GitHub, maar zet ze sowieso nooit in je projectmap.

## Google Cloud-project (eenmalig)

Deze sleutel gebruik je zelf in Visual Studio, en dezelfde sleutel gaat (via GitHub-secrets) in de officiële download.

1. Ga naar [console.cloud.google.com](https://console.cloud.google.com) en maak een project `IdleDash`.
2. Zet **Google Calendar API** en **Google Tasks API** aan (zoeken, dan *Inschakelen*).
3. **Google Auth Platform** > *Aan de slag*: app-naam `IdleDash`, je e-mailadres, doelgroep *Extern*. De velden bij App domain en Authorized domains mag je leeg laten. Upload (nog) geen logo.
4. *Doelgroep* (Audience) > **App publiceren** (Publish app). Zo verloopt de login niet elke 7 dagen.
5. *Clients* > *Client maken* > type **Desktop-app** > *Maken* > JSON downloaden.
6. In een zelf gebouwde IdleDash: instellingen > *Sleutelbestand kiezen…* > kies het JSON-bestand > *Koppelen met Google*.
7. Bij "Google heeft deze app niet geverifieerd": *Geavanceerd* > *Ga naar IdleDash*.

## De ingebouwde sleutel voor de download

Zodat andere mensen niet zelf een Google Cloud-project hoeven te maken, bouwt GitHub jouw sleutel in de officiële download. Hij staat nooit in de broncode.

1. Open het gedownloade JSON-bestand met Kladblok. Je hebt `client_id` en `client_secret` nodig (binnen `"installed"`).
2. Op GitHub: je repository > *Settings* > *Secrets and variables* > *Actions* > *New repository secret*.
3. Maak `GOOGLE_CLIENT_ID` met de waarde van `client_id`, en `GOOGLE_CLIENT_SECRET` met de waarde van `client_secret`.

Voor desktop-apps is het normaal dat deze sleutel in het programma zit: Google behandelt de "secret" van een desktop-app niet als geheim. Iedereen logt in met zijn eigen Google-account en krijgt zijn eigen toegang.

Zolang de app niet door Google is geverifieerd, zien gebruikers een waarschuwing en kunnen er in totaal maximaal 100 mensen koppelen, voor altijd. Hoeveel het er zijn, zie je bij Google Auth Platform > *Doelgroep*. Gebruikers kunnen altijd nog een eigen sleutel kiezen (*Eigen sleutel (geavanceerd)…* in de instellingen).

## Nieuwe versie uitbrengen

1. Pas de code aan en test met F5.
2. In Visual Studio: *Git Changes* > typ wat je hebt veranderd > *Commit All* > *Push*.
3. Op GitHub: *Releases* > *Draft a new release* > *Choose a tag* > typ een nieuw nummer, bijvoorbeeld `v1.0.1` > *Create new tag*.
4. Titel, bijvoorbeeld `IdleDash 1.0.1`, en een korte beschrijving van wat er nieuw is. Klik *Publish release*.
5. GitHub bouwt nu de installer (volg het in het tabblad *Actions*). Na 5 à 10 minuten staan `IdleDash-Setup-1.0.1.exe` en de portable zip onder *Assets*.

Versienummers altijd als `vX.Y.Z` en steeds hoger, anders krijgen gebruikers geen update-melding.

Eerst testen zonder release: tabblad *Actions* > *IdleDash bouwen* > *Run workflow*. Als hij klaar is, staan de bestanden onderaan de run bij *Artifacts*.

## Google-verificatie (voor meer dan 100 gebruikers)

Pas nodig als je meer dan 100 gebruikers verwacht of de waarschuwing weg wilt hebben. Je hebt een eigen domein nodig; een `github.io`-adres kan Google niet als jouw domein verifiëren.

1. Laat een subdomein, bijvoorbeeld `idledash.minecraftgooners.nl`, via een CNAME-record wijzen naar `meesjekeesie.github.io`.
2. GitHub: *Settings* > *Pages* > *Deploy from a branch* > `main` en map `/docs`. Vul bij *Custom domain* je subdomein in en zet *Enforce HTTPS* aan.
3. [Google Search Console](https://search.google.com/search-console): voeg `minecraftgooners.nl` toe als domein en verifieer met het TXT-record dat Google geeft. Gebruik hetzelfde Google-account als voor je Cloud-project.
4. Google Auth Platform > *Branding*: homepage `https://idledash.minecraftgooners.nl/`, privacybeleid `https://idledash.minecraftgooners.nl/privacy.html`, authorized domain `minecraftgooners.nl`. Voorwaarden mag leeg blijven.
5. *Gegevenstoegang* (Data access): voeg de scopes `.../auth/calendar.readonly` en `.../auth/tasks` toe, met een korte uitleg waarom.
6. Maak een demovideo (YouTube, niet vermeld) waarin je laat zien hoe je koppelt en waar de agenda en taken in IdleDash verschijnen.
7. *Verificatiecentrum* > indienen.

## Problemen oplossen

- **Visual Studio geeft fouten**: kopieer de Foutenlijst (Error List) en zoek of vraag het na.
- **De build op GitHub mislukt**: open de rode run in het tabblad *Actions* en klik op de stap met het rode kruisje; daar staat de foutmelding.
- **Waarschuwing "De Google-secrets ontbreken"**: de secrets uit "De ingebouwde sleutel voor de download" zijn nog niet ingesteld. De download werkt, maar zonder Google-koppeling.
- **Google: "Toegang geblokkeerd" of fout 403**: de app staat nog in testmodus. Doe stap 4 bij "Google Cloud-project".
