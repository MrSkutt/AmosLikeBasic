# Dela ditt spel

När ditt spel är klart vill du förstås att andra ska kunna spela det, även de som inte har AMOS Like Basic. Det går att göra ett fristående program av spelet, en app för Mac eller en .exe-fil för Windows.

## Export app

Välj **Project → Export app...** i menyn. Ange vad appen ska heta och vilken plattform den ska vara för:

- **Mac and Windows** gör båda.
- **Mac (.app)** gör en app för Mac.
- **Windows (.exe)** gör en mapp med en .exe-fil.

Klicka på **Export**. Det tar ungefär en halv minut, och i loggfönstret ser du hur det går. När det är klart öppnas mappen `Export` i projektmappen. Där ligger appen och en zip-fil som är klar att skicka till någon.

Det som exporteras är programmet som står i editorn just då, även om du inte har sparat det. Bilderna och ljuden som programmet använder följer med automatiskt. Den som får appen behöver inte installera något, allt som behövs finns i den.

> **Obs:** Exporten bygger appen från AMOS Like Basics källkod, så den kräver att du kör utvecklingsmiljön från projektmappen och har .NET SDK installerat.

## Filer som följer med

Exporten letar efter alla filnamn inom citattecken i ditt program, som `"Graphics/hero.png"`, och tar med de filer som finns. Bygger programmet ihop filnamn medan det kör, till exempel `"level" & NIVA & ".amosmap"`, hittas de inte. Då kan du exportera från terminalen och ange dem själv:

```text
python3 Tools/export_app.py Projects/spelet.amosproj --name Spelet --include Graphics/Levels
```

Med `--icon bild.png` får Mac-appen en egen ikon.

## Första gången på en annan dator

Apparna är inte signerade av Apple eller Microsoft, så datorn varnar första gången.

- **Mac:** Högerklicka på appen och välj **Öppna**, och bekräfta. Sedan startar den som vanligt.
- **Windows:** Klicka på **Mer information** och **Kör ändå** om SmartScreen varnar.

## Spela

I det exporterade spelet växlar **F12** mellan fönster och helskärm, **Escape** lämnar helskärm och **F8** slår på och av bildrörseffekten. Spelet ritas i sin egen upplösning och skalas upp till skärmen med skarpa pixlar, så det ser lika retro ut på en 4K-skärm.
