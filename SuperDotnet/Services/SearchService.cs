namespace SuperDotnet.Services;

public unsafe sealed class SearchService
{
    private const int K = 5;
    private readonly DatasetReader _datasetReader;

    public SearchService(DatasetReader datasetReader)
    {
        _datasetReader = datasetReader;
    }

    public float SearchFraudScore(ReadOnlySpan<short> query, int baseBucket, int riskIndex, float amountVsAvg)
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
        byte* labels = _datasetReader.LabelPointer;
        int candidates = 0;

        var (offset, count) = _datasetReader.GetBucketRange(baseBucket, riskIndex);

        if (count <= BucketTable.ChunkSize)
        {
            ScanRange(vectors, query, topIndexes, topDistances, offset, offset + count);
            candidates = count;
        }
        else
        {
            int chunkBase = _datasetReader.EstimateChunkStart(baseBucket, riskIndex, amountVsAvg);
            int chunkIndex = (chunkBase - offset) / BucketTable.ChunkSize;
            int totalChunks = (count + BucketTable.ChunkSize - 1) / BucketTable.ChunkSize;

            for (int radius = 0; ; radius++)
            {
                int lo = chunkIndex - radius;
                int hi = chunkIndex + radius;
                bool expanded = false;

                if (lo >= 0 && lo < totalChunks)
                {
                    int chunkOffset = offset + lo * BucketTable.ChunkSize;
                    int chunkSize = Math.Min(BucketTable.ChunkSize, offset + count - chunkOffset);
                    ScanRange(vectors, query, topIndexes, topDistances, chunkOffset, chunkOffset + chunkSize);
                    candidates += chunkSize;
                    expanded = true;
                }
                if (hi >= 0 && hi < totalChunks && hi != lo)
                {
                    int chunkOffset = offset + hi * BucketTable.ChunkSize;
                    int chunkSize = Math.Min(BucketTable.ChunkSize, offset + count - chunkOffset);
                    ScanRange(vectors, query, topIndexes, topDistances, chunkOffset, chunkOffset + chunkSize);
                    candidates += chunkSize;
                    expanded = true;
                }

                if (!expanded)
                    break;

                if (candidates >= BucketTable.TargetMinCandidates)
                    break;
            }
        }

        if (candidates < BucketTable.TargetMinCandidates)
        {
            int[] riskOrder = BucketTable.RiskExpansionOrders[riskIndex];
            for (int ri = 0; ri < riskOrder.Length && candidates < BucketTable.TargetMinCandidates; ri++)
            {
                int adjRisk = riskOrder[ri];
                if (adjRisk == riskIndex) continue;

                var (adjOffset, adjCount) = _datasetReader.GetBucketRange(baseBucket, adjRisk);
                if (adjCount == 0) continue;

                ScanRange(vectors, query, topIndexes, topDistances, adjOffset, adjOffset + adjCount);
                candidates += adjCount;
            }
        }

        int fraudCount = 0;
        for (int i = 0; i < K; i++)
        {
            int index = topIndexes[i];
            if (index >= 0)
                fraudCount += labels[index];
        }

        return fraudCount / (float)K;
    }

    private static void ScanRange(
        byte* vectors,
        ReadOnlySpan<short> query,
        Span<int> topIndexes,
        Span<long> topDistances,
        int start,
        int end)
    {
        for (int i = start; i < end; i++)
        {
            byte* vectorStart = vectors + ((long)i * DatasetReader.VectorSizeBytes);
            long distance = DistanceSquaredEarlyAbort(query, vectorStart, topDistances[^1]);

            if (distance < topDistances[^1])
                InsertTopK(topIndexes, topDistances, i, distance);
        }
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
