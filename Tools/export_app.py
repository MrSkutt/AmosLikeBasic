#!/usr/bin/env python3
"""Exports a BASIC program as a stand-alone app for Mac and/or Windows.

    python3 Tools/export_app.py Projects/foresthop.amosproj --name ForestHop
    python3 Tools/export_app.py Projects/foresthop.amosproj --name ForestHop --platform win

The app is AmosLikeBasic in player mode: it finds game.amosproj next to itself and
runs the game straight away, without the editor. .NET is included (self-contained),
so the person who gets the app does not need to install anything. Only the images
and sounds that the program uses are included.

Output in Export/:
    Mac:     <Name>.app  and  <Name>-mac.zip
    Windows: <Name>-win/<Name>.exe  and  <Name>-win.zip

Needs the .NET SDK. The Mac app has to be built on a Mac (for codesign). It is only
signed "ad hoc", so the first time the receiver needs to right-click and choose Open.
The Windows version can be built on a Mac.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CSPROJ = os.path.join(ROOT, "AmosLikeBasic.csproj")
EXE = "AmosLikeBasic"

PLATFORMS = {
    "mac": "osx-arm64",
    "win": "win-x64",
}

# Mappar som csproj kopierar till varje build. Spelet tar bara med det det använder.
ASSET_DIRS_IN_BUILD = ("Graphics", "Sound", "Projects")


def find_assets(program, extra):
    """Alla strängar i programmet som pekar på en fil i projektet, t.ex. "Graphics/hero.png"."""
    assets = set()
    for literal in re.findall(r'"([^"\n]+)"', program):
        path = literal.strip().lstrip("/\\")
        if path and os.path.isfile(os.path.join(ROOT, path)):
            assets.add(path.replace("\\", "/"))
    for item in extra:
        full = os.path.join(ROOT, item)
        if os.path.isdir(full):
            for dirpath, _, files in os.walk(full):
                for f in files:
                    if not f.startswith("."):
                        assets.add(os.path.relpath(os.path.join(dirpath, f), ROOT).replace("\\", "/"))
        elif os.path.isfile(full):
            assets.add(item.replace("\\", "/"))
        else:
            sys.exit(f"Cannot find --include {item}")
    return sorted(assets)


def publish(rid, out_dir):
    print(f"== dotnet publish ({rid})")
    subprocess.run(
        ["dotnet", "publish", CSPROJ, "-c", "Release", "-r", rid, "--self-contained", "true",
         "-p:DebugType=None", "-p:DebugSymbols=false", "-o", out_dir],
        check=True)
    for d in ASSET_DIRS_IN_BUILD:
        shutil.rmtree(os.path.join(out_dir, d), ignore_errors=True)
    # Felsökningsfiler från NuGet-paket (libSkiaSharp.pdb är ensam 80 MB) och länkfiler behövs inte
    for f in os.listdir(out_dir):
        if f.endswith((".pdb", ".def", ".dll.a")):
            os.remove(os.path.join(out_dir, f))


def write_game(res_dir, program_path, title, assets):
    with open(program_path, encoding="utf-8") as f:
        project = json.load(f)
    project["Title"] = title
    os.makedirs(res_dir, exist_ok=True)
    with open(os.path.join(res_dir, "game.amosproj"), "w", encoding="utf-8") as f:
        json.dump(project, f, ensure_ascii=False)
    for a in assets:
        dst = os.path.join(res_dir, a)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(os.path.join(ROOT, a), dst)


def slug(name):
    return re.sub(r"[^a-z0-9]+", "", name.lower()) or "game"


def build_mac(args, program, assets, work):
    app = os.path.join(args.out, f"{args.name}.app")
    shutil.rmtree(app, ignore_errors=True)
    publish(PLATFORMS["mac"], os.path.join(app, "Contents", "MacOS"))
    write_game(os.path.join(app, "Contents", "Resources"), args.project, args.name, assets)

    icon_key = ""
    if args.icon:
        icon_key = make_icns(args.icon, os.path.join(app, "Contents", "Resources", "AppIcon.icns"), work)

    plist = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>{args.name}</string>
    <key>CFBundleDisplayName</key>
    <string>{args.name}</string>
    <key>CFBundleIdentifier</key>
    <string>{args.bundle_id or "se.amoslikebasic." + slug(args.name)}</string>
    <key>CFBundleExecutable</key>
    <string>{EXE}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleVersion</key>
    <string>{args.version}</string>
    <key>CFBundleShortVersionString</key>
    <string>{args.version}</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>{icon_key}
</dict>
</plist>
"""
    with open(os.path.join(app, "Contents", "Info.plist"), "w", encoding="utf-8") as f:
        f.write(plist)
    os.chmod(os.path.join(app, "Contents", "MacOS", EXE), 0o755)

    if shutil.which("codesign"):
        # Apple Silicon kör bara signerade program; ad hoc-signatur räcker för egen dator
        subprocess.run(["codesign", "--force", "--deep", "--sign", "-", app], check=True)
    else:
        print("!! codesign is missing (build on a Mac), the app will not start on Apple Silicon")

    zip_path = os.path.join(args.out, f"{args.name}-mac.zip")
    if os.path.exists(zip_path):
        os.remove(zip_path)
    if shutil.which("ditto"):
        subprocess.run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, zip_path], check=True)
    else:
        zip_dir(app, zip_path)
    print(f"== Mac: {app}\n        {zip_path}")


