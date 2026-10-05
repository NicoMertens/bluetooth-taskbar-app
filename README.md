# BluetoothFlyout

Ein leichtgewichtiges Tray-Applet für Windows 11, das die Bluetooth-Schnellein­stellungen
ersetzt. Optik und Bedienung orientieren sich am Bluetooth-Widget von KDE Plasma
(Breeze Dark), wie es CachyOS ausliefert: Kopfzeile, flache Geräteliste mit
Verbinden-Schalter und Akkustand, Fußzeile mit Verweis in die Systemeinstellungen.

```
┌──────────────────────────────────────┐
│ Bluetooth                         ⟳  │
├──────────────────────────────────────┤
│  ⌨  IDE 4000 keyboard        82% ▭   │
│      Connected                       │
│  🎧  OpenCirclet i10  [●━━]  90% ▭   │
│      Connected                       │
│  🎧  Jabra Elite      [━━○]          │
│      Not connected                   │
├──────────────────────────────────────┤
│  ⚙  Configure Bluetooth…             │
└──────────────────────────────────────┘
```

## Funktionsumfang (v1)

- Gekoppelte Geräte auflisten, mit Live-Aktualisierung über einen `DeviceWatcher`
- Verbinden und Trennen per Schalter
- Akkustand für Geräte, die ihn melden; unter 30 % orange hervorgehoben
- Tray-Icon zeigt die Anzahl der verbundenen Geräte als Ziffer neben dem Glyph
- Glyphfarbe folgt dem Taskbar-Theme und wechselt bei Umschaltung sofort mit
- Zeigt „Bluetooth is off“ statt einer Geräteliste, wenn der Adapter aus ist
- Kontextmenü (Rechtsklick): Refresh, Bluetooth settings, Exit

Die Oberfläche ist englisch; diese Doku ist es nicht.

Bewusst **nicht** enthalten: Adapter ein-/ausschalten sowie Suchen und Koppeln
neuer Geräte. Dafür führt die Fußzeile in die Windows-Einstellungen.

## Tray-Icon

Sobald mindestens ein Gerät verbunden ist, steht die Anzahl als Ziffer rechts
neben dem Bluetooth-Glyph — beide exakt gleich hoch. Tray-Icons sind quadratisch
und bei 100 % Skalierung nur 16 px breit, deshalb berechnet `IconRenderer` aus den
tatsächlichen Ink-Bounds von Glyph und Ziffer die größtmögliche gemeinsame Höhe,
die nebeneinander noch hineinpasst, und rendert vierfach übersampelt.

Ab zehn verbundenen Geräten steht dort „9+“; zwei Zeichen plus Glyph lassen auf
16 px allerdings kaum Platz. Praktisch tritt das nicht auf — der Windows-Stack
hält nur eine Handvoll Verbindungen gleichzeitig.

**Windows 11 versteckt Icons neuer Apps zunächst im Overflow.** Einmal per
Drag-and-drop in die Taskbar ziehen, dann bleibt es sichtbar. Hinterlegt wird das
unter `HKCU:\Control Panel\NotifyIconSettings` als `IsPromoted` — pro
EXE-Pfad, Debug- und Release-Build zählen also getrennt.

## Bauen und Starten

Voraussetzung ist das .NET 10 SDK; weitere Abhängigkeiten gibt es nicht.

```powershell
dotnet build source
dotnet run --project source/BluetoothFlyout
```

Die App startet ohne Fenster und legt nur ein Tray-Icon an. Zum Ausprobieren ohne
Tray-Klick öffnet `--show` das Flyout direkt — es schließt sich wie immer, sobald
der Fokus woanders hingeht:

```powershell
dotnet run --project source/BluetoothFlyout -- --show
```

## Installieren und Aktualisieren

Unter **Releases** die `BluetoothFlyout-win-Setup.exe` der neuesten Version laden
und ausführen. Das Setup installiert ohne Adminrechte nach
`%LocalAppData%\BluetoothFlyout`, legt eine Startmenü- und eine Autostart-Verknüpfung
an und installiert die .NET 10 Desktop Runtime mit, falls sie fehlt. Die Exe ist
nicht signiert; SmartScreen warnt deshalb beim ersten Start („Weitere Informationen“
→ „Trotzdem ausführen“).

