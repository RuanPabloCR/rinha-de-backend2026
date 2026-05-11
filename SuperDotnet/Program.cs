using System.Text.Json;
using System.Text.Json.Serialization;
using SuperDotnet.Models;
using SuperDotnet.Services;

if (!File.Exists("data/vectors.bin") || !File.Exists("data/labels.bin"))
    throw new Exception("One or more required files are missing");
var vectorFileSize = new FileInfo("data/vectors.bin").Length;
long count = vectorFileSize / DatasetReader.VectorSizeBytes;
var labelFileSize = new FileInfo("data/labels.bin").Length;
if (labelFileSize != count)
    throw new Exception(
        $"Vectors and labels file size mismatch. Expected {count} labels, got {labelFileSize}");

Console.WriteLine($"Total items in vectors.bin: {count}");

var mcc_risk_json = File.ReadAllText("data/mcc_risk.json");
var normalization_json = File.ReadAllText("data/normalization.json");

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<DatasetReader>();
builder.Services.AddSingleton<MccRisk>(new MccRisk(JsonSerializer.Deserialize(mcc_risk_json, AppJsonSerializerContext.Default.DictionaryStringSingle)!));
builder.Services.AddSingleton<Normalization>(JsonSerializer.Deserialize(normalization_json, AppJsonSerializerContext.Default.Normalization)!);
builder.Services.AddSingleton<TransactionVectorizer>();
builder.Services.AddSingleton<SearchService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

app.MapGet("/ready", () => Results.Ok("API is ready!"));
app.MapPost("/fraud-score", (
    FraudScoreRequest request,
    TransactionVectorizer vectorizer,
    SearchService searchService) =>
{
    Span<float> query = stackalloc float[DatasetReader.Dimensions];
    vectorizer.Vectorize(request, query);
    float fraudScore = searchService.SearchFraudScore(query);
    Console.WriteLine($"Calculated fraud score: {fraudScore}");
    return Results.Ok(new FraudScoreResponse
    {
        Approved = fraudScore < 0.6f,
        FraudScore = fraudScore
    });
});
app.Run();


[JsonSerializable(typeof(FraudScoreRequest))]
[JsonSerializable(typeof(FraudScoreResponse))]
[JsonSerializable(typeof(Dictionary<string, float>))]
[JsonSerializable(typeof(Normalization))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}
