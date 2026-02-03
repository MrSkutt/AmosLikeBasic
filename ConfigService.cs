using System;
using System.IO;
using System.Text.Json;
using System.Text.Json;

public class AppConfig
{
    public string LastProjectPath { get; set; } = "";
    public int WindowWidth { get; set; } = 800;
    public int WindowHeight { get; set; } = 600;
    public int WindowTop { get; set; } = 0;
    public int WindowLeft { get; set; } = 0;
    public bool IsMaximized { get; set; } = false;
    public string DefaultTheme { get; set; } = "ClassicBlue";

    // Lägg till fler inställningar här
}

public class ConfigService
{
    private readonly string _appName;
    private readonly string _configFolder;
    private readonly string _configFilePath;

    public AppConfig Config { get; private set; }

    public ConfigService(string appName)
    {
        _appName = appName;
        _configFolder = GetConfigFolder(appName);
        Directory.CreateDirectory(_configFolder); // Skapa mappen om den inte finns
        _configFilePath = Path.Combine(_configFolder, "config.json");
    }

    // Load config från fil eller skapa ny med standardvärden
    public void Load()
    {
        if (File.Exists(_configFilePath))
        {
            try
            {
                string json = File.ReadAllText(_configFilePath);
                Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                // Om något går fel, fallback till default
                Config = new AppConfig();
            }
        }
        else
        {
            Config = new AppConfig();
        }
    }

    // Spara config till fil
    public void Save()
    {
        string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configFilePath, json);
    }

    // Cross-platform config folder
    private static string GetConfigFolder(string appName)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
        }
        else if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support", appName);
        }
        else // Linux och fallback
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", appName);
        }
    }
}