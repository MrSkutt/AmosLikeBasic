# AMOS Professional Engine
**An AMOS-like BASIC environment for modern platforms (macOS / Windows)**

This project is a modern reimplementation and evolution of the classic **AMOS Professional** environment, designed for contemporary systems while preserving the original development philosophy, syntax style, and creative workflow.

The engine provides a structured BASIC-like language, graphics/audio primitives, windowing, input handling, and tooling intended for:
- retro-style game development
- educational use
- creative coding
- rapid prototyping
- nostalgia-driven projects

---

## Features

- AMOS-style BASIC syntax
- Multi-platform support (macOS / Windows)
- Modern rendering and system APIs
- Structured command system
- Modular engine architecture
- Scriptable runtime
- Developer-focused tooling
- Extensible command system
- AMOS-compatible concepts and workflows
- Retro Graphics Engine: Supports classic AMOS concepts like Screens, Sprites, Bobs (Blitter Objects), and Tilemaps.
- Hardware Acceleration: Uses SkiaSharp (via Avalonia) for high-performance rendering.
- Shader Support: Includes support for GPU-accelerated effects and raster manipulation (SkSL).
- Asset Management: Custom resource loader for handling fonts, images, and project files.

---

## Philosophy

This project is **not an emulator**.  
It is a **modern engine inspired by AMOS**, built to:
- preserve the spirit of classic BASIC environments
- remove legacy hardware limitations
- provide a productive creative coding workflow
- enable modern deployment pipelines

---

## Status

This project is under active development.  
APIs, commands, and internal architecture may change.

---

## Prerequisites
To compile and run this project, you need to have the following installed on your development machine:
- .NET 9.0 SDK (or later)
- IDE: JetBrains Rider, Visual Studio 2022, or VS Code.

## Dependencies
The project relies on the following NuGet packages. These should restore automatically during the build process, but ensure your project file references them:
- Avalonia (UI Framework and Windowing)
- Avalonia.Desktop
- Avalonia.Skia (Rendering backend)
- Avalonia.ReactiveUI
- ManageBass (For Audio/Sound playback)
- SkiaSharp

## How to Compile and Run
1. Clone the repository:
   - git clone https://github.com/MrSkutt/AmosLikeBasic
   - cd AmosLikeBasic
2. Restore dependencies:
   - dotnet restore
3. Build the project:
   - dotnet build
4. Run the application:
   - dotnet run

## A Note on Resources
The application uses a custom ResourceLoader to find assets (images, fonts, sounds). Ensure your assets are placed in one of the following locations relative to the executable:
- Directly in the application folder.
- In a folder named Resources next to the executable.
- (For macOS Bundles) In the ../Resources directory.

## Optional: How to add dependencies manually
If you are starting from scratch or need to add the packages manually, use the following commands
- dotnet add package Avalonia
- dotnet add package Avalonia.Desktop
- dotnet add package Avalonia.Themes.Fluent
- dotnet add package Avalonia.Skia
- dotnet add package ManageBass


---

## License

MIT License

Copyright (c) [YEAR] [YOUR NAME]

Permission is hereby granted, free of charge, to any person obtaining a copy  
of this software and associated documentation files (the "Software"), to deal  
in the Software without restriction, including without limitation the rights  
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell  
copies of the Software, and to permit persons to whom the Software is  
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all  
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR  
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,  
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE  
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER  
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,  
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE  
SOFTWARE.

---

IDE
![Applikation](images/Applikation.png)




Exampel on Adventure game
![Adventure](images/Adventure.png)

