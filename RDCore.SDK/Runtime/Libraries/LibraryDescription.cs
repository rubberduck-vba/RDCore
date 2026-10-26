using System.Collections.Immutable;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// What a referenced library declares, as a file: the types a project that references the library can name, and the members of each.
/// </summary>
/// <remarks>
/// <para>
/// A library is known by its <see cref="Name"/> and by nothing else: a project references <c>Scripting</c>, and the name is all that finds it, as if the
/// library were VBA code. Where the library came from - a type library of the machine it was described on, with the identity and version that machine's
/// registry gives it - is <see cref="Origin"/>, which says where a description was made and is not what finds it.
/// </para>
/// <para>
/// The description is a file because what a library declares is needed where the library is not: a workspace is analyzed on a machine that does not have
/// Excel, and the symbols it needs are those of a description that ships with the platform. It is written by a tool that reads the library where it is, and by
/// no one else; it is plain JSON (<see cref="LibraryJson"/>) so that a change to one reads as a change in review.
/// </para>
/// <para>
/// What it holds is what a VBA project can name: enumerations, and classes with their members and events. A type is named in a description the way VBA source
/// names it (<see cref="ParameterDescription.Type"/>).
/// </para>
/// </remarks>
public sealed record class LibraryDescription
{
    /// <summary>
    /// The version of the format of the file, which a reader that does not know it refuses rather than misreads.
    /// </summary>
    public int FormatVersion { get; init; } = 1;

    /// <summary>
    /// The name of the library: what a project references it by, and what qualifies the types it declares (<c>Excel.Worksheet</c>).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// What the library says it is, in a sentence.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Where the description was made.
    /// </summary>
    public LibraryOrigin? Origin { get; init; }

    /// <summary>
    /// The names of the libraries whose types this one's members are declared as. They are loaded with it, whether or not a project references them: a project
    /// that references Excel can use what Excel returns.
    /// </summary>
    public ImmutableArray<string> DependsOn { get; init; } = [];

    /// <summary>
    /// The enumerations the library declares.
    /// </summary>
    public ImmutableArray<EnumDescription> Enums { get; init; } = [];

    /// <summary>
    /// The classes the library declares.
    /// </summary>
    public ImmutableArray<ClassDescription> Classes { get; init; } = [];
}

/// <summary>
/// Where a library description was made. Informational: it is not what finds the library, and nothing resolves a name by it.
/// </summary>
public sealed record class LibraryOrigin
{
    /// <summary>The identifier the machine gave the library, when it gave one.</summary>
    public string? Guid { get; init; }

    /// <summary>The version of the library the description was made from.</summary>
    public string? Version { get; init; }

    /// <summary>The file the library was read from.</summary>
    public string? File { get; init; }

    /// <summary>What made the description.</summary>
    public string? ExportedBy { get; init; }
}

/// <summary>
/// An enumeration a library declares (<strong>MS-VBAL §5.2.3.4</strong>).
/// </summary>
public sealed record class EnumDescription
{
    /// <summary>The name of the enumeration.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What the library says the enumeration is.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the library leaves it out of what it shows.</summary>
    public bool IsHidden { get; init; }

    /// <summary>The members of the enumeration, in declaration order.</summary>
    public ImmutableArray<EnumMemberDescription> Members { get; init; } = [];
}

/// <summary>
/// A member of an enumeration a library declares.
/// </summary>
public sealed record class EnumMemberDescription
{
    /// <summary>The name of the member.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Its value.</summary>
    public long Value { get; init; }

    /// <summary>What the library says it is.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the library leaves it out of what it shows.</summary>
    public bool IsHidden { get; init; }
}

/// <summary>
/// A class a library declares.
/// </summary>
public sealed record class ClassDescription
{
    /// <summary>The name of the class.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What the library says the class is.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The name an object of the class is created by from a string - <c>CreateObject("Scripting.Dictionary")</c> - when it can be.
    /// </summary>
    public string? ProgId { get; init; }

    /// <summary>
    /// Whether the class can be created: with <c>New</c>, or by its <see cref="ProgId"/>. A class that is not is only ever obtained from another object.
    /// </summary>
    public bool IsCreatable { get; init; }

