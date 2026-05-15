using SuperDotnet.Models;

namespace SuperDotnet.Services;

public sealed class TransactionVectorizer
{
    private readonly Normalization _normalization;
    private readonly MccRisk _mccRisk;

    public TransactionVectorizer(Normalization normalization, MccRisk mccRisk)
    {
        _normalization = normalization;
        _mccRisk = mccRisk;
    }

    public void Vectorize(FraudScoreRequest request, Span<float> destination)
    {
        if (destination.Length < VectorLayout.Dimensions)
            throw new ArgumentException("Destination vector is too small.", nameof(destination));

        Span<float> spec = stackalloc float[VectorLayout.Dimensions];
        FillSpec(request, spec);

        var order = VectorLayout.SpecDimensionOrder;
        for (int i = 0; i < VectorLayout.Dimensions; i++)
            destination[i] = spec[order[i]];
    }

    public void VectorizeQuantized(
        FraudScoreRequest request,
        Span<short> destination,
        out int baseBucket,
        out int riskIndex)
    {
        if (destination.Length < VectorLayout.Dimensions)
            throw new ArgumentException("Destination vector is too small.", nameof(destination));

        Span<float> spec = stackalloc float[VectorLayout.Dimensions];
        FillSpec(request, spec);

        baseBucket = BucketTable.GetBaseBucket(
            request.LastTransaction is not null,
            request.Terminal.IsOnline,
            request.Terminal.CardPresent,
            spec[11] >= 0.5f);
        riskIndex = BucketTable.GetRiskIndex(spec[12]);

        var order = VectorLayout.SpecDimensionOrder;
        for (int i = 0; i < VectorLayout.Dimensions; i++)
            destination[i] = BucketTable.QuantizeQ15(spec[order[i]]);
    }

    private void FillSpec(FraudScoreRequest request, Span<float> spec)
    {
        var requestedAt = request.Transaction.RequestedAt.ToUniversalTime();

        spec[0] = Clamp01(request.Transaction.Amount / _normalization.max_amount);
        spec[1] = Clamp01((float)request.Transaction.Installments / _normalization.max_installments);
        spec[2] = Clamp01((request.Transaction.Amount / request.Customer.AvgAmount) / _normalization.amount_vs_avg_ratio);
        spec[3] = requestedAt.Hour / 23f;
        spec[4] = (((int)requestedAt.DayOfWeek + 6) % 7) / 6f;

        if (request.LastTransaction is null)
        {
            spec[5] = -1f;
            spec[6] = -1f;
        }
        else
        {
            var lastAt = request.LastTransaction.Timestamp.ToUniversalTime();
            spec[5] = Clamp01((float)(requestedAt - lastAt).TotalMinutes / _normalization.max_minutes);
            spec[6] = Clamp01(request.LastTransaction.KmFromCurrent / _normalization.max_km);
        }

        spec[7] = Clamp01(request.Terminal.KmFromHome / _normalization.max_km);
        spec[8] = Clamp01((float)request.Customer.TxCount24h / _normalization.max_tx_count_24h);
        spec[9] = request.Terminal.IsOnline ? 1f : 0f;
        spec[10] = request.Terminal.CardPresent ? 1f : 0f;
        spec[11] = IsKnownMerchant(request.Customer.KnownMerchants, request.Merchant.Id) ? 0f : 1f;
        spec[12] = _mccRisk.Verify(request.Merchant.Mcc);
        spec[13] = Clamp01(request.Merchant.AvgAmount / _normalization.max_merchant_avg_amount);
    }

    private static bool IsKnownMerchant(string[] knownMerchants, string merchantId)
    {
        for (int i = 0; i < knownMerchants.Length; i++)
        {
            if (knownMerchants[i] == merchantId)
                return true;
        }

        return false;
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value) || value <= 0f)
            return 0f;

        return value >= 1f ? 1f : value;
    }
}
