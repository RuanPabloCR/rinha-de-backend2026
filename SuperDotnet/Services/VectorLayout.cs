namespace SuperDotnet.Services;

public static class VectorLayout
{
    public const int Dimensions = 14;

    // High-variance/sentinel dimensions first so scalar early-abort cuts sooner.
    public static ReadOnlySpan<int> SpecDimensionOrder => [5, 6, 2, 7, 8, 9, 10, 11, 12, 0, 1, 3, 4, 13];
}
