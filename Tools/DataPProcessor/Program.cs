// See https://aka.ms/new-console-template for more information
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

Console.WriteLine("Starting");

const string inputPath = "data/references.json.gz";
const string vectorsOutput = "data/vectors.bin";
const string labelsOutput = "data/labels.bin";

Directory.CreateDirectory("data");

await using var inputFile = File.OpenRead(inputPath);
await using var gzip = new GZipStream(inputFile, CompressionMode.Decompress);

await using var vectorsStream = File.Create(vectorsOutput);
await using var labelsStream = File.Create(labelsOutput);

using var vectorWriter = new BinaryWriter(vectorsStream);
using var labelWriter = new BinaryWriter(labelsStream);

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

int count = 0;

await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<Vector>(
                   gzip,
                   options))
{
    if (item is null)
        continue;

    if (item.Values.Length != 14)
        throw new InvalidOperationException(
            $"Expected 14 dimensions, got {item.Values.Length}");

    foreach (var value in item.Values)
    {
        WriteHalf(vectorWriter, (Half)value);
    }

    labelWriter.Write(item.Label == "fraud" ? (byte)1 : (byte)0);

    count++;

    if (count % 100_000 == 0)
    {
        Console.WriteLine($"Processed {count:N0} items...");
    }
}

Console.WriteLine($"Done. Total processed: {count:N0}");

static void WriteHalf(BinaryWriter writer, Half value)
{
    short bits = BitConverter.HalfToInt16Bits(value);
    writer.Write(bits);
}

sealed class Vector
{
    [JsonPropertyName("vector")]
    public float[] Values { get; set; } = [];
    public string Label { get; set; } = string.Empty;
}