# language: de
@sample @localised
Funktionalität: Lokalisierte Gherkin-Schlüsselwörter
  Deutsche Schlüsselwörter. Der Report muss `keywordType` (Context/Action/Outcome) für das
  Styling verwenden und das rohe Schlüsselwort anzeigen — siehe SPEC 4.3. Jeder Code der
  auf "Given"/"When"/"Then" vergleicht, scheitert hier.

  Grundlage:
    Angenommen der Katalog ist befüllt

  Szenario: Ein Produkt in den Warenkorb legen
    Angenommen ein Produkt "Widget" kostet 9.99
    Wenn ich es in den Warenkorb lege
    Dann sollte die Summe 9.99 betragen

  Szenariogrundriss: Rabattstufen
    Angenommen ein Produkt "<name>" kostet <preis>
    Wenn ich die Rabattstufe "<stufe>" anwende
    Dann sollte die Summe <erwartet> betragen

    Beispiele:
      | name   | preis | stufe | erwartet |
      | Widget | 100   | keine | 100      |
      | Widget | 100   | zehn  | 90       |
