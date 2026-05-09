namespace SuperDotnet.Models;

public record Operation
{
    public string id { get; set; } = string.Empty;
    public decimal transaction_amount { get; set; }
    public int transaction_installments { get; set; }
    public string transaction_requested_at { get; set; } = string.Empty;
    public decimal customer_avg_amount { get; set; }
    public int customer_tx_count_24h { get; set; }
    public string[] customer_known_merchants { get; set; } = [];
    public string merchant_id { get; set; } = string.Empty;
    public string merchant_mcc { get; set; } = string.Empty;
    public decimal merchant_avg_amount { get; set; }
    public bool terminal_is_online { get; set; }
    public bool terminal_card_present { get; set; }
    public decimal terminal_km_from_home { get; set; }
    public decimal last_transaction_km_from_current { get; set; }
}
