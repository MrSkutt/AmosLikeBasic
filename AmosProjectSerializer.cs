using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AmosLikeBasic;

public static class AmosProjectSerializer
{
    public static async Task SaveAsync(Stream stream, ProjectFile project)
    {
        await JsonSerializer.SerializeAsync(stream, project);
    }

    public static async Task<ProjectFile?> LoadAsync(Stream stream)
    {
        return await JsonSerializer.DeserializeAsync<ProjectFile>(stream);
    }
}