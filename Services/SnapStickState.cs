namespace XinputG84HE.Services;

/// <summary>Tracks physical press edges, not changes in depth. Owned by the HID lock.</summary>
public sealed class SnapStickState
{
    private readonly Dictionary<ushort, long> _pressOrder = new();
    private long _sequence;

    public void Update(ushort keyId, bool pressed)
    {
        if (!pressed)
            _pressOrder.Remove(keyId);
        else if (!_pressOrder.ContainsKey(keyId))
            _pressOrder[keyId] = ++_sequence;
    }

    public (double positive, double negative) Resolve(
        ushort? positiveId, double positive, ushort? negativeId, double negative)
    {
        if (positive <= 0 || negative <= 0)
            return (positive, negative);

        long positiveOrder = positiveId.HasValue ? _pressOrder.GetValueOrDefault(positiveId.Value) : 0;
        long negativeOrder = negativeId.HasValue ? _pressOrder.GetValueOrDefault(negativeId.Value) : 0;
        if (positiveOrder == negativeOrder)
            return (0, 0); // The same physical key cannot win both directions.
        return positiveOrder > negativeOrder ? (positive, 0) : (0, negative);
    }

    public void Clear()
    {
        _pressOrder.Clear();
        _sequence = 0;
    }
}
