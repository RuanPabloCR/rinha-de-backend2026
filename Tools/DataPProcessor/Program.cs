// See https://aka.ms/new-console-template for more information
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

Console.WriteLine("Starting");

const string inputPath = "data/references.json.gz";
const string vectorsOutput = "data/vectors.bin";
const string labelsOutput = "data/labels.bin";
const string bucketsOutput = "data/buckets.bin";
int[] specDimensionOrder = [5, 6, 2, 7, 8, 9, 10, 11, 12, 0, 1, 3, 4, 13];

Directory.CreateDirectory("data");

await using var inputFile = File.OpenRead(inputPath);
await using var gzip = new GZipStream(inputFile, CompressionMode.Decompress);

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

var vectorBuckets = new MemoryStream[BucketTable.TotalBuckets];
var labelBuckets = new MemoryStream[BucketTable.TotalBuckets];
var vectorWriters = new BinaryWriter[BucketTable.TotalBuckets];
var labelWriters = new BinaryWriter[BucketTable.TotalBuckets];

for (int i = 0; i < BucketTable.TotalBuckets; i++)
{
    vectorBuckets[i] = new MemoryStream();
    labelBuckets[i] = new MemoryStream();
    vectorWriters[i] = new BinaryWriter(vectorBuckets[i]);
    labelWriters[i] = new BinaryWriter(labelBuckets[i]);
}

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

    int bucket = BucketTable.GetBucketId(
        item.Values[5] != -1f,
        item.Values[9] >= 0.5f,
        item.Values[10] >= 0.5f,
        item.Values[11] >= 0.5f,
        item.Values[12]);

    var vectorWriter = vectorWriters[bucket];
    foreach (var dimension in specDimensionOrder)
    {
        vectorWriter.Write(QuantizeQ15(item.Values[dimension]));
    }

    var labelWriter = labelWriters[bucket];
    labelWriter.Write(item.Label == "fraud" ? (byte)1 : (byte)0);

    count++;

    if (count % 100_000 == 0)
    {
        Console.WriteLine($"Processed {count:N0} items...");
    }
}

foreach (var writer in vectorWriters)
    writer.Flush();

foreach (var writer in labelWriters)
    writer.Flush();

await using var vectorsStream = File.Create(vectorsOutput);
await using var labelsStream = File.Create(labelsOutput);
await using var bucketsStream = File.Create(bucketsOutput);
using var bucketWriter = new BinaryWriter(bucketsStream);

int offset = 0;
int largestBucket = 0;

for (int baseBucket = 0; baseBucket < BucketTable.BaseBucketCount; baseBucket++)
{
    for (int riskIndex = 0; riskIndex < BucketTable.RiskCount; riskIndex++)
    {
        int bucket = BucketTable.ToBucketId(baseBucket, riskIndex);
        int bucketCount = checked((int)labelBuckets[bucket].Length);

        bucketWriter.Write(baseBucket);
        bucketWriter.Write(riskIndex);
        bucketWriter.Write(offset);
        bucketWriter.Write(bucketCount);

        vectorBuckets[bucket].Position = 0;
        labelBuckets[bucket].Position = 0;

        await vectorBuckets[bucket].CopyToAsync(vectorsStream);
        await labelBuckets[bucket].CopyToAsync(labelsStream);

        largestBucket = Math.Max(largestBucket, bucketCount);
        offset += bucketCount;
    }
}

Console.WriteLine($"Done. Total processed: {count:N0}");
Console.WriteLine($"Largest exact bucket: {largestBucket:N0}");

static short QuantizeQ15(float value)
{
    if (value <= -1f)
        return -32767;

    if (value >= 1f)
        return 32767;

    return (short)MathF.Round(value * 32767f);
}

sealed class Vector
{
    [JsonPropertyName("vector")]
    public float[] Values { get; set; } = [];
    public string Label { get; set; } = string.Empty;
}

static class BucketTable
{
    public const int BaseBucketCount = 16;
    public const int RiskCount = 10;
    public const int TotalBuckets = BaseBucketCount * RiskCount;

    private static ReadOnlySpan<float> Risks =>
    [
        0.15f, 0.20f, 0.25f, 0.30f, 0.35f,
        0.45f, 0.50f, 0.75f, 0.80f, 0.85f
    ];

    public static int GetBucketId(
        bool hasLastTransaction,
        bool isOnline,
        bool cardPresent,
        bool unknownMerchant,
        float mccRisk)
    {
        int baseBucket = 0;
        if (hasLastTransaction)
            baseBucket |= 1;
        if (isOnline)
            baseBucket |= 1 << 1;
        if (cardPresent)
            baseBucket |= 1 << 2;
        if (unknownMerchant)
            baseBucket |= 1 << 3;

        return ToBucketId(baseBucket, GetRiskIndex(mccRisk));
    }

    public static int ToBucketId(int baseBucket, int riskIndex)
    {
        return (baseBucket * RiskCount) + riskIndex;
    }

    private static int GetRiskIndex(float value)
    {
        int bestIndex = 0;
        float bestDistance = MathF.Abs(value - Risks[0]);

        for (int i = 1; i < Risks.Length; i++)
        {
            float distance = MathF.Abs(value - Risks[i]);
            if (distance < bestDistance)
            {
                bestIndex = i;
                bestDistance = distance;
            }
        }

        return bestIndex;
    }
}
