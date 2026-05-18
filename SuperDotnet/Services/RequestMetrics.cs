namespace SuperDotnet.Services;

public sealed class RequestMetrics
{
    private long _totalRequests;

    // Bucket thresholds in microseconds
    private static readonly int[] BucketsUs = [0, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 50000, 100000, 500000];

    private readonly long[] _parseCounts = new long[BucketsUs.Length];
    private readonly long[] _queueCounts = new long[BucketsUs.Length];
    private readonly long[] _classifyCounts = new long[BucketsUs.Length];
    private readonly long[] _writeCounts = new long[BucketsUs.Length];
    private readonly long[] _totalCounts = new long[BucketsUs.Length];

    private long _parseSum, _queueSum, _classifySum, _writeSum, _totalSum;
    private long _parseMax, _queueMax, _classifyMax, _writeMax, _totalMax;

    public void RecordTimings(long parseUs, long queueUs, long classifyUs, long writeUs, long totalUs)
    {
        Interlocked.Increment(ref _totalRequests);
        RecordInto(_parseCounts, parseUs);
        RecordInto(_queueCounts, queueUs);
        RecordInto(_classifyCounts, classifyUs);
        RecordInto(_writeCounts, writeUs);
        RecordInto(_totalCounts, totalUs);
        Interlocked.Add(ref _parseSum, parseUs);
        Interlocked.Add(ref _queueSum, queueUs);
        Interlocked.Add(ref _classifySum, classifyUs);
        Interlocked.Add(ref _writeSum, writeUs);
        Interlocked.Add(ref _totalSum, totalUs);
        UpdateMax(ref _parseMax, parseUs);
        UpdateMax(ref _queueMax, queueUs);
        UpdateMax(ref _classifyMax, classifyUs);
        UpdateMax(ref _writeMax, writeUs);
        UpdateMax(ref _totalMax, totalUs);
    }

    private static void RecordInto(long[] counts, long value)
    {
        int idx = Array.BinarySearch(BucketsUs, (int)Math.Clamp(value, 0, int.MaxValue));
        if (idx < 0) idx = ~idx - 1;
        Interlocked.Increment(ref counts[Math.Max(0, idx)]);
    }

    public TimingsSnapshot GetSnapshot()
    {
        long total = Interlocked.Read(ref _totalRequests);
        return new()
        {
            TotalRequests = total,
            ParseAvgUs = Avg(total, _parseSum), QueueAvgUs = Avg(total, _queueSum),
            ClassifyAvgUs = Avg(total, _classifySum), WriteAvgUs = Avg(total, _writeSum), TotalAvgUs = Avg(total, _totalSum),
            ParseMaxUs = Interlocked.Read(ref _parseMax), QueueMaxUs = Interlocked.Read(ref _queueMax),
            ClassifyMaxUs = Interlocked.Read(ref _classifyMax), WriteMaxUs = Interlocked.Read(ref _writeMax), TotalMaxUs = Interlocked.Read(ref _totalMax),
            BucketThresholdsUs = BucketsUs,
            ParseBucketCounts = (long[])_parseCounts.Clone(), QueueBucketCounts = (long[])_queueCounts.Clone(),
            ClassifyBucketCounts = (long[])_classifyCounts.Clone(), WriteBucketCounts = (long[])_writeCounts.Clone(),
            TotalBucketCounts = (long[])_totalCounts.Clone()
        };
    }

    private static double Avg(long count, long sum) => count > 0 ? sum / (double)count : 0;

    private static void UpdateMax(ref long target, long value)
    {
        long initial;
        do
        {
            initial = target;
            if (value <= initial) break;
        }
        while (Interlocked.CompareExchange(ref target, value, initial) != initial);
    }
}

public sealed class TimingsSnapshot
{
    public long TotalRequests { get; set; }
    public double ParseAvgUs { get; set; }
    public double QueueAvgUs { get; set; }
    public double ClassifyAvgUs { get; set; }
    public double WriteAvgUs { get; set; }
    public double TotalAvgUs { get; set; }
    public long ParseMaxUs { get; set; }
    public long QueueMaxUs { get; set; }
    public long ClassifyMaxUs { get; set; }
    public long WriteMaxUs { get; set; }
    public long TotalMaxUs { get; set; }
    public int[] BucketThresholdsUs { get; set; } = [];
    public long[] ParseBucketCounts { get; set; } = [];
    public long[] QueueBucketCounts { get; set; } = [];
    public long[] ClassifyBucketCounts { get; set; } = [];
    public long[] WriteBucketCounts { get; set; } = [];
    public long[] TotalBucketCounts { get; set; } = [];
}
