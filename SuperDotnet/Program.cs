using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SuperDotnet.Models;
using SuperDotnet.Services;

if (!File.Exists("data/vectors.bin") || !File.Exists("data/labels.bin") || !File.Exists("data/buckets.bin"))
    throw new Exception("One or more required files are missing");
var vectorFileSize = new FileInfo("data/vectors.bin").Length;
long count = vectorFileSize / DatasetReader.VectorSizeBytes;
var labelFileSize = new FileInfo("data/labels.bin").Length;
if (labelFileSize != count)
    throw new Exception(
        $"Vectors and labels file size mismatch. Expected {count} labels, got {labelFileSize}");

var bucketFileSize = new FileInfo("data/buckets.bin").Length;
var expectedBucketFileSize = BucketTable.TotalBuckets * BucketTable.RecordSizeBytes;
if (bucketFileSize != expectedBucketFileSize)
    throw new Exception(
        $"Invalid buckets file size. Expected {expectedBucketFileSize}, got {bucketFileSize}");


var mcc_risk_json = File.ReadAllText("data/mcc_risk.json");
var normalization_json = File.ReadAllText("data/normalization.json");

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<DatasetReader>();
builder.Services.AddSingleton<MccRisk>(new MccRisk(JsonSerializer.Deserialize(mcc_risk_json, AppJsonSerializerContext.Default.DictionaryStringSingle)!));
builder.Services.AddSingleton<Normalization>(JsonSerializer.Deserialize(normalization_json, AppJsonSerializerContext.Default.Normalization)!);
builder.Services.AddSingleton<TransactionVectorizer>();
builder.Services.AddSingleton<SearchService>();

var concurrencyLimiter = new SemaphoreSlim(2, 2);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

var dataset = app.Services.GetRequiredService<DatasetReader>();
var sw = Stopwatch.StartNew();
dataset.Warmup();
sw.Stop();
Console.WriteLine($"Dataset MMF warmup completed in {sw.ElapsedMilliseconds}ms");

var vectorizer = app.Services.GetRequiredService<TransactionVectorizer>();
var searchService = app.Services.GetRequiredService<SearchService>();
var warmupSw = Stopwatch.StartNew();

Span<short> warmupQuery = stackalloc short[DatasetReader.Dimensions];

// Synthetic query for each (baseBucket, riskIndex) — warms branch predictor, TLB, and data cache
for (int bb = 0; bb < BucketTable.BaseBucketCount; bb++)
    for (int ri = 0; ri < BucketTable.RiskCount; ri++)
    {
        warmupQuery.Clear();
        searchService.SearchFraudScore(warmupQuery, bb, ri, 0.5f);

        // Also pre-fault bucket metadata
        _ = dataset.GetBucketOffset(bb, ri);
        _ = dataset.GetBucketCount(bb, ri);
    }

// Stable heap before traffic — eliminates GC pauses during parse phase
GC.Collect(2, GCCollectionMode.Aggressive);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Aggressive);

warmupSw.Stop();
Console.WriteLine($"Pipeline warmup completed in {warmupSw.ElapsedMilliseconds}ms");

app.MapGet("/ready", () => Results.Ok("API is ready!"));
app.MapPost("/fraud-score", async (
    FraudScoreRequest request,
    TransactionVectorizer vectorizer,
    SearchService searchService,
    HttpContext httpContext) =>
{
    await concurrencyLimiter.WaitAsync(httpContext.RequestAborted);

    try
    {
        Span<short> query = stackalloc short[DatasetReader.Dimensions];
        vectorizer.VectorizeQuantized(request, query, out int baseBucket, out int riskIndex, out float amountVsAvg);
        float fraudScore = searchService.SearchFraudScore(query, baseBucket, riskIndex, amountVsAvg);

        return Results.Ok(new FraudScoreResponse
        {
            Approved = fraudScore < 0.6f,
            FraudScore = fraudScore
        });
    }
    finally
    {
        concurrencyLimiter.Release();
    }
});

app.Run();


[JsonSerializable(typeof(FraudScoreRequest))]
[JsonSerializable(typeof(FraudScoreResponse))]
[JsonSerializable(typeof(Dictionary<string, float>))]
[JsonSerializable(typeof(Normalization))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}
