using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
if (!File.Exists("data/vectors.bin") || !File.Exists("data/labels.bin"))
    throw new Exception("One or more required files are missing");
var vectorFileSize = new FileInfo("data/vectors.bin").Length;
long count = vectorFileSize / 28;
var labelFileSize = new FileInfo("data/labels.bin").Length;
if (labelFileSize != count)
    throw new Exception(
        $"Vectors and labels file size mismatch. Expected {count} labels, got {labelFileSize}");

Console.WriteLine($"Total items in vectors.bin: {count}");

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.MapGet("/ready", () => Results.Ok("API is ready!"));
app.MapPost("/fraud-score", () => Results.Ok(new { Score = new Random().Next(0, 100) }));
app.MapGet("/random-vector", () =>
{
    const int dimensions = 14;
    const int vectorSizeBytes = 28;
    const int totalVectors = 3_000_000;

    var random = Random.Shared.Next(totalVectors);

    using var stream = File.OpenRead("data/vectors.bin");
    using var reader = new BinaryReader(stream);
    using var labelStream = File.OpenRead("data/labels.bin");
    stream.Seek(random * vectorSizeBytes, SeekOrigin.Begin);
    labelStream.Seek(random, SeekOrigin.Begin);
    var values = new float[dimensions];

    for (int i = 0; i < dimensions; i++)
    {
        short bits = reader.ReadInt16();
        values[i] = (float)BitConverter.Int16BitsToHalf(bits);
    }
    byte labelByte = (byte)labelStream.ReadByte();
    var label = labelByte == 1 ? "fraud" : "legit";
    return Results.Ok(new RandomVectorReponse
    {
        Label = label,
        Index = random,
        Values = values
    });
});

app.Run();


[JsonSerializable(typeof(RandomVectorReponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}

sealed class RandomVectorReponse
{
    public int Index { get; set; }
    public string Label { get; set; } = string.Empty;
    public float[] Values { get; set; } = [];
}