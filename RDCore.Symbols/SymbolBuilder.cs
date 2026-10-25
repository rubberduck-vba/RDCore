using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Workspace.Forms;
using System.Collections.Immutable;

namespace RDCore.Symbols;

/// <summary>
/// Turns a declaration AST node into the bound <see cref="Symbol"/> it declares. One trivial method
/// per node kind, mirroring <c>DeclarationNodeBuilder</c> on the parsing side. Declared types are
/// resolved through <see cref="ISymbolResolver"/> so the same call path binds real types once a
/// resolver with intrinsic/library/project symbols is available; a type reference that doesn't
/// resolve stays <see cref="VBUnknownType"/>.
/// </summary>
/// <remarks>
/// <paramref name="memberScope"/> is the allocation scope every module-level member is defined in —
/// <see cref="ScopeKind.Module"/> for a standard module, <see cref="ScopeKind.Instance"/> for a
/// class module. Parameters are <see cref="ScopeKind.Local"/> and user-defined-type fields are
/// <see cref="ScopeKind.Instance"/> (reached through an instance of the type), regardless.
/// <para>
/// <paramref name="implicitType"/> is the declared type of a variable, parameter or function whose
/// declaration names none (<strong>MS-VBAL §5.2.3.1.5</strong>) and that no <c>Def&lt;Type&gt;</c> directive
/// of the module covers, which is <see cref="VBVariantType"/>. Omitted, it is <see cref="VBUnknownType"/> — not yet determined, never a wrong answer.
/// <paramref name="typeDefs"/> are the module's <c>Def&lt;Type&gt;</c> directives (<strong>MS-VBAL §5.2.2</strong>): the first letter of the name decides.
/// </para>
/// </remarks>
public sealed class SymbolBuilder(Uri workspaceRoot, Uri moduleUri, ScopeKind memberScope, ISymbolResolver resolver, VBType? implicitType = null, ImmutableArray<TypeDefDirectiveNode> typeDefs = default)
{
    // MS-VBAL 3.3.2 type-declaration characters name a reserved type the resolver can bind.
    private static readonly ImmutableDictionary<string, string> _typeHintNames = new Dictionary<string, string>
    {
        ["%"] = VBTypeNames.VBInteger,
        ["&"] = VBTypeNames.VBLong,
        ["^"] = VBTypeNames.VBLongLong,
        ["!"] = VBTypeNames.VBSingle,
        ["#"] = VBTypeNames.VBDouble,
        ["@"] = VBTypeNames.VBCurrency,
        ["$"] = VBTypeNames.VBString,
    }.ToImmutableDictionary();

