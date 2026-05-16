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

var bucketItems = new List<(float amountVsAvg, short[] vector, byte label)>[BucketTable.TotalBuckets];
for (int i = 0; i < BucketTable.TotalBuckets; i++)
    bucketItems[i] = [];

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

    short[] vec = new short[14];
    for (int d = 0; d < 14; d++)
        vec[d] = QuantizeQ15(item.Values[specDimensionOrder[d]]);

    byte label = item.Label == "fraud" ? (byte)1 : (byte)0;
    float amountVsAvg = item.Values[2];

    bucketItems[bucket].Add((amountVsAvg, vec, label));

    count++;

    if (count % 100_000 == 0)
        Console.WriteLine($"Processed {count:N0} items...");
}

Console.WriteLine($"Sorting {count:N0} items by amount_vs_avg...");
var sortSw = System.Diagnostics.Stopwatch.StartNew();

for (int i = 0; i < BucketTable.TotalBuckets; i++)
{
    if (bucketItems[i].Count > 0)
    {
        var items = bucketItems[i];
        items.Sort((a, b) => a.amountVsAvg.CompareTo(b.amountVsAvg));
        bucketItems[i] = items;
    }
}

sortSw.Stop();
Console.WriteLine($"Sort completed in {sortSw.ElapsedMilliseconds}ms");

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
        int bucketId = BucketTable.ToBucketId(baseBucket, riskIndex);
        var items = bucketItems[bucketId];
        int bucketCount = items.Count;

        bucketWriter.Write(baseBucket);
        bucketWriter.Write(riskIndex);
        bucketWriter.Write(offset);
        bucketWriter.Write(bucketCount);

        foreach (var (_, vec, label) in items)
        {
            foreach (short val in vec)
                vectorsStream.Write(BitConverter.GetBytes(val));
            labelsStream.WriteByte(label);
        }

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
    public const int ChunkSize = 16384;

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
