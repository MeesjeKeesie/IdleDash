using System.IO;
using System.Windows;
using System.Windows.Controls;
using IdleDash.Core;
using IdleDash.Services;

namespace IdleDash;

/// <summary>De onderdelen van het instellingenscherm die in code gebouwd worden.</summary>
public partial class SettingsWindow
{
    // ─────────────────────────── Taal en weergave ───────────────────────────

    private void BuildLanguageSection()
    {
        void Changed()
        {
            _settings.NotifyChanged();   // het dashboard zet de nieuwe taal door; dit venster wordt dan opnieuw geopend
        }

        LanguagePanel.Children.Add(Ui.Columns(
            (Loc.T("Taal"), Ui.Combo(new[]
            {
                (Loc.T("Automatisch (Windows)"), "auto"), ("Nederlands", "nl"), ("English", "en"),
            }, _settings.Language, v => { _settings.Language = v; Changed(); }), "*"),
            (Loc.T("Tijd"), Ui.Combo(new[]
            {
                (Loc.T("Automatisch"), "auto"), (Loc.T("24-uurs (14:30)"), "24"), (Loc.T("12-uurs (2:30 PM)"), "12"),
            }, _settings.TimeFormat, v => { _settings.TimeFormat = v; Changed(); }), "*")));
        LanguagePanel.Children.Add(Ui.Columns(
            (Loc.T("Temperatuur"), Ui.Combo(new[]
            {
                (Loc.T("Automatisch"), "auto"), ("°C", "c"), ("°F", "f"),
            }, _settings.TemperatureUnit, v => { _settings.TemperatureUnit = v; Changed(); }), "*"),
            (Loc.T("Windsnelheid"), Ui.Combo(new[]
            {
                (Loc.T("Automatisch"), "auto"), (Loc.T("km/u"), "kmh"), ("mph", "mph"),
            }, _settings.WindUnit, v => { _settings.WindUnit = v; Changed(); }), "*")));
    }

    // ─────────────────────────── Thema ───────────────────────────

