namespace SuperDotnet.Services;

public unsafe sealed class SearchService
{
    private const int K = 5;
    private readonly DatasetReader _datasetReader;

    public SearchService(DatasetReader datasetReader)
    {
        _datasetReader = datasetReader;
    }

    public float SearchFraudScore(ReadOnlySpan<short> query, int baseBucket, int riskIndex)
    {
        if (query.Length != DatasetReader.Dimensions)
            throw new ArgumentException("Vector size mismatch.", nameof(query));

        Span<int> topIndexes = stackalloc int[K];
        Span<long> topDistances = stackalloc long[K];

        for (int i = 0; i < K; i++)
        {
            topIndexes[i] = -1;
            topDistances[i] = long.MaxValue;
        }

        byte* vectors = _datasetReader.VectorPointer;
        Span<int> riskOrder = stackalloc int[BucketTable.RiskCount];
        BucketTable.FillRiskExpansionOrder(riskIndex, riskOrder);

        int selectedCandidates = 0;
        for (int riskOrderIndex = 0; riskOrderIndex < BucketTable.RiskCount; riskOrderIndex++)
        {
            int currentRiskIndex = riskOrder[riskOrderIndex];
            int offset = _datasetReader.GetBucketOffset(baseBucket, currentRiskIndex);
            int count = _datasetReader.GetBucketCount(baseBucket, currentRiskIndex);

            for (int i = 0; i < count; i++)
            {
                int index = offset + i;
                byte* vectorStart = vectors + ((long)index * DatasetReader.VectorSizeBytes);
                long distance = DistanceSquaredEarlyAbort(query, vectorStart, topDistances[^1]);

                if (distance < topDistances[^1])
                    InsertTopK(topIndexes, topDistances, index, distance);
            }

            selectedCandidates += count;
            if (selectedCandidates >= BucketTable.TargetMinCandidates)
                break;
        }

        int fraudCount = 0;
        byte* labels = _datasetReader.LabelPointer;
        for (int i = 0; i < K; i++)
        {
            int index = topIndexes[i];
            if (index >= 0)
                fraudCount += labels[index];
        }

        return fraudCount / (float)K;
    }

    public float SearchFraudScoreFullScan(ReadOnlySpan<short> query)
    {
        if (query.Length != DatasetReader.Dimensions)
            throw new ArgumentException("Vector size mismatch.", nameof(query));

        Span<int> topIndexes = stackalloc int[K];
        Span<long> topDistances = stackalloc long[K];

        for (int i = 0; i < K; i++)
        {
            topIndexes[i] = -1;
            topDistances[i] = long.MaxValue;
        }

        byte* vectors = _datasetReader.VectorPointer;
        for (int i = 0; i < _datasetReader.TotalVectors; i++)
        {
            byte* vectorStart = vectors + ((long)i * DatasetReader.VectorSizeBytes);
            long distance = DistanceSquaredEarlyAbort(query, vectorStart, topDistances[^1]);

            if (distance < topDistances[^1])
                InsertTopK(topIndexes, topDistances, i, distance);
        }

        int fraudCount = 0;
        byte* labels = _datasetReader.LabelPointer;
        for (int i = 0; i < K; i++)
        {
            int index = topIndexes[i];
            if (index >= 0)
                fraudCount += labels[index];
        }

        return fraudCount / (float)K;
    }

    private static long DistanceSquaredEarlyAbort(ReadOnlySpan<short> query, byte* candidate, long cutoff)
    {
        long sum = 0;
        short* values = (short*)candidate;

        for (int i = 0; i < DatasetReader.Dimensions; i++)
        {
            int diff = values[i] - query[i];
            sum += (long)diff * diff;

            if (sum >= cutoff)
                return sum;
        }

        return sum;
    }

    private static void InsertTopK(Span<int> indexes, Span<long> distances, int index, long distance)
    {
        int position = K - 1;
        while (position > 0 && distance < distances[position - 1])
        {
            distances[position] = distances[position - 1];
            indexes[position] = indexes[position - 1];
            position--;
        }

        distances[position] = distance;
        indexes[position] = index;
    }
}