    /// <summary>Whether the library leaves it out of what it shows.</summary>
    public bool IsHidden { get; init; }

    /// <summary>
    /// Whether the class is the one the library's host is, which a host makes members of implicitly available (<c>Excel.Application</c>). Its members are never
    /// names of a project that references the library: a project says which object it is talking to.
    /// </summary>
    public bool IsApplication { get; init; }

    /// <summary>
    /// The name of the interface the class's <see cref="Members"/> are declared by, when it is not the class's own: <c>IReturnBoolean</c> of Microsoft Forms'
    /// <c>ReturnBoolean</c>, <c>_Worksheet</c> of Excel's <c>Worksheet</c>. It is a type name of the library too, which a program can declare a variable as
    /// (<c>As MSForms.IReturnBoolean</c>), and which an object of the class is; <see langword="null"/> for a class whose interface is not described.
    /// </summary>
    public string? DefaultInterface { get; init; }

    /// <summary>Whether the library leaves the <see cref="DefaultInterface"/> out of what it shows.</summary>
    public bool IsDefaultInterfaceHidden { get; init; }

    /// <summary>The members of the class, in declaration order.</summary>
    public ImmutableArray<MemberDescription> Members { get; init; } = [];

    /// <summary>The events the class raises, in declaration order.</summary>
    public ImmutableArray<EventDescription> Events { get; init; } = [];
}

/// <summary>
/// What a member of a class is.
/// </summary>
public enum MemberKind
{
    /// <summary>A <c>Sub</c>, or a <c>Function</c> when it has a <see cref="MemberDescription.Type"/>.</summary>
    Method = 0,

    /// <summary>A <c>Property Get</c>.</summary>
    PropertyGet = 1,

    /// <summary>A <c>Property Let</c>.</summary>
    PropertyLet = 2,

    /// <summary>A <c>Property Set</c>.</summary>
    PropertySet = 3,
}

/// <summary>
/// A member of a class a library declares.
/// </summary>
public sealed record class MemberDescription
{
    /// <summary>The name of the member.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What the member is.</summary>
    public MemberKind Kind { get; init; }

    /// <summary>
    /// The type the member returns, or <see langword="null"/> for one that returns none: a <c>Sub</c>, a <c>Property Let</c>, a <c>Property Set</c>.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// The dispatch identifier the library gives the member, when it gives one. <c>0</c> marks the default member of a class and <c>-4</c> its enumerator.
    /// </summary>
    public int? DispId { get; init; }

    /// <summary>What the library says the member is.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the library leaves it out of what it shows.</summary>
    public bool IsHidden { get; init; }

    /// <summary>The parameters of the member, in declaration order.</summary>
    public ImmutableArray<ParameterDescription> Parameters { get; init; } = [];
}

/// <summary>
/// A parameter of a member or of an event.
/// </summary>
public sealed record class ParameterDescription
{
    /// <summary>The name of the parameter.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The type of the parameter, written as VBA source writes it: an intrinsic type (<c>Long</c>, <c>Variant</c>), a type of the library (<c>Worksheet</c>),
    /// a type of another library (<c>Office.CommandBar</c>), or any of them as an array (<c>String()</c>).
    /// </summary>
    public string Type { get; init; } = "Variant";

    /// <summary>Whether it is passed by value. A parameter is passed by reference unless it says otherwise.</summary>
    public bool IsByVal { get; init; }

    /// <summary>Whether the argument may be left out.</summary>
    public bool IsOptional { get; init; }

    /// <summary>
    /// The value an optional parameter takes when its argument is left out, written as a VBA literal, when the library states one.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>Whether it is a <c>ParamArray</c>.</summary>
    public bool IsParamArray { get; init; }
}

/// <summary>
/// An event a class raises.
/// </summary>
public sealed record class EventDescription
{
    /// <summary>The name of the event.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What the library says the event is.</summary>
    public string? Description { get; init; }

    /// <summary>The parameters a handler of the event receives, in declaration order.</summary>
    public ImmutableArray<ParameterDescription> Parameters { get; init; } = [];
}
