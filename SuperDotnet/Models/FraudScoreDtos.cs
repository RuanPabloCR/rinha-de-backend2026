using System.Text.Json.Serialization;

namespace SuperDotnet.Models;

public sealed class FraudScoreRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("transaction")]
    public TransactionData Transaction { get; set; } = new();

    [JsonPropertyName("customer")]
    public CustomerData Customer { get; set; } = new();

    [JsonPropertyName("merchant")]
    public MerchantData Merchant { get; set; } = new();

    [JsonPropertyName("terminal")]
    public TerminalData Terminal { get; set; } = new();

    [JsonPropertyName("last_transaction")]
    public LastTransactionData? LastTransaction { get; set; }
}

public sealed class TransactionData
{
    [JsonPropertyName("amount")]
    public float Amount { get; set; }

    [JsonPropertyName("installments")]
    public int Installments { get; set; }

    [JsonPropertyName("requested_at")]
    public DateTimeOffset RequestedAt { get; set; }
}

public sealed class CustomerData
{
    [JsonPropertyName("avg_amount")]
    public float AvgAmount { get; set; }

    [JsonPropertyName("tx_count_24h")]
    public int TxCount24h { get; set; }

    [JsonPropertyName("known_merchants")]
    public string[] KnownMerchants { get; set; } = [];
}

public sealed class MerchantData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("mcc")]
    public string Mcc { get; set; } = string.Empty;

    [JsonPropertyName("avg_amount")]
    public float AvgAmount { get; set; }
}

public sealed class TerminalData
{
    [JsonPropertyName("is_online")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("card_present")]
    public bool CardPresent { get; set; }

    [JsonPropertyName("km_from_home")]
    public float KmFromHome { get; set; }
}

public sealed class LastTransactionData
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("km_from_current")]
    public float KmFromCurrent { get; set; }
}

public sealed class FraudScoreResponse
{
    [JsonPropertyName("approved")]
    public bool Approved { get; set; }

    [JsonPropertyName("fraud_score")]
    public float FraudScore { get; set; }
}
