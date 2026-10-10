namespace YoUI;

/// <summary>
/// Immutable metadata for a field-backed property. No registry, reflection or value store is used.
/// Normalization runs before equality comparison; changed runs after storage and invalidation.
/// </summary>
public sealed class UiProperty<TOwner, TValue> where TOwner : Element
{
    private readonly Func<TValue, TValue>? normalize;
    private readonly Action<TOwner, TValue, TValue>? changed;
    public string Name { get; }
    public TValue DefaultValue { get; }
    public DirtyFlags Invalidation { get; }

    public UiProperty(string name, TValue defaultValue, DirtyFlags invalidation,
        Func<TValue, TValue>? normalize = null, Action<TOwner, TValue, TValue>? changed = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name; Invalidation = invalidation; this.normalize = normalize; this.changed = changed;
        DefaultValue = Normalize(defaultValue);
    }

    internal TValue Normalize(TValue value) => normalize is null ? value : normalize(value);
    internal void Notify(TOwner owner, TValue oldValue, TValue newValue) => changed?.Invoke(owner, oldValue, newValue);
}
