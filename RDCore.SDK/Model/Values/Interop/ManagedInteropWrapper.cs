namespace RDCore.SDK.Model.Values.Interop;

public readonly record struct ManagedInteropWrapper
{
    public ManagedInteropWrapper(ManagedInteropValue value)
    {
        InteropValue = value;
    }
    public ManagedInteropWrapper(ManagedInteropReference reference)
    {
        InteropReference = reference;
    }
    public ManagedInteropWrapper(ManagedInteropVariant variant)
    {
        InteropVariant = variant;
    }

    public ManagedInteropValue? InteropValue { get; init; }
    public ManagedInteropReference? InteropReference { get; init; }
    public ManagedInteropVariant? InteropVariant { get; init; }
}
