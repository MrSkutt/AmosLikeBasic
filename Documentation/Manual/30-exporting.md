# Sharing your game

When your game is finished you will want others to play it, even those who do not have AMOS Like Basic. You can turn the game into a stand-alone program: an app for the Mac or an .exe file for Windows.

## Export app

Choose **Project → Export app...** in the menu. Give the name of the app and the platform it is for:

- **Mac and Windows** makes both.
- **Mac (.app)** makes an app for the Mac.
- **Windows (.exe)** makes a folder with an .exe file.

Click **Export**. It takes about half a minute, and the log window shows how it is going. When it is done the folder `Export` in the project folder opens. The app is in there, together with a zip file that is ready to send to someone.

What is exported is the program in the editor right then, even if you have not saved it. The images and sounds the program uses are included automatically. The person who gets the app does not need to install anything; everything it needs is inside it.

> **Note:** The export builds the app from the AMOS Like Basic source code, so it needs you to run the editor from the project folder and to have the .NET SDK installed.

## Files that are included

The export looks for every file name in quotation marks in your program, such as `"Graphics/hero.png"` or `"Sound/Tracker/forest_song.amostrk"`, and includes the files that exist. Songs from the tracker are therefore included just like images and sounds. If the program builds file names while it runs, for example `"level" & LEVEL & ".amosmap"`, they are not found. Then you can export from the terminal and name them yourself:

```text
python3 Tools/export_app.py Projects/mygame.amosproj --name MyGame --include Graphics/Levels
```

With `--icon picture.png` the Mac app gets an icon of its own.

> **Note:** Fonts chosen with `FONT SET`, such as `"C64 PRO MONO"`, are not included. They must be installed on the computer that runs the game, or the text falls back to another font. Bitmap fonts loaded with `FONT LOAD` are ordinary image files and are always included.

## The first time on another computer

The apps are not signed by Apple or Microsoft, so the computer warns the first time.

- **Mac:** Right-click the app and choose **Open**, and confirm. After that it starts as usual.
- **Windows:** Click **More info** and **Run anyway** if SmartScreen warns.

## Playing

In the exported game **F12** switches between window and full screen, **Escape** leaves full screen and **F8** turns the CRT effect on and off. The game is drawn at its own resolution and scaled up to the screen with sharp pixels, so it looks just as retro on a 4K screen.
