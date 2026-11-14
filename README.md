# Desktop Widgets for Home Assistant

Unabhängige Windows-App für Home Assistant. Die Widgets liegen frei positionierbar auf dem Desktop. Das Programm-Icon im Infobereich öffnet per Rechtsklick das Menü **Widget hinzufügen**.

- **Solar** zeigt PV-Leistung, Akkustand, Hausverbrauch und Netzleistung.
- **Fenster-Tür-Sensoren** zeigt Öffnungszustände mit passenden Symbolen für Fenster, Türen und Garagentore.
- **Schalter** zeigt Ein-/Aus-Zustände und schaltet steuerbare Entitäten per Klick. Die Anzeige kann waagerecht oder senkrecht angeordnet werden.

Sensoren lassen sich aus Home Assistant auswählen. Die Widgets bieten eine Live-Vorschau für Schrift, Farben, Transparenz, Symbolgröße und Abstände. Namen, Reihenfolge und Positionen werden lokal gespeichert.

## Screenshots

Alle Anzeigen verwenden Beispieldaten.

| Solar | Fenster-Tür-Sensoren |
| --- | --- |
| ![Solar-Widget mit PV-Leistung, Akku, Hausverbrauch und Netzleistung](docs/screenshots/01-solar.png) | ![Garagentor, offene Haustür und geschlossenes Fenster](docs/screenshots/02-fenster-tueren.png) |

**Schalter in senkrechter Anordnung**

![Drei Schalter mit Ein- und Aus-Zuständen](docs/screenshots/03-schalter.png)

## Bauen und starten

Unter Windows **Build.cmd** ausführen. Das Skript verwendet den .NET-Framework-Compiler und erzeugt **SolarKachel.exe** sowie **SolarKachel-Setup.exe**. Der Installer richtet die App im aktuellen Windows-Konto ein. Die Programmdatei kann auch direkt gestartet werden.

In den Einstellungen die eigene Home-Assistant-Adresse und einen langlebigen Zugriffstoken eintragen. Die App ruft Zustände über die [Home-Assistant-REST-API](https://contributors.home-assistant.io/core/internals/rest-api/#states) ab. Die Abfrage läuft alle 15 Sekunden. Der Token wird lokal mit Windows DPAPI verschlüsselt.

## Symbole

Die Material Design Icons von Pictogrammers unterliegen [Apache 2.0](MDI-LICENSE.txt). Die verwendeten Symbole sind in [MDI-NOTICE.txt](MDI-NOTICE.txt) aufgeführt.