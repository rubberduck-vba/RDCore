using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Immutable;
using System.Globalization;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// Reads the symbols that libraries declare off their descriptions (<see cref="LibraryDescription"/>), as <see cref="StdLibSymbolReader"/> reads those of the
/// standard library off its declarations.
/// </summary>
/// <remarks>
/// <para>
/// A library is a project of its own, named for the library, and everything it declares says which (<see cref="SymbolProperties.Library"/>): that is what lets
/// <c>Excel.Worksheet</c> name the library's class and nothing else of the name, and what keeps one library's types from standing in for another's. What it
/// declares is a class module for each class, with its members and events, and for each enumeration a symbol and a constant for each member. The enumerations
/// hang off a synthetic module of the library that no source can name (<c>_Excel</c>), which is what makes both the enumeration and its constants names of
/// the project (<strong>MS-VBAL §5.2.3.4</strong>), as the standard library's are.
/// </para>
/// <para>
/// A member's code is not the workspace's, so none of it has an instruction list: each carries the key of what it stands for
/// (<see cref="SymbolProperties.ExternalTarget"/>), <c>Library.Class.Member/kind</c> with the kind one of <c>method</c>, <c>get</c>, <c>let</c> and <c>set</c>,
/// and a call to it is dispatched to whatever can reach the library.
/// </para>
/// <para>
/// The types of a library name one another - a worksheet has ranges, and a range has a worksheet - so a class cannot always be read after every class it names.
/// A class is read after those it names whenever it can be, and where two name each other the one that is read second names the first as a type that is
/// resolved by its name when it is used (<see cref="VBUnresolvedType"/>), as a workspace's classes that name one another are.
/// </para>
/// </remarks>
/// <param name="workspaceRoot">
/// The workspace the symbols are addressed under. Library symbols are not <em>of</em> the workspace, but every <see cref="Symbol"/> is addressed relative to
/// one, so they share its root.
/// </param>
/// <param name="is64Bit">The pointer width of the environment: <c>LongPtr</c> is a different type in each.</param>
public sealed class LibrarySymbolReader(Uri workspaceRoot, bool is64Bit = true)
{
    /// <summary>
    /// Reads the symbols of libraries.
    /// </summary>
    /// <param name="libraries">The libraries, each after those it depends on (<see cref="ReferencedLibraries.Libraries"/>).</param>
    /// <param name="priorities">
    /// The precedence of each library (<see cref="ReferencedLibraries.Priorities"/>), which every symbol that is a name of the project carries
    /// (<see cref="SymbolProperties.LibraryPriority"/>). The position of a library among the others when omitted.
    /// </param>
    public ImmutableArray<Symbol> Read(IReadOnlyList<LibraryDescription> libraries, IReadOnlyDictionary<string, int>? priorities = null)
    {
        var symbols = ImmutableArray.CreateBuilder<Symbol>();
        var enums = new Dictionary<(string Library, string Name), VBEnumType>(TypeKeyComparer.Instance);
        var classes = new Dictionary<(string Library, string Name), VBClassType>(TypeKeyComparer.Instance);
        var declared = libraries.ToDictionary(library => library.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var library in libraries)
        {
            symbols.Add(new VBProjectSymbol(workspaceRoot, library.Name).With(SymbolProperties.Library, library.Name));

            var owner = new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, "_" + library.Name).With(SymbolProperties.Library, library.Name);
            symbols.Add(owner);

            foreach (var description in library.Enums)
            {
                var symbol = ReadEnum(library, description, owner.Uri);
                enums[(library.Name, description.Name)] = (VBEnumType)symbol.ResolvedType;
                symbols.Add(symbol);
                symbols.AddRange(((VBEnumType)symbol.ResolvedType).Members.Select(member => member.With(SymbolProperties.Library, library.Name)));
            }
        }

