namespace SuperDotnet.Services;

public static class BucketTable
{
    public const int BaseBucketCount = 16;
    public const int RiskCount = 10;
    public const int TotalBuckets = BaseBucketCount * RiskCount;
    public const int RecordSizeBytes = sizeof(int) * 4;
    public const int ChunkSize = 16384;
    public const int TargetMinCandidates = 150_000;

    private static readonly float[] RiskValues =
    [
        0.15f, 0.20f, 0.25f, 0.30f, 0.35f,
        0.45f, 0.50f, 0.75f, 0.80f, 0.85f
    ];

    public static readonly int[][] RiskExpansionOrders;

    static BucketTable()
    {
        RiskExpansionOrders = new int[RiskCount][];
        for (int ri = 0; ri < RiskCount; ri++)
        {
            var order = new int[RiskCount];
            for (int i = 0; i < RiskCount; i++)
                order[i] = i;

            float target = RiskValues[ri];
            Array.Sort(order, (a, b) =>
            {
                float da = MathF.Abs(RiskValues[a] - target);
                float db = MathF.Abs(RiskValues[b] - target);
                int cmp = da.CompareTo(db);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });
            RiskExpansionOrders[ri] = order;
        }
    }

    public static short QuantizeQ15(float value)
    {
        if (value <= -1f)
            return -32767;

        if (value >= 1f)
            return 32767;

        return (short)MathF.Round(value * 32767f);
    }

    public static int GetBaseBucket(
        bool hasLastTransaction,
        bool isOnline,
        bool cardPresent,
        bool unknownMerchant)
    {
        int bucket = 0;
        if (hasLastTransaction)
            bucket |= 1;
        if (isOnline)
            bucket |= 1 << 1;
        if (cardPresent)
            bucket |= 1 << 2;
        if (unknownMerchant)
            bucket |= 1 << 3;

        return bucket;
    }

    public static int ToBucketId(int baseBucket, int riskIndex)
    {
        return (baseBucket * RiskCount) + riskIndex;
    }

    public static int GetRiskIndex(float value)
    {
        int bestIndex = 0;
        float bestDistance = MathF.Abs(value - RiskValues[0]);

        for (int i = 1; i < RiskValues.Length; i++)
        {
            float distance = MathF.Abs(value - RiskValues[i]);
            if (distance < bestDistance)
            {
                bestIndex = i;
                bestDistance = distance;
            }
        }

        return bestIndex;
    }
}
