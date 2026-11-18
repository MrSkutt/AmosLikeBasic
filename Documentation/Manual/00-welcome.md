# Welcome

Welcome to AMOS Like Basic! This is a programming language and editor inspired by AMOS, the BASIC that many people used to learn game programming on the Amiga in the late 80s. The idea is the same now as then: you write a few lines of code, press RUN and see the result on the screen straight away. You can draw graphics, move figures, play music and read the keyboard with simple commands.

The difference is that AMOS Like Basic runs on a modern Mac or PC. The graphics are drawn by the graphics card, so you can have many scrolling layers, hundreds of sprites and special effects without the computer slowing down.

## How this book works

The book has three parts:

- **Part 1: Learn BASIC** starts from scratch. You do not need to know anything about programming. Each chapter builds on the one before, so read them in order.
- **Part 2: Graphics and games** shows how to draw, use sprites, build levels from tiles, play sound and read the keyboard and gamepad. The part ends with a complete platform game.
- **Part 3: Reference** lists all commands and functions in alphabetical order, with their exact syntax. That is where you look things up once you know the basics.

The code examples look like this:

```basic
PRINT "HELLO THERE!"
```

Type them into the editor and press **RUN**. Change them and see what happens; that is the best way to learn. You cannot break anything by trying.

> **Tip:** The colours in the code examples show what is what: yellow is commands, blue is functions, green is text in quotation marks, orange is numbers and grey is comments.

At the end of many chapters there are exercises marked **Try it**. Do them before you read on if you can.

## The editor

When you start AMOS Like Basic you see the editor, where you write the program. At the top there are some buttons:

| Button | What it does |
|---|---|
| **RUN** | Starts the program. A new window, the game window, opens and shows the result. |
| **STOP** | Stops the program. |
| **PAUSE** / **STEP** | Pauses the program and then runs it one line at a time. Good for finding mistakes. |
| **SPRITES** | Opens the sprite editor, where you draw figures pixel by pixel. |
| **MAP** | Opens the map editor, where you build levels from tiles. |
| **TRACKER** | Opens the tracker, where you write music in patterns with notes and effects. |
| **LOAD** / **SAVE** | Opens and saves projects (files ending in `.amosproj`). |

If you click in the margin to the left of a line you set a **breakpoint**. The program then stops when it reaches that line, and you can see the value of every variable in the variable panel (press **F1** to show or hide it, and drag its left edge to make it wider or narrower).

While the program is paused you can also **change** a variable: choose it in the panel, type the new value in the box at the bottom and press **Enter** (or **Set**). Text is typed as it is, numbers as in BASIC (`12`, `-3.5`, `$FF`). Double-click an array to see all its elements; there you change an element the same way. The program carries on with the new values when you press **RESUME** or **STEP**, so you can try out a change without starting again, for example give yourself more lives. The box at the top filters the list by name.

In the game window **F12** switches between window and full screen, and **F8** turns on and off an effect that makes the picture look like an old TV. The title bar of the game window shows how fast the program runs: **FPS** (frames per second), **BASIC** (how much of the time your code is working), **CPU**, and **SYNC**: the screen refresh rate that `WAIT VBL` follows, or `timer` when the window is not being drawn.

If you downloaded AMOS Like Basic, the folder `Documents/AmosLikeBasic` is made the first time you start it, with the example projects in `Projects`, their images in `Graphics`, their sounds in `Sound` and an empty `Export` for the games you export. You can change all of them; files that are already there are never overwritten. That is where file names in your programs count from: an image you load with `"Graphics/hero.png"` is in `Documents/AmosLikeBasic/Graphics`.

**Project → Folders...** shows the four folders, and you can move each of them, for example keep your graphics in a folder of their own. The file dialogs start in the right folder (projects in Projects, pictures and maps in Graphics, songs in Sound), and a program's `"Graphics/..."` and `"Sound/..."` are looked for in the folders you chose. The choice is remembered until you change it.

The quickest way to see what AMOS Like Basic can do is **Project → Examples**. The examples are sorted into games, graphics and effects, and sound and music; open one and press **RUN**. Hold the mouse over a name to see what the example shows. Change as much as you like: the examples are your own copies, and you can save your version under a new name with **Save as...**.

The **Project** menu also has **Export app...**, which turns your game into a finished program that others can run without installing AMOS Like Basic. It is described in the chapter on exporting.
