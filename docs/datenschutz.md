# Datenschutz für Desktop Widgets for Home Assistant

Desktop Widgets for Home Assistant ist eine unabhängige Windows-App von Marcin Kulmaczewski. Die App zeigt Zustände aus deiner eigenen Home-Assistant-Instanz an und übermittelt Bedienaktionen an diese Instanz.

Die App speichert die Home-Assistant-Adresse, Widget-Einstellungen, ausgewählte Entitäten und Positionen im lokalen Windows-Benutzerprofil. Der Zugriffstoken wird dort mit der Windows Data Protection API für das Benutzerkonto geschützt. Beim Export einer Widget-Sicherung werden die Home-Assistant-Adresse und der Zugriffstoken nicht exportiert.

Für die Verbindung übermittelt die App den Zugriffstoken an den Home-Assistant-Server, den du eingetragen hast. Sie ruft dort Zustände und Geräteattribute ab und sendet bei deinen Bedienaktionen die entsprechenden Befehle. Die App sendet keine Nutzungsdaten oder Geräteinformationen an den Entwickler und enthält keine Werbung oder Analysefunktion.

Du kannst lokal gespeicherte Daten durch Zurücksetzen oder Löschen der App-Daten entfernen. Den Zugriffstoken kannst du zusätzlich in deinem Home-Assistant-Profil widerrufen. Für den Microsoft Store gelten die Datenschutzbestimmungen von Microsoft.

Bei Fragen zum Datenschutz kannst du den Entwickler über sein [GitHub-Profil](https://github.com/kulmi84) kontaktieren.
