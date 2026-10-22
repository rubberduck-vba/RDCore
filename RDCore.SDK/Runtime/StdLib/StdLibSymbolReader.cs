using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
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
/// stated by <see cref="StdLibMemberAttribute"/>; and a parameter's default value, unless it is an enumeration
/// constant, by <see cref="StdLibDefaultAttribute"/>.
/// <para>
/// A constant is declared as the value it is: an enumeration's are its members, and a module's are its
/// <c>static readonly</c> fields of a <see cref="VBTypedValue"/> type, each read as a <c>Public Const</c>
/// of that type. Either way the symbol carries the value, which is what a use site folds.
/// </para>
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
    private readonly bool _is64Bit;

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
    /// <param name="is64Bit">
    /// The pointer width of the environment the symbols are for, which is the one thing a declaration
    /// cannot state: <c>LongPtr</c> is a different type in each (<strong>MS-VBAL §3.3.2</strong>), so
    /// <c>Conversion.CLngPtr</c>'s return type is not knowable from its signature alone. Defaults to
    /// <c>true</c>, matching <c>SdkEnvironmentOptions.Is64Bit</c>'s own default — the one configured
    /// value both the language server and the environment host derive their profile from, so this is
    /// that setting rather than a second opinion about it.
    /// </param>
    public StdLibSymbolReader(Uri workspaceRoot, Uri? globalsOwnerUri = null, bool is64Bit = true)
    {
        _workspaceRoot = workspaceRoot;
        _globalsOwnerUri = globalsOwnerUri ?? workspaceRoot;
        _is64Bit = is64Bit;
    }

    /// <summary>
    /// Reads every standard-library declaration an assembly exports.
    /// </summary>
    /// <param name="assembly">
    /// The assembly to scan. Nothing declares which modules a workspace gets — every VBA project has
    /// the standard library whether or not a <c>.rdproj</c> mentions it (<strong>RD-VBAL §6.1</strong>)
    /// — so the set is whatever carries the markers.
    /// </param>
    /// <summary>
    /// The key that says which declaration a standard-library symbol was read off, carried on the symbol as
    /// <see cref="SymbolProperties.ExternalTarget"/> so that a dispatcher can find the method again.
    /// </summary>
    /// <remarks>
    /// The reader is what defines the format, because the reader is what stamps it - a dispatcher that built
    /// its own key format could disagree with this one and nothing would say so until a call went to the
    /// wrong member.
    /// <para>
    /// Arity is part of the key because a property's two accessors are C# overloads of one name -
    /// <c>Description()</c> and <c>Description(value)</c> - and nothing else about them differs. Two members
    /// of one interface sharing a name <em>and</em> an arity would collide, which a dispatcher building its
    /// table is expected to detect rather than dispatch one of them to the other.
    /// </para>
    /// </remarks>
    /// <param name="declaringType">The <c>IStd*</c> interface declaring the member.</param>
    /// <param name="method">The method declaring the member.</param>
    public static string ExternalTargetOf(Type declaringType, MethodInfo method)
        => $"{declaringType.Name}.{method.Name}/{method.GetParameters().Length}";

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
    /// neither an intrinsic value nor a marked enumeration or class, an element type stated for a
    /// parameter that is not a required ByVal array, a default stated for a parameter that cannot take it, or a
    /// module's public static field that is not a <c>static readonly</c> intrinsic value. That is a mistake in the declaration rather than a
    /// condition to degrade over: the symbol it would produce would be declared as a type nothing can
    /// bind.
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
        // A class that has a member declared as another class - Collection's _NewEnum returns an IEnumVARIANT - binds that class's type, which has to know its
        // own members first: so a class is read after every class it names, whatever order the declarations are in.
        var classTypes = new Dictionary<Type, VBClassType>();
        var marked = Marked<StdLibClassAttribute>(all);
        var reading = new HashSet<Type>();
        void ReadClassAfterItsDependencies(Type declaration)
        {
            if (classTypes.ContainsKey(declaration))
            {
                return;
            }

            if (!reading.Add(declaration))
            {
                throw new InvalidOperationException(
                    $"'{declaration.Name}' names itself through the classes its members are declared as: a class is read after every class it names, and this one has none to be read first.");
            }

            foreach (var named in ClassesNamedBy(declaration, marked))
            {
                ReadClassAfterItsDependencies(named);
            }

            var symbol = ReadClass(declaration, enumTypes, classTypes);
            classTypes[declaration] = VBClassType.FromClassModule(symbol);
            symbols.Add(symbol);
            reading.Remove(declaration);
        }

        foreach (var declaration in marked)
        {
            ReadClassAfterItsDependencies(declaration);
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

    // the other marked classes a class's members are declared as: the type a member returns, or one of its parameters is.
    private static IEnumerable<Type> ClassesNamedBy(Type declaration, Type[] markedClasses)
        => MembersOf(declaration)
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.GetCustomAttribute<StdLibMemberAttribute>()?.ReturnType ?? method.ReturnType))
            .Select(type => type.IsGenericType ? type.GetGenericArguments()[0] : type)
            .Where(type => type != declaration && markedClasses.Contains(type))
            .Distinct();

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
            .Select(field => (VBEnumConstMemberSymbol)new VBEnumConstMemberSymbol(
                _workspaceRoot, symbol.Uri,
                field.GetCustomAttribute<StdLibConstantAttribute>()?.Name ?? StdLibNames.ConstantName(field.Name),
                ScopeKind.Module, SymbolKindExt.EnumMember, SourceRange.Empty, SourceRange.Empty)
                .With(SymbolProperties.EnumValue, Convert.ToInt64(field.GetRawConstantValue())))
            .ToArray();

        return symbol with { ResolvedType = new VBEnumType(symbol, constants) };
    }

    private VBClassModuleSymbol ReadClass(
        Type declaration, Dictionary<Type, VBEnumType> enumTypes, Dictionary<Type, VBClassType> classTypes)
    {
        var attribute = declaration.GetCustomAttribute<StdLibClassAttribute>()!;
        var name = attribute.Name ?? StdLibNames.ClassName(declaration.Name);

        var symbol = (VBClassModuleSymbol)new VBClassModuleSymbol(_workspaceRoot, _workspaceRoot, name)
            .With(SymbolProperties.Creatable, attribute.IsCreatable)
            // a library's class, as any library's is: MS-VBA compiles a member its declaration does not have (`Err.Nope`, `c.Nope` of a Collection) and
            // raises 438 at run time (Excel-verified).
            .With(SymbolProperties.Extensible, true);

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
        var attribute = declaration.GetCustomAttribute<StdLibModuleAttribute>()!;
        var name = attribute.Name ?? StdLibNames.ModuleName(declaration.Name);
        var module = new VBStandardModuleSymbol(_workspaceRoot, _workspaceRoot, name);

        // a standard module's members are separate symbols parented to it, which is what promotes the
        // non-Private ones to the project scope so that `IsNumeric(x)` resolves unqualified - and
        // `vbCrLf`, a constant being a member like any other. A hidden module's procedures are hidden,
        // which is a flag of each member and leaves them resolving just the same.
        var members = MembersOf(declaration)
            .Select(method => ReadMember(method, module.Uri, ScopeKind.Module, enumTypes, classTypes, attribute.IsHidden))
            .Concat(ConstantsOf(declaration).Select(field => ReadConstant(field, module.Uri)))
            .ToArray();

        return (module, members);
    }

    // in declaration order, for the same reason as Marked.
    private static FieldInfo[] ConstantsOf(Type declaration)
        => [.. declaration.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(field => field.MetadataToken)];

    private VBConstantMemberSymbol ReadConstant(FieldInfo field, Uri moduleUri)
    {
        var name = field.GetCustomAttribute<StdLibConstantAttribute>()?.Name ?? StdLibNames.ConstantName(field.Name);

        // the declared value is the constant, so it has to be one: a VBTypedValue that nothing can
        // reassign. A C# const is a primitive, whose VBA type would be a guess - a C# long is a VBA
        // LongLong, so a `const long` meaning a Long says the wrong thing - and a field that is not
        // readonly is not a constant at all.
        if (!field.IsInitOnly || !IntrinsicVBTypes.TryResolveValueType(field.FieldType, out var type))
        {
            throw new InvalidOperationException(
                $"'{field.Name}' in '{field.DeclaringType?.Name}' is not a constant a standard-library module can " +
                $"declare: a module constant is a static readonly field of a {nameof(VBTypedValue)} implementation.");
        }

        // Public: a Const with no access modifier is not visible outside its module (MS-VBAL §5.2.3), and
        // this one has to reach the project scope to resolve unqualified, as the module's procedures do.
        return new VBConstantMemberSymbol(
            _workspaceRoot, moduleUri, name, ScopeKind.Module, type, SourceRange.Empty, SourceRange.Empty,
            AccessModifier.Public, LiteralOf((VBTypedValue)field.GetValue(null)!, moduleUri));
    }

    private Symbol ReadMember(
        MethodInfo method, Uri ownerUri, ScopeKind scope,
        Dictionary<Type, VBEnumType> enumTypes, Dictionary<Type, VBClassType> classTypes, bool ownerIsHidden = false)
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
        member = member switch
        {
            VBReturningMemberSymbol returning => returning with { Parameters = parameters },
            VBProcedureMemberSymbol procedure => procedure with { Parameters = parameters },
            _ => member,
        };

        // the declaration this was read off, carried on the symbol: the code that runs for this member is
        // not the workspace's, so there is no instruction list for it, and this is what an
        // IExternalDispatcher finds the implementation by. Nothing downstream could reconstruct it.
        member = member.With(SymbolProperties.ExternalTarget, ExternalTargetOf(method.DeclaringType!, method));

        // the default member of a class, and its enumeration member, are found by the id they carry - as the attributes of a workspace class say.
        if (attribute is { UserMemId: not StdLibMemberAttribute.NoUserMemId })
        {
            member = member.With(SymbolProperties.UserMemId, attribute.UserMemId);
        }

        return ownerIsHidden || attribute?.IsHidden == true
            ? member.With(SymbolProperties.MemberFlags, member.GetProperty(SymbolProperties.MemberFlags) | SymbolProperties.HiddenMemberFlag)
            : member;
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
            var declared = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
            var byRef = parameter.ParameterType.IsByRef || parameter.GetCustomAttribute<StdLibByRefAttribute>() is not null;
            var array = parameter.GetCustomAttribute<StdLibArrayAttribute>();
            var paramArray = parameter.GetCustomAttribute<ParamArrayAttribute>() is not null;

            // an element type makes the parameter an array of it, which only an array parameter can be - a Byte()
            // being a type of its own, and a ParamArray always a Variant(). And the library declares no optional
            // array and no ByRef one, so nothing models what an omitted one would be or marshals one back.
            if (array is not null && (paramArray || parameter.IsOptional || byRef
                || !(declared == typeof(VBResizableArrayValue)
                    || declared == typeof(VBResizableByteArrayValue) && array.ElementType == typeof(VBByteValue))))
            {
                throw new InvalidOperationException(
                    $"'{name}' in '{method.DeclaringType?.Name}.{method.Name}' states an element type, which only a required " +
                    $"ByVal parameter declared as a {nameof(VBResizableArrayValue)} has, or a {nameof(VBResizableByteArrayValue)} " +
                    $"stating {nameof(VBByteValue)}.");
            }

            // a ParamArray is never omitted: a call that passes it nothing passes it an empty array.
            if (paramArray && parameter.GetCustomAttribute<StdLibDefaultAttribute>() is not null)
            {
                throw new InvalidOperationException(
                    $"'{name}' in '{method.DeclaringType?.Name}.{method.Name}' states a default, which a ParamArray cannot have.");
            }

            // what takes the calling module's Option Compare when omitted is the Compare of a comparing member, declared as the
            // specification declares it: Optional ByVal Compare As VbCompareMethod = vbBinaryCompare.
            var optionCompare = parameter.GetCustomAttribute<StdLibOptionCompareAttribute>() is not null;
            if (optionCompare && (paramArray || byRef || !parameter.IsOptional || declared != typeof(VBCompareMethod)))
            {
                throw new InvalidOperationException(
                    $"'{name}' in '{method.DeclaringType?.Name}.{method.Name}' takes the calling module's Option Compare when omitted, " +
                    $"which only an optional ByVal parameter declared as a {nameof(VBCompareMethod)} can.");
            }

            if (paramArray)
            {
                builder.Add(new ParamArrayParameterSymbol(
                    _workspaceRoot, memberUri, name, SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal));
                continue;
            }

            var declaredType = array is not null
                ? ArrayTypeOf(DeclaredTypeOf(array.ElementType, method, enumTypes, classTypes))
                : DeclaredTypeOf(declared, method, enumTypes, classTypes);
            var symbol = new VBParameterSymbol(
                _workspaceRoot, memberUri, name, SourceRange.Empty, SourceRange.Empty,
                byRef ? ParameterKind.ExplicitByRef : ParameterKind.ExplicitByVal,
                declaredType, parameter.IsOptional, DefaultValueOf(parameter, declaredType, name, method, memberUri));

            // the declared default stays: it is what a caller with no calling module gets.
            if (optionCompare)
            {
                symbol = (VBParameterSymbol)symbol.With(SymbolProperties.DefaultsToOptionCompare, true);
            }

            // a Variant parameter that reads the value of its argument, as MS-VBA compiles it, which its declared type does not say.
            if (parameter.GetCustomAttribute<StdLibValueAttribute>() is { } value)
            {
                if (declared != typeof(VBVariantValue))
                {
                    throw new InvalidOperationException(
                        $"'{name}' in '{method.DeclaringType?.Name}.{method.Name}' states that it reads a value, which only a Variant parameter needs " +
                        "to say: one declared with a value type reads a value of that type.");
                }

                symbol = (VBParameterSymbol)symbol.With(SymbolProperties.ValueDestination, value.Destination);
            }

            builder.Add(symbol);
        }

        return builder.ToImmutable();
    }

    private VBType ReturnTypeOf(
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

    private VBType DeclaredTypeOf(
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

        // LongPtr is the one intrinsic IntrinsicVBTypes deliberately does not resolve, because it is a
        // different type in each pointer width — and the width is the environment's, not the
        // declaration's, which is why the reader is told it rather than reading it off a signature.
        if (declared == typeof(VBLongPtrValue))
        {
            return _is64Bit ? VBLongPtrType_x64.TypeInfo : VBLongPtrType_x86.TypeInfo;
        }

        if (IntrinsicVBTypes.TryResolveValueType(Nullable.GetUnderlyingType(declared) ?? declared, out var intrinsic))
        {
            return intrinsic;
        }

        throw new InvalidOperationException(
            $"'{declared.Name}' in '{method.DeclaringType?.Name}.{method.Name}' names no VBA type: a standard-library " +
            $"declaration states one with a {nameof(VBTypedValue)} implementation, or with a marked enumeration or class.");
    }

    // the array type a Dim declares for the same element type, Byte's included: a Byte() is a type of its own.
    private static VBType ArrayTypeOf(VBType elementType)
        => elementType is VBByteType ? VBResizableByteArrayType.TypeInfo : new VBResizableArrayType(elementType);

    // an enumeration constant is the one <default-value> clause expressible as a C# default, and is read
    // off it. Any other optional is `= default`, which is no clause at all - an unmapped argument then
    // takes the declared type's own default value - unless StdLibDefaultAttribute states one: Split's
    // `Limit As Long = -1`, Filter's `Include As Boolean = True`, which are values of the declared type.
    //
    // MS-VBAL 5.3.1.5 defines a default as a constant expression, which is what the symbol carries; a
    // library parameter's is written in C# rather than in VBA source, and a literal node over the value
    // it already is says exactly that.
    private static ExpressionNode? DefaultValueOf(ParameterInfo parameter, VBType declaredType, string name, MethodInfo method, Uri memberUri)
    {
        if (parameter.GetCustomAttribute<StdLibDefaultAttribute>() is not { } stated)
        {
            return parameter is { IsOptional: true, DefaultValue: { } value } && parameter.ParameterType.IsEnum
                ? LiteralOf(new VBLongValue(Convert.ToInt32(value)), memberUri)
                : null;
        }

        // a required parameter has no default to state, and an enumeration's is its C# default already.
        VBTypedValue? literal = !parameter.IsOptional || parameter.ParameterType.IsEnum ? null : (declaredType, stated.Value) switch
        {
            (VBLongType, int number) => new VBLongValue(number),
            (VBIntegerType, int number) when number is >= short.MinValue and <= short.MaxValue => new VBIntegerValue((short)number),
            (VBBooleanType, bool flag) => new VBBooleanValue(flag),
            (VBStringType, string text) => new VBStringValue(text),
            _ => null,
        };

        return literal is not null
            ? LiteralOf(literal, memberUri)
            : throw new InvalidOperationException(
                $"'{name}' in '{method.DeclaringType?.Name}.{method.Name}' states a default of {stated.Value ?? "null"} " +
                $"({stated.Value?.GetType().Name ?? "no type"}), which it cannot take: {nameof(StdLibDefaultAttribute)} states the default " +
                "of an optional parameter declared as a Long, an Integer, a Boolean or a String, as a value of that type, and an " +
                "enumeration's default is its C# default.");
    }

    // a constant expression written in C# rather than in VBA source - a parameter's default, a module
    // constant's value - is the value it already is, so a literal node over that value is the whole of it:
    // with the uri of the symbol that declares it for a location, there being no source position to point at.
    private static LiteralExpressionNode LiteralOf(VBTypedValue value, Uri declaringUri)
        => new(
            new SyntaxNodeId(declaringUri.AbsolutePath, []),
            new SourceLocation(declaringUri, SourceRange.Empty),
            value);
}
