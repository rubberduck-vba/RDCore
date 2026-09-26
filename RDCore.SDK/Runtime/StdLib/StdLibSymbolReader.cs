using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Immutable;
using System.Reflection;

namespace RDCore.SDK.Runtime.StdLib;

/// <summary>
/// Reads the standard library's symbols off the SDK declarations that define it — the interfaces and
/// enumerations in <see cref="RDCore.SDK.Runtime.Abstract.StdLib"/> marked with
/// <see cref="StdLibModuleAttribute"/>, <see cref="StdLibClassAttribute"/> and
/// <see cref="StdLibEnumAttribute"/>.
/// </summary>
/// <remarks>
/// The declarations are the single source of truth: a module's members, their names, their parameters
/// and their return types are all read back off the signature an implementation has to satisfy, so the
/// symbol a workspace resolves and the method the runtime invokes cannot drift apart. What a signature
/// cannot express — an accessor kind, a <c>$</c>-suffixed name, a class or enumeration return type — is
/// stated by <see cref="StdLibMemberAttribute"/>.
/// <para>
/// 🚧 A member whose declared type is another standard-library <em>class</em> resolves only because
/// classes are read before the modules that return them, which is enough for
/// <c>Information.Err() As ErrObject</c>. The <c>RegExp</c> family is mutually referential
/// (<c>Execute</c> returns a <c>MatchCollection</c> of <c>Match</c>, each with <c>SubMatches</c>), so
/// TODO: order class completion topologically, with a cycle guard, once those classes declare their
/// members.
/// </para>
/// </remarks>
public sealed class StdLibSymbolReader
{
    private readonly Uri _workspaceRoot;
    private readonly Uri _globalsOwnerUri;

    /// <summary>
    /// Creates the reader.
    /// </summary>
    /// <param name="workspaceRoot">
    /// The workspace the symbols are addressed under. Standard-library symbols are not <em>of</em> the
    /// workspace, but every <see cref="Symbol"/> is addressed relative to one.
    /// </param>
    /// <param name="globalsOwnerUri">
    /// The standard module a predefined enum is declared in. The library's enums belong to no module of
    /// MS-VBAL's own description, but a declaration has to be <em>somewhere</em> for both halves of it to
    /// resolve: a standard module's public members reach the project scope, which is the tier a type
    /// reference and an unqualified enum constant are both looked up in. Defaults to
    /// <paramref name="workspaceRoot"/>, which leaves them in the global scope — where the constants
    /// still resolve, but the enum's own name is not a type reference.
    /// </param>
    public StdLibSymbolReader(Uri workspaceRoot, Uri? globalsOwnerUri = null)
    {
        _workspaceRoot = workspaceRoot;
        _globalsOwnerUri = globalsOwnerUri ?? workspaceRoot;
    }

    /// <summary>
    /// Reads every standard-library declaration an assembly exports.
    /// </summary>
    /// <param name="assembly">
    /// The assembly to scan. Nothing declares which modules a workspace gets — every VBA project has
    /// the standard library whether or not a <c>.rdproj</c> mentions it (<strong>RD-VBAL §6.1</strong>)
    /// — so the set is whatever carries the markers.
    /// </param>
    public ImmutableArray<Symbol> Read(Assembly assembly) => Read(assembly.GetExportedTypes());

    /// <summary>
    /// Reads the given standard-library declarations.
    /// </summary>
    /// <param name="declarations">
    /// The marked interfaces and enumerations. Anything unmarked is ignored, so this may be handed a
    /// whole assembly's types.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A marked declaration is not expressible as a VBA symbol — a parameter or return type that is
    /// neither an intrinsic value nor a marked enumeration or class. That is a mistake in the
    /// declaration rather than a condition to degrade over: the symbol it would produce would be
    /// declared as a type nothing can bind.
    /// </exception>
    public ImmutableArray<Symbol> Read(IEnumerable<Type> declarations)
    {
        var all = declarations.ToArray();
        var symbols = ImmutableArray.CreateBuilder<Symbol>();

        // enums first: a module member or a class member may be declared as one.
        var enumTypes = new Dictionary<Type, VBEnumType>();
        foreach (var declaration in Marked<StdLibEnumAttribute>(all))
        {
            var symbol = ReadEnum(declaration);
            var type = (VBEnumType)symbol.ResolvedType;
            enumTypes[declaration] = type;
            symbols.Add(symbol);
            symbols.AddRange(type.Members);
        }

        // then classes, so a module member declared as one — Information.Err() As ErrObject — binds a
        // class type that already knows its own members.
        var classTypes = new Dictionary<Type, VBClassType>();
        foreach (var declaration in Marked<StdLibClassAttribute>(all))
        {
            var symbol = ReadClass(declaration, enumTypes, classTypes);
            classTypes[declaration] = VBClassType.FromClassModule(symbol);
            symbols.Add(symbol);
        }

        foreach (var declaration in Marked<StdLibModuleAttribute>(all))
        {
            var (module, members) = ReadModule(declaration, enumTypes, classTypes);
            symbols.Add(module);
            symbols.AddRange(members);
        }

        return symbols.ToImmutable();
    }

