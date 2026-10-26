using RDCore.External.Automation;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Concurrent;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// The objects of a server that no referenced library declares the class of - what <c>CreateObject</c> and <c>GetObject</c> give for a class the project does
/// not reference - and their members, which a program reaches by name, as <strong>MS-VBA</strong> does: through what the object answers to (<c>IDispatch</c>),
/// whatever a type library says of it.
/// </summary>
/// <remarks>
/// <para>
/// Such an object is an instance of a class of its own name (<c>Dictionary</c>), which declares no member: a member is made for each name it is asked for, the
/// first time it is, and is a member of a library like any other - one whose external target names it - so that it is called, given its arguments and
/// returns as every member of an object a server holds does (<see cref="AutomationCallProvider"/>). Its arguments are its <c>ParamArray</c> - and the value an
/// assignment gives it a <c>Variant</c> after them - and what it returns a <c>Variant</c>.
/// </para>
/// <para>
/// An argument that is a variable is passed by reference, as MS-VBA passes it: the elements of a param array are the variables they are given
/// (<strong>MS-VBAL §5.3.1.5</strong>), which the server is given as the types they are declared, and what it writes into one is the variable's.
/// </para>
/// </remarks>
internal static class LateBinding
{
    /// <summary>The name of the library the classes of the late-bound objects are of, which no project references.</summary>
    public const string LibraryName = "LateBound";

    /// <summary>The name a late-bound object's default member is called by: its dispatch identifier, <c>DISPID_VALUE</c>.</summary>
    public const string DefaultMemberName = "[DISPID=0]";

    /// <summary>The name a late-bound object's enumeration member is called by: <c>DISPID_NEWENUM</c>.</summary>
    public const string EnumerationMemberName = "[DISPID=-4]";

    private static readonly Uri Root = new("rdcore://late-bound/");
    private static readonly ConcurrentDictionary<string, VBClassModuleSymbol> Classes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<(string Class, string Member, AutomationInvocation Invocation), VBTypeMemberSymbol> Members = new();

    /// <summary>
    /// The class of the late-bound objects of a name: the one the server gives the object's class, without its library, or <c>Object</c> when it gives none.
    /// </summary>
    public static VBClassModuleSymbol ClassNamed(string? serverName)
    {
        var name = serverName is { Length: > 0 } ? serverName[(serverName.LastIndexOf('.') + 1)..] : Tokens.Object;
        return Classes.GetOrAdd(name, named => (VBClassModuleSymbol)new VBClassModuleSymbol(Root, new VBProjectSymbol(Root, LibraryName).Uri, named)
            .With(SymbolProperties.Library, LibraryName)
            // what the object answers to is what the server says, whatever the class declares.
            .With(SymbolProperties.Extensible, true));
    }

    /// <summary>
    /// Whether <paramref name="classModule"/> is the class of late-bound objects, whose members are reached by name.
    /// </summary>
    public static bool IsLateBound(VBClassModuleSymbol classModule) => classModule.GetProperty(SymbolProperties.Library) == LibraryName;

    /// <summary>
    /// Whether the object <paramref name="identity"/> is a late-bound one.
    /// </summary>
    public static bool IsLateBound(IRuntimeSession session, SDK.Model.Values.Runtime.VBRuntimeObjectId identity)
        => session.Symbols.TryGetInstance(identity, out var instance) && IsLateBound(instance.ClassModule);

    /// <summary>
    /// The member of a late-bound class a name reaches, the way it is reached: read or called (<see cref="AutomationInvocation.Get"/>), or assigned
    /// (<see cref="AutomationInvocation.Let"/>, <see cref="AutomationInvocation.Set"/>).
    /// </summary>
    /// <param name="classModule">The late-bound class.</param>
    /// <param name="name">The member's name, or <see cref="DefaultMemberName"/>, <see cref="EnumerationMemberName"/>.</param>
    /// <param name="invocation">How it is reached.</param>
    public static VBTypeMemberSymbol MemberOf(VBClassModuleSymbol classModule, string name, AutomationInvocation invocation)
        => Members.GetOrAdd((classModule.Name, name.ToUpperInvariant(), invocation), _ => Make(classModule, name, invocation));

    private static VBTypeMemberSymbol Make(VBClassModuleSymbol classModule, string name, AutomationInvocation invocation)
    {
        Symbol member = invocation switch
        {
            AutomationInvocation.Let => new VBPropertyLetMemberSymbol(
                Root, classModule.Uri, name, ScopeKind.Instance, SymbolKindExt.Property, VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            AutomationInvocation.Set => new VBPropertySetMemberSymbol(
                Root, classModule.Uri, name, ScopeKind.Instance, SymbolKindExt.Property, VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            _ => new VBFunctionMemberSymbol(
                Root, classModule.Uri, name, ScopeKind.Instance, SymbolKindExt.Function, VBVariantType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
        };

        // an assignment's value is the last argument of the accessor, after what the property is indexed by: which source cannot declare after a ParamArray,
        // and DISPATCH_PROPERTYPUT takes in that order.
        System.Collections.Immutable.ImmutableArray<VBParameterSymbol> parameters =
        [
            new VBParameterSymbol(Root, member.Uri, Tokens.Me, SourceRange.Empty, SourceRange.Empty, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo),
            // a param array has no parameter mechanism, and its elements are ByRef: what MS-VBA gives a server for a variable is the variable (MS-VBAL §5.3.1.5).
            new ParamArrayParameterSymbol(Root, member.Uri, "Arguments", SourceRange.Empty, SourceRange.Empty, ParameterKind.ImplicitByRef),
            .. invocation is AutomationInvocation.Let or AutomationInvocation.Set
                ? [new VBParameterSymbol(Root, member.Uri, "Value", SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal, VBVariantType.TypeInfo)]
                : System.Collections.Immutable.ImmutableArray<VBParameterSymbol>.Empty,
        ];

        member = member switch
        {
            VBReturningMemberSymbol returning => returning with { Parameters = parameters },
            VBProcedureMemberSymbol procedure => procedure with { Parameters = parameters },
            _ => member,
        };

        member = member.With(SymbolProperties.Library, LibraryName)
            .With(SymbolProperties.ExternalTarget, $"{LibraryName}.{classModule.Name}.{name}/{invocation switch
            {
                AutomationInvocation.Let => "let",
                AutomationInvocation.Set => "set",
                _ => "get",
            }}");

        // the default member and the enumerator are found by the identifiers they carry, as those of a class of a library are.
        return (VBTypeMemberSymbol)(name switch
        {
            DefaultMemberName => member.With(SymbolProperties.UserMemId, WellKnownDispIds.Value),
            EnumerationMemberName => member.With(SymbolProperties.UserMemId, WellKnownDispIds.NewEnum),
            _ => member,
        });
    }
}
