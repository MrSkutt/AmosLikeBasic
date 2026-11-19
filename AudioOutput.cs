using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ManagedBass;

namespace AmosLikeBasic;

// Where the sound goes: the sound card and which pair of its channels. Normally the system's
// default card on channels 1 – 2. A card with many channels (a mixer such as a Roland MX-1) may
// listen on other ones; the system settings' "stereo channels" are not used by BASS, so the pair
// is chosen here, in View → Sound Output. Kept in audio.json next to the IDE's config, and used
// from the next start.
public static class AudioOutput
{
    public const int MaxPair = 15;   // BASS can address 15 pairs of speakers: channels 1 – 30

    public sealed class Settings
    {
        public string? Device { get; set; }   // the card's name; null = the system's default
        public int Pair { get; set; } = 1;    // 1 = channels 1 – 2, 2 = 3 – 4 ... 9 = 17 – 18
    }

    public static Settings Current { get; private set; } = new();

    // What the sound actually went to when it started, for the log (with the reason if the chosen
    // card could not be opened and the default output was used instead)
    public static string Description { get; private set; } = "";

    // The BASS device the sound plays on; streams are made on it (BASS keeps the device per thread)
    public static int DeviceNumber { get; private set; } = -1;

    public static void UseDevice()
    {
        if (DeviceNumber > 0 && Bass.CurrentDevice != DeviceNumber) Bass.CurrentDevice = DeviceNumber;
    }

    private static string SettingsFile => Path.Combine(ConfigService.GetConfigFolder("AmosProject"), "audio.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new Settings();
        }
        catch (Exception) { Current = new Settings(); }
        Current.Pair = Math.Clamp(Current.Pair, 1, MaxPair);
    }

    public static void Save(Settings settings)
    {
        Current = settings;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Could not save the sound settings: " + ex.Message); }
    }

    // The flags that send a stream to the chosen pair of channels (none for channels 1 – 2)
    public static BassFlags PairFlags => Current.Pair <= 1 ? BassFlags.Default : (BassFlags)(Current.Pair << 24);

    // Starts BASS on the chosen card; true if it runs (also when it was already started)
    public static bool Start()
    {
        Load();
        int device = DeviceIndex(Current.Device);
        // A pair above 1 needs all the card's channels open
        var flags = Current.Pair > 1 ? DeviceInitFlags.ForcedSpeakerAssignment : DeviceInitFlags.Default;
        string problem = "";
        if (!string.IsNullOrEmpty(Current.Device) && device == -1)
            problem = $" ({Current.Device} was not found, so the default output is used)";
        bool ok = Bass.Init(device, 44100, flags) || Bass.LastError == Errors.Already;
        if (ok && device != -1) Bass.CurrentDevice = device;   // the chosen card also when BASS was started already
        if (!ok && device != -1)
        {
            // The card cannot be opened: say why, and play on the default output instead
            problem = $" ({Current.Device} could not be opened: {Bass.LastError}, so the default output is used)";
            ok = Bass.Init(-1, 44100, flags) || Bass.LastError == Errors.Already;
        }

        DeviceNumber = Bass.CurrentDevice;
        string name = Bass.GetDeviceInfo(DeviceNumber, out var info) ? info.Name : "the default output";
        Description = $"{name}, channels {Current.Pair * 2 - 1}–{Current.Pair * 2}{problem}";
        return ok;
    }

    // The cards that can play sound, by name
    public static List<string> Devices()
    {
        var names = new List<string>();
        for (int i = 1; Bass.GetDeviceInfo(i, out var info); i++)
            if (info.IsEnabled) names.Add(info.Name);
        return names;
    }

    private static int DeviceIndex(string? name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        for (int i = 1; Bass.GetDeviceInfo(i, out var info); i++)
            if (info.IsEnabled && info.Name == name) return i;
        return -1;
    }
}