    // reflection promises no member order, and a class's default interface is ordered
    // (VBClassType.FromClassModule), so order by declaration — which the metadata token follows.
    private static Type[] Marked<TAttribute>(Type[] declarations) where TAttribute : Attribute
        => [.. declarations
            .Where(type => type.GetCustomAttribute<TAttribute>() is not null)
            .OrderBy(type => type.MetadataToken)];

    private static MethodInfo[] MembersOf(Type declaration)
        => [.. declaration.GetMethods().OrderBy(method => method.MetadataToken)];

    private VBEnumMemberSymbol ReadEnum(Type declaration)
    {
        var name = declaration.GetCustomAttribute<StdLibEnumAttribute>()!.Name ?? StdLibNames.EnumName(declaration.Name);

        // Public, and of the module that owns the environment's globals rather than of any module the
        // specification names: that is what puts both the enum's own name and its constants in the
        // project scope (MS-VBAL §5.2.3.4), so `VbDayOfWeek` binds as a type and `vbSunday` as a value,
        // neither of them needing a qualifier.
        var symbol = new VBEnumMemberSymbol(
            _workspaceRoot, _globalsOwnerUri, name, ScopeKind.Module, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        var constants = declaration.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => new VBEnumConstMemberSymbol(
                _workspaceRoot, symbol.Uri,
                field.GetCustomAttribute<StdLibConstantAttribute>()?.Name ?? StdLibNames.ConstantName(field.Name),
                ScopeKind.Module, SymbolKindExt.EnumMember, SourceRange.Empty, SourceRange.Empty))
            .ToArray();

        return symbol with { ResolvedType = new VBEnumType(symbol, constants) };
    }

    private VBClassModuleSymbol ReadClass(
        Type declaration, Dictionary<Type, VBEnumType> enumTypes, Dictionary<Type, VBClassType> classTypes)
    {
        var attribute = declaration.GetCustomAttribute<StdLibClassAttribute>()!;
        var name = attribute.Name ?? StdLibNames.ClassName(declaration.Name);

        var symbol = (VBClassModuleSymbol)new VBClassModuleSymbol(_workspaceRoot, _workspaceRoot, name)
            .With(SymbolProperties.Creatable, attribute.IsCreatable);

        symbol = symbol with
        {
            Members =
            [
                .. MembersOf(declaration).Select(method =>
                    (VBTypeMemberSymbol)ReadMember(method, symbol.Uri, ScopeKind.Instance, enumTypes, classTypes)),
            ],
        };
        // a pure function of Members, and fixed the moment they are known — the same ordering a
        // workspace's own class modules follow.
        return symbol with { DefaultInterfaceMembers = VBClassType.FromClassModule(symbol).Members };
    }

    private (VBStandardModuleSymbol Module, IEnumerable<Symbol> Members) ReadModule(
        Type declaration, Dictionary<Type, VBEnumType> enumTypes, Dictionary<Type, VBClassType> classTypes)
    {
        var name = declaration.GetCustomAttribute<StdLibModuleAttribute>()!.Name ?? StdLibNames.ModuleName(declaration.Name);
        var module = new VBStandardModuleSymbol(_workspaceRoot, _workspaceRoot, name);

        // a standard module's members are separate symbols parented to it, which is what promotes the
        // non-Private ones to the project scope so that `IsNumeric(x)` resolves unqualified.
        var members = MembersOf(declaration)
            .Select(method => ReadMember(method, module.Uri, ScopeKind.Module, enumTypes, classTypes))
            .ToArray();

        return (module, members);
    }

    private Symbol ReadMember(
        MethodInfo method, Uri ownerUri, ScopeKind scope,
        Dictionary<Type, VBEnumType> enumTypes, Dictionary<Type, VBClassType> classTypes)
    {
        var attribute = method.GetCustomAttribute<StdLibMemberAttribute>();
        var name = attribute?.Name ?? method.Name;
        var returnType = ReturnTypeOf(method, attribute, enumTypes, classTypes);

        Symbol member = (attribute?.Kind ?? StdLibMemberKind.Procedure) switch
        {
            StdLibMemberKind.PropertyGet => new VBPropertyGetMemberSymbol(
                _workspaceRoot, ownerUri, scope, name, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public)
            {
                ResolvedType = returnType,
            },
            StdLibMemberKind.PropertyLet => new VBPropertyLetMemberSymbol(
                _workspaceRoot, ownerUri, name, scope, SymbolKindExt.Property, VBVoidType.TypeInfo,
                SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            StdLibMemberKind.PropertySet => new VBPropertySetMemberSymbol(
                _workspaceRoot, ownerUri, name, scope, SymbolKindExt.Property, VBVoidType.TypeInfo,
                SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            _ when returnType is VBVoidType => new VBProcedureMemberSymbol(
                _workspaceRoot, ownerUri, name, scope, SymbolKindExt.Procedure, VBVoidType.TypeInfo,
                SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            _ => new VBFunctionMemberSymbol(
                _workspaceRoot, ownerUri, name, scope, SymbolKindExt.Function, returnType,
                SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
        };

        var parameters = ReadParameters(method, member.Uri, scope, enumTypes, classTypes);
        return member switch
        {
            VBReturningMemberSymbol returning => returning with { Parameters = parameters },
            VBProcedureMemberSymbol procedure => procedure with { Parameters = parameters },
            _ => member,
        };
    }

    private ImmutableArray<VBParameterSymbol> ReadParameters(
        MethodInfo method, Uri memberUri, ScopeKind scope,
        Dictionary<Type, VBEnumType> enumTypes, Dictionary<Type, VBClassType> classTypes)
    {
        var parameters = method.GetParameters();
        var builder = ImmutableArray.CreateBuilder<VBParameterSymbol>(parameters.Length + 1);

        // an instance member's implicit parameter 0, bound to the object the call is dispatched on,
        // exactly as SymbolBuilder gives a class module's own members one.
        if (scope is ScopeKind.Instance)
        {
            builder.Add(new VBParameterSymbol(
                _workspaceRoot, memberUri, "Me", SourceRange.Empty, SourceRange.Empty,
                ParameterKind.ImplicitByRef, VBObjectType.TypeInfo));
        }

        foreach (var parameter in parameters)
        {
            var name = StdLibNames.ParameterName(parameter.Name);
            if (parameter.GetCustomAttribute<ParamArrayAttribute>() is not null)
            {
                builder.Add(new ParamArrayParameterSymbol(
                    _workspaceRoot, memberUri, name, SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal));
                continue;
            }

            var byRef = parameter.ParameterType.IsByRef;
            var declared = byRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
            builder.Add(new VBParameterSymbol(
                _workspaceRoot, memberUri, name, SourceRange.Empty, SourceRange.Empty,
                byRef ? ParameterKind.ExplicitByRef : ParameterKind.ExplicitByVal,
                DeclaredTypeOf(declared, method, enumTypes, classTypes),
                parameter.IsOptional, DefaultValueOf(parameter)));
        }

        return builder.ToImmutable();
    }

    private static VBType ReturnTypeOf(
        MethodInfo method, StdLibMemberAttribute? attribute,
        Dictionary<Type, VBEnumType> enumTypes, Dictionary<Type, VBClassType> classTypes)
    {
        // a class or an enumeration is not expressible as the result's type argument — every value of
        // one is an object reference or a Long — so the declaration names it here instead.
        if (attribute?.ReturnType is { } named)
        {
            return DeclaredTypeOf(named, method, enumTypes, classTypes);
        }

        // RuntimeSemanticsEvaluationResult<T> states a return type; the non-generic one states none,
        // which is what makes the member a Sub (or a Property Let / Property Set).
        var result = method.ReturnType;
        return result.IsGenericType && result.GetGenericTypeDefinition() == typeof(RuntimeSemanticsEvaluationResult<>)
            ? DeclaredTypeOf(result.GetGenericArguments()[0], method, enumTypes, classTypes)
            : VBVoidType.TypeInfo;
    }

    private static VBType DeclaredTypeOf(
        Type declared, MethodInfo method,
        Dictionary<Type, VBEnumType> enumTypes, Dictionary<Type, VBClassType> classTypes)
    {
        if (enumTypes.TryGetValue(declared, out var enumType))
        {
            return enumType;
        }

        if (classTypes.TryGetValue(declared, out var classType))
        {
            return classType;
        }

        if (IntrinsicVBTypes.TryResolveValueType(Nullable.GetUnderlyingType(declared) ?? declared, out var intrinsic))
        {
            return intrinsic;
        }

        throw new InvalidOperationException(
            $"'{declared.Name}' in '{method.DeclaringType?.Name}.{method.Name}' names no VBA type: a standard-library " +
            $"declaration states one with a {nameof(VBTypedValue)} implementation, or with a marked enumeration or class.");
    }

    // only an enumeration constant is expressible as a C# default, and it is the only kind of
    // <default-value> clause the standard library has. Everything else optional is `= default`, which
    // is no clause at all: an unmapped argument then takes the declared type's own default value.
    private static VBTypedValue? DefaultValueOf(ParameterInfo parameter)
        => parameter is { IsOptional: true, DefaultValue: { } value } && parameter.ParameterType.IsEnum
            ? new VBLongValue(Convert.ToInt32(value))
            : null;
}
