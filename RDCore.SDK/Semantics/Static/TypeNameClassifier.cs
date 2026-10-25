using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What a name written as a type binds to in the type binding context (<strong>MS-VBAL §5.6.4</strong>).
/// </summary>
internal enum TypeNameClass
{
    /// <summary>
    /// A type: an intrinsic type, a class, a user-defined type or an enumeration.
    /// </summary>
    Type,

    /// <summary>
    /// A procedural module of a project of the workspace (MS-VBA: "A module is not a valid type").
    /// </summary>
    ProceduralModule,

    /// <summary>
    /// A procedural module of a referenced library, <c>VBA.Strings</c> (MS-VBA: "Automation type not supported in Visual Basic").
    /// </summary>
    LibraryModule,

    /// <summary>
    /// A project or a library, <c>VBA</c> (MS-VBA: "Expected user-defined type, not project").
    /// </summary>
    Project,

    /// <summary>
    /// Nothing the type binding context binds: no name of a type, or the name of a variable or a procedure (MS-VBA: "User-defined type not defined").
    /// </summary>
    Unresolved,
}

/// <summary>
/// Classifies a name written as a type (<strong>MS-VBAL §5.6.16.7</strong>: "A &lt;defined-type-expression&gt; is invalid if it is not classified as a
/// type"): the one rule a declaration's <c>As</c> clause and <c>TypeOf ... Is</c> share, so that the two report a name that is no type the same way.
/// </summary>
/// <remarks>
/// The type binding context binds a project and a procedural module as well as a type (§5.6.10), which is why a name that resolves is not a type for that
/// alone. MS-VBA tells three of them apart (Excel-verified, oracle\s29): a module of the workspace, a module of a library, and a project.
/// </remarks>
internal static class TypeNameClassifier
{
    /// <summary>
    /// Classifies <paramref name="typeName"/>, qualified by <paramref name="qualifier"/> when it is, as seen from the symbol at <paramref name="handle"/>.
    /// </summary>
    /// <param name="qualifier">The project or module the name is qualified by, or <see langword="null"/>.</param>
    /// <param name="typeName">The name of the type.</param>
    /// <param name="handle">The symbol the name is resolved from.</param>
    /// <param name="resolver">The resolver names are bound by.</param>
    /// <returns>What the name binds to, and the symbol it binds to when it binds one.</returns>
    public static (TypeNameClass Class, Symbol? Symbol) Classify(string? qualifier, string typeName, Uri handle, ISymbolResolver resolver)
    {
        if (qualifier is null && IntrinsicVBTypes.IsIntrinsic(typeName))
        {
            return (TypeNameClass.Type, null);
        }

        var result = VBProjectSymbol.ResolveQualifiedType(resolver, qualifier, typeName, handle);
        return result switch
        {
            { IsResolved: false } => (TypeNameClass.Unresolved, null),
            { Symbol: VBProjectSymbol project } => (TypeNameClass.Project, project),
            { Symbol: VBStandardModuleSymbol module } when module.GetProperty(SymbolProperties.Library) is not null => (TypeNameClass.LibraryModule, module),
            { Symbol: VBStandardModuleSymbol module } => (TypeNameClass.ProceduralModule, module),
            { Symbol: var symbol } => (TypeNameClass.Type, symbol),
        };
    }

    /// <summary>
    /// Classifies a type written as <c>Qualifier.Name</c>, or as <c>Name</c>: the qualifier is everything before the last dot.
    /// </summary>
    /// <param name="name">The type as written.</param>
    /// <param name="handle">The symbol the name is resolved from.</param>
    /// <param name="resolver">The resolver names are bound by.</param>
    /// <returns>What the name binds to, and the symbol it binds to when it binds one.</returns>
    public static (TypeNameClass Class, Symbol? Symbol) Classify(string name, Uri handle, ISymbolResolver resolver)
    {
        var split = name.LastIndexOf('.');
        return split < 0 ? Classify(null, name, handle, resolver) : Classify(name[..split], name[(split + 1)..], handle, resolver);
    }
}
