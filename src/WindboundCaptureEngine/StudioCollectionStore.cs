using System.Text.Json;
using NunuWindboundBard.Core;
namespace WindboundCaptureEngine;
internal static class StudioCollectionStore
{
    public static StudioCollection Load(string path)
    {
        if (!File.Exists(path)) return new();
        var collection = JsonSerializer.Deserialize<StudioCollection>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid studio collection.");
        if (collection.Version != StudioProtocol.Version) throw new InvalidDataException("Unsupported studio collection version.");
        return collection;
    }
    public static void Save(string path, StudioCollection collection)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(collection, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
}