    public Symbol BuildProcedure(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBProcedureMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri, includeMe: true) };
    }

    public Symbol BuildFunction(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBFunctionMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Function,
            VBUnknownType.TypeInfo, range, range, node.AccessModifier);
        return symbol with
        {
            ResolvedType = ReturnType(node, symbol.Uri),
            Parameters = BuildParameters(node, symbol.Uri, includeMe: true),
        };
    }

    public Symbol BuildPropertyGet(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBPropertyGetMemberSymbol(
            workspaceRoot, moduleUri, memberScope, node.Name, range, range, node.AccessModifier);
        return symbol with
        {
            ResolvedType = ReturnType(node, symbol.Uri),
            Parameters = BuildParameters(node, symbol.Uri, includeMe: true),
        };
    }

    public Symbol BuildPropertyLet(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBPropertyLetMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Property,
            VBVoidType.TypeInfo, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri, includeMe: true) };
    }

    public Symbol BuildPropertySet(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBPropertySetMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Property,
            VBVoidType.TypeInfo, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri, includeMe: true) };
    }

    public Symbol BuildExternal(ExternalMemberDeclarationNode node)
    {
        var range = RangeOf(node);
        if (node.MemberKind == MemberKind.ExternalFunction)
        {
            var function = new VBExternalFunctionMemberSymbol(
                workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Function,
                VBUnknownType.TypeInfo, range, range, node.AccessModifier, node.IsPtrSafe, node.Library, node.Alias);
            return function with
            {
                ResolvedType = ReturnType(node, function.Uri),
                Parameters = BuildParameters(node, function.Uri),
            };
        }

        var procedure = new VBExternalSubMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, range, range, node.AccessModifier, node.IsPtrSafe, node.Library, node.Alias);
        return procedure with { Parameters = BuildParameters(node, procedure.Uri) };
    }

    public Symbol BuildEvent(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBEventMemberSymbol(workspaceRoot, moduleUri, node.Name, memberScope, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri) };
    }

    public Symbol BuildUserDefinedType(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        return new VBUserDefinedTypeMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, range, range, node.AccessModifier);
    }

    public Symbol BuildEnum(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        return new VBEnumMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, range, range, node.AccessModifier);
    }

    // Enum members parent to the enum symbol, not the module. MS-VBAL 5.2.3.4: a member without an expression is 1 greater than the one
    // before it, and the first is 0 - so each carries the nearest expression at or before it and how far it is past that one.
    public IEnumerable<Symbol> BuildEnumMembers(MemberDeclarationNode enumNode, Uri enumUri)
    {
        ExpressionNode? baseValue = null;
        var offset = 0;
        foreach (var member in enumNode.Children.OfType<ConstantDeclarationNode>())
        {
            if (ConstantExpressionOf(member) is { } expression)
            {
                baseValue = expression;
                offset = 0;
            }

            var range = RangeOf(member);
            yield return new VBEnumConstMemberSymbol(
                workspaceRoot, enumUri, member.Name, memberScope, SymbolKindExt.EnumMember, range, range, new EnumMemberValue(baseValue, offset));
            offset++;
        }
    }

    public Symbol BuildModuleField(VariableDeclarationNode node)
    {
        var range = RangeOf(node);
        var asType = AsTypeOf(node);
        var bounds = node.Children.OfType<ArrayBoundsNode>().FirstOrDefault();
        var type = VariableType(ArrayElementType(node.Name, asType, node.TypeHint, moduleUri), asType, bounds);
        var field = WithArrayBounds(AutoInstantiatedIfDeclaredAsNew(new VBModuleFieldVariableMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, type, range, node.NameRange ?? range, node.AccessModifier), asType), bounds);

        // MS-VBAL §5.2.3.1.2: what makes the procedures named for this variable event handlers.
        return node.IsWithEvents ? field.With(SymbolProperties.WithEvents, true) : field;
    }

    /// <summary>
    /// The member of a form's class that one of its controls is: a <c>Public</c>, read-only property of the control's class - a <c>Property Get</c> and no
    /// <c>Property Let</c> or <c>Set</c> - whose events the form's procedures named for it handle (<see cref="SymbolProperties.DesignerControl"/>).
    /// </summary>
    /// <remarks>
    /// The designer declares it, so no text of the module does: it has no range of its own. A control whose class the designer names by its CLSID only (an
    /// ActiveX control that is not one of the referenced libraries') has a type that is not resolved, as an unknown <c>As</c> type is.
    /// </remarks>
    /// <param name="control">The control, as the form's designer describes it.</param>
    /// <param name="form">The name of the form, which names what hosts it (<see cref="FormObject.TargetOf"/>).</param>
    public Symbol BuildDesignerControl(FormControlDescriptor control, string form)
    {
        var type = control.Class.Name is { } className
            ? ResolveTypeName(className, moduleUri, control.Class.Library)
            : new VBUnresolvedType(control.Class.ToString());
        var property = new VBPropertyGetMemberSymbol(workspaceRoot, moduleUri, memberScope, control.Name, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        // the implicit parameter 0 a library's instance member has: the form the control is reached through, which is what the call is dispatched on.
        return (property with
                {
                    ResolvedType = type,
                    Parameters = [new VBParameterSymbol(workspaceRoot, property.Uri, "Me", SourceRange.Empty, SourceRange.Empty, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo)],
                })
            .With(SymbolProperties.WithEvents, true)
            .With(SymbolProperties.DesignerControl, true)
            .With(SymbolProperties.ExternalTarget, FormObject.TargetOf(form, control.Name, "get"));
    }

    /// <summary>
    /// The members a form's class has besides its controls and its code: those of the VBA form object (<see cref="FormObject"/>: <c>Show</c>, <c>Hide</c>,
    /// <c>Left</c>…), and those of the Microsoft Forms <c>UserForm</c> it hosts (<c>Caption</c>, <c>Controls</c>…), when the project references Microsoft
    /// Forms. They are members of the form's class itself, part of its default interface: <c>Me.Caption</c> is a member of the form whose code it is in, and
    /// <c>Caption</c>, there, is too.
    /// </summary>
    /// <remarks>
    /// Each is declared as the member it is of its own class, with the form for its parent, and dispatched outside the workspace's code
    /// (<see cref="FormObject.TargetOf"/>). A name the form's code or one of its controls declares is not declared again: that one is the member of that name.
    /// </remarks>
    /// <param name="form">The name of the form.</param>
    /// <param name="taken">The names the form's code and its controls declare.</param>
    public IEnumerable<Symbol> BuildFormMembers(string form, IReadOnlySet<string> taken)
    {
        var userForm = VBProjectSymbol.ResolveQualifiedType(resolver, FormDesigner.MSFormsLibrary, FormObject.ClassName, moduleUri).Symbol as VBClassModuleSymbol;
        var inherited = (userForm?.Members ?? []).Concat(FormObject.ClassIn(workspaceRoot).Members)
            .Where(member => member is not VBEventMemberSymbol && !taken.Contains(member.Name));
        foreach (var member in inherited)
        {
            if (Rehome(member) is { } rehomed)
            {
                yield return rehomed.With(SymbolProperties.ExternalTarget, FormObject.TargetOf(form, member.Name, KindOf(member)));
            }
        }
    }

    private static string KindOf(VBTypeMemberSymbol member) => member switch
    {
        VBPropertyGetMemberSymbol => "get",
        VBPropertyLetMemberSymbol => "let",
        VBPropertySetMemberSymbol => "set",
        _ => "method",
    };

    // a library member declared again as a member of the module: the same kind of member, of the same type and parameters, whose parent is the module. A
    // symbol's identity is its parent's, made when it is constructed, so it is built anew; its default-member id and its flags (hidden) go with it.
    private Symbol? Rehome(VBTypeMemberSymbol member)
    {
        Symbol? rehomed = member switch
        {
            VBPropertyGetMemberSymbol => new VBPropertyGetMemberSymbol(
                workspaceRoot, moduleUri, memberScope, member.Name, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public) { ResolvedType = member.ResolvedType },
            VBPropertyLetMemberSymbol => new VBPropertyLetMemberSymbol(
                workspaceRoot, moduleUri, member.Name, memberScope, SymbolKindExt.Property, VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            VBPropertySetMemberSymbol => new VBPropertySetMemberSymbol(
                workspaceRoot, moduleUri, member.Name, memberScope, SymbolKindExt.Property, VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            VBFunctionMemberSymbol => new VBFunctionMemberSymbol(
                workspaceRoot, moduleUri, member.Name, memberScope, SymbolKindExt.Function, member.ResolvedType, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            VBProcedureMemberSymbol => new VBProcedureMemberSymbol(
                workspaceRoot, moduleUri, member.Name, memberScope, SymbolKindExt.Procedure, VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public),
            _ => null,
        };

        var parameters = member switch
        {
            VBReturningMemberSymbol returning => returning.Parameters,
            VBProcedureMemberSymbol procedure => procedure.Parameters,
            _ => [],
        };
        ImmutableArray<VBParameterSymbol> RehomedParameters(Uri owner) => [.. parameters.Select(parameter => parameter is ParamArrayParameterSymbol
            ? new ParamArrayParameterSymbol(workspaceRoot, owner, parameter.Name, SourceRange.Empty, SourceRange.Empty, parameter.ParameterKind)
            : new VBParameterSymbol(workspaceRoot, owner, parameter.Name, SourceRange.Empty, SourceRange.Empty, parameter.ParameterKind, parameter.ResolvedType,
                parameter.IsOptional, parameter.DefaultValue))];
        rehomed = rehomed switch
        {
            VBReturningMemberSymbol returning => returning with { Parameters = RehomedParameters(returning.Uri) },
            VBProcedureMemberSymbol procedure => procedure with { Parameters = RehomedParameters(procedure.Uri) },
            _ => rehomed,
        };

        if (rehomed is not null && member.TryGetProperty(SymbolProperties.UserMemId, out var userMemId))
        {
            rehomed = rehomed.With(SymbolProperties.UserMemId, userMemId);
        }

        return rehomed?.With(SymbolProperties.MemberFlags, member.GetProperty(SymbolProperties.MemberFlags));
    }

    // a fixed-size array is as big as its bounds say, which are constant expressions the type has no room for: they ride the symbol,
    // and whatever allocates its storage reduces them.
    private static Symbol WithArrayBounds(Symbol variable, ArrayBoundsNode? bounds)
        => bounds is { IsResizable: false } ? variable.With(SymbolProperties.ArrayBounds, bounds.Bounds) : variable;

    // MS-VBAL 5.2.3.1.1 / 2.5.1: an <as-auto-object> clause (`As New Foo`) makes the variable it declares - or,
    // for an array, each of its dependent variables - an automatic instantiation variable.
    private static Symbol AutoInstantiatedIfDeclaredAsNew(Symbol variable, AsTypeExpressionNode? asType)
        => asType is { AsAutoObject: true } ? variable.With(SymbolProperties.AutoInstantiated, true) : variable;

    public Symbol BuildConstant(ConstantDeclarationNode node)
    {
        var range = RangeOf(node);
        var type = DeclaredType(AsTypeOf(node), node.TypeHint, moduleUri);
        return new VBConstantMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, type, range, node.NameRange ?? range, node.AccessModifier,
            ConstantExpressionOf(node));
    }

    // MS-VBAL 5.4.3.2: a constant statically evaluates to a value, and has no storage anywhere to read
    // one back from — so the declaration's own expression travels with the symbol and is reduced at the
    // host, where the operators that can reduce it live. The As-type clause is the declaration's other
    // expression-shaped child.
    //
    // Shared with an Optional parameter's `default-value`, which MS-VBAL 5.3.1.5 defines as a constant
    // expression in exactly the same way, on a node of exactly the same shape.
    //
    // The expression is what follows `=`, so it is after the As clause when there is one: a declaration `As` a class or `As Object`
    // also has the type's name among its children, ahead of the clause, which is not an expression of the declaration's value.
    private static ExpressionNode? ConstantExpressionOf(SyntaxNode node)
    {
        var children = node.Children.IsDefault ? [] : node.Children;
        var asClause = children.IndexOf(children.OfType<AsTypeExpressionNode>().FirstOrDefault()!);
        return children.Skip(asClause + 1).OfType<ExpressionNode>().FirstOrDefault(child => child is not AsTypeExpressionNode);
    }

    // A UDT field parents to the enclosing user-defined-type symbol, not the module.
    public Symbol BuildUserDefinedTypeField(MemberDeclarationNode node, Uri userDefinedTypeUri)
    {
        var range = RangeOf(node);
        var asType = AsTypeOf(node);
        // MS-VBAL §5.2.3.3: a field is declared as a variable is, an array included - fixed-size or resizable.
        var bounds = node.Children.OfType<ArrayBoundsNode>().FirstOrDefault();
        var type = VariableType(ArrayElementType(node.Name, asType, typeHint: null, userDefinedTypeUri), asType, bounds);
        return WithArrayBounds(AutoInstantiatedIfDeclaredAsNew(new VBUserDefinedTypeFieldSymbol(
            workspaceRoot, userDefinedTypeUri, node.Name, type, range, range, node.AccessModifier), asType), bounds);
    }

    // includeMe is opt-in per caller: a procedure/function/property body has a Me in scope
    // (MS-VBAL 5.6.11), but an Event or Declare signature never does - it's never invoked with a
    // live instance activation the way a member body is.
    private ImmutableArray<VBParameterSymbol> BuildParameters(MemberDeclarationNode member, Uri memberUri, bool includeMe = false)
    {
        var parameters = member.Children.OfType<ParameterDeclarationNode>().ToArray();
        if (parameters.Length == 0 && !(includeMe && memberScope is ScopeKind.Instance))
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<VBParameterSymbol>(parameters.Length + 1);
        if (includeMe && memberScope is ScopeKind.Instance)
        {
            // an implicit parameter at slot 0, bound to the current object instance
            // (rdcore-me-implicit-parameter-design): Me then resolves exactly like any other
            // parameter, nothing special-cased at the interpreter level. Its declared type is
            // deliberately left as VBObjectType here - InstanceExpressionStaticSemantics resolves the
            // enclosing class's own VBClassType independently, since this symbol is built before the
            // module's Members list exists yet (WorkspaceSymbolResolver.Compose's second pass).
            var range = RangeOf(member);
            builder.Add(new VBParameterSymbol(
                workspaceRoot, memberUri, "Me", range, range, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo));
        }
        foreach (var parameter in parameters)
        {
            var range = RangeOf(parameter);
            builder.Add(parameter.IsParamArray
                ? new ParamArrayParameterSymbol(workspaceRoot, memberUri, parameter.Name, range, range, parameter.ParameterKind)
                : new VBParameterSymbol(
                    workspaceRoot, memberUri, parameter.Name, range, range, parameter.ParameterKind,
                    ParameterType(parameter, memberUri), parameter.IsOptional,
                    DefaultValueOf(parameter)));
        }
        return builder.ToImmutable();
    }

    // MS-VBAL 5.3.1.5: `default-value = "=" constant-expression`, and 5.3.1.10 Let-assigns that value to
    // an unmapped optional parameter's own local. The same shape a Const declaration has, and the same
    // kind of expression, so it travels the same way and the host reduces it the same way: the symbol
    // carries it across instead of leaving the invoker with only the declared type's default, which is
    // what `Optional k As Long = 5` was invoked with, arriving as 0.
    private static ExpressionNode? DefaultValueOf(ParameterDeclarationNode parameter)
        => ConstantExpressionOf(parameter);

    // `Function F() As Long()` returns a resizable array of Long (MS-VBAL 5.3.1.6): the parentheses are on the As clause.
    private VBType ReturnType(MemberDeclarationNode member, Uri memberUri)
    {
        var asType = member.Children.OfType<AsTypeExpressionNode>().FirstOrDefault();
        return asType is { IsArrayDef: true }
            ? ResizableArrayType(ArrayElementType(member.Name, asType, member.TypeHint, memberUri))
            : ImplicitOrDeclaredType(member.Name, asType, member.TypeHint, memberUri);
    }

    // `Items() As Long`: the parentheses are on the parameter, the element type is its As clause's.
    private VBType ParameterType(ParameterDeclarationNode parameter, Uri memberUri)
    {
        var asType = AsTypeOf(parameter);
        return parameter.IsArray || asType is { IsArrayDef: true }
            ? ResizableArrayType(ArrayElementType(parameter.Name, asType, parameter.TypeHint, memberUri))
            : ImplicitOrDeclaredType(parameter.Name, asType, parameter.TypeHint, memberUri);
    }

    private static AsTypeExpressionNode? AsTypeOf(SyntaxNode node)
        => node.Children.OfType<AsTypeExpressionNode>().FirstOrDefault();

    // A declaration that names no type - no As clause, no type-declaration character - has the
    // module's implicit declared type (MS-VBAL 5.2.3.1.5). Not for a Const (its type comes from its
    // value) nor a UDT field (the grammar requires an As clause there).
    private VBType ImplicitOrDeclaredType(string name, AsTypeExpressionNode? asType, string? typeHint, Uri handle)
        => asType is null && typeHint is null
            ? ImplicitTypeOf(name, handle)
            : DeclaredType(asType, typeHint, handle);

    // The directive that covers the first letter of the name gives it its type; where several do (a static error, MS-VBAL 5.2.2) the first one written wins.
    private VBType ImplicitTypeOf(string name, Uri handle)
    {
        var directive = typeDefs.IsDefault ? null : TypeDefDirectiveNode.Covering(typeDefs, name);
        return directive?.TypeName is { } typeName
            ? ResolveTypeName(typeName, handle)
            : implicitType ?? VBUnknownType.TypeInfo;
    }

    // A declared type is a name the resolver binds from the given scope, optionally qualified by a
    // project name (MS-VBAL 5.6.4's type binding context - see VBProjectSymbol.ResolveQualifiedType). An
    // array definition needs more than a name lookup, so it is left unresolved for a later semantic pass.
    private VBType DeclaredType(AsTypeExpressionNode? asType, string? typeHint, Uri handle)
    {
        string? typeName = null;
        if (asType is { IsArrayDef: false })
        {
            typeName = asType.TypeName;
        }
        else if (asType is null && typeHint is not null && _typeHintNames.TryGetValue(typeHint, out var hintName))
        {
            typeName = hintName;
        }

        if (typeName is null)
        {
            return VBUnknownType.TypeInfo;
        }

        var resolved = ResolveTypeName(typeName, handle, asType?.QualifierName);

        // MS-VBAL 5.2.3.1.4: `String * n` is the type String*n. A length that is not a number nor a constant this can read is the type as written,
        // which a later pass is the one to report.
        return asType?.FixedLength is { } length && resolved is VBStringType && FixedLengthOf(length, handle) is { } characters
            ? new VBFixedStringType(characters)
            : resolved;
    }

    // MS-VBAL 5.2.3.1.4: "the data value of its <INTEGER> element or the data value referenced by its <constant-name> Let-coerced to declared type Long" -
    // a number as written, a constant whose value is a literal, or a member of an enumeration whose value is one. A length no fixed-length string can be
    // (0, 70000) is no type at all: the host reports it (DeclaredSizeStaticSemantics), and the declaration is the variable-length String it was written with.
    private int? FixedLengthOf(string length, Uri handle)
    {
        var characters = VBFixedStringType.TryParseLengthLiteral(length, out var written) ? written : ConstantLengthOf(length, handle);
        return characters is { } valid && VBFixedStringType.IsValidLength(valid) ? (int)valid : null;
    }

    private long? ConstantLengthOf(string length, Uri handle)
    {
        var value = resolver.ResolveValue(length, ScopeKind.Local, handle).Symbol switch
        {
            VBConstantMemberSymbol { Value: LiteralExpressionNode literal } => literal.StaticValue.RuntimeValue.BoxedValue,
            VBLocalConstantSymbol { Value: LiteralExpressionNode literal } => literal.StaticValue.RuntimeValue.BoxedValue,
            VBEnumConstMemberSymbol { DeclaredValue: null } => 0,
            VBEnumConstMemberSymbol { DeclaredValue: { BaseValue: null, Offset: var offset } } => offset,
            VBEnumConstMemberSymbol { DeclaredValue: { BaseValue: LiteralExpressionNode literal, Offset: var offset } }
                when literal.StaticValue.RuntimeValue.BoxedValue is { } number && IsNumber(number)
                => Convert.ToDouble(number, System.Globalization.CultureInfo.InvariantCulture) + offset,
            _ => null,
        };

        // Let-coerced to Long: a fraction rounds half to even (Const K = 2.5 is 2), and a String that reads as a number is that number (Const K = "3" is 3),
        // as MS-VBA has it (Excel-verified, oracle\s29 F05, F06).
        return value switch
        {
            _ when value is not null && IsNumber(value) => RoundToLong(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)),
            string text when double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
                => RoundToLong(number),
            _ => null,
        };
    }

    private static bool IsNumber(object value) => value is byte or short or int or long or float or double or decimal;

    private static long? RoundToLong(double value)
        => Math.Round(value, MidpointRounding.ToEven) is var rounded && rounded >= long.MinValue && rounded <= long.MaxValue ? (long)rounded : null;

    // Binds a reserved/declared type name through the resolver, optionally qualified by a project name
    // (MS-VBAL 5.6.4); an unresolved name stays Unknown, and remembers the name (VBUnresolvedType). A resolved user-defined type, enum or class
    // module is a symbol carrying no VBType of its own, so build one.
    private VBType ResolveTypeName(string typeName, Uri handle, string? qualifier = null)
        => VBProjectSymbol.ResolveQualifiedType(resolver, qualifier, typeName, handle).Symbol switch
        {
            VBUserDefinedTypeMemberSymbol udt => new VBUserDefinedType(udt, udt.Members),
            VBEnumMemberSymbol enumType => new VBEnumType(enumType, members: null),
            VBClassModuleSymbol classModule => VBClassType.Of(classModule),
            BoundTypedSymbol bound => bound.ResolvedType,
            UnboundTypedSymbol unbound => unbound.ResolvedType,
            _ => new VBUnresolvedType(qualifier is null ? typeName : $"{qualifier}.{typeName}"),
        };

    // Procedure-local Dim/Static/Const declarations, plus the symbols a ReDim or a reference to an
    // undeclared name introduces. Locals parent to the procedure symbol (MS-VBAL 5.4.3.1-3),
    // mirroring how enum members / UDT fields parent to their declaration.
    // <paramref name="outerScopeNames"/> are the names already visible from outside the body — the
    // procedure's parameters and this module's fields/consts — against which a ReDim target is a
    // re-dimension rather than an implicit declaration. <paramref name="directives"/> decides whether
    // this module has an implicit declaration mode at all.
    public IEnumerable<Symbol> BuildLocals(
        MemberDeclarationNode member, Uri procedureUri, IReadOnlySet<string> outerScopeNames,
        ModuleDirectives directives = default, bool withImplicitDeclarations = true,
        ImplicitDeclarationScope implicitScope = ImplicitDeclarationScope.Procedure)
    {
        var results = new List<Symbol>();
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // a declaration nested in an If/ElseIf/Else branch still parents to the procedure symbol —
        // VBA has no block scope (MS-VBAL 5.4.3.1-3) — so locals are hunted for through the whole
        // body, not just the member's immediate children.
        var body = member.Children.SelectMany(DescendantsAndSelf).ToArray();

        // pass 1 — explicit declarations. order-independent: VBA hoists them, so a name declared
        // anywhere in the body is in scope for the whole procedure.
        foreach (var child in body)
        {
            switch (child)
            {
                case VariableDeclarationNode local:
                    results.Add(BuildLocalVariable(local, procedureUri));
                    declared.Add(local.Name);
                    break;

                case ConstantDeclarationNode { ConstKind: ConstKind.Local } local:
                    results.Add(BuildLocalConstant(local, procedureUri));
                    declared.Add(local.Name);
                    break;
            }
        }

        // pass 2 — ReDim. an unqualified target that matches nothing implicitly declares a
        // dynamic-array local (MS-VBAL 5.4.3.3: "If the name has no matches"; legal under Option
        // Explicit). What it matches is a variable or a constant, of another module or of a library
        // too, and the result of the function the ReDim is in: a ReDim of another module's
        // `Public Items() As Long` re-dimensions that array. A procedure, a module or a project is no
        // match - `ReDim Left(1)` declares an array that hides the function (MS-VBA, Excel-verified,
        // oracle\s29 R01-R11, E01-E16).
        foreach (var redim in body.OfType<RedimDeclarationNode>())
        {
            if (!redim.IsSimpleName || outerScopeNames.Contains(redim.Name) || declared.Contains(redim.Name)
                || redim.Target is SimpleNameExpressionNode target && MatchesARedimTarget(target, procedureUri))
            {
                continue;
            }

            declared.Add(redim.Name);
            results.Add(BuildRedimLocal(redim, procedureUri));
        }

        // pass 3 — implicit declarations. MS-VBAL 5.6.10: a simple name expression in the default
        // binding context that matches nothing on any tier implicitly declares a local variable in the
        // current procedure, "as if by a local variable declaration statement immediately preceding
        // this statement" — so with an implicit type, which is Variant unless a Def<Type> directive
        // says otherwise. A module that declares Option Explicit has no implicit declaration mode at
        // all: there the same expression is a compile error, which
        // SimpleNameExpressionStaticSemantics reports.
        if (!directives.Explicit && withImplicitDeclarations)
        {
            foreach (var reference in body.SelectMany(ValueContextSimpleNames))
            {
                if (outerScopeNames.Contains(reference.IdentifierName) || declared.Contains(reference.IdentifierName))
                {
                    continue;
                }

                // "if all tiers have no matches". An ambiguous or otherwise erroneous lookup IS a
                // match — one the reference has to qualify — so only a genuinely unbound name declares
                // anything. NOTE the resolver's universe is smaller than VBA's until the library declares
                // all of itself: Len and vbCrLf resolve, but a standard-library name it does not declare
                // yet (vbBlue) is unbound here and becomes an implicit Variant rather than resolving.
                // That is this rule applied to an incomplete name universe, not a different rule; it
                // corrects itself as the universe grows. A typed name (s$, Left$) is looked up as every
                // other lookup of a simple name looks it up, so it matches what the expression itself
                // binds — and when it matches nothing, what it declares is its bare name.
                var resolved = resolver.ResolveValue(reference, ScopeKind.Local, procedureUri);
                if (!resolved.IsUnbound && !IsOwnLocal(resolved, procedureUri) && !IsOwnModuleVariable(resolved))
                {
                    continue;
                }

                declared.Add(reference.IdentifierName);

                // a name is the same name however it is spelled (MS-VBAL 3.3.5): the variable an earlier pass declared is declared again under the
                // spelling it has, so that its identity - and its value, which a session keeps across a redefinition - is the one it had.
                var name = resolved.Symbol?.Name;

                // where the variable lives is the environment's to say (ImplicitDeclarationScope). Declared at module
                // level it is one variable however many procedures refer to the name - each of them declares it, and
                // the provider fuses the declarations that share an identity.
                results.Add(implicitScope == ImplicitDeclarationScope.Module
                    ? BuildImplicitModuleVariable(reference, name)
                    : BuildImplicitLocal(reference, procedureUri, name));
            }
        }

        // MS-VBAL §5.3.1.2: in a procedure declared Static every local variable has module extent, as if it were declared Static itself - the
        // implicit ones and the ReDim's too. A constant is not a variable, and a parameter is not one of these.
        return member.IsStatic
            ? results.Select(symbol => symbol is VBLocalVariableSymbol { IsStatic: false } local and not VBParameterSymbol ? local with { IsStatic = true } : symbol).ToList()
            : results;
    }

    // A resolver is composed over the symbols an earlier extraction pass produced, and that pass
    // declares implicit locals too - so a local this method declared last time round resolves now, and
    // would suppress its own re-declaration, leaving the final symbol set without it. A match that is
    // this procedure's own local is therefore no match at all: its parameters and explicit declarations
    // were ruled out above, so whatever is left can only be an earlier pass's own output.
    //
    // This is not the same guard as withImplicitDeclarations, which keeps the FIRST pass - the one whose
    // resolver knows only the intrinsics - from inventing locals for names it could never resolve.
    private static bool IsOwnLocal(SymbolResolutionResult resolved, Uri procedureUri)
        => resolved.Symbol is VBLocalVariableSymbol local
            && local.ParentUri.AbsoluteUri == procedureUri.AbsoluteUri;

    // The same trap one level up: with ImplicitDeclarationScope.Module the variable an earlier pass declared is this
    // module's, and a pass over a resolver that holds it would find a match and declare nothing. Only a variable that
    // was declared implicitly is an earlier pass's own output - one the source declares is a real match.
    private bool IsOwnModuleVariable(SymbolResolutionResult resolved)
        => resolved.Symbol is VBModuleFieldVariableMemberSymbol variable
            && variable.GetProperty(SymbolProperties.ImplicitlyDeclared)
            && variable.ParentUri.AbsoluteUri == moduleUri.AbsoluteUri;

    // what a ReDim's target matches (MS-VBAL §5.4.3.3), as MS-VBA has it: a variable or a constant of any module or library, a member of an enumeration,
    // the result of the function or the Property Get the ReDim is in; and a name that is ambiguous, which the ReDim has to qualify. The local an earlier pass
    // declared for the ReDim, or an implicit one, is that pass's output: what else the name matches is looked up from the module, past the procedure's locals.
    private bool MatchesARedimTarget(SimpleNameExpressionNode target, Uri procedureUri)
    {
        var resolved = resolver.ResolveValue(target, ScopeKind.Local, procedureUri);
        if (IsOwnLocal(resolved, procedureUri))
        {
            resolved = resolver.ResolveValue(target, ScopeKind.Local, moduleUri);
        }

        return resolved switch
        {
            { IsError: true } => true,
            { Symbol: null } => false,
            _ when IsOwnModuleVariable(resolved) => false,
            { Symbol: var symbol } when symbol.Uri.AbsoluteUri == procedureUri.AbsoluteUri => symbol is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol,
            { Symbol: VBModuleFieldVariableMemberSymbol or VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol } => true,
            _ => false,
        };
    }

    /// <summary>
    /// The simple name expressions of <paramref name="statement"/>, and of the statements in its body, that sit in the <em>default</em> binding context
    /// (<strong>MS-VBAL §5.6.10</strong>) — the references an implicit declaration can come from, found as a procedure's are.
    /// </summary>
    /// <remarks>
    /// What a statement run where a program waits declares, when it names what the procedure does not: the same names a procedure's own
    /// body would have declared, so that the immediate window and the code agree on what is a variable.
    /// </remarks>
    /// <param name="statement">The statement.</param>
    public static IEnumerable<SimpleNameExpressionNode> ImplicitDeclarationCandidates(SyntaxNode statement)
        => DescendantsAndSelf(statement).SelectMany(ValueContextSimpleNames);

    /// <summary>
    /// The simple name expressions of <paramref name="node"/> that sit in the <em>default</em> binding
    /// context (<strong>MS-VBAL §5.6.10</strong>) — the only ones an implicit declaration can come
    /// from.
    /// </summary>
    /// <remarks>
    /// Three kinds of name are deliberately not among them. A name in the <em>type</em> binding
    /// context (<c>New Foo</c>, <c>TypeOf x Is Foo</c>) names a type, never a variable; an <c>As</c>
    /// clause carries its type name as a string rather than an expression, so it never arrives here at
    /// all. A statement label (<c>GoTo Done</c>, <c>Resume Done</c>) is not a name expression either,
    /// however much it looks like one. And the member half of a member or dictionary access
    /// (<c>Debug.Print</c>'s <c>Print</c>) is resolved against the owner's type, not against the
    /// enclosing scope — only the owner is a simple name in this sense.
    /// <para>
    /// Like <c>DescendantsAndSelf</c>, this grows with the AST: a new node type that carries an
    /// expression the walker does not know to skip would have its names treated as value references.
    /// </para>
    /// </remarks>
    private static IEnumerable<SimpleNameExpressionNode> ValueContextSimpleNames(SyntaxNode node)
    {
        switch (node)
        {
            case SimpleNameExpressionNode simpleName:
                yield return simpleName;
                yield break;

            // the member is resolved against the owner's type; the owner is the value reference.
            case MemberAccessExpressionNode { Owner: { } accessOwner }:
                foreach (var name in ValueContextSimpleNames(accessOwner))
                {
                    yield return name;
                }
                yield break;

            case DictionaryAccessExpressionNode { Owner: { } dictionaryOwner }:
                foreach (var name in ValueContextSimpleNames(dictionaryOwner))
                {
                    yield return name;
                }
                yield break;

            // a with-relative access has no owner to walk, and a type name or a label is not a value.
            case MemberAccessExpressionNode:
            case DictionaryAccessExpressionNode:
            case NewExpressionNode:
            case GoToStatementNode:
            case GoSubStatementNode:
            case ResumeStatementNode:
                yield break;

            case TypeOfIsExpressionNode typeOfIs:
                foreach (var name in ValueContextSimpleNames(typeOfIs.Operand))
                {
                    yield return name;
                }
                yield break;

            // the selector is evaluated; the labels are branch targets.
            case OnGoToStatementNode onGoTo:
                foreach (var name in ValueContextSimpleNames(onGoTo.Selector))
                {
                    yield return name;
                }
                yield break;

            case OnGoSubStatementNode onGoSub:
                foreach (var name in ValueContextSimpleNames(onGoSub.Selector))
                {
                    yield return name;
                }
                yield break;

            default:
                foreach (var name in node.Children.SelectMany(ValueContextSimpleNames))
                {
                    yield return name;
                }
                yield break;
        }
    }

    // yields a node and, for the statement shapes that carry a nested body today (If/ElseIf/Else,
    // single-line If/Else, While, the 5 Do...Loop shapes, For, For Each, Select Case/Case Else, With),
    // everything reachable inside it — recursively, so a construct nested inside another's branch is
    // still found. Grows as more statement-body node types come online; a new body-bearing type with no
    // case here silently drops every local declared inside it from the symbol table (found in practice
    // for InlineIfStatementNode — see adversarial review PRs #208-224, item 2).
    private static IEnumerable<SyntaxNode> DescendantsAndSelf(SyntaxNode node)
    {
        yield return node;

        switch (node)
        {
            case IfBlockStatementNode ifBlock:
                foreach (var descendant in ifBlock.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                foreach (var elseIfBlock in ifBlock.ElseIfBlocks)
                {
                    foreach (var descendant in DescendantsAndSelf(elseIfBlock))
                    {
                        yield return descendant;
                    }
                }
                if (ifBlock.ElseBlock is { } elseBlock)
                {
                    foreach (var descendant in DescendantsAndSelf(elseBlock))
                    {
                        yield return descendant;
                    }
                }
                break;

            case ElseIfBlockStatementNode elseIfBlockNode:
                foreach (var descendant in elseIfBlockNode.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case ElseBlockStatementNode elseBlockNode:
                foreach (var descendant in elseBlockNode.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case WhileWendStatementNode whileWend:
                foreach (var descendant in whileWend.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case DoLoopStatementNode doLoop:
                foreach (var descendant in doLoop.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case DoWhileLoopStatementNode doWhileLoop:
                foreach (var descendant in doWhileLoop.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case DoUntilLoopStatementNode doUntilLoop:
                foreach (var descendant in doUntilLoop.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case DoLoopWhileStatementNode doLoopWhile:
                foreach (var descendant in doLoopWhile.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case DoLoopUntilStatementNode doLoopUntil:
                foreach (var descendant in doLoopUntil.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case ForStatementNode forStatement:
                foreach (var descendant in forStatement.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case ForEachStatementNode forEachStatement:
                foreach (var descendant in forEachStatement.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case SelectCaseStatementNode selectCase:
                foreach (var caseBlock in selectCase.CaseExpressionBlocks)
                {
                    foreach (var descendant in caseBlock.Block.Children.SelectMany(DescendantsAndSelf))
                    {
                        yield return descendant;
                    }
                }
                if (selectCase.CaseElseBlock is { } caseElse)
                {
                    foreach (var descendant in caseElse.Body.Children.SelectMany(DescendantsAndSelf))
                    {
                        yield return descendant;
                    }
                }
                break;

            case WithStatementNode withStatement:
                foreach (var descendant in withStatement.Body.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                break;

            case InlineIfStatementNode inlineIf:
                foreach (var descendant in inlineIf.ThenBody.Children.SelectMany(DescendantsAndSelf))
                {
                    yield return descendant;
                }
                if (inlineIf.ElseBody is { } elseBody)
                {
                    foreach (var descendant in elseBody.Children.SelectMany(DescendantsAndSelf))
                    {
                        yield return descendant;
                    }
                }
                break;
        }
    }

    public Symbol BuildLocalVariable(VariableDeclarationNode node, Uri procedureUri)
    {
        var range = RangeOf(node);
        var asType = AsTypeOf(node);
        var elementType = ArrayElementType(node.Name, asType, node.TypeHint, procedureUri);
        var bounds = node.Children.OfType<ArrayBoundsNode>().FirstOrDefault();
        var type = VariableType(elementType, asType, bounds);
        return WithArrayBounds(AutoInstantiatedIfDeclaredAsNew(new VBLocalVariableSymbol(
            workspaceRoot, procedureUri, node.Name, ScopeKind.Local, range, node.NameRange ?? range,
            IsStatic: node.IsStatic, ResolvedType: type), asType), bounds);
    }

    public Symbol BuildLocalConstant(ConstantDeclarationNode node, Uri procedureUri)
    {
        var range = RangeOf(node);
        var type = DeclaredType(AsTypeOf(node), node.TypeHint, procedureUri);
        return new VBLocalConstantSymbol(workspaceRoot, procedureUri, node.Name, range, node.NameRange ?? range, type, ConstantExpressionOf(node));
    }

    /// <summary>
    /// The local variable a reference to an undeclared name implicitly declares
    /// (<strong>MS-VBAL §5.6.10</strong>).
    /// </summary>
    /// <remarks>
    /// The spec's own wording is "as if by a local variable declaration statement ... with a
    /// &lt;variable-declaration-list&gt; containing a single &lt;variable-dcl&gt; element consisting of
    /// the text of &lt;name&gt;" — a declaration with no <c>As</c> clause and no type-declaration
    /// character, whose type is therefore the module's implicit type. The reference's own location is
    /// the declaration site, because it is the declaration: there is nowhere else to point.
    /// </remarks>
    /// <param name="node">The simple name expression that declared it.</param>
    /// <param name="procedureUri">The procedure the variable is local to.</param>
    /// <param name="name">
    /// The name it is declared under: the spelling of the variable an earlier pass declared, when this is that one declared again, so that a reference spelled
    /// otherwise (<c>v</c> for <c>V</c>) is the same variable and not a second one. The reference's own spelling when <see langword="null"/>.
    /// </param>
    public Symbol BuildImplicitLocal(SimpleNameExpressionNode node, Uri procedureUri, string? name = null)
    {
        var range = RangeOf(node);
        name ??= node.IdentifierName;
        return new VBLocalVariableSymbol(
            workspaceRoot, procedureUri, name, ScopeKind.Local, range, range,
            ResolvedType: ImplicitOrDeclaredType(name, asType: null, node.TypeHint, procedureUri),
            DeclaredBy: LocalDeclarationKind.Implicit);
    }

    /// <summary>
    /// Builds the module-level variable a reference to an undeclared name declares when the environment's
    /// <see cref="ImplicitDeclarationScope"/> is <see cref="ImplicitDeclarationScope.Module"/>.
    /// </summary>
    /// <remarks>
    /// The same variable as <c>Dim Name</c> at the top of the module would declare — an implicitly typed one, with no
    /// access modifier — marked as implicit so that a later pass can tell it from one the source declares.
    /// </remarks>
    /// <param name="node">The simple name expression that declared it.</param>
    /// <param name="name"><inheritdoc cref="BuildImplicitLocal" path="/param[@name='name']"/></param>
    public Symbol BuildImplicitModuleVariable(SimpleNameExpressionNode node, string? name = null)
    {
        var range = RangeOf(node);
        name ??= node.IdentifierName;
        return new VBModuleFieldVariableMemberSymbol(
                workspaceRoot, moduleUri, name, memberScope,
                ImplicitOrDeclaredType(name, asType: null, node.TypeHint, moduleUri), range, range, AccessModifier.Implicit)
            .With(SymbolProperties.ImplicitlyDeclared, true);
    }

    // A ReDim always targets a dynamic array (MS-VBAL 5.4.3.3); the new dimensions stay unresolved.
    public Symbol BuildRedimLocal(RedimDeclarationNode node, Uri procedureUri)
    {
        var range = RangeOf(node);
        var elementType = ArrayElementType(node.Name, AsTypeOf(node), node.TypeHint, procedureUri);
        return new VBLocalVariableSymbol(
            workspaceRoot, procedureUri, node.Name, ScopeKind.Local, range, range,
            ResolvedType: ResizableArrayType(elementType), DeclaredBy: LocalDeclarationKind.ReDim);
    }

    // The element type for an array declaration also reads the name behind a trailing `()` on the
    // As-clause (`Dim x As Long()`), which DeclaredType leaves unresolved for the scalar case.
    private VBType ArrayElementType(string name, AsTypeExpressionNode? asType, string? typeHint, Uri handle)
        => asType is { IsArrayDef: true, QualifierName: null }
            ? ResolveTypeName(asType.TypeName, handle)
            : ImplicitOrDeclaredType(name, asType, typeHint, handle);

    // MS-VBAL 5.2.3.1 / RD-VBAL 2.5.2.1.2: an array-dim clause with bounds declares a fixed-size
    // array; an empty `()` clause, or a trailing `()` on the As-clause, declares a dynamic array —
    // a dynamic `Byte()` array binds the specialized VBResizableByteArrayType (RD-VBAL 2.4.1.3).
    // Bound evaluation and the Variant default for an omitted type are a later semantic pass's concern.
    private static VBType VariableType(VBType elementType, AsTypeExpressionNode? asType, ArrayBoundsNode? bounds)
    {
        if (bounds is { IsResizable: false })
        {
            return new VBFixedSizeArrayType(elementType);
        }

        if (bounds is not null || asType is { IsArrayDef: true })
        {
            return ResizableArrayType(elementType);
        }

        return elementType;
    }

    // RD-VBAL 2.4.1.3: a dynamic Byte() array binds the specialized VBResizableByteArrayType.
    private static VBType ResizableArrayType(VBType elementType)
        => elementType is VBByteType ? VBResizableByteArrayType.TypeInfo : new VBResizableArrayType(elementType);

    private static SourceRange RangeOf(SyntaxNode node) => node.SourceLocation.Range;
}