def make_icns(png, dst, work):
    """Gör en .icns av en PNG (helst 1024x1024) med sips och iconutil."""
    iconset = os.path.join(work, "AppIcon.iconset")
    shutil.rmtree(iconset, ignore_errors=True)
    os.makedirs(iconset)
    for size in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            px = size * scale
            name = f"icon_{size}x{size}{'@2x' if scale == 2 else ''}.png"
            subprocess.run(["sips", "-z", str(px), str(px), png, "--out", os.path.join(iconset, name)],
                           check=True, stdout=subprocess.DEVNULL)
    subprocess.run(["iconutil", "-c", "icns", iconset, "-o", dst], check=True)
    return "\n    <key>CFBundleIconFile</key>\n    <string>AppIcon</string>"


def build_win(args, program, assets):
    folder = os.path.join(args.out, f"{args.name}-win")
    shutil.rmtree(folder, ignore_errors=True)
    publish(PLATFORMS["win"], folder)
    write_game(folder, args.project, args.name, assets)
    os.replace(os.path.join(folder, f"{EXE}.exe"), os.path.join(folder, f"{args.name}.exe"))

    zip_path = os.path.join(args.out, f"{args.name}-win.zip")
    zip_dir(folder, zip_path)
    print(f"== Windows: {os.path.join(folder, args.name + '.exe')}\n            {zip_path}")


def zip_dir(src, zip_path):
    base = os.path.dirname(src)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for dirpath, _, files in os.walk(src):
            for f in files:
                full = os.path.join(dirpath, f)
                z.write(full, os.path.relpath(full, base))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("project", help="the program, e.g. Projects/foresthop.amosproj")
    p.add_argument("--name", help="name of the app (default: the file name)")
    p.add_argument("--platform", choices=["mac", "win", "both"], default="both")
    p.add_argument("--out", default=os.path.join(ROOT, "Export"), help="output folder (default: Export/)")
    p.add_argument("--include", action="append", default=[],
                   help="extra file or folder to include, if the program builds file names in code")
    p.add_argument("--icon", help="PNG to make the Mac icon from (ideally 1024x1024)")
    p.add_argument("--bundle-id", help="Mac bundle identifier (default: se.amoslikebasic.<name>)")
    p.add_argument("--version", default="1.0")
    args = p.parse_args()

    args.project = os.path.abspath(args.project)
    args.name = args.name or os.path.splitext(os.path.basename(args.project))[0]
    args.out = os.path.abspath(args.out)
    if not re.fullmatch(r"[\w .-]+", args.name):
        sys.exit("--name may only contain letters, digits, spaces, dots and hyphens")

    with open(args.project, encoding="utf-8") as f:
        program = json.load(f)["ProgramText"]
    assets = find_assets(program, args.include)
    print(f"== {len(assets)} files are included:")
    for a in assets:
        print("   " + a)

    os.makedirs(args.out, exist_ok=True)
    work = os.path.join(args.out, ".work")
    os.makedirs(work, exist_ok=True)
    if args.platform in ("mac", "both"):
        build_mac(args, program, assets, work)
    if args.platform in ("win", "both"):
        build_win(args, program, assets)
    shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    main()
