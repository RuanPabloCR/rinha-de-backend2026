using SuperDotnet.Services;

public sealed class SearchService
{
    private readonly DatasetReader _datasetReader;
    public SearchService(DatasetReader datasetReader)
    {
        _datasetReader = datasetReader;
    }
    public SearchResult Search(float[] query)
    {
        var top = new TopK(5);

        for (int i = 0; i < _datasetReader.TotalVectors; i++)
        {
            var candidate = _datasetReader.ReadVector(i);

            float distance = DistanceSquared(query, candidate);
            // Deixar pra adicionar o Label depois, pra otimizar leitura
            top.TryAdd(i, distance, _datasetReader.ReadLabel(i));
        }

        return new SearchResult(top.GetResults());
    }
    // Squared sem squared kk
    public static float DistanceSquared(float[] a, float[] b)
    {
        float sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            float diff = b[i] - a[i];
            sum += diff * diff;
        }
        return sum;
    }

}

public sealed record Neighbor(
    int Index,
    float Distance,
    byte Label
);
public sealed class TopK
{
    private readonly Neighbor[] _items;

    public TopK(int k)
    {
        _items = new Neighbor[k];

        for (int i = 0; i < k; i++)
        {
            _items[i] = new Neighbor(-1, float.MaxValue, 0);
        }
    }

    public void TryAdd(int index, float distance, byte label)
    {
        if (distance >= _items[^1].Distance)
            return;

        _items[^1] = new Neighbor(index, distance, label);
        // Otimizar Ordenação depois
        Array.Sort(_items, (a, b) => a.Distance.CompareTo(b.Distance));
    }

    public Neighbor[] GetResults() => _items;
}

public sealed record SearchResult(Neighbor[] Neighbors);