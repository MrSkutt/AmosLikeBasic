# Välkommen

Välkommen till AMOS Like Basic! Det här är ett programspråk och en utvecklingsmiljö som är inspirerade av AMOS, BASIC-språket som många lärde sig programmera spel med på Amiga i slutet av 80-talet. Idén är densamma nu som då: du skriver några rader kod, trycker på RUN och ser direkt resultatet på skärmen. Du kan rita grafik, flytta figurer, spela musik och läsa av tangentbordet med enkla kommandon.

Skillnaden är att AMOS Like Basic kör på en modern Mac eller PC. Grafiken ritas av grafikkortet, så du kan ha många lager som scrollar, hundratals sprites och specialeffekter utan att datorn blir långsam.

## Hur den här boken fungerar

Boken har tre delar:

- **Del 1: Lär dig BASIC** börjar från noll. Du behöver inte kunna något om programmering. Varje kapitel bygger på det förra, så läs dem i ordning.
- **Del 2: Grafik och spel** visar hur du ritar, använder sprites, bygger banor av tiles, spelar ljud och läser av tangentbord och handkontroll. Delen slutar med ett helt plattformsspel.
- **Del 3: Referens** listar alla kommandon och funktioner i bokstavsordning, med exakt syntax. Där slår du upp saker när du väl kan grunderna.

Kodexemplen ser ut så här:

```basic
PRINT "HEJ PÅ DIG!"
```

Skriv in dem i editorn och tryck på **RUN**. Ändra i dem och se vad som händer, det är det bästa sättet att lära sig. Du kan inte förstöra något genom att prova.

> **Tips:** Färgerna i kodexemplen visar vad som är vad: gult är kommandon, blått är funktioner, grönt är text inom citattecken, orange är tal och grått är kommentarer.

I slutet av många kapitel finns uppgifter märkta **Prova själv**. Gör dem gärna innan du läser vidare.

## Utvecklingsmiljön

När du startar AMOS Like Basic ser du editorn, där du skriver programmet. Överst finns några knappar:

| Knapp | Vad den gör |
|---|---|
| **RUN** | Startar programmet. Ett nytt fönster, spelfönstret, öppnas och visar resultatet. |
| **STOP** | Stoppar programmet. |
| **PAUSE** / **STEP** | Pausar programmet och kör det sedan en rad i taget. Bra för att hitta fel. |
| **SPRITES** | Öppnar sprite-editorn, där du ritar figurer pixel för pixel. |
| **MAP** | Öppnar karteditorn, där du bygger banor av tiles. |
| **LOAD** / **SAVE** | Öppnar och sparar projekt (filer som slutar på `.amosproj`). |

Klickar du i marginalen till vänster om en rad sätter du en **brytpunkt**. Programmet stannar då när det kommer till den raden, och du kan se värdet på alla variabler i variabelrutan (tryck **F1** för att visa eller dölja den).

I spelfönstret växlar **F12** mellan fönster och helskärm. Spelfönstrets titelrad visar hur fort programmet går: **FPS** (bilder per sekund), **BASIC** (hur stor del av tiden din kod arbetar) och **CPU**.

Under menyn **Project** finns **Export app...**, som gör ett färdigt program av ditt spel som andra kan köra utan att installera AMOS Like Basic. Det beskrivs i kapitlet om att exportera.
