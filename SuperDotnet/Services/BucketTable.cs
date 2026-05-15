namespace SuperDotnet.Services;

public static class BucketTable
{
    public const int BaseBucketCount = 16;
    public const int RiskCount = 10;
    public const int TotalBuckets = BaseBucketCount * RiskCount;
    public const int RecordSizeBytes = sizeof(int) * 4;
    public const int TargetMinCandidates = 300_000;
    public const int TargetMaxCandidates = 500_000;

    public static ReadOnlySpan<float> Risks =>
    [
        0.15f, 0.20f, 0.25f, 0.30f, 0.35f,
        0.45f, 0.50f, 0.75f, 0.80f, 0.85f
    ];

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

    public static void FillRiskExpansionOrder(int riskIndex, Span<int> destination)
    {
        if (destination.Length < RiskCount)
            throw new ArgumentException("Destination span is too small.", nameof(destination));

        for (int i = 0; i < RiskCount; i++)
            destination[i] = i;

        float target = Risks[riskIndex];
        for (int i = 1; i < RiskCount; i++)
        {
            int candidate = destination[i];
            float candidateDistance = MathF.Abs(Risks[candidate] - target);
            int position = i - 1;

            while (position >= 0)
            {
                int current = destination[position];
                float currentDistance = MathF.Abs(Risks[current] - target);
                if (currentDistance < candidateDistance)
                    break;

                if (currentDistance == candidateDistance && current < candidate)
                    break;

                destination[position + 1] = current;
                position--;
            }

            destination[position + 1] = candidate;
        }
    }
}
