using System.Reflection;

namespace RDCore.SDK.Client;

/// <summary>
/// Specifies a supported core platform capability of this assembly.
/// </summary>
/// <typeparam name="TCapability">The provided <see cref="CorePlatformClientCapability"/> type.</typeparam>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class ProvidesCorePlatformClientCapabilityAttribute<TCapability> : Attribute
    where TCapability : CorePlatformClientCapability
{ }

/// <summary>
/// Reads the <see cref="ProvidesCorePlatformClientCapabilityAttribute{TCapability}"/> assembly attributes.
/// </summary>
public static class ProvidedCorePlatformCapabilities
{
    /// <summary>
    /// The names of the capability types the specified assembly declares it provides (e.g. <c>"ParseFullDocument"</c>).
    /// A flat list of names rather than the objects themselves, so it round-trips through the JSON-RPC serializer.
    /// </summary>
    public static IReadOnlyList<string> Reflect(Assembly assembly)
        => [.. assembly.GetCustomAttributes()
            .Select(attribute => attribute.GetType())
            .Where(type => type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(ProvidesCorePlatformClientCapabilityAttribute<>))
            .Select(type => type.GetGenericArguments()[0].Name)];
}