        var types = new TypeNames(declared, enums, classes, is64Bit);
        foreach (var library in libraries)
        {
            var reading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byName = library.Classes
                .GroupBy(description => description.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            // a class is read after the classes of the library it names; a class that is being read already is a cycle, and names no type until it is used.
            void ReadAfterDependencies(ClassDescription description)
            {
                if (classes.ContainsKey((library.Name, description.Name)) || !reading.Add(description.Name))
                {
                    return;
                }

                foreach (var named in ClassesNamedBy(description).Where(name => byName.ContainsKey(name)))
                {
                    ReadAfterDependencies(byName[named]);
                }

                var symbol = ReadClass(library, description, types);
                classes[(library.Name, description.Name)] = VBClassType.FromClassModule(symbol);
                symbols.Add(symbol);
            }

            foreach (var description in library.Classes)
            {
                ReadAfterDependencies(description);
            }
        }

        // what shadows what is read off the symbols that are names of the project - the library's project, its module of enumerations, the enumerations and their
        // constants, the classes. The members of a class are reached through an object, and no name of the project is one of them.
        var rankOf = priorities ?? libraries.Select((library, index) => (library.Name, Rank: index + 1)).ToDictionary(entry => entry.Name, entry => entry.Rank, StringComparer.OrdinalIgnoreCase);
        return [.. symbols.Select(symbol => symbol.GetProperty(SymbolProperties.Library) is { } library && rankOf.TryGetValue(library, out var rank)
            ? symbol.With(SymbolProperties.LibraryPriority, rank)
            : symbol)];
    }

