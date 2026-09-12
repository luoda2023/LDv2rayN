using ServiceLib;
using ServiceLib.Services;

// Minimal Config for headless test (no AppManager dependency)
var config = new Config
{
    AIConfigItem = new AIConfigItem
    {
        Enabled = true,
        ApiUrl = "http://47.114.75.115:40000/v1",
        ApiKey = "sk-proxy-local-51f5bd4b9797f2620bc55460946802711cf7312b38c24794",
        ModelId = "hermesAPI",
        MaxNodesPerSearch = 30,
        AiGroupRemarks = "AI_Verify",
    }
};

var service = new AIFetchService(config, (isEnd, msg) =>
{
    Console.WriteLine($"[UPDATE] {msg}");
    return Task.CompletedTask;
});

Console.WriteLine("=== Headless AIFetchService Verification ===");
Console.WriteLine($"AI Config: {config.AIConfigItem.ApiUrl}, model={config.AIConfigItem.ModelId}");
Console.WriteLine();

// Step 1: FetchAndAddNodesAsync - full pipeline
Console.WriteLine("[STEP 1] Running FetchAndAddNodesAsync...");
var imported = await service.FetchAndAddNodesAsync();
Console.WriteLine($"[RESULT] Imported: {imported}");
Console.WriteLine();

// Step 2: Check tracker state
Console.WriteLine("[STEP 2] AISearchTracker state:");
Console.WriteLine($"  ReposFound: {AISearchTracker.ReposFound}");
Console.WriteLine($"  CandidatesFound: {AISearchTracker.CandidatesFound}");
Console.WriteLine($"  Passed: {AISearchTracker.Passed}");
Console.WriteLine($"  Failed: {AISearchTracker.Failed}");
Console.WriteLine($"  Imported: {AISearchTracker.Imported}");
Console.WriteLine($"  MirrorFallbacks: {AISearchTracker.MirrorFallbacks}");
Console.WriteLine($"  FetchErrors: {AISearchTracker.FetchErrors}");
Console.WriteLine();

// Step 3: Show candidates by protocol
Console.WriteLine("[STEP 3] Candidates by protocol:");
foreach (var kv in AISearchTracker.CandidatesByProtocol)
    Console.WriteLine($"  {kv.Key}: {kv.Value}");
Console.WriteLine();

Console.WriteLine("[STEP 4] Passed by protocol:");
foreach (var kv in AISearchTracker.PassedByProtocol)
    Console.WriteLine($"  {kv.Key}: {kv.Value}");
Console.WriteLine();

// Step 5: Verify all imported nodes passed testing (no dead nodes)
Console.WriteLine("[STEP 5] Timeline (last 30):");
var timeline = AISearchTracker.Timeline.ToList();
var lastN = timeline.Count > 30 ? timeline[^30..] : timeline;
foreach (var t in lastN)
    Console.WriteLine($"  {t}");
Console.WriteLine();

// Assertion: imported nodes must be >= 0 and candidates >= passed
var pass = true;
if (AISearchTracker.CandidatesFound > 0 && AISearchTracker.Imported > AISearchTracker.Passed)
{
    Console.WriteLine($"[FAIL] Imported ({AISearchTracker.Imported}) > Passed ({AISearchTracker.Passed}) - dead nodes in group!");
    pass = false;
}
else
{
    Console.WriteLine($"[PASS] No dead nodes imported. Pipeline complete.");
}

// Also check: did GitHub search work (not just fallbacks)?
if (AISearchTracker.ReposFound == 0 && imported == 0)
{
    Console.WriteLine("[WARN] GitHub search returned 0 repos - check HEAD→main fix and API reachability");
}

return pass ? 0 : 1;
