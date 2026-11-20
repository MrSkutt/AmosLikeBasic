using System;
using System.IO;
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

    // Add more settings here
}

public class ConfigService
{
    private readonly string _appName;
    private readonly string _configFolder;
    private readonly string _configFilePath;

    public AppConfig Config { get; private set; } = new AppConfig();

    public ConfigService(string appName)
    {
        _appName = appName;
        _configFolder = GetConfigFolder(appName);
        Directory.CreateDirectory(_configFolder); // Create the folder if it does not exist
        _configFilePath = Path.Combine(_configFolder, "config.json");
    }

    // Load the config from a file or create a new one with default values
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
                // If something goes wrong, fall back to the defaults
                Config = new AppConfig();
            }
        }
        else
        {
            Config = new AppConfig();
        }
    }

    // Save the config to a file
    public void Save()
    {
        string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configFilePath, json);
    }

    // Cross-platform config folder
    public static string GetConfigFolder(string appName)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
        }
        else if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support", appName);
        }
        else // Linux and fallback
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", appName);
        }
    }
}