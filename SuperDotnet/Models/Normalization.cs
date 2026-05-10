namespace SuperDotnet.Models;

public sealed record Normalization
{
    public int max_amount { get; init; }
    public int max_installments { get; init; }
    public int amount_vs_avg_ratio { get; init; }
    public int max_minutes { get; init; }
    public int max_km { get; init; }
    public int max_tx_count_24h { get; init; }
    public int max_merchant_avg_amount { get; init; }
}