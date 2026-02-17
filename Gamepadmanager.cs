// ================================================================
// GamepadManager.cs — Tråd-baserad version
// PollLoop körs i bakgrundstråd (~60Hz)
// ================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using static SDL2.SDL;

namespace AmosLikeBasic;

public static class GamepadManager
{
    private static readonly bool[]   _buttonState  = new bool[4 * 16];
    private static readonly IntPtr[] _controllers  = new IntPtr[4];
    private static bool              _initialized  = false;
    private static bool              _running      = false;
    private static Thread?           _pollThread   = null;
    private static readonly float[]  _axisState   = new float[4 * 6];

    private static readonly Dictionary<string, SDL_GameControllerButton> _buttonMap
        = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"]      = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A,
            ["B"]      = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B,
            ["X"]      = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X,
            ["Y"]      = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y,
            ["LB"]     = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER,
            ["RB"]     = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER,
            ["Start"]  = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START,
            ["Select"] = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK,
            ["Up"]     = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP,
            ["Down"]   = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN,
            ["Left"]   = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT,
            ["Right"]  = SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT,
        };
    
    private static readonly Dictionary<string, (SDL_GameControllerAxis Axis, int Slot)> _axisMap
        = new(StringComparer.OrdinalIgnoreCase)
        {
            ["LeftX"]  = (SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX,        0),
            ["LeftY"]  = (SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY,        1),
            ["RightX"] = (SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX,       2),
            ["RightY"] = (SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY,       3),
            ["LT"]     = (SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT,  4),
            ["RT"]     = (SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT, 5),
        };
    
    // Lista för snabb iteration i PollLoop
    private static readonly List<(string Name, SDL_GameControllerButton Button, int Slot)> _buttonList = new();

    // ── Init / Shutdown ─────────────────────────────────────────

    public static void Start()
    {
        Debug.WriteLine($"[GamepadManager.Start] anropad, _initialized={_initialized}");

        if (_initialized) return;

        if (SDL_Init(SDL_INIT_GAMECONTROLLER) != 0)
        {
            Debug.WriteLine($"[GamepadManager] SDL_Init failed: {SDL_GetError()}");
            return;
        }

        // Bygg snabb lookup-lista
        _buttonList.Clear();
        int slot = 0;
        foreach (var kv in _buttonMap)
            _buttonList.Add((kv.Key, kv.Value, slot++));

        for (int i = 0; i < 4; i++)
            _controllers[i] = IntPtr.Zero;

        // Öppna redan anslutna controllers
        int count = SDL_NumJoysticks();
        for (int i = 0; i < Math.Min(count, 4); i++)
        {
            if (SDL_IsGameController(i) == SDL_bool.SDL_TRUE)
                OpenController(i);
        }

        _running    = true;
        _initialized = true;

        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "GamepadPollThread" };
        _pollThread.Start();

        Debug.WriteLine("[GamepadManager] Initierad, tråd startad");
    }

    public static void Stop()
    {
        Debug.WriteLine("[GamepadManager.Stop] anropad");

        if (!_initialized) return;

        _running     = false;
        _initialized = false;

        _pollThread?.Join(500);
        _pollThread = null;

        for (int i = 0; i < 4; i++)
        {
            if (_controllers[i] != IntPtr.Zero)
            {
                SDL_GameControllerClose(_controllers[i]);
                _controllers[i] = IntPtr.Zero;
            }
        }

        SDL_Quit();
        Array.Clear(_buttonState, 0, _buttonState.Length);

        Debug.WriteLine("[GamepadManager] Stoppad");
    }

    // ── Poll-loop — körs i bakgrundstråd ────────────────────────

    private static void PollLoop()
    {
        Debug.WriteLine("[GamepadManager.PollLoop] Tråd startad");

        int loopCount = 0;

        while (_running)
        {
            // Pumpa SDL-events
            while (SDL_PollEvent(out SDL_Event e) != 0)
            {
                if (e.type == SDL_EventType.SDL_CONTROLLERDEVICEADDED)
                    OpenController(e.cdevice.which);
                else if (e.type == SDL_EventType.SDL_CONTROLLERDEVICEREMOVED)
                    CloseController(e.cdevice.which);
            }

            // Läs knappar
            for (int i = 0; i < 4; i++)
            {
                if (_controllers[i] == IntPtr.Zero) continue;

                foreach (var (name, btn, slot) in _buttonList)
                {
                    bool pressed = SDL_GameControllerGetButton(_controllers[i], btn) == 1;
                    int  idx     = i * 16 + slot;

                    if (pressed && !_buttonState[idx])
                        Debug.WriteLine($"[GamepadManager] Pad {i + 1} {name} nedtryckt");

                    _buttonState[idx] = pressed;
                }
                foreach (var (name, (axis, slot)) in _axisMap)
                {
                    short raw = SDL_GameControllerGetAxis(_controllers[i], axis);
                    // SDL returnerar -32768 till 32767, konvertera till -100 till 100
                    float normalized;
                    if (axis == SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT ||
                        axis == SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT)
                    {
                        // Triggers går 0 till 32767, mappa till 0-100
                        normalized = raw / 32767f * 100f;
                    }
                    else
                    {
                        // Spakar går -32768 till 32767, mappa till -100 till 100
                        // Lägg till lite deadzone (5%) för att undvika drift
                        normalized = raw / 32767f * 100f;
                        if (Math.Abs(normalized) < 5f) normalized = 0f;
                    }
                    _axisState[i * 6 + slot] = normalized;
                }
            }

            
            // Debug var 60:e loop (~1 sekund)
            //loopCount++;
            //if (loopCount % 60 == 0)
              //  Debug.WriteLine($"[GamepadManager.PollLoop] {loopCount} iterationer");

            Thread.Sleep(16);
        }

        Debug.WriteLine("[GamepadManager.PollLoop] Tråd avslutad");
    }

    // ── BASIC API ───────────────────────────────────────────────

    public static bool IsButtonDown(int pad, string button)
    {
        if (!_initialized || pad < 1 || pad > 4) return false;

        int padIdx = pad - 1;
        if (_controllers[padIdx] == IntPtr.Zero) return false;

        foreach (var (name, _, slot) in _buttonList)
        {
            if (string.Equals(name, button, StringComparison.OrdinalIgnoreCase))
                return _buttonState[padIdx * 16 + slot];
        }
        return false;
    }
    
    public static float GetAxis(int pad, string axis)
    {
        if (!_initialized || pad < 1 || pad > 4) return 0f;

        int padIdx = pad - 1;
        if (_controllers[padIdx] == IntPtr.Zero) return 0f;

        if (_axisMap.TryGetValue(axis, out var entry))
            return _axisState[padIdx * 6 + entry.Slot];

        return 0f;
    }
    
    // ── Intern ──────────────────────────────────────────────────

    private static void OpenController(int deviceIndex)
    {
        if (deviceIndex < 0 || deviceIndex >= 4) return;
        if (_controllers[deviceIndex] != IntPtr.Zero) return;

        IntPtr ctrl = SDL_GameControllerOpen(deviceIndex);
        if (ctrl != IntPtr.Zero)
        {
            _controllers[deviceIndex] = ctrl;
            Debug.WriteLine(
                $"[GamepadManager] Pad {deviceIndex + 1} ansluten: {SDL_GameControllerName(ctrl)}");
        }
    }

    private static void CloseController(int instanceId)
    {
        IntPtr ctrl = SDL_GameControllerFromInstanceID(instanceId);
        if (ctrl == IntPtr.Zero) return;

        for (int i = 0; i < 4; i++)
        {
            if (_controllers[i] != ctrl) continue;

            for (int s = 0; s < 16; s++)
                _buttonState[i * 16 + s] = false;
            
            // Rensa axlar för denna pad
            for (int s = 0; s < 6; s++)
                _axisState[i * 6 + s] = 0f;

            SDL_GameControllerClose(_controllers[i]);
            _controllers[i] = IntPtr.Zero;
            Debug.WriteLine($"[GamepadManager] Pad {i + 1} frånkopplad");
            break;
        }

    }
}