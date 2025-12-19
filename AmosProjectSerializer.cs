using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AmosLikeBasic;

public static class AmosProjectSerializer
{
    public static async Task SaveAsync(System.IO.Stream stream, AmosGraphics.ProjectFile project)
    {
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, project);
    }

    public static async Task<AmosGraphics.ProjectFile> LoadAsync(System.IO.Stream stream)
    {
        return await System.Text.Json.JsonSerializer.DeserializeAsync<AmosGraphics.ProjectFile>(stream);
    }
}