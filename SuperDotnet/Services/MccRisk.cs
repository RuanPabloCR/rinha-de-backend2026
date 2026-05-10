namespace SuperDotnet.Services;

public sealed class MccRisk
{
    private readonly Dictionary<string, float> _mccRisk = new Dictionary<string, float>
    {
    };
    public MccRisk(Dictionary<string, float> mccRisk)
    {
        _mccRisk = mccRisk;
    }
    public float Verify(String key)
    {
        return _mccRisk.TryGetValue(key, out var risk) ? risk : 0.5f;
    }
}