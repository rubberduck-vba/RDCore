using System.Diagnostics.CodeAnalysis;
using RDCore.SDK.Runtime.Libraries;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// Libraries for tests to reference: a library is a file in production, and a description here.
/// </summary>
internal static class LibraryFixtures
{
    /// <summary>
    /// A library of two classes that name each other (a gadget has parts, and a part has a gadget), an enumeration, and members of the shapes libraries have:
    /// a default member, a parameter that is optional with a default, one that is a <c>ParamArray</c>, one that is an array, and an event.
    /// </summary>
    public static LibraryDescription Widgets { get; } = new()
    {
        Name = "Widgets",
        Description = "Widgets for tests.",
        Origin = new LibraryOrigin { Guid = "{00000000-0000-0000-0000-000000000001}", Version = "1.0", ExportedBy = "tests" },
        Enums =
        [
            new EnumDescription
            {
                Name = "WidgetMode",
                Members = [new EnumMemberDescription { Name = "wgOff", Value = 0 }, new EnumMemberDescription { Name = "wgOn", Value = 1 }],
            },
        ],
        Classes =
        [
            new ClassDescription
            {
                Name = "Gadget",
                ProgId = "Widgets.Gadget",
                IsCreatable = true,
                Members =
                [
                    new MemberDescription { Name = "Parts", Kind = MemberKind.PropertyGet, Type = "Part", DispId = 0 },
                    new MemberDescription { Name = "Mode", Kind = MemberKind.PropertyGet, Type = "WidgetMode" },
                    new MemberDescription
                    {
                        Name = "Mode", Kind = MemberKind.PropertyLet,
                        Parameters = [new ParameterDescription { Name = "Value", Type = "WidgetMode", IsByVal = true }],
                    },
                    new MemberDescription
                    {
                        Name = "Find", Kind = MemberKind.Method, Type = "Part",
                        Parameters =
                        [
                            new ParameterDescription { Name = "Name", Type = "String", IsByVal = true },
                            new ParameterDescription { Name = "Limit", Type = "Long", IsOptional = true, DefaultValue = "10" },
                            new ParameterDescription { Name = "Exact", Type = "Boolean", IsOptional = true, DefaultValue = "True" },
                        ],
                    },
                    new MemberDescription { Name = "Names", Kind = MemberKind.Method, Type = "String()" },
                    new MemberDescription
                    {
                        Name = "Reset", Kind = MemberKind.Method,
                        Parameters = [new ParameterDescription { Name = "Reasons", Type = "Variant", IsParamArray = true }],
                    },
                    new MemberDescription { Name = "Secret", Kind = MemberKind.Method, IsHidden = true },
                ],
                Events = [new EventDescription { Name = "Changed", Parameters = [new ParameterDescription { Name = "Part", Type = "Part", IsByVal = true }] }],
            },
            new ClassDescription
            {
                Name = "Part",
                Members = [new MemberDescription { Name = "Owner", Kind = MemberKind.PropertyGet, Type = "Gadget" }],
            },
        ],
    };

    /// <summary>
    /// A library of one class that names a type of <see cref="Widgets"/>, which it depends on.
    /// </summary>
    public static LibraryDescription Panels { get; } = new()
    {
        Name = "Panels",
        DependsOn = ["Widgets"],
        Classes =
        [
            new ClassDescription
            {
                Name = "Panel",
                IsCreatable = true,
                Members =
                [
                    new MemberDescription { Name = "Gadget", Kind = MemberKind.PropertyGet, Type = "Widgets.Gadget" },
                    new MemberDescription { Name = "Beyond", Kind = MemberKind.PropertyGet, Type = "Elsewhere.Thing" },
                ],
            },
        ],
    };

    public static LibraryDescription Named(string name, params string[] dependsOn) => new() { Name = name, DependsOn = [.. dependsOn] };
}

/// <summary>
/// A source of library descriptions that is a dictionary.
/// </summary>
internal sealed class InMemoryLibrarySource(params LibraryDescription[] descriptions) : ILibrarySource
{
    private readonly Dictionary<string, LibraryDescription> _byName = descriptions.ToDictionary(description => description.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The names asked of the source, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>The names of libraries that are there and cannot be read.</summary>
    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Names => _byName.Keys;

    public bool TryGet(string name, [NotNullWhen(true)] out LibraryDescription? description)
    {
        Asked.Add(name);
        if (Unreadable.Contains(name))
        {
            throw new InvalidDataException($"{name}.json: cannot be read.");
        }

        return _byName.TryGetValue(name, out description);
    }
}