    // the names, without the library, of the types of this library that a class declares its members and events as.
    private static IEnumerable<string> ClassesNamedBy(ClassDescription description)
        => description.Members.SelectMany(member => member.Parameters.Select(parameter => parameter.Type).Append(member.Type))
            .Concat(description.Events.SelectMany(declaredEvent => declaredEvent.Parameters.Select(parameter => parameter.Type)))
            .OfType<string>()
            .Select(type => type.EndsWith("()", StringComparison.Ordinal) ? type[..^2] : type)
            .Where(type => type.Length > 0 && !type.Contains('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private VBEnumMemberSymbol ReadEnum(LibraryDescription library, EnumDescription description, Uri ownerUri)
    {
        var symbol = new VBEnumMemberSymbol(
            workspaceRoot, ownerUri, description.Name, ScopeKind.Module, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        var constants = description.Members
            .Select(member => (VBEnumConstMemberSymbol)new VBEnumConstMemberSymbol(
                workspaceRoot, symbol.Uri, member.Name, ScopeKind.Module, SymbolKindExt.EnumMember, SourceRange.Empty, SourceRange.Empty)
                .With(SymbolProperties.EnumValue, member.Value))
            .ToArray();

        return (VBEnumMemberSymbol)(symbol with { ResolvedType = new VBEnumType(symbol, constants, description.IsHidden) }).With(SymbolProperties.Library, library.Name);
    }

    private VBClassModuleSymbol ReadClass(LibraryDescription library, ClassDescription description, TypeNames types)
    {
        // a class belongs to the project of its library, and is identified as that project's: two libraries can declare a class of one name (Excel's Application
        // and the editor's), and a symbol is known by its Uri alone, so they could not be told apart as the workspace's own are.
        var symbol = (VBClassModuleSymbol)new VBClassModuleSymbol(workspaceRoot, new VBProjectSymbol(workspaceRoot, library.Name).Uri, description.Name)
            .With(SymbolProperties.Library, library.Name)
            .With(SymbolProperties.Creatable, description.IsCreatable)
            // a library's classes are the host's, and a host adds members to its objects that the library does not declare.
            .With(SymbolProperties.Extensible, true);
        if (description.ProgId is { Length: > 0 } progId)
        {
            symbol = (VBClassModuleSymbol)symbol.With(SymbolProperties.ProgId, progId);
        }

        ImmutableArray<VBTypeMemberSymbol> members =
        [
            .. description.Members.Select(member => ReadMember(library, description, member, symbol.Uri, types)),
            .. description.Events.Select(declaredEvent => ReadEvent(library, declaredEvent, symbol.Uri, types)),
        ];

        symbol = symbol with { Members = members };
        return symbol with { DefaultInterfaceMembers = VBClassType.FromClassModule(symbol).Members };
    }

    private VBTypeMemberSymbol ReadMember(
        LibraryDescription library, ClassDescription owner, MemberDescription description, Uri ownerUri, TypeNames types)
    {
        var returnType = description.Type is { Length: > 0 } type ? types.Resolve(type, library) : VBVoidType.TypeInfo;

        Symbol member = description.Kind switch
        {
            MemberKind.PropertyGet => new VBPropertyGetMemberSymbol(
                workspaceRoot, ownerUri, ScopeKind.Instance, description.Name, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public) { ResolvedType = returnType },
            MemberKind.PropertyLet => new VBPropertyLetMemberSymbol(
                workspaceRoot, ownerUri, description.Name, ScopeKind.Instance, SymbolKindExt.Property, VBVoidType.TypeInfo,
                SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            MemberKind.PropertySet => new VBPropertySetMemberSymbol(
                workspaceRoot, ownerUri, description.Name, ScopeKind.Instance, SymbolKindExt.Property, VBVoidType.TypeInfo,
                SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            _ when returnType is VBVoidType => new VBProcedureMemberSymbol(
                workspaceRoot, ownerUri, description.Name, ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo,
                SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            _ => new VBFunctionMemberSymbol(
                workspaceRoot, ownerUri, description.Name, ScopeKind.Instance, SymbolKindExt.Function, returnType,
                SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
        };

        var parameters = ReadParameters(library, description.Parameters, member.Uri, withMe: true, types);
        member = member switch
        {
            VBReturningMemberSymbol returning => returning with { Parameters = parameters },
            VBProcedureMemberSymbol procedure => procedure with { Parameters = parameters },
            _ => member,
        };

        member = member.With(SymbolProperties.Library, library.Name)
            .With(SymbolProperties.ExternalTarget, ExternalTargetOf(library, owner, description));

        // the default member of a class and its enumerator are found by the id they carry, as those of a workspace class are by their attributes.
        if (description.DispId is 0 or -4)
        {
            member = member.With(SymbolProperties.UserMemId, description.DispId.Value);
        }

        return (VBTypeMemberSymbol)(description.IsHidden
            ? member.With(SymbolProperties.MemberFlags, member.GetProperty(SymbolProperties.MemberFlags) | SymbolProperties.HiddenMemberFlag)
            : member);
    }

    private VBTypeMemberSymbol ReadEvent(LibraryDescription library, EventDescription description, Uri ownerUri, TypeNames types)
    {
        var declaredEvent = new VBEventMemberSymbol(
            workspaceRoot, ownerUri, description.Name, ScopeKind.Instance, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        return (VBTypeMemberSymbol)(declaredEvent with { Parameters = ReadParameters(library, description.Parameters, declaredEvent.Uri, withMe: false, types) })
            .With(SymbolProperties.Library, library.Name);
    }

    /// <summary>
    /// The key a member of a library class carries as <see cref="SymbolProperties.ExternalTarget"/>: <c>Library.Class.Member/kind</c>, with the kind one of
    /// <c>method</c>, <c>get</c>, <c>let</c> and <c>set</c>.
    /// </summary>
    /// <remarks>
    /// The reader defines the format because the reader stamps it, as the standard library's does: a dispatcher that built its own key could disagree with
    /// this one and nothing would say so until a call went to the wrong member. The kind is part of it because the accessors of a property share a name.
    /// </remarks>
    /// <param name="library">The library.</param>
    /// <param name="owner">The class.</param>
    /// <param name="member">The member.</param>
    public static string ExternalTargetOf(LibraryDescription library, ClassDescription owner, MemberDescription member)
        => $"{library.Name}.{owner.Name}.{member.Name}/{member.Kind switch
        {
            MemberKind.PropertyGet => "get",
            MemberKind.PropertyLet => "let",
            MemberKind.PropertySet => "set",
            _ => "method",
        }}";

    private ImmutableArray<VBParameterSymbol> ReadParameters(
        LibraryDescription library, ImmutableArray<ParameterDescription> descriptions, Uri memberUri, bool withMe, TypeNames types)
    {
        var builder = ImmutableArray.CreateBuilder<VBParameterSymbol>(descriptions.Length + 1);

        // an instance member's implicit parameter 0, bound to the object the call is dispatched on.
        if (withMe)
        {
            builder.Add(new VBParameterSymbol(
                workspaceRoot, memberUri, "Me", SourceRange.Empty, SourceRange.Empty, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo));
        }

        foreach (var description in descriptions)
        {
            if (description.IsParamArray)
            {
                builder.Add(new ParamArrayParameterSymbol(
                    workspaceRoot, memberUri, description.Name, SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal));
                continue;
            }

            builder.Add(new VBParameterSymbol(
                workspaceRoot, memberUri, description.Name, SourceRange.Empty, SourceRange.Empty,
                description.IsByVal ? ParameterKind.ExplicitByVal : ParameterKind.ExplicitByRef,
                types.Resolve(description.Type, library), description.IsOptional, DefaultValueOf(description, memberUri)));
        }

        return builder.ToImmutable();
    }

    // MS-VBAL 5.3.1.5 defines a default as a constant expression, which is what the symbol carries; a library's is written as a literal in its description,
    // and a literal node over the value says exactly that - with the member's own address for a location, there being no source to point at. A default that
    // is not a literal this reads is no clause at all: an argument that is left out then takes the declared type's own default.
    private static LiteralExpressionNode? DefaultValueOf(ParameterDescription description, Uri memberUri)
        => description is { IsOptional: true, DefaultValue: { Length: > 0 } text } && LiteralOf(text) is { } value
            ? new LiteralExpressionNode(new SyntaxNodeId(memberUri.AbsolutePath, []), new SourceLocation(memberUri, SourceRange.Empty), value)
            : null;

    private static VBTypedValue? LiteralOf(string text)
    {
        if (text.Equals("True", StringComparison.OrdinalIgnoreCase))
        {
            return VBBooleanValue.True;
        }

        if (text.Equals("False", StringComparison.OrdinalIgnoreCase))
        {
            return VBBooleanValue.False;
        }

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            return new VBStringValue(text[1..^1].Replace("\"\"", "\""));
        }

        if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
        {
            return new VBLongValue(whole);
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? new VBDoubleValue(number) : null;
    }

    // a type is named in a description the way VBA source names it, and meant the way the library means it: its own types first, then the libraries it
    // depends on and qualified by the library they are of.
    private sealed class TypeNames(
        Dictionary<string, LibraryDescription> libraries,
        Dictionary<(string Library, string Name), VBEnumType> enums,
        Dictionary<(string Library, string Name), VBClassType> classes,
        bool is64Bit)
    {
        public VBType Resolve(string name, LibraryDescription from)
        {
            var text = name.Trim();
            if (text.EndsWith("()", StringComparison.Ordinal))
            {
                var element = Resolve(text[..^2], from);
                return element is VBByteType ? VBResizableByteArrayType.TypeInfo : new VBResizableArrayType(element);
            }

            if (text.Equals("LongPtr", StringComparison.OrdinalIgnoreCase))
            {
                return is64Bit ? VBLongPtrType_x64.TypeInfo : VBLongPtrType_x86.TypeInfo;
            }

            if (IntrinsicVBTypes.TryResolve(text, out var intrinsic))
            {
                return intrinsic;
            }

            var split = text.IndexOf('.');
            if (split > 0)
            {
                var qualifier = text[..split];
                return libraries.TryGetValue(qualifier, out var named) && Find(named.Name, text[(split + 1)..]) is { } qualified
                    ? qualified
                    : new VBUnresolvedType(text);
            }

            foreach (var library in new[] { from }.Concat(from.DependsOn.Where(libraries.ContainsKey).Select(dependency => libraries[dependency])))
            {
                if (Find(library.Name, text) is { } found)
                {
                    return found;
                }
            }

            // a class of this library that has not been read yet - or is being read, when two name each other - is named by the way the library names it.
            return new VBUnresolvedType(libraries.ContainsKey(from.Name) && from.Classes.Any(candidate => candidate.Name.Equals(text, StringComparison.OrdinalIgnoreCase))
                ? $"{from.Name}.{text}"
                : text);
        }

        private VBType? Find(string library, string name)
            => enums.TryGetValue((library, name), out var enumType) ? enumType
                : classes.TryGetValue((library, name), out var classType) ? classType
                : null;
    }

    private sealed class TypeKeyComparer : IEqualityComparer<(string Library, string Name)>
    {
        public static TypeKeyComparer Instance { get; } = new();

        public bool Equals((string Library, string Name) x, (string Library, string Name) y)
            => string.Equals(x.Library, y.Library, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Library, string Name) key)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Library), StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name));
    }
}
