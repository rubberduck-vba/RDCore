namespace RDCore.SDK.Semantics;

/// <summary>
/// Where the class of an object created by name was found.
/// </summary>
public enum ObjectClassSource
{
    /// <summary>
    /// A class the platform implements itself, in the standard library: it is found on any platform, before the machine's servers are asked.
    /// </summary>
    StandardLibrary,

    /// <summary>
    /// A class of a library the project references: one the project could declare a variable of, and create with <c>New</c>.
    /// </summary>
    ReferencedLibrary,

    /// <summary>
    /// A class of a library the environment has a description of, and the project does not reference: known to tooling, and to nothing else.
    /// </summary>
    UnreferencedLibrary,
}

/// <summary>
/// The class of the object a <c>CreateObject</c> call creates, when the programmatic identifier it is given is written as a constant: what the object is known to
/// be, beyond the <c>Object</c> its declared type says.
/// </summary>
/// <remarks>
/// <para>
/// A fact for tooling - what a completion list offers after the object, what an analyzer says of the call - and for nothing else. It is never a declaration:
/// the object is late-bound, its members are found when they are called (<strong>MS-VBAL §6.1.2.8.1.4</strong>), and nothing that is or is not true of the
/// class described here is a compile error or changes what the program does.
/// </para>
/// <para>
/// A class is found by the identifier its library describes it with (<see cref="Runtime.Libraries.ClassDescription.ProgId"/>), in the library the identifier's
/// first part names, without regard to case. An identifier that names no version (<c>DAO.DBEngine</c>) is the class whose identifier is the same with one
/// (<c>DAO.DBEngine.120</c>): which version a machine has is its own to say, and the class is the same one. 🚧 TODO A library whose name is not the first part
/// of the identifiers of its classes (<c>VBScript_RegExp_55</c>, for <c>VBScript.RegExp</c>) is not found.
/// </para>
/// </remarks>
/// <param name="ProgId">The programmatic identifier, as it is written.</param>
/// <param name="Library">The name of the library the class is of.</param>
/// <param name="ClassName">The name of the class.</param>
/// <param name="Source">Where it was found.</param>
public sealed record class ObjectCreationFact(string ProgId, string Library, string ClassName, ObjectClassSource Source);
