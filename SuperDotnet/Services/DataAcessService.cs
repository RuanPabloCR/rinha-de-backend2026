using System.IO.MemoryMappedFiles;

namespace SuperDotnet.Services;

public unsafe sealed class DatasetReader : IDisposable
{
    public const int Dimensions = VectorLayout.Dimensions;
    private const int BytesPerValue = 2;
    public const int VectorSizeBytes = Dimensions * BytesPerValue;
    private readonly MemoryMappedFile _vectorMmf;
    private readonly MemoryMappedViewAccessor _vectorAccessor;
    private byte* _vectorPtr;

    private readonly MemoryMappedFile _labelMmf;
    private readonly MemoryMappedViewAccessor _labelAccessor;
    private byte* _labelPtr;
    private readonly int[] _bucketOffsets;
    private readonly int[] _bucketCounts;

    private volatile byte _warmupSink;

    public int TotalVectors { get; }
    public byte* VectorPointer => _vectorPtr;
    public byte* LabelPointer => _labelPtr;

    public DatasetReader(
        string vectorPath = "data/vectors.bin",
        string labelPath = "data/labels.bin",
        string bucketsPath = "data/buckets.bin")
    {
        var vectorSize = new FileInfo(vectorPath).Length;

        TotalVectors = (int)(vectorSize / VectorSizeBytes);

        _vectorMmf = MemoryMappedFile.CreateFromFile(
            vectorPath,
            FileMode.Open,
            null,
            0,
            MemoryMappedFileAccess.Read);

        _vectorAccessor = _vectorMmf.CreateViewAccessor(
            0,
            0,
            MemoryMappedFileAccess.Read);

        byte* vectorPtr = null;
        _vectorAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref vectorPtr);
        _vectorPtr = vectorPtr;

        _labelMmf = MemoryMappedFile.CreateFromFile(
            labelPath,
            FileMode.Open,
            null,
            0,
            MemoryMappedFileAccess.Read);

        _labelAccessor = _labelMmf.CreateViewAccessor(
            0,
            0,
            MemoryMappedFileAccess.Read);

        byte* labelPtr = null;
        _labelAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref labelPtr);
        _labelPtr = labelPtr;

        (_bucketOffsets, _bucketCounts) = LoadBuckets(bucketsPath, TotalVectors);
    }

    public void Warmup()
    {
        long totalVectorBytes = (long)TotalVectors * VectorSizeBytes;

        byte sink = 0;

        // Touch every cache line (64B stride) of vectors
        for (long offset = 0; offset < totalVectorBytes; offset += 64)
            sink ^= *(_vectorPtr + offset);

        // Touch every cache line of labels
        for (long offset = 0; offset < TotalVectors; offset += 64)
            sink ^= *(_labelPtr + offset);

        // Pre-touch first ChunkSize of every non-empty bucket (hot path)
        for (int b = 0; b < BucketTable.BaseBucketCount; b++)
            for (int r = 0; r < BucketTable.RiskCount; r++)
            {
                var (off, cnt) = GetBucketRange(b, r);
                int warmCnt = Math.Min(cnt, BucketTable.ChunkSize);
                for (int i = 0; i < warmCnt; i += 16)  // every 16th vector = 448B stride
                    sink ^= *(_vectorPtr + (long)(off + i) * VectorSizeBytes);
            }

        _warmupSink = sink;
        GC.KeepAlive(_warmupSink);
    }

    public void ReadVector(int index, Span<float> destination)
    {
        if ((uint)index >= (uint)TotalVectors)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (destination.Length < Dimensions)
            throw new ArgumentException(
                $"Destination span must be at least {Dimensions} elements.",
                nameof(destination));

        byte* vectorStart = _vectorPtr + ((long)index * VectorSizeBytes);

        for (int i = 0; i < Dimensions; i++)
        {
            short bits = *(short*)(vectorStart + (i * BytesPerValue));
            destination[i] = bits / 32767f;
        }
    }

    public byte ReadLabel(int index)
    {
        if ((uint)index >= (uint)TotalVectors)
            throw new ArgumentOutOfRangeException(nameof(index));

        return *(_labelPtr + index);
    }

    public int GetBucketOffset(int baseBucket, int riskIndex)
    {
        int bucket = BucketTable.ToBucketId(baseBucket, riskIndex);
        return _bucketOffsets[bucket];
    }

    public int GetBucketCount(int baseBucket, int riskIndex)
    {
        int bucket = BucketTable.ToBucketId(baseBucket, riskIndex);
        return _bucketCounts[bucket];
    }

    public (int Offset, int Count) GetBucketRange(int baseBucket, int riskIndex)
    {
        int id = BucketTable.ToBucketId(baseBucket, riskIndex);
        return (_bucketOffsets[id], _bucketCounts[id]);
    }

    public int EstimateChunkStart(int baseBucket, int riskIndex, float amountVsAvg)
    {
        var (offset, count) = GetBucketRange(baseBucket, riskIndex);
        if (count <= BucketTable.ChunkSize)
            return offset;

        float clamped = float.IsNaN(amountVsAvg) || amountVsAvg <= 0f ? 0f :
                        amountVsAvg >= 1f ? 1f : amountVsAvg;
        int position = (int)(clamped * (count - 1));
        int chunkIndex = position / BucketTable.ChunkSize;
        return offset + chunkIndex * BucketTable.ChunkSize;
    }

    private static (int[] Offsets, int[] Counts) LoadBuckets(string bucketsPath, int totalVectors)
    {
        var fileSize = new FileInfo(bucketsPath).Length;
        var expectedSize = BucketTable.TotalBuckets * BucketTable.RecordSizeBytes;
        if (fileSize != expectedSize)
            throw new InvalidOperationException(
                $"Invalid buckets file size. Expected {expectedSize}, got {fileSize}.");

        var offsets = new int[BucketTable.TotalBuckets];
        var counts = new int[BucketTable.TotalBuckets];

        using var stream = File.OpenRead(bucketsPath);
        using var reader = new BinaryReader(stream);

        int expectedOffset = 0;
        for (int baseBucket = 0; baseBucket < BucketTable.BaseBucketCount; baseBucket++)
        {
            for (int riskIndex = 0; riskIndex < BucketTable.RiskCount; riskIndex++)
            {
                int bucket = BucketTable.ToBucketId(baseBucket, riskIndex);
                int storedBaseBucket = reader.ReadInt32();
                int storedRiskIndex = reader.ReadInt32();
                int offset = reader.ReadInt32();
                int count = reader.ReadInt32();

                if (storedBaseBucket != baseBucket || storedRiskIndex != riskIndex)
                    throw new InvalidOperationException("Invalid bucket ordering in buckets.bin.");

                if (offset != expectedOffset || count < 0)
                    throw new InvalidOperationException("Invalid bucket range in buckets.bin.");

                offsets[bucket] = offset;
                counts[bucket] = count;
                expectedOffset += count;
            }
        }

        if (expectedOffset != totalVectors)
            throw new InvalidOperationException(
                $"Bucket counts mismatch. Expected {totalVectors}, got {expectedOffset}.");

        return (offsets, counts);
    }

    public void Dispose()
    {
        _vectorAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
        _vectorAccessor.Dispose();
        _vectorMmf.Dispose();

        _labelAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
        _labelAccessor.Dispose();
        _labelMmf.Dispose();
    }
}
