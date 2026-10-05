# Architektur und Entscheidungen

## Warum WPF

WinUI 3 bräuchte das Windows App SDK als Laufzeitabhängigkeit und bringt den
Fluent-Look mit, der hier gerade nicht gewollt ist. WPF auf
`net10.0-windows10.0.26100.0` liefert die WinRT-Projektionen ohne Zusatzpaket und
lässt das Aussehen vollständig selbst bestimmen. Das Tray-Icon spricht
`Shell_NotifyIcon` direkt an, damit keine WinForms-Abhängigkeit nötig ist.

## Tray-Icon

Tray-Icons sind quadratisch und bei 100 % Skalierung nur 16 px breit. Damit Glyph und
Ziffer gleich hoch und so groß wie möglich nebeneinander passen, wird das Icon zur
Laufzeit gerendert: aus den tatsächlichen Ink-Bounds beider Zeichen ergibt sich die
größte gemeinsame Höhe, gerendert wird vierfach übersampelt. Ab zehn Geräten steht
dort „9+“, was auf 16 px kaum Platz hat – praktisch hält der Windows-Stack aber nur
eine Handvoll Verbindungen gleichzeitig.

Ob Windows das Icon in der Taskbar oder im Überlauf zeigt, merkt es sich pro
EXE-Pfad unter `HKCU:\Control Panel\NotifyIconSettings` (`IsPromoted`). Debug-,
Release- und installierter Build zählen also getrennt.

## Releases und Updates

Releases baut [Velopack](https://velopack.io) im Workflow
`.github/workflows/release.yml`: Setup, Vollpaket und ab dem zweiten Release ein
Delta-Paket, veröffentlicht als GitHub-Release. Die Repo-Adresse übergibt der
Workflow als `RepositoryUrl`; das SDK macht daraus ein Assembly-Attribut, aus dem die
App zur Laufzeit die Update-Quelle liest. Lokale Builds haben keins und suchen
deshalb nicht.

Velopack muss vor jeder UI laufen, weil Installer und Updater die Exe mit
Hook-Argumenten starten und ein sofortiges Beenden erwarten. Darum ersetzt ein
eigenes `Program.Main` den von WPF generierten Einstiegspunkt.

Ein Update wird über `WaitExitThenApplyUpdates` und ein reguläres Beenden
eingespielt, nicht über `ApplyUpdatesAndRestart`: Letzteres beendet den Prozess hart,
und das Tray-Icon bliebe als Geist stehen, bis die Maus darüberfährt.

## Exe-Größe

Die Exe ist rund 27 MB groß, obwohl der eigene Code nur gut 100 KB ausmacht. Der Rest
ist `Microsoft.Windows.SDK.NET.dll` (26,3 MB), die vollständige WinRT-Projektion, die
der TFM mitbringt. Trimming lehnt das SDK bei WPF ab (`NETSDK1168`), und
Bundle-Kompression gibt es nur für self-contained Builds, die insgesamt größer wären.
Schrumpfen ließe sie sich nur mit einer selbst erzeugten CsWinRT-Projektion, die
ausschließlich `Windows.Devices.Bluetooth` und `Windows.Devices.Enumeration` enthält.
Im Betrieb zählt ohnehin der Speicher: im Leerlauf rund 51 MB Private Bytes.