Die installierte App sucht beim Start und danach alle sechs Stunden nach einer neuen
Version und lädt sie im Hintergrund. Ist eine geladen, bietet das Tray-Menü
„Restart to update to …“ an; ohne diesen Klick wird das Update beim nächsten
Beenden eingespielt. Lokale Builds und `dotnet run` suchen nicht nach Updates.

Deinstalliert wird über *Einstellungen → Apps*.

## Release veröffentlichen

Ein Versions-Tag startet den Workflow `.github/workflows/release.yml`:

```powershell
git tag v1.2.0
git push origin v1.2.0
```

Er baut mit [Velopack](https://velopack.io) das Setup, ein Vollpaket und – ab dem
zweiten Release – ein Delta-Paket und veröffentlicht alles als GitHub-Release. Die
Repo-Adresse für die Update-Suche übergibt der Workflow beim Bauen selbst.

Zum lokalen Ausprobieren ohne Installer eine Einzeldatei veröffentlichen:

```powershell
dotnet publish source/BluetoothFlyout -c Release -r win-x64
```

Das Ergebnis ist eine einzelne, framework-abhängige `_out\publish\BluetoothFlyout.exe`
(siehe unten).

Laufzeit-Fußabdruck im Leerlauf: rund 51 MB Private Bytes, 105 MB Working Set.

## Aufbau

| Pfad | Aufgabe |
| --- | --- |
| `Bluetooth/BluetoothMonitor.cs` | Zwei `DeviceWatcher` (BR/EDR und LE), zusammengeführt zu einer Geräteliste |
| `Bluetooth/ConnectionController.cs` | Verbinden und Trennen, mit Erfolgskontrolle |
| `Bluetooth/AvailabilityProbe.cs` | Erkennt Kopplungen fremder Adapter |
| `Bluetooth/RadioMonitor.cs` | Zustand des Bluetooth-Adapters (an/aus) |
| `Bluetooth/DeviceDetailsProbe.cs` | Akkustand und HID-Besitz aus einer PnP-Abfrage |
| `Bluetooth/DeviceKind.cs` | Class of Device → Icon-Kategorie |
| `Bluetooth/ServiceCache.cs` | Merkt sich Dienst-GUIDs über ein Trennen hinweg |
| `Interop/` | P/Invoke für `Shell_NotifyIcon` und `BluetoothAPIs` |
| `Tray/` | Tray-Icon mit Anzahl-Ziffer, zur Laufzeit gerendert; Theme-Erkennung |
| `Themes/Breeze.xaml` | Breeze-Dark-Palette, Toggle-Switch, Icon-Geometrien |
| `ViewModels/`, `Views/` | Flyout-Fenster und seine Zustände |
| `Updates/UpdateService.cs` | Update-Suche und -Einspielen über Velopack aus den GitHub-Releases |
| `Program.cs` | Einstiegspunkt; lässt Velopack vor dem WPF-Start laufen |

### Warum WPF

WinUI 3 bräuchte das Windows App SDK als Laufzeitabhängigkeit und bringt den
Fluent-Look mit, der hier gerade nicht gewollt ist. WPF auf
`net10.0-windows10.0.26100.0` liefert die WinRT-Projektionen ohne Zusatzpaket und
lässt das Aussehen vollständig selbst bestimmen. Das Tray-Icon spricht
`Shell_NotifyIcon` direkt an, damit keine WinForms-Abhängigkeit nötig ist.

## Bekannte Einschränkungen

**Verbinden und Trennen ist nicht so sauber, wie es sein sollte — weil Windows
keine öffentliche API dafür hat.**

- *BR/EDR* (Headsets, Mäuse, Tastaturen): Die App ruft
  `BluetoothSetServiceState` auf und schaltet damit die Profile des Geräts an
  oder ab. Das ist der Weg, den auch andere Tools gehen, aber es ist kein echtes
  „Connect“: Beim Trennen verschwinden die Dienste aus der Enumeration, weshalb
  `ServiceCache` sie vorher festhält. Ist für ein Gerät nichts gespeichert, fällt
  die App auf die Profile zurück, die seine Geräteklasse nahelegt
  (`BluetoothProfiles.ForKind`). Bei exotischen Geräten kann das Verbinden
  deshalb fehlschlagen; die Zeile zeigt dann kurz den Fehler an.
- *LE*: Zum Verbinden hält die App eine `GattSession` mit `MaintainConnection`
  offen. Das ist dokumentiert und zuverlässig, bindet die Verbindung aber an die
  Laufzeit der App — beim Beenden werden LE-Verbindungen gelöst.
- *LE mit HID* (Bluetooth-Mäuse, -Tastaturen, -Gamepads): **Trennen ist hier gar
  nicht möglich.** Die Verbindung gehört Windows' eigenem HID-over-GATT-Treiber,
  und es gibt keine API, mit der eine Anwendung ihn dazu bringen könnte,
  loszulassen — deshalb bietet auch die Einstellungen-App für solche Geräte nur
  „Entfernen“ an, kein „Trennen“. `DeviceDetailsProbe` erkennt den Fall an der
  PnP-Geräteklasse (nicht am Namen) und blendet den Schalter aus, solange das
  Gerät verbunden ist. Trennt sich das Gerät von selbst, erscheint der Schalter
  wieder — Verbinden funktioniert nämlich sehr wohl, nur Trennen nicht.
- *Adapter aus*: Mit abgeschaltetem Radio listet der Watcher weiterhin alle
  gekoppelten Geräte, und deren `IsConnected` behält den Wert von vorher — ohne
  Gegenmaßnahme sähe also alles verbunden aus. `RadioMonitor` liest den Zustand
  deshalb getrennt über `Windows.Devices.Radios` und abonniert `StateChanged`;
  ist er aus, bleibt die Liste leer und das Flyout zeigt „Bluetooth is off“.
- *Verwaiste Kopplungen*: Windows führt Kopplungen weiter, die mit einem anderen
  Adapter entstanden sind (alter Dongle, gewechselte Hardware) — meist für längst
  entsorgte Geräte. Der aktuelle Adapter erreicht sie nicht. `AvailabilityProbe`
  prüft das gegen den Radio-Pairing-Store und blendet solche Einträge aus. In den
  Windows-Einstellungen bleiben sie sichtbar und lassen sich dort entfernen.
  Ist gar kein Adapter vorhanden, wird nicht gefiltert — „keine Aussage möglich“
  darf nicht als „verwaist“ durchgehen und die Liste leeren.

Jede Zustandsänderung wird nach dem Auslösen gegen
`System.Devices.Aep.IsConnected` gegengeprüft, bevor sie als Erfolg gilt — sonst
würde ein stillschweigend ignoriertes Trennen als gelungen durchgehen.

**Die EXE ist 27 MB groß, obwohl der eigene Code nur 107 KB ausmacht.** Der Rest
ist `Microsoft.Windows.SDK.NET.dll` (26,3 MB) — die vollständige WinRT-Projektion
des Windows SDK, die der TFM `net10.0-windows10.0.26100.0` mitbringt. Sie lässt
sich hier nicht kleiner bekommen: Trimming lehnt das SDK bei WPF ab
(`NETSDK1168`), und Bundle-Kompression gibt es nur für self-contained Builds, was
das Ergebnis größer statt kleiner machen würde. Wirklich schrumpfen ließe sie sich
nur mit einer selbst erzeugten CsWinRT-Projektion, die ausschließlich
`Windows.Devices.Bluetooth` und `Windows.Devices.Enumeration` enthält — ein
spürbarer Umbau, der v1 nicht wert war. Im Betrieb zählt ohnehin der
Speicherverbrauch, und der liegt bei rund 51 MB.

Weitere Punkte:

- Der Akkustand stammt primär aus der PnP-Eigenschaft, die Windows selbst
  befüllt. Geräte, die sie nicht setzen, zeigen keinen Wert; für verbundene
  LE-Geräte wird ersatzweise der GATT Battery Service gelesen. Abgefragt wird
  alle 90 Sekunden sowie beim Öffnen des Flyouts.
- Die Liste zeigt ausschließlich gekoppelte Geräte.
- Das Flyout gibt es nur in Dark Mode — Breeze Dark ist fest verdrahtet. Nur
  das Tray-Icon folgt dem hellen bzw. dunklen Taskbar-Theme.
