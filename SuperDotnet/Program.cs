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

var metrics = new RequestMetrics();
builder.Services.AddSingleton(metrics);

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

// Middleware: captures t0 (request arrived) and t4 (response flushed)
app.Use(async (context, next) =>
{
    long t0 = Stopwatch.GetTimestamp();
    await next(context);
    long t4 = Stopwatch.GetTimestamp();

    if (context.Items.TryGetValue("t1", out var t1Obj) &&
        context.Items.TryGetValue("t2", out var t2Obj) &&
        context.Items.TryGetValue("t3", out var t3Obj) &&
        t1Obj is long t1 && t2Obj is long t2 && t3Obj is long t3)
    {

        long parseUs = (t1 - t0) * 1_000_000 / Stopwatch.Frequency;
        long queueUs = (t2 - t1) * 1_000_000 / Stopwatch.Frequency;
        long classifyUs = (t3 - t2) * 1_000_000 / Stopwatch.Frequency;
        long writeUs = (t4 - t3) * 1_000_000 / Stopwatch.Frequency;
        long totalUs = (t4 - t0) * 1_000_000 / Stopwatch.Frequency;

        metrics.RecordTimings(parseUs, queueUs, classifyUs, writeUs, totalUs);
    }
});

app.MapGet("/ready", () => Results.Ok("API is ready!"));
app.MapPost("/fraud-score", async (
    FraudScoreRequest request,
    TransactionVectorizer vectorizer,
    SearchService searchService,
    HttpContext httpContext) =>
{
    long t1 = Stopwatch.GetTimestamp();  // body parsed + DTO ready

    await concurrencyLimiter.WaitAsync(httpContext.RequestAborted);
    long t2 = Stopwatch.GetTimestamp();  // semaphore acquired

    try
    {
        Span<short> query = stackalloc short[DatasetReader.Dimensions];
        vectorizer.VectorizeQuantized(request, query, out int baseBucket, out int riskIndex, out float amountVsAvg);
        float fraudScore = searchService.SearchFraudScore(query, baseBucket, riskIndex, amountVsAvg);
        long t3 = Stopwatch.GetTimestamp();  // classification done

        httpContext.Items["t1"] = t1;
        httpContext.Items["t2"] = t2;
        httpContext.Items["t3"] = t3;

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

app.MapGet("/metrics", (RequestMetrics m) => Results.Ok(m.GetSnapshot()));

app.Run();


[JsonSerializable(typeof(FraudScoreRequest))]
[JsonSerializable(typeof(FraudScoreResponse))]
[JsonSerializable(typeof(Dictionary<string, float>))]
[JsonSerializable(typeof(Normalization))]
[JsonSerializable(typeof(TimingsSnapshot))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}
