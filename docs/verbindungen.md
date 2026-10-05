# Verbinden und Trennen

Windows hat keine öffentliche API, um ein gekoppeltes Bluetooth-Gerät zu verbinden
oder zu trennen. Die App kombiniert deshalb je nach Gerätetyp verschiedene Wege, und
jeder hat seine Grenzen.

## BR/EDR (klassisches Bluetooth: Headsets, Mäuse, Tastaturen)

Die App ruft `BluetoothSetServiceState` auf und schaltet damit die Profile des Geräts
an oder ab. Das ist der Weg, den auch andere Tools gehen, aber kein echtes „Connect“:
Beim Trennen verschwinden die Dienste aus der Enumeration, deshalb merkt sich
`ServiceCache` sie vorher. Ist für ein Gerät nichts gespeichert, fällt die App auf die
Profile zurück, die seine Geräteklasse nahelegt. Bei exotischen Geräten kann das
Verbinden daher fehlschlagen.

## LE

Zum Verbinden hält die App eine `GattSession` mit `MaintainConnection` offen. Das ist
dokumentiert und zuverlässig, bindet die Verbindung aber an die Laufzeit der App –
beim Beenden werden LE-Verbindungen gelöst.

## LE mit HID (Mäuse, Tastaturen, Gamepads)

Trennen ist hier nicht möglich. Die Verbindung gehört dem HID-over-GATT-Treiber von
Windows, und keine API bringt ihn dazu loszulassen; auch die Einstellungen-App bietet
für solche Geräte nur „Entfernen“ an. Die App erkennt den Fall an der PnP-Geräteklasse
(nicht am Namen) und blendet den Schalter aus, solange das Gerät verbunden ist.
Verbinden funktioniert dagegen.

## Adapter aus

Mit abgeschaltetem Radio listet der `DeviceWatcher` weiterhin alle gekoppelten Geräte,
und ihr `IsConnected` behält den letzten Wert – ohne Gegenmaßnahme sähe alles
verbunden aus. Der Radiozustand wird deshalb getrennt über `Windows.Devices.Radios`
gelesen; ist er aus, bleibt die Liste leer und das Flyout zeigt „Bluetooth is off“.

## Verwaiste Kopplungen

Windows führt Kopplungen weiter, die mit einem anderen Adapter entstanden sind (alter
Dongle, gewechselte Hardware). Der aktuelle Adapter erreicht sie nicht; die App prüft
das gegen den Pairing-Store des Radios und blendet solche Einträge aus. Ist gar kein
Adapter vorhanden, wird nicht gefiltert – „keine Aussage möglich“ darf nicht als
„verwaist“ durchgehen und die Liste leeren.

## Erfolgskontrolle

Jede Zustandsänderung wird nach dem Auslösen gegen `System.Devices.Aep.IsConnected`
geprüft, bevor sie als Erfolg gilt – sonst ginge ein stillschweigend ignoriertes
Trennen als gelungen durch.

## Akkustand

Der Akkustand stammt aus der PnP-Eigenschaft, die Windows selbst befüllt. Geräte, die
sie nicht setzen, zeigen keinen Wert; für verbundene LE-Geräte wird ersatzweise der
GATT Battery Service gelesen. Abgefragt wird alle 90 Sekunden und beim Öffnen des
Flyouts.