    private void BuildThemeSection()
    {
        ThemePanel.Children.Clear();
        var theme = _settings.Theme;

        void Apply(bool rebuild = false)
        {
            _settings.NotifyChanged();
            if (rebuild) BuildThemeSection();
        }

        // Kant-en-klaar of eigen thema kiezen
        var choices = ThemePresets.All.Select(t => (Loc.T(t.Name), "preset:" + t.Name))
            .Concat(_settings.CustomThemes.Select(t => (t.Name, "custom:" + t.Name)))
            .ToList();
        string current = ThemePresets.IsPreset(theme.Name) ? "preset:" + theme.Name
            : _settings.CustomThemes.Any(t => t.Name == theme.Name) ? "custom:" + theme.Name
            : "";
        if (current.Length == 0) choices.Insert(0, (Loc.T("Aangepast (niet opgeslagen)"), ""));
        ThemePanel.Children.Add(Ui.Label(Loc.T("Thema van het dashboard")));
        ThemePanel.Children.Add(Ui.Combo(choices, current, key =>
        {
            if (key.StartsWith("preset:")) _settings.Theme = ThemePresets.All.First(t => t.Name == key[7..]).Clone();
            else if (key.StartsWith("custom:")) _settings.Theme = _settings.CustomThemes.First(t => t.Name == key[7..]).Clone();
            else return;
            Apply(rebuild: true);
        }));

        // Kleuren
        void Edited()
        {
            // Een aangepast standaardthema wordt "aangepast"; een eigen thema pas je gewoon aan
            if (ThemePresets.IsPreset(theme.Name)) theme.Name = Loc.T("Aangepast");
            Apply();
        }
        ThemePanel.Children.Add(Ui.Label(Loc.T("Accentkleur (balkjes, knoppen)")));
        ThemePanel.Children.Add(Ui.ColorPicker(theme.Accent, c => { theme.Accent = c; Edited(); }));
        ThemePanel.Children.Add(Ui.Label(Loc.T("Tekstkleur")));
        ThemePanel.Children.Add(Ui.ColorPicker(theme.Text, c => { theme.Text = c; Edited(); }));

        // Achtergrond
        ThemePanel.Children.Add(Ui.Label(Loc.T("Achtergrond")));
        ThemePanel.Children.Add(Ui.Combo(new[]
        {
            (Loc.T("Lucht die meekleurt met de tijd"), "sky"), (Loc.T("Effen kleur"), "solid"),
            (Loc.T("Kleurverloop"), "gradient"), (Loc.T("Eigen foto"), "photo"), (Loc.T("Fotomap (wisselend)"), "folder"),
        }, theme.Background, v => { theme.Background = v; Edited(); BuildThemeSection(); }));

        if (theme.Background is "solid" or "gradient")
        {
            ThemePanel.Children.Add(Ui.Label(theme.Background == "solid" ? Loc.T("Kleur") : Loc.T("Kleur boven")));
            ThemePanel.Children.Add(Ui.ColorPicker(theme.Color1, c => { theme.Color1 = c; Edited(); }));
        }
        if (theme.Background == "gradient")
        {
            ThemePanel.Children.Add(Ui.Label(Loc.T("Kleur onder")));
            ThemePanel.Children.Add(Ui.ColorPicker(theme.Color2, c => { theme.Color2 = c; Edited(); }));
        }
        if (theme.Background == "photo")
        {
            var photoName = Ui.Hint(theme.Photo != null ? Path.GetFileName(theme.Photo) : Loc.T("Nog geen foto gekozen."));
            ThemePanel.Children.Add(photoName);
            ThemePanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Foto kiezen…"), () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = Loc.T("Kies een foto voor de achtergrond"),
                    Filter = Loc.T("Afbeeldingen") + "|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.heic",
                };
                if (dialog.ShowDialog(this) != true) return;
                theme.Photo = dialog.FileName;
                photoName.Text = Path.GetFileName(dialog.FileName);
                Edited();
            })));
        }
        if (theme.Background == "folder")
        {
            var folderName = Ui.Hint(theme.PhotoFolder ?? Loc.T("Nog geen map gekozen."));
            ThemePanel.Children.Add(folderName);
            ThemePanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Map kiezen…"), () =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Kies een map met foto's") };
                if (dialog.ShowDialog(this) != true) return;
                theme.PhotoFolder = dialog.FolderName;
                folderName.Text = dialog.FolderName;
                Edited();
            })));
            ThemePanel.Children.Add(Ui.Switch(Loc.T("Ook submappen"), null, theme.PhotoSubfolders, v => { theme.PhotoSubfolders = v; Edited(); }));
            ThemePanel.Children.Add(Ui.Columns(
                (Loc.T("Wisselen elke"), Ui.Combo(new[]
                {
                    (Loc.T("1 minuut"), 60), (Loc.T("5 minuten"), 300), (Loc.T("15 minuten"), 900), (Loc.T("30 minuten"), 1800), (Loc.T("1 uur"), 3600),
                }, theme.PhotoInterval, v => { theme.PhotoInterval = v; Edited(); }), "*"),
                (Loc.T("Volgorde"), Ui.Combo(new[]
                {
                    (Loc.T("Willekeurig"), "random"), (Loc.T("Op naam"), "name"), (Loc.T("Op datum"), "date"),
                }, theme.PhotoOrder, v => { theme.PhotoOrder = v; Edited(); }), "*")));
        }
        if (theme.Background is "photo" or "folder")
        {
            ThemePanel.Children.Add(Ui.Label(Loc.T("Passend maken")));
            ThemePanel.Children.Add(Ui.Combo(new[]
            {
                (Loc.T("Opvullen (scherm helemaal vol)"), "fill"), (Loc.T("Aanpassen (hele foto zichtbaar)"), "fit"),
                (Loc.T("Uitrekken"), "stretch"), (Loc.T("Centreren (ware grootte)"), "center"),
            }, theme.PhotoFit, v => { theme.PhotoFit = v; Edited(); BuildThemeSection(); }));
            if (theme.PhotoFit == "fill")
            {
                ThemePanel.Children.Add(Ui.Label(Loc.T("Welk deel zichtbaar blijft")));
                ThemePanel.Children.Add(Ui.Combo(new[]
                {
                    (Loc.T("Midden"), "center"), (Loc.T("Boven"), "top"), (Loc.T("Onder"), "bottom"), (Loc.T("Links"), "left"), (Loc.T("Rechts"), "right"),
                }, theme.PhotoAlign, v => { theme.PhotoAlign = v; Edited(); }));
            }
            ThemePanel.Children.Add(Ui.Label(Loc.T("Foto donkerder maken (voor leesbare tekst)")));
            ThemePanel.Children.Add(Ui.Combo(new[] { (Loc.T("Niet"), 0), ("20%", 20), ("40%", 40), ("60%", 60) },
                theme.PhotoDim, v => { theme.PhotoDim = v; Edited(); }));
        }

        ThemePanel.Children.Add(Ui.Label(Loc.T("Kaartje achter de widgets")));
        ThemePanel.Children.Add(Ui.Combo(new[]
        {
            (Loc.T("Geen"), 0), (Loc.T("Licht"), 20), (Loc.T("Middel"), 40), (Loc.T("Stevig"), 60), (Loc.T("Heel stevig"), 85),
        }, theme.CardOpacity, v => { theme.CardOpacity = v; Edited(); }));
        ThemePanel.Children.Add(Ui.Hint(Loc.T("Handig bij een foto als achtergrond. Per widget kun je ook een eigen thema kiezen (tandwieltje in de bewerkmodus).")));

        // Bewaren, delen en verwijderen
        var name = new TextBox { Text = ThemePresets.IsPreset(theme.Name) ? "" : theme.Name, Margin = new Thickness(0, 4, 0, 0) };
        ThemePanel.Children.Add(Ui.Label(Loc.T("Naam om dit thema te bewaren")));
        ThemePanel.Children.Add(name);
        var status = Ui.Hint("");
        ThemePanel.Children.Add(Ui.Row(
            Ui.Button(Loc.T("Bewaren als eigen thema"), () =>
            {
                string n = name.Text.Trim();
                if (n.Length == 0 || ThemePresets.IsPreset(n))
                {
                    status.Text = Loc.T("Kies een eigen naam voor het thema.");
                    return;
                }
                var copy = theme.Clone();
                copy.Name = n;
                _settings.CustomThemes.RemoveAll(t => t.Name == n);
                _settings.CustomThemes.Add(copy);
                _settings.Theme = copy.Clone();
                Apply(rebuild: true);
            }),
            Ui.Button(Loc.T("Delen (exporteren)…"), () =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = (ThemePresets.IsPreset(theme.Name) ? "IdleDash-thema" : theme.Name) + ".json",
                    Filter = Loc.T("IdleDash-thema") + " (*.json)|*.json",
                };
                if (dialog.ShowDialog(this) != true) return;
                var export = theme.Clone();
                export.Photo = null;   // paden van jouw pc werken niet bij iemand anders
                export.PhotoFolder = null;
                File.WriteAllText(dialog.FileName, AppSettings.ToJson(export));
                status.Text = Loc.T("Opgeslagen. Stuur het bestand naar iemand die het kan importeren.");
            }),
            Ui.Button(Loc.T("Importeren…"), () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("IdleDash-thema") + " (*.json)|*.json" };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    var imported = AppSettings.FromJson<ThemeSettings>(File.ReadAllText(dialog.FileName))
                                   ?? throw new InvalidDataException();
                    if (string.IsNullOrWhiteSpace(imported.Name) || ThemePresets.IsPreset(imported.Name))
                        imported.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
                    _settings.CustomThemes.RemoveAll(t => t.Name == imported.Name);
                    _settings.CustomThemes.Add(imported);
                    _settings.Theme = imported.Clone();
                    Apply(rebuild: true);
                }
                catch
                {
                    status.Text = Loc.T("Dit is geen geldig thema-bestand.");
                }
            })));
        if (_settings.CustomThemes.Any(t => t.Name == theme.Name))
        {
            ThemePanel.Children.Add(Ui.Button(Loc.T("Dit eigen thema verwijderen"), () =>
            {
                _settings.CustomThemes.RemoveAll(t => t.Name == theme.Name);
                _settings.Theme = ThemePresets.Sky();
                Apply(rebuild: true);
            }));
        }
        ThemePanel.Children.Add(status);
    }

    // ─────────────────────────── Apple iCloud ───────────────────────────

    private void BuildAppleSection()
    {
        ApplePanel.Children.Clear();
        var status = Ui.Hint("");

        if (_settings.Apple is { } apple)
        {
            ApplePanel.Children.Add(new TextBlock { Text = Loc.T("Gekoppeld met {0}", apple.Email), Margin = new Thickness(0, 6, 0, 0) });
            ApplePanel.Children.Add(Ui.Hint(apple.Calendars.Count == 0
                ? Loc.T("Geen agenda's gevonden.")
                : string.Join(", ", apple.Calendars.Select(c => c.Name))));
            ApplePanel.Children.Add(Ui.Row(
                Ui.Button(Loc.T("Agenda's vernieuwen"), async () =>
                {
                    status.Text = Loc.T("Bezig…");
                    try
                    {
                        apple.Calendars = await CalDav.DiscoverAsync(CalDav.ICloudServer, apple.Email, Secrets.Unprotect(apple.Password));
                        _settings.NotifyChanged();
                        BuildAppleSection();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        status.Text = Loc.T("Apple ID of app-specifiek wachtwoord klopt niet.");
                    }
                    catch
                    {
                        status.Text = Loc.T("iCloud is nu niet bereikbaar.");
                    }
                }),
                Ui.Button(Loc.T("Ontkoppelen"), () =>
                {
                    _settings.Apple = null;
                    _settings.NotifyChanged();
                    BuildAppleSection();
                })));
            ApplePanel.Children.Add(status);
            return;
        }

        ApplePanel.Children.Add(Ui.Hint(Loc.T("Je hebt een app-specifiek wachtwoord nodig (niet je gewone wachtwoord). Maak het aan op account.apple.com, onder Inloggen en beveiliging, App-specifieke wachtwoorden.")));
        var email = new TextBox();
        var password = new PasswordBox();
        ApplePanel.Children.Add(Ui.Columns((Loc.T("Apple ID (e-mailadres)"), email, "*"), (Loc.T("App-specifiek wachtwoord"), password, "*")));
        ApplePanel.Children.Add(Ui.Row(
            Ui.Button(Loc.T("Koppelen"), async () =>
            {
                if (email.Text.Trim().Length < 3 || password.Password.Length < 4)
                {
                    status.Text = Loc.T("Vul je Apple ID en het app-specifieke wachtwoord in.");
                    return;
                }
                status.Text = Loc.T("Verbinden met iCloud…");
                try
                {
                    var calendars = await CalDav.DiscoverAsync(CalDav.ICloudServer, email.Text.Trim(), password.Password.Trim());
                    _settings.Apple = new AppleCalendarAccount
                    {
                        Email = email.Text.Trim(),
                        Password = Secrets.Protect(password.Password.Trim()),
                        Calendars = calendars,
                    };
                    _settings.NotifyChanged();
                    BuildAppleSection();
                }
                catch (UnauthorizedAccessException)
                {
                    status.Text = Loc.T("Apple ID of app-specifiek wachtwoord klopt niet.");
                }
                catch
                {
                    status.Text = Loc.T("iCloud is nu niet bereikbaar.");
                }
            }, accent: true),
            Ui.Button(Loc.T("account.apple.com openen"), () => Browser.Open("https://account.apple.com"))));
        ApplePanel.Children.Add(status);
    }

    // ─────────────────────────── Agenda-links (ICS) ───────────────────────────

    private void BuildIcsSection()
    {
        IcsPanel.Children.Clear();
        IcsPanel.Children.Add(Ui.Hint(Loc.T("Een gedeelde agenda-link (.ics of webcal://), bijvoorbeeld van Outlook, je gemeente (afvalkalender) of school. Alleen lezen.")));
        foreach (var feed in _settings.IcsFeeds.ToList())
        {
            IcsPanel.Children.Add(Ui.RemovableRow(feed.Name, feed.Url, () =>
            {
                _settings.IcsFeeds.Remove(feed);
                _settings.NotifyChanged();
                BuildIcsSection();
            }));
        }
        var name = new TextBox();
        var url = new TextBox();
        IcsPanel.Children.Add(Ui.Columns((Loc.T("Naam"), name, "*"), (Loc.T("Link"), url, "2*")));
        var status = Ui.Hint("");
        IcsPanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Agenda-link toevoegen"), () =>
        {
            string link = url.Text.Trim();
            bool valid = link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                         || link.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                         || link.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase);
            if (!valid)
            {
                status.Text = Loc.T("Plak een link die begint met https:// of webcal://.");
                return;
            }
            string[] colors = { "#8FB3E6", "#A8D5BA", "#FFD6A5", "#E57373", "#C792EA", "#F2A65A" };
            _settings.IcsFeeds.Add(new IcsFeedSettings
            {
                Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : Loc.T("Agenda"),
                Url = link,
                Color = colors[_settings.IcsFeeds.Count % colors.Length],
            });
            _settings.NotifyChanged();
            BuildIcsSection();
        })));
        IcsPanel.Children.Add(status);
    }

    // ─────────────────────────── Smarthome ───────────────────────────

    private void BuildSmartHomeSection()
    {
        SmartHomePanel.Children.Clear();
        var home = _settings.SmartHome;

        void Saved()
        {
            _settings.Save();
            SmartHomeService.Configure(_settings);
            BuildSmartHomeSection();
        }

        // Home Assistant
        SmartHomePanel.Children.Add(Ui.Label("Home Assistant"));
        var haStatus = Ui.Hint("");
        if (!string.IsNullOrWhiteSpace(home.HomeAssistantUrl) && !string.IsNullOrEmpty(home.HomeAssistantToken))
        {
            SmartHomePanel.Children.Add(Ui.RemovableRow(Loc.T("Gekoppeld"), home.HomeAssistantUrl, () =>
            {
                home.HomeAssistantUrl = null;
                home.HomeAssistantToken = null;
                Saved();
            }));
        }
        else
        {
            var url = new TextBox { Text = "http://homeassistant.local:8123" };
            var token = new PasswordBox();
            SmartHomePanel.Children.Add(Ui.Columns((Loc.T("Adres"), url, "*"), (Loc.T("Token"), token, "*")));
            SmartHomePanel.Children.Add(Ui.Hint(Loc.T("Token maken: klik in Home Assistant linksonder op je naam, ga naar Beveiliging en maak een langlevend toegangstoken aan.")));
            SmartHomePanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Koppelen"), async () =>
            {
                haStatus.Text = Loc.T("Verbinden…");
                try
                {
                    var provider = new HomeAssistantProvider(url.Text.Trim(), token.Password.Trim());
                    if (!await provider.TestAsync())
                    {
                        haStatus.Text = Loc.T("Home Assistant weigert het token. Maak een nieuw token aan en probeer het opnieuw.");
                        return;
                    }
                    home.HomeAssistantUrl = url.Text.Trim();
                    home.HomeAssistantToken = Secrets.Protect(token.Password.Trim());
                    Saved();
                }
                catch
                {
                    haStatus.Text = Loc.T("Home Assistant is niet bereikbaar op dit adres.");
                }
            }, accent: true)));
        }
        SmartHomePanel.Children.Add(haStatus);

        // Philips Hue
        SmartHomePanel.Children.Add(Ui.Label("Philips Hue"));
        var hueStatus = Ui.Hint("");
        if (!string.IsNullOrWhiteSpace(home.HueBridge) && !string.IsNullOrEmpty(home.HueUser))
        {
            SmartHomePanel.Children.Add(Ui.RemovableRow(Loc.T("Gekoppeld"), home.HueBridge, () =>
            {
                home.HueBridge = null;
                home.HueUser = null;
                Saved();
            }));
        }
        else
        {
            var bridge = new TextBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            SmartHomePanel.Children.Add(Ui.Hint(Loc.T("Klik op Zoeken, druk dan op de ronde knop bovenop je Hue-bridge en klik binnen 30 seconden op Koppelen.")));
            SmartHomePanel.Children.Add(Ui.Columns((Loc.T("IP-adres van de bridge"), bridge, "*")));
            SmartHomePanel.Children.Add(Ui.Row(
                Ui.Button(Loc.T("Zoeken"), async () =>
                {
                    hueStatus.Text = Loc.T("Zoeken…");
                    var found = await HueProvider.DiscoverAsync();
                    if (found.Count == 0)
                    {
                        hueStatus.Text = Loc.T("Geen bridge gevonden. Vul het IP-adres zelf in (staat in de Hue-app bij Instellingen, Hue Bridges).");
                        return;
                    }
                    bridge.Text = found[0];
                    hueStatus.Text = Loc.T("Gevonden. Druk nu op de knop van de bridge en klik op Koppelen.");
                }),
                Ui.Button(Loc.T("Koppelen"), async () =>
                {
                    if (bridge.Text.Trim().Length == 0) return;
                    try
                    {
                        var (user, notPressed) = await HueProvider.PairAsync(bridge.Text.Trim());
                        if (user == null)
                        {
                            hueStatus.Text = notPressed
                                ? Loc.T("Druk eerst op de ronde knop van de bridge en klik daarna binnen 30 seconden op Koppelen.")
                                : Loc.T("Koppelen lukte niet.");
                            return;
                        }
                        home.HueBridge = bridge.Text.Trim();
                        home.HueUser = Secrets.Protect(user);
                        Saved();
                    }
                    catch
                    {
                        hueStatus.Text = Loc.T("De bridge is niet bereikbaar op dit adres.");
                    }
                }, accent: true)));
        }
        SmartHomePanel.Children.Add(hueStatus);

        // Shelly
        SmartHomePanel.Children.Add(Ui.Label("Shelly"));
        foreach (string host in home.ShellyDevices.ToList())
            SmartHomePanel.Children.Add(Ui.RemovableRow(host, null, () => { home.ShellyDevices.Remove(host); Saved(); }));
        var shelly = new TextBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        var shellyStatus = Ui.Hint(Loc.T("IP-adres van je Shelly, te vinden in de Shelly-app bij de apparaatinformatie."));
        SmartHomePanel.Children.Add(shelly);
        SmartHomePanel.Children.Add(shellyStatus);
        SmartHomePanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Shelly toevoegen"), async () =>
        {
            string address = shelly.Text.Trim();
            if (address.Length == 0 || home.ShellyDevices.Contains(address)) return;
            shellyStatus.Text = Loc.T("Verbinden…");
            var devices = await new ShellyProvider(new[] { address }).GetDevicesAsync(CancellationToken.None);
            if (devices.Count == 0)
            {
                shellyStatus.Text = Loc.T("Geen Shelly gevonden op dit adres (of hij heeft een wachtwoord).");
                return;
            }
            home.ShellyDevices.Add(address);
            Saved();
        })));

        // Meldingen en veiligheid
        SmartHomePanel.Children.Add(Ui.Label(Loc.T("Melding geven bij")));
        var notifyList = new StackPanel();
        SmartHomePanel.Children.Add(notifyList);
        SmartHomePanel.Children.Add(Ui.Switch(Loc.T("Sloten, alarm en garagedeuren mogen bediend worden"),
            Loc.T("Altijd met een bevestiging, want iedereen bij je scherm kan klikken."), home.AllowSensitive,
            v => { home.AllowSensitive = v; _settings.Save(); SmartHomeService.Configure(_settings); }));

        // Eigen groepen
        SmartHomePanel.Children.Add(Ui.Label(Loc.T("Groepen")));
        SmartHomePanel.Children.Add(Ui.Hint(Loc.T("Zet lampen en schakelaars, ook van verschillende merken, samen in één tegel. Een klik op de tegel zet alles tegelijk aan of uit.")));
        var groupsPanel = new StackPanel();
        SmartHomePanel.Children.Add(groupsPanel);

        if (!SmartHomeService.HasAnySystem)
        {
            notifyList.Children.Add(Ui.Hint(Loc.T("Koppel eerst een systeem, dan kun je hier kiezen bij welke sensor (bv. de deurbel) je een melding krijgt.")));
            groupsPanel.Children.Add(Ui.Hint(Loc.T("Koppel eerst een systeem, dan kun je groepen maken.")));
            return;
        }
        notifyList.Children.Add(Ui.Hint(Loc.T("Sensoren ophalen…")));
        SmartHomePanel.Loaded += async (_, _) => await FillDeviceListsAsync(notifyList, groupsPanel);
        if (SmartHomePanel.IsLoaded) _ = FillDeviceListsAsync(notifyList, groupsPanel);
    }

    private async Task FillDeviceListsAsync(StackPanel list, StackPanel groupsPanel)
    {
        var (devices, problems) = await SmartHomeService.GetDevicesAsync(fresh: true);
        FillGroups(groupsPanel, devices);
        var sensors = devices.Where(d => d.Kind is DeviceKind.Binary or DeviceKind.Event).ToList();
        list.Children.Clear();
        if (sensors.Count == 0)
        {
            list.Children.Add(Ui.Hint(problems.FirstOrDefault() ?? Loc.T("Geen sensoren gevonden.")));
            return;
        }
        var notify = _settings.SmartHome.NotifyDevices;
        foreach (var sensor in sensors)
        {
            var box = Ui.Switch(sensor.Name, sensor.Source, notify.Contains(sensor.Key), on =>
            {
                if (on && !notify.Contains(sensor.Key)) notify.Add(sensor.Key);
                if (!on) notify.Remove(sensor.Key);
                _settings.Save();
                SmartHomeService.Configure(_settings);
            });
            box.Margin = new Thickness(0, 8, 0, 0);
            list.Children.Add(box);
        }
    }

    /// <summary>Per groep: de naam en een schakelaar voor elke lamp of schakelaar die erin kan.</summary>
    private void FillGroups(StackPanel panel, List<SmartDevice> devices)
    {
        panel.Children.Clear();
        var candidates = devices.Where(SmartGroups.CanJoin).ToList();
        if (candidates.Count == 0) panel.Children.Add(Ui.Hint(Loc.T("Geen lampen of schakelaars gevonden.")));

        void SaveGroups()
        {
            _settings.Save();
            SmartHomeService.Refresh();
        }

        foreach (var group in _settings.SmartHome.Groups.ToList())
        {
            var card = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            card.Children.Add(Ui.Columns((Loc.T("Naam van de groep"), Ui.Field(group.Name, v => { group.Name = v.Trim(); SaveGroups(); }), "*")));
            foreach (var device in candidates)
            {
                var box = Ui.Switch(device.Name, device.Source, group.Devices.Contains(device.Key), on =>
                {
                    if (on && !group.Devices.Contains(device.Key)) group.Devices.Add(device.Key);
                    if (!on) group.Devices.Remove(device.Key);
                    SaveGroups();
                });
                box.Margin = new Thickness(0, 6, 0, 0);
                card.Children.Add(box);
            }
            var remove = Ui.Button(Loc.T("Groep verwijderen"), () =>
            {
                _settings.SmartHome.Groups.Remove(group);
                SaveGroups();
                FillGroups(panel, devices);
            });
            remove.Margin = new Thickness(0, 10, 0, 0);
            card.Children.Add(remove);
            panel.Children.Add(card);
        }

        if (candidates.Count == 0) return;
        var add = Ui.Button(Loc.T("Nieuwe groep"), () =>
        {
            _settings.SmartHome.Groups.Add(new SmartGroupSettings { Name = Loc.T("Nieuwe groep") });
            SaveGroups();
            FillGroups(panel, devices);
        });
        add.Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(add);
    }

    // ─────────────────────────── Deurbel (ntfy) ───────────────────────────

    private TextBlock? _doorbellStatus;

    private void BuildDoorbellSection()
    {
        DoorbellPanel.Children.Clear();
        var bell = _settings.Doorbell;
        _doorbellStatus = Ui.Hint("");

        void Changed()
        {
            _settings.Save();
            App.Instance.ConfigureDoorbell();
            UpdateDoorbellStatus();
        }

        DoorbellPanel.Children.Add(Ui.Hint(Loc.T("Laat je deurbel (bijvoorbeeld een ESP32) een bericht sturen naar een onderwerp op ntfy. IdleDash luistert mee, net als de ntfy-app op je telefoon.")));
        DoorbellPanel.Children.Add(Ui.Switch(Loc.T("Luisteren naar de deurbel"), null, bell.Enabled, v => { bell.Enabled = v; Changed(); }));

        var server = Ui.Field(bell.Server, v => { bell.Server = NtfyService.NormalizeServer(v); Changed(); });
        var topic = Ui.Field(bell.Topic ?? "", v => { bell.Topic = v.Trim(); Changed(); });
        DoorbellPanel.Children.Add(Ui.Columns((Loc.T("Server"), server, "*"), (Loc.T("Onderwerp"), topic, "*")));
        DoorbellPanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Willekeurige naam maken"), () =>
        {
            const string letters = "abcdefghijkmnpqrstuvwxyz23456789";
            string name = "deurbel-" + new string(Enumerable.Range(0, 12).Select(_ => letters[Random.Shared.Next(letters.Length)]).ToArray());
            topic.Text = name;
            bell.Topic = name;
            Changed();
        })));
        DoorbellPanel.Children.Add(Ui.Hint(Loc.T("Kies een lange, moeilijk te raden naam: op ntfy.sh kan iedereen meelezen die de naam kent. Je deurbel stuurt een gewoon webverzoek (POST) naar de server met daarachter / en het onderwerp.")));

        DoorbellPanel.Children.Add(Ui.Label(Loc.T("Token (alleen bij een beveiligd onderwerp)")));
        var token = new PasswordBox { Password = Secrets.Unprotect(bell.Token) ?? "" };
        token.LostFocus += (_, _) =>
        {
            bell.Token = token.Password.Length > 0 ? Secrets.Protect(token.Password) : null;
            Changed();
        };
        DoorbellPanel.Children.Add(token);

        DoorbellPanel.Children.Add(Ui.Switch(Loc.T("Muziek pauzeren als de bel gaat"), null, bell.PauseMusic, v => { bell.PauseMusic = v; _settings.Save(); }));

        DoorbellPanel.Children.Add(Ui.Label(Loc.T("Geluid")));
        DoorbellPanel.Children.Add(Ui.Combo(new[]
        {
            (Loc.T("Ingebouwde dingdong"), "builtin"), (Loc.T("Eigen geluid"), "custom"), (Loc.T("Geen geluid"), "none"),
        }, bell.Sound, v =>
        {
            bell.Sound = v;
            _settings.Save();
            BuildDoorbellSection();
        }));
        if (bell.Sound == "custom")
        {
            var soundName = Ui.Hint(bell.SoundFile != null && File.Exists(bell.SoundFile) ? Loc.T("Gekozen.") : Loc.T("Nog geen geluid gekozen."));
            DoorbellPanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Geluid kiezen…"), () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("Geluiden") + " (*.mp3, *.wav, *.wma, *.m4a)|*.mp3;*.wav;*.wma;*.m4a" };
                if (dialog.ShowDialog(this) != true) return;
                if (DoorbellSound.ImportCustom(dialog.FileName) is not { } copy)
                {
                    soundName.Text = Loc.T("Dit bestand kon niet gekopieerd worden.");
                    return;
                }
                bell.SoundFile = copy;
                _settings.Save();
                soundName.Text = Loc.T("Gekozen: {0}", Path.GetFileName(dialog.FileName));
            })));
            DoorbellPanel.Children.Add(soundName);
        }

        DoorbellPanel.Children.Add(Ui.Row(
            Ui.Button(Loc.T("Test op deze pc"), () => App.Instance.TestDoorbell()),
            Ui.Button(Loc.T("Testbericht via ntfy"), async () =>
            {
                _doorbellStatus.Text = Loc.T("Versturen…");
                string? error = await NtfyService.SendTestAsync(bell.Server, bell.Topic, Secrets.Unprotect(bell.Token), Loc.T("Test vanuit IdleDash"));
                _doorbellStatus.Text = error ?? Loc.T("Verstuurd. Staat alles goed, dan gaat nu de bel op deze pc en op je telefoon.");
            })));
        DoorbellPanel.Children.Add(_doorbellStatus);
        UpdateDoorbellStatus();
    }

    private void UpdateDoorbellStatus()
    {
        if (_doorbellStatus == null) return;
        var bell = _settings.Doorbell;
        _doorbellStatus.Text = !bell.Enabled ? Loc.T("Staat uit.")
            : !NtfyService.IsValidTopic(bell.Topic) ? Loc.T("Vul een onderwerp in: letters, cijfers, - en _.")
            : NtfyService.Problem ?? Loc.T("IdleDash luistert mee.");
    }

    // ─────────────────────────── Back-up ───────────────────────────

    private void BuildBackupSection()
    {
        BackupPanel.Children.Clear();
        var status = Ui.Hint("");
        BackupPanel.Children.Add(Ui.Hint(Loc.T("Een back-up bevat je indeling, widgets, thema's en koppelingen. Wachtwoorden en tokens werken alleen op deze pc met dit Windows-account. Je Google-login zit er niet in.")));
        BackupPanel.Children.Add(Ui.Row(
            Ui.Button(Loc.T("Back-up maken…"), () =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"IdleDash-back-up-{DateTime.Now:yyyy-MM-dd}.json",
                    Filter = Loc.T("Back-up van IdleDash") + " (*.json)|*.json",
                };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    File.WriteAllText(dialog.FileName, BackupService.Create(_settings, AppInfo.VersionText, DateTime.Now));
                    status.Text = Loc.T("Back-up opgeslagen.");
                }
                catch (Exception ex)
                {
                    status.Text = Loc.T("Opslaan lukte niet: {0}", ex.Message);
                }
            }, accent: true),
            Ui.Button(Loc.T("Terugzetten…"), () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("Back-up van IdleDash") + " (*.json)|*.json" };
                if (dialog.ShowDialog(this) == true) Restore(File.ReadAllText(dialog.FileName), status);
            })));
        BackupPanel.Children.Add(Ui.Switch(Loc.T("Elke dag automatisch een back-up maken"), Loc.T("De laatste 10 blijven bewaard."), _settings.AutoBackup,
            v => { _settings.AutoBackup = v; _settings.Save(); }));

        var automatic = BackupService.ListAutomatic();
        if (automatic.Count > 0)
        {
            BackupPanel.Children.Add(Ui.Label(Loc.T("Automatische back-ups")));
            foreach (var (path, date) in automatic)
            {
                var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = Loc.Date(date), VerticalAlignment = VerticalAlignment.Center });
                var button = Ui.Button(Loc.T("Terugzetten"), () => Restore(File.ReadAllText(path), status));
                button.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(button, 1);
                row.Children.Add(button);
                BackupPanel.Children.Add(row);
            }
            BackupPanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Map met back-ups openen"), () =>
                System.Diagnostics.Process.Start("explorer.exe", $"\"{BackupService.AutoFolder}\""))));
        }
        BackupPanel.Children.Add(status);
    }

    private void Restore(string content, TextBlock status)
    {
        string? json = BackupService.Validate(content, out string? error);
        if (json == null)
        {
            status.Text = error ?? "";
            return;
        }
        var created = BackupService.CreatedAt(content);
        var (ok, _) = Dialogs.Confirm(Loc.T("Back-up terugzetten?"),
            created is DateTime date
                ? Loc.T("Je huidige instellingen worden vervangen door die van {0}. IdleDash start daarvoor even opnieuw.", Loc.Date(date))
                : Loc.T("Je huidige instellingen worden vervangen door die uit de back-up. IdleDash start daarvoor even opnieuw."),
            Loc.T("Terugzetten"));
        if (!ok) return;
        try
        {
            BackupService.ScheduleRestore(json, AppInfo.VersionText);
            App.Instance.Restart();
        }
        catch (Exception ex)
        {
            status.Text = Loc.T("Terugzetten lukte niet: {0}", ex.Message);
        }
    }

    // ─────────────────────────── Spotify ───────────────────────────

    private void BuildSpotifySection()
    {
        SpotifyPanel.Children.Clear();
        var spotify = _settings.Spotify;
        var status = Ui.Hint("");
        SpotifyPanel.Children.Add(Ui.Hint(Loc.T("Start je playlists met een knop op het dashboard (widget Playlists). Dit werkt met Spotify Premium. Spotify vraagt dat je daarvoor eenmalig een eigen Spotify-app aanmaakt; dat duurt een paar minuten.")));

        if (SpotifyService.IsConnected)
        {
            SpotifyPanel.Children.Add(new TextBlock
            {
                Text = spotify.AccountName != null ? Loc.T("Gekoppeld met {0}", spotify.AccountName) : Loc.T("Gekoppeld"),
                Margin = new Thickness(0, 8, 0, 0),
            });
            SpotifyPanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Ontkoppelen"), SpotifyService.Disconnect)));
            return;
        }

        SpotifyPanel.Children.Add(Ui.Hint(Loc.T("1. Open het Spotify-dashboard en log in. 2. Klik op Create app en vul een naam in, bijvoorbeeld IdleDash. 3. Vul bij Redirect URIs precies het adres hieronder in en klik op Add. 4. Vink Web API aan, ga akkoord en klik op Save. 5. Kopieer de Client ID en plak hem hieronder."), 10));
        SpotifyPanel.Children.Add(Ui.Label(Loc.T("Redirect URI")));
        SpotifyPanel.Children.Add(new TextBox { Text = SpotifyApi.RedirectUri, IsReadOnly = true });
        SpotifyPanel.Children.Add(Ui.Row(
            Ui.Button(Loc.T("Spotify-dashboard openen"), () => Browser.Open("https://developer.spotify.com/dashboard")),
            Ui.Button(Loc.T("Adres kopiëren"), () =>
            {
                try
                {
                    Clipboard.SetText(SpotifyApi.RedirectUri);
                    status.Text = Loc.T("Gekopieerd.");
                }
                catch
                {
                    // klembord even bezet: dan zelf overtypen
                }
            })));

        SpotifyPanel.Children.Add(Ui.Label(Loc.T("Client ID")));
        var clientId = new TextBox { Text = spotify.ClientId ?? "" };
        SpotifyPanel.Children.Add(clientId);
        SpotifyPanel.Children.Add(Ui.Row(Ui.Button(Loc.T("Koppelen met Spotify"), async () =>
        {
            spotify.ClientId = clientId.Text.Trim();
            _settings.Save();
            SpotifyService.Configure(_settings);
            if (spotify.ClientId.Length < 16)
            {
                status.Text = Loc.T("Plak eerst de Client ID van je Spotify-app.");
                return;
            }
            status.Text = Loc.T("Er is een browservenster geopend. Log daar in bij Spotify en geef toestemming.");
            status.Text = await SpotifyService.ConnectAsync() ?? "";
        }, accent: true)));
        SpotifyPanel.Children.Add(status);
    }
}
