// Headless test: run FetchAndAddNodesAsync and verify the full pipeline:
// download → test alive → only add passed nodes to AI group
using System.Reflection;

var asm = Assembly.LoadFrom(@"D:\LUODA\LDv2rayN\ServiceLib\bin\Debug\net10.0\ServiceLib.dll");

// Find the AIFetchService type
var aiType = asm.GetType("ServiceLib.Services.AIFetchService")
    ?? throw new Exception("AIFetchService not found");

// Check static fields
var repoField = aiType.GetField("_repoRawFiles", BindingFlags.NonPublic | BindingFlags.Static);
var trackerProp = aiType.GetProperty("Tracker", BindingFlags.Public | BindingFlags.Static);

Console.WriteLine($"AIFetchService type: {aiType.FullName}");
Console.WriteLine($"Tracker property: {trackerProp?.PropertyType.Name}");

// Check the repos list
var repos = repoField?.GetValue(null) as string[][];
if (repos == null)
{
    Console.WriteLine("ERROR: _repoRawFiles is null");
    return 1;
}
Console.WriteLine($"_repoRawFiles count: {repos.Length}");

foreach (var repo in repos)
{
    Console.WriteLine($"  {repo[0]}: {repo[1]}");
}

// Find FetchAndAddNodesAsync method
var method = aiType.GetMethod("FetchAndAddNodesAsync",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
Console.WriteLine($"\nFetchAndAddNodesAsync method: {method}");

// The method likely needs a Config and returns Task<(int total, int passed, int imported)>
// Let's find the method signature
if (method != null)
{
    var ps = method.GetParameters();
    Console.WriteLine($"  Parameters: {string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}"))}");
    Console.WriteLine($"  ReturnType: {method.ReturnType}");
}

// Actually let's call the method that fetches nodes and adds to group
// FetchAndAddNodesAsync(Config config, string[] repos)
// But it may not be static. Let's check for static methods
var fetchMethod = aiType.GetMethod("FetchAndAddNodesAsync",
    BindingFlags.Public | BindingFlags.Static);
if (fetchMethod == null)
{
    Console.WriteLine("No static FetchAndAddNodesAsync. Listing all static methods:");
    foreach (var m in aiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
        Console.WriteLine($"  {m.Name}");
    }
    return 1;
}

Console.WriteLine($"\nCalling FetchAndAddNodesAsync...");

// Need a Config object - create a default one
var configType = asm.GetType("ServiceLib.Models.Config");
if (configType == null) { Console.WriteLine("Config type not found"); return 1; }
var config = Activator.CreateInstance(configType);

// Set GUIPath to a temp dir
var guiPathProp = configType.GetProperty("GUIPath");
if (guiPathProp != null)
{
    guiPathProp.SetValue(config, @"C:\Temp\ai_test_gui");
    Console.WriteLine($"GUIPath set to: {guiPathProp.GetValue(config)}");
}

// Set Inbound field
var inboundProp = configType.GetProperty("Inbound");
if (inboundProp != null)
{
    var inboundArr = Array.CreateInstance(
        configType.GetNestedType("InboundItem") ?? asm.GetType("ServiceLib.Models.Config+InboundItem"), 1);
    if (inboundArr.GetValue(0) == null)
    {
        var itemType = inboundArr.GetType().GetElementType();
        var item = Activator.CreateInstance(itemType);
        // Set localPort
        itemType.GetProperty("LocalPort")?.SetValue(item, 10808);
        inboundArr.SetValue(item, 0);
    }
    inboundProp.SetValue(config, inboundArr);
}

// Invoke the method
var parameters = fetchMethod.GetParameters();
Console.WriteLine($"Method params: {string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))}");

try
{
    var result = fetchMethod.Invoke(null, new object[] { config });
    if (result is Task t)
    {
        t.Wait();
        var resultProp = t.GetType().GetProperty("Result");
        var val = resultProp?.GetValue(t);
        Console.WriteLine($"\nResult: {val}");
    }
    else
    {
        Console.WriteLine($"Result: {result}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR invoking: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"Inner: {ex.InnerException.Message}");
    Console.WriteLine(ex.StackTrace);
    return 1;
}

Console.WriteLine("\nDone!");
return 0;
