using System.Globalization;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// A library as a reference or a dependency asks for it: by its name, and by the identifier and version of its type library when it says which one it means.
/// </summary>
/// <remarks>
/// <para>
/// The name is what source code qualifies the library's types with (<c>DAO.Recordset</c>), and it is unique among the libraries of a project. Libraries of
/// one name can still be several: <c>DAO</c> is the Access database engine's, DAO 3.6's and the DAO 2.5/3.51 compatibility library's, each a type library
/// of its own identifier or version. An identity with a <see cref="Guid"/> means one of them; one without means the library of that name
/// (<strong>RD-VBAL §2.3.1.2</strong>).
/// </para>
/// <para>
/// A version matches as <c>LoadRegTypeLib</c> matches a registered type library's: of the same identifier and major version, the description of exactly the
/// minor version asked for, or else the one of the highest minor version above it; one of another major version never matches (<see cref="Choose"/>). An
/// identity that gives no major version takes the highest version of the identifier, and one that gives no minor version the highest minor version of its
/// major. One that gives a minor version and no major version is not valid (<see cref="IsValid"/>), and means no description.
/// </para>
/// </remarks>
/// <param name="Name">The name of the library.</param>
/// <param name="Guid">The identifier of the type library it means; <see langword="null"/> for the library of the name, whichever it is.</param>
/// <param name="Major">The major version it means; <see langword="null"/> for any.</param>
/// <param name="Minor">
/// The minor version of <paramref name="Major"/> it means: a description of exactly this one, or else of the highest above it; <see langword="null"/> for the
/// highest.
/// </param>
public sealed record class LibraryIdentity(string Name, Guid? Guid = null, int? Major = null, int? Minor = null)
{
    /// <summary>
    /// The library of a name, whichever it is.
    /// </summary>
    /// <param name="name">The name of the library.</param>
    public static implicit operator LibraryIdentity(string name) => new(name);

    /// <summary>
    /// The identity of the type library a description was made from (<see cref="LibraryDescription.Origin"/>): its name, identifier and version.
    /// </summary>
    /// <param name="description">The description.</param>
    /// <returns>The identity; one with no <see cref="Guid"/> when the description's origin states none.</returns>
    public static LibraryIdentity Of(LibraryDescription description)
    {
        var guid = System.Guid.TryParse(description.Origin?.Guid, out var parsed) ? parsed : (Guid?)null;
        var (major, minor) = ParseVersion(description.Origin?.Version);
        return new LibraryIdentity(description.Name, guid, major, minor);
    }

    /// <summary>
    /// A version as a description writes it, <c>major.minor</c>, read: <c>(3, 5)</c> for <c>"3.5"</c>, and <c>(null, null)</c> for a version that is not stated
    /// or not of that form.
    /// </summary>
    /// <param name="version">The version, as written.</param>
    public static (int? Major, int? Minor) ParseVersion(string? version)
    {
        var parts = version?.Split('.') ?? [];
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
                ? (major, minor)
                : (null, null);
    }

    /// <summary>
    /// The version as a description writes it (<c>3.5</c>); <see langword="null"/> when the identity gives no major version, and <c>major.0</c> when it gives no
    /// minor one.
    /// </summary>
    public string? Version => Major is { } major ? string.Create(CultureInfo.InvariantCulture, $"{major}.{Minor ?? 0}") : null;

    /// <summary>
    /// Whether the identity says which version it means: one that gives an identifier and a minor version gives a major version too. The version of an identity
    /// that gives no identifier is not read.
    /// </summary>
    public bool IsValid => Guid is null || Minor is null || Major is not null;

    /// <summary>
    /// Whether a description is one this identity can mean: of the same name, and, when the identity gives one, of the same identifier and major version, and
    /// a minor version at least the one it asks for. An identity that is not valid (<see cref="IsValid"/>) accepts none.
    /// </summary>
    /// <param name="description">The description.</param>
    public bool Accepts(LibraryDescription description)
    {
        if (!IsValid || !string.Equals(description.Name, Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Guid is null)
        {
            return true;
        }

        var made = Of(description);
        return made.Guid == Guid
            && (Major is null || made.Major == Major)
            && (Minor is null || made.Minor >= Minor);
    }

    /// <summary>
    /// Of the descriptions this identity accepts (<see cref="Accepts"/>), the one it means: the one of exactly the version it asks for, and otherwise the one of
    /// the highest version.
    /// </summary>
    /// <param name="candidates">The descriptions of the name.</param>
    /// <returns>The description, or <see langword="null"/> when it accepts none of them.</returns>
    public LibraryDescription? Choose(IEnumerable<LibraryDescription> candidates)
    {
        var accepted = candidates.Where(Accepts).ToArray();
        return accepted.FirstOrDefault(candidate => Major is not null && Minor is not null && Of(candidate) is var made && made.Major == Major && made.Minor == Minor)
            ?? accepted.OrderByDescending(candidate => Of(candidate).Major ?? -1).ThenByDescending(candidate => Of(candidate).Minor ?? -1).FirstOrDefault();
    }

    /// <summary>
    /// The identity as a problem or a message states it: the name, then the identifier and the version when it gives them, as it gives them (<c>5</c> for a
    /// major version and no minor one).
    /// </summary>
    public override string ToString()
    {
        if (Guid is not { } guid)
        {
            return Name;
        }

        var version = Major is not { } major ? string.Empty
            : Minor is { } minor ? string.Create(CultureInfo.InvariantCulture, $" {major}.{minor}")
            : string.Create(CultureInfo.InvariantCulture, $" {major}");
        return $"{Name} {guid.ToString("B").ToUpperInvariant()}{version}";
    }
}
