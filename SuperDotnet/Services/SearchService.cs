namespace SuperDotnet.Services;

public unsafe sealed class SearchService
{
    private const int K = 5;
    private readonly DatasetReader _datasetReader;

    public SearchService(DatasetReader datasetReader)
    {
        _datasetReader = datasetReader;
    }

    public float SearchFraudScore(ReadOnlySpan<float> query)
    {
        if (query.Length != DatasetReader.Dimensions)
            throw new ArgumentException("Vector size mismatch.", nameof(query));

        Span<int> topIndexes = stackalloc int[K];
        Span<float> topDistances = stackalloc float[K];

        for (int i = 0; i < K; i++)
        {
            topIndexes[i] = -1;
            topDistances[i] = float.MaxValue;
        }

        byte* vectors = _datasetReader.VectorPointer;
        for (int i = 0; i < _datasetReader.TotalVectors; i++)
        {
            byte* vectorStart = vectors + ((long)i * DatasetReader.VectorSizeBytes);
            float distance = DistanceSquaredEarlyAbort(query, vectorStart, topDistances[^1]);

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

    private static float DistanceSquaredEarlyAbort(ReadOnlySpan<float> query, byte* candidate, float cutoff)
    {
        float sum = 0;

        for (int i = 0; i < DatasetReader.Dimensions; i++)
        {
            short bits = *(short*)(candidate + (i * 2));
            float value = (float)BitConverter.Int16BitsToHalf(bits);
            float diff = value - query[i];
            sum += diff * diff;

            if (sum >= cutoff)
                return sum;
        }

        return sum;
    }

    private static void InsertTopK(Span<int> indexes, Span<float> distances, int index, float distance)
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
