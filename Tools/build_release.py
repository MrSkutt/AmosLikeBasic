#!/usr/bin/env python3
"""Builds the AMOS Like Basic IDE for download, for Mac and Windows.

    python3 Tools/build_release.py 0.9.0

Output in Export/Release/:
    AmosLikeBasic-<version>-mac.zip   AMOS Like Basic.app and the manual
    AmosLikeBasic-<version>-win.zip   a folder with AmosLikeBasic.exe and the manual

.NET is included (self-contained), so nothing needs installing. The examples, images, sounds and
fonts come with the app; on the first start the example projects, graphics and sounds are
copied to Documents/AmosLikeBasic, which becomes the folder that programs count their file paths from.

Needs the .NET SDK. The Mac app must be built on a Mac (for codesign); the Windows version can
be built anywhere. GitHub Actions runs this for every version tag (.github/workflows/release.yml).
"""
import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from export_app import CSPROJ, EXE, PLATFORMS, ROOT, make_icns, zip_dir  # noqa: E402

APP_NAME = "AMOS Like Basic"
MANUAL = os.path.join(ROOT, "Documentation", "AMOS Like Basic - Manual.pdf")
ICON_ICO = os.path.join(ROOT, "Assets", "AmosLikeBasic.ico")


def publish(rid, out_dir, version):
    print(f"== dotnet publish ({rid})")
    subprocess.run(
        ["dotnet", "publish", CSPROJ, "-c", "Release", "-r", rid, "--self-contained", "true",
         "-p:DebugType=None", "-p:DebugSymbols=false", f"-p:Version={version}", "-o", out_dir],
        check=True)
    # Debug files from NuGet packages (libSkiaSharp.pdb alone is 80 MB) and link files are not needed
    for f in os.listdir(out_dir):
        if f.endswith((".pdb", ".def", ".dll.a")):
            os.remove(os.path.join(out_dir, f))


def build_mac(version, out, work):
    stage = os.path.join(work, f"AmosLikeBasic-{version}-mac")
    app = os.path.join(stage, f"{APP_NAME}.app")
    publish(PLATFORMS["mac"], os.path.join(app, "Contents", "MacOS"), version)

    icon_key = ""
    if shutil.which("sips") and shutil.which("iconutil") and os.path.exists(ICON_ICO):
        png = os.path.join(work, "icon.png")
        subprocess.run(["sips", "-s", "format", "png", ICON_ICO, "--out", png], check=True, stdout=subprocess.DEVNULL)
        os.makedirs(os.path.join(app, "Contents", "Resources"), exist_ok=True)
        icon_key = make_icns(png, os.path.join(app, "Contents", "Resources", "AppIcon.icns"), work)

    plist = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>{APP_NAME}</string>
    <key>CFBundleDisplayName</key>
    <string>{APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>se.amoslikebasic.ide</string>
    <key>CFBundleExecutable</key>
    <string>{EXE}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleVersion</key>
    <string>{version}</string>
    <key>CFBundleShortVersionString</key>
    <string>{version}</string>
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
        # Apple Silicon only runs signed programs; an ad hoc signature is enough to start it
        subprocess.run(["codesign", "--force", "--deep", "--sign", "-", app], check=True)
    else:
        print("!! codesign is missing (build on a Mac), the app will not start on Apple Silicon")

    shutil.copy2(MANUAL, stage)
    zip_path = os.path.join(out, f"AmosLikeBasic-{version}-mac.zip")
    if shutil.which("ditto"):
        subprocess.run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", stage, zip_path], check=True)
    else:
        zip_dir(stage, zip_path)
    print(f"== Mac: {zip_path}")
    return zip_path


def build_win(version, out, work):
    stage = os.path.join(work, f"AmosLikeBasic-{version}-win")
    publish(PLATFORMS["win"], stage, version)
    shutil.copy2(MANUAL, stage)
    zip_path = os.path.join(out, f"AmosLikeBasic-{version}-win.zip")
    zip_dir(stage, zip_path)
    print(f"== Windows: {zip_path}")
    return zip_path


def main():
    p = argparse.ArgumentParser(description="Build the AMOS Like Basic IDE for download.")
    p.add_argument("version", help="the version, e.g. 0.9.0 (a leading v is removed)")
    p.add_argument("--platform", choices=["mac", "win", "both"], default="both")
    p.add_argument("--out", default=os.path.join(ROOT, "Export", "Release"), help="output folder (default: Export/Release/)")
    args = p.parse_args()

    version = args.version.lstrip("vV")
    if not re.fullmatch(r"\d+(\.\d+){0,3}([-+][\w.]+)?", version):
        sys.exit(f"'{args.version}' is not a version number like 0.9.0")
    if not os.path.exists(MANUAL):
        sys.exit(f"The manual is missing: {MANUAL} (build it with python3 Tools/build_manual.py)")

    os.makedirs(args.out, exist_ok=True)
    with tempfile.TemporaryDirectory() as work:
        built = []
        if args.platform in ("mac", "both"):
            built.append(build_mac(version, args.out, work))
        if args.platform in ("win", "both"):
            built.append(build_win(version, args.out, work))
    for path in built:
        print(f"   {os.path.basename(path)}  {os.path.getsize(path) / 1e6:.0f} MB")


if __name__ == "__main__":
    main()
