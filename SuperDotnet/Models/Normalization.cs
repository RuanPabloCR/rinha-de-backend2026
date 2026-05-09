namespace SuperDotnet.Models;

public struct Normalization
{

    public int max_amount { get; }
    public int max_installments { get; }
    public int amount_vs_avg_ratio { get; }
    public int max_minutes { get; }
    public int max_km { get; }
    public int max_tx_count_24h { get; }
    public int max_merchant_avg_amount { get; }
}
