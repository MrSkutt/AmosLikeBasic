using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AmosLikeBasic;

public static class AmosProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task SaveAsync(string filePath, AmosGraphics.ProjectFile project)
    {
        var json = JsonSerializer.Serialize(project, Options);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static async Task<AmosGraphics.ProjectFile> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var project = JsonSerializer.Deserialize<AmosGraphics.ProjectFile>(json, Options);
        if (project is null)
            throw new InvalidDataException("Project file is invalid or empty.");
        return project;
    }
}