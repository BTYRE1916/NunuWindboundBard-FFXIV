using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length < 2)
    throw new ArgumentException("Pass the built plugin DLL and the directory containing Dalamud.dll. Optional: --expect-old-failure");

string pluginFile = Path.GetFullPath(args[0]);
string dalamudDirectory = Path.GetFullPath(args[1]);
string[] folders = [Path.GetDirectoryName(pluginFile)!, dalamudDirectory];
var context = new AssemblyLoadContext("startup-config-" + Guid.NewGuid(), isCollectible: true);
context.Resolving += (loader, name) =>
{
    foreach (string folder in folders)
    {
        string file = Path.Combine(folder, name.Name + ".dll");
        if (File.Exists(file)) return loader.LoadFromAssemblyPath(file);
    }
    return null;
};

int checks = 0;
void Check(bool ok, string description)
{
    if (!ok) throw new Exception(description);
    Console.WriteLine("PASS " + description);
    checks++;
}

try
{
    using var stream = File.OpenRead(pluginFile);
    var assembly = context.LoadFromStream(stream);
    Check(assembly.Location.Length == 0, "actual plugin assembly is loaded from memory with no reflection path");
    Type configType = assembly.GetType("NunuWindboundBard.Configuration", throwOnError: true)!;

    if (args.Contains("--expect-old-failure"))
    {
        try
        {
            Activator.CreateInstance(configType);
            throw new Exception("Old failure was not reproduced.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentNullException { ParamName: "path1" })
        {
            Check(true, "old compiled Configuration reproduces the reported null path1 crash");
        }
        return;
    }

    object config = Activator.CreateInstance(configType) ?? throw new Exception("Configuration constructor returned null.");
    Check(true, "Configuration constructs before any Dalamud plugin service is assigned");
    object? Get(object value, string name) => configType.GetProperty(name)!.GetValue(value);
    void Set(object value, string name, object? setting) => configType.GetProperty(name)!.SetValue(value, setting);
    var resolve = configType.GetMethod("ResolveCapturePaths", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new Exception("Runtime capture-path resolver is missing.");
    (string Engine, string Ffmpeg) Resolve(object value, string? directory) =>
        ((string, string))resolve.Invoke(value, [directory])!;

    string directory = new FileInfo(Path.Combine(Path.GetTempPath(), "memory-plugin-test", "0.42.3.0", "NunuWindboundBard.dll")).DirectoryName!;
    var bundled = (Engine: Path.Combine(directory, "CaptureEngine", "WindboundCaptureEngine.exe"),
        Ffmpeg: Path.Combine(directory, "CaptureEngine", "ffmpeg.exe"));
    Check((string?)Get(config, "CaptureEnginePath") == "" && (string?)Get(config, "CaptureFfmpegPath") == "",
        "new configuration stores empty overrides instead of evaluating assembly paths");
    Check(Resolve(config, directory) == bundled, "bundled engine and codec resolve relative to the supplied plugin directory");
    Check((string?)Get(config, "CaptureEnginePath") == "" && (string?)Get(config, "CaptureFfmpegPath") == "",
        "resolving bundled paths does not persist a version-specific install directory");

    string updatedDirectory = Path.Combine(Path.GetTempPath(), "memory-plugin-test", "next-version");
    Check(Resolve(config, updatedDirectory).Engine == Path.Combine(updatedDirectory, "CaptureEngine", "WindboundCaptureEngine.exe"),
        "default paths follow a new install directory after an update");
    Check(Resolve(config, null) == ("", ""), "missing provider directory leaves paths empty without crashing");
    Check(Resolve(config, "  ") == ("", ""), "blank provider directory never resolves against the game directory");

    const string customEngine = @"E:\Windbound Studio\CustomEngine.exe";
    const string customFfmpeg = @"E:\Codecs\ffmpeg.exe";
    Set(config, "CaptureEnginePath", customEngine);
    Check(Resolve(config, directory) == (customEngine, bundled.Ffmpeg), "custom engine override and bundled codec work independently");
    Set(config, "CaptureEnginePath", "");
    Set(config, "CaptureFfmpegPath", customFfmpeg);
    Check(Resolve(config, directory) == (bundled.Engine, customFfmpeg), "custom codec override and bundled engine work independently");
    Set(config, "CaptureEnginePath", customEngine);
    Set(config, "CaptureBackend", 0);
    Check(Resolve(config, directory) == (customEngine, customFfmpeg), "both custom paths are preserved exactly");
    Check((int)Get(config, "CaptureBackend")! == 0, "path resolution preserves the selected capture option");
    Check(Resolve(config, null) == (customEngine, customFfmpeg), "custom paths remain usable without a provider directory");

    Set(config, "CaptureEnginePath", null);
    Set(config, "CaptureFfmpegPath", null);
    Check(Resolve(config, directory) == bundled, "null values from an existing configuration resolve safely");
    Set(config, "CaptureEnginePath", "  ");
    Set(config, "CaptureFfmpegPath", "\t");
    Check(Resolve(config, directory) == bundled, "whitespace overrides resolve to bundled paths");

    object oldSettings = JsonSerializer.Deserialize("{\"Version\":38,\"Language\":\"fr\",\"PreviewVolume\":0.31}", configType)!;
    Check(Resolve(oldSettings, directory) == bundled, "older JSON without capture paths constructs and resolves safely");
    Check((string?)Get(oldSettings, "Language") == "fr" && Math.Abs((float)Get(oldSettings, "PreviewVolume")! - .31f) < .00001f,
        "existing unrelated settings survive configuration loading");
    string customJson = JsonSerializer.Serialize(new { Version = 39, CaptureBackend = 0, CaptureEnginePath = customEngine, CaptureFfmpegPath = customFfmpeg });
    object savedCustom = JsonSerializer.Deserialize(customJson, configType)!;
    Check(Resolve(savedCustom, directory) == (customEngine, customFfmpeg), "saved custom Windows paths survive deserialization");

    using var roundtrip = JsonDocument.Parse(JsonSerializer.Serialize(oldSettings, configType));
    Check(roundtrip.RootElement.GetProperty("CaptureEnginePath").GetString() == "" &&
          roundtrip.RootElement.GetProperty("CaptureFfmpegPath").GetString() == "",
        "saving configuration retains default-path intent rather than the current version folder");
    Console.WriteLine($"ALL {checks} STARTUP CONFIGURATION CHECKS PASSED. Full in-game initialization is separate.");
}
finally
{
    context.Unload();
}
