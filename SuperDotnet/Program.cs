using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using SuperDotnet.Models;
using SuperDotnet.Services;

if (!File.Exists("data/vectors.bin") || !File.Exists("data/labels.bin"))
    throw new Exception("One or more required files are missing");
var vectorFileSize = new FileInfo("data/vectors.bin").Length;
long count = vectorFileSize / 28;
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
builder.Services.AddScoped<SearchService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});


builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.MapGet("/ready", () => Results.Ok("API is ready!"));
app.MapPost("/fraud-score", () => Results.Ok(new { Score = new Random().Next(0, 100) }));
app.MapGet("/random-vector", (DatasetReader datasetReader) =>
{


    var random = Random.Shared.Next(datasetReader.TotalVectors);

    var values = datasetReader.ReadVector(random);
    var label = datasetReader.ReadLabel(random);
    var labelString = label == 1 ? "fraud" : "legit";
    return Results.Ok(new RandomVectorReponse
    {
        Label = labelString,
        Index = random,
        Values = values
    });
});
app.MapGet("/search/{index:int}", (
    int index,
    DatasetReader dataset,
    SearchService searchService) =>
{
    var query = dataset.ReadVector(index);
    SearchResult? result = searchService.Search(query);

    return Results.Ok(result);
});
app.Run();


[JsonSerializable(typeof(RandomVectorReponse))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(Neighbor))]
[JsonSerializable(typeof(Dictionary<string, float>))]
[JsonSerializable(typeof(Normalization))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

sealed class RandomVectorReponse
{
    public int Index { get; set; }
    public string Label { get; set; } = string.Empty;
    public float[] Values { get; set; } = [];
}