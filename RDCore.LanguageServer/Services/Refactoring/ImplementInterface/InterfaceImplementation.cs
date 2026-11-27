using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using System.Text.RegularExpressions;

namespace RDCore.LanguageServer.Services.Refactoring.ImplementInterface;

/// <summary>
/// Why the members an <c>Implements</c> directive or a <c>WithEvents</c> variable calls for are not written.
/// </summary>
internal enum ImplementationRefusalKind
{
    /// <summary>
    /// The directive is not valid (<strong>MS-VBAL §5.2.4.2</strong>): its compile error says why.
    /// </summary>
    InvalidDirective,

    /// <summary>
    /// Where the members would be written is in a branch of an <c>#If</c> block (<strong>MS-VBAL §3.4</strong>), where they would be compiled only when that
    /// branch is live.
    /// </summary>
    Conditional,

    /// <summary>
    /// A member of the interface is of a library, with an <c>Optional</c> parameter whose default value cannot be written from what the library says of it.
    /// </summary>
    NotWritable,
}

/// <summary>
/// What an Implement Interface is asked of: an <c>Implements</c> directive of a class module, or a <c>WithEvents</c> variable of one.
/// </summary>
/// <param name="Name">The name of the interface class, or of the variable.</param>
/// <param name="Range">Where the directive or the variable's declaration is written.</param>
/// <param name="IsVariable">Whether it is a <c>WithEvents</c> variable, whose events are handled.</param>
internal sealed record class ImplementationTarget(string Name, SourceRange Range, bool IsVariable);

/// <summary>
/// A procedure Implement Interface writes.
/// </summary>
/// <param name="Name">Its name: the interface's or the variable's, an underscore, and the member's.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Text">Its whole text, unindented.</param>
/// <param name="Forwards">Whether its body calls the class's own member of the interface member's name.</param>
internal sealed record class ImplementationStub(string Name, MemberKind Kind, string Text, bool Forwards);

/// <summary>
/// Why the members are not written, and the member that is why, when it is one.
/// </summary>
/// <param name="Kind">Why.</param>
/// <param name="Member">The member of the interface; <see langword="null"/> when it is not one.</param>
internal sealed record class ImplementationRefusal(ImplementationRefusalKind Kind, string? Member = null);

/// <summary>
/// The procedures an <c>Implements</c> directive or a <c>WithEvents</c> variable calls for, and the edit that writes them, or why they are not written.
/// </summary>
/// <param name="Target">The directive or the variable.</param>
/// <param name="Stubs">The procedures, in the order the interface declares its members.</param>
/// <param name="Edit">The edit that writes them, unindented; <see langword="null"/> when they are not written.</param>
/// <param name="Refusal">Why they are not written; <see langword="null"/> when they are.</param>
internal sealed record class ImplementationPlan(ImplementationTarget Target, IReadOnlyList<ImplementationStub> Stubs, RefactorTextEdit? Edit, ImplementationRefusal? Refusal);

/// <summary>
/// Implement Interface: the procedures a class module needs to implement the members of an interface class it names in an <c>Implements</c> directive
/// (<strong>MS-VBAL §5.2.4.2</strong>, <strong>§5.3.1.9</strong>), or to handle the events of the class of a <c>WithEvents</c> variable
/// (<strong>MS-VBAL §5.3.1.8</strong>), that it does not have.
/// </summary>
/// <remarks>
/// <para>
/// Each procedure is <c>Private</c>, named for the interface or the variable and the member, with the member's parameters: their names, their passing written
/// explicitly, their types and their <c>Optional</c> defaults, as the member's declaration writes them; and its type. A <c>Public</c> variable of the interface
/// is implemented by a <c>Property Get</c>, with a <c>Property Let</c> for a value, a <c>Property Set</c> for an object, and both for a <c>Variant</c>.
/// </para>
/// <para>
/// A member of the interface that the class also has as a <c>Public</c> member of its own, of the same name, kind, parameters and type, is implemented by a
/// call to that member, which is given the procedure's parameters. One with a <c>ParamArray</c> is not, since its arguments would reach the call as one array;
/// nor one whose value is a <c>Variant</c> returned by a procedure, which could only be assigned by calling it twice. Any other raises error 5, with a comment
/// to say it is to be written.
/// </para>
/// <para>
/// An event handler is an <c>Exit Sub</c>, with a comment to say it is to be written: a handler with no statement is reported as an empty procedure, and one that
/// raised error 5, as an unimplemented interface member does, would raise it to the code that raises the event, which ran without an error before the handler
/// was written.
/// </para>
/// </remarks>
internal static partial class InterfaceImplementation
{
    private static readonly MemberKind[] Procedures =
        [MemberKind.Procedure, MemberKind.Function, MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet];

    /// <summary>
    /// The procedures the directive or the <c>WithEvents</c> variable at a range of a class module calls for, and the edit that writes them after the module's
    /// last procedure; or <see langword="null"/> when the range is on neither, or names no interface or class the workspace knows.
    /// </summary>
    /// <param name="context">The workspace, bound.</param>
    /// <param name="module">The class module.</param>
    /// <param name="parse">Its parse, of the text <paramref name="context"/> has of it.</param>
    /// <param name="range">The range: on a line of the directive or of the variable's declaration.</param>
    /// <param name="todo">The comment the body of a procedure that raises error 5 ends with.</param>
    /// <param name="handlerTodo">The comment the <c>Exit Sub</c> of an event handler ends with.</param>
    public static ImplementationPlan? Plan(RenameContext context, RenameModule module, ModuleParseResult parse, SourceRange range, string todo, string handlerTodo)
    {
        if (parse.SyntaxTree is not { } tree
            || NameOccurrences.SymbolOf(context, new SemanticId(module.Module)) is not VBClassModuleSymbol type)
        {
            return null;
        }

        var text = TextLines.Of(module.Source);
        var writer = new Writer(context, text.NewLine, todo, handlerTodo, context.DeadTestOf(parse));
        ImplementationTarget target;
        IReadOnlyList<ImplementationStub> stubs;
        if (DirectiveAt(tree, range) is { } directive)
        {
            if (InterfaceOf(type, directive) is not { } implemented)
            {
                return null;
            }

            target = new ImplementationTarget(implemented.Interface.Name, directive.Location.Range, IsVariable: false);
            if (implemented.Invalid)
            {
                return new ImplementationPlan(target, [], null, new(ImplementationRefusalKind.InvalidDirective));
            }

            (stubs, var refusal) = writer.Implementations(type, implemented.Interface);
            if (refusal is not null)
            {
                return new ImplementationPlan(target, [], null, refusal);
            }
        }
        else if (WithEventsAt(tree, range) is { } declaration
            && type.WithEventsVariables.FirstOrDefault(variable => string.Equals(variable.Name, declaration.Name, StringComparison.OrdinalIgnoreCase)) is { } variable
            && variable.ResolvedType is VBClassType { Symbol: { } source })
        {
            target = new ImplementationTarget(variable.Name, declaration.SourceLocation.Range, IsVariable: true);
            stubs = writer.Handlers(type, variable, source);
        }
        else
        {
            return null;
        }

        if (stubs.Count == 0)
        {
            return new ImplementationPlan(target, [], null, null);
        }

        var written = string.Join(text.NewLine + text.NewLine, stubs.Select(stub => stub.Text));
        var last = tree.Children.OfType<MemberDeclarationNode>()
            .Where(member => member is not ExternalMemberDeclarationNode && Procedures.Contains(member.MemberKind))
            .Select(member => MemberEdits.Extent(parse, module.Source, member))
            .LastOrDefault(extent => !extent.IsConditional);
        var edit = last is not null ? MemberEdits.InsertAfter(module.Source, last, written) : DeclarationEdits.Insert(parse, module.Source, written);
        return edit is null
            ? new ImplementationPlan(target, stubs, null, new(ImplementationRefusalKind.Conditional))
            : new ImplementationPlan(target, stubs, edit, null);
    }

    // the directive written on the line a range starts on.
    private static ImplementsDirectiveNode? DirectiveAt(ModuleNode tree, SourceRange range)
        => tree.Children.OfType<ImplementsDirectiveNode>().FirstOrDefault(directive => directive.NameExpression is not null && OnItsLines(directive.Location.Range, range));

    // the WithEvents variable declared on the line a range starts on.
    private static VariableDeclarationNode? WithEventsAt(ModuleNode tree, SourceRange range)
        => tree.Children.OfType<VariableDeclarationNode>().FirstOrDefault(variable => variable.IsWithEvents && OnItsLines(variable.SourceLocation.Range, range));

    private static bool OnItsLines(SourceRange written, SourceRange range) => range.Start.Line >= written.Start.Line && range.Start.Line <= written.End.Line;

    // the interface class a directive names, as the class resolved it, and whether the directive is one the class has more than once or names the class itself:
    // those are reported by the compile error of the directive, and the members are not written for it.
    private static (VBClassModuleSymbol Interface, bool Invalid)? InterfaceOf(VBClassModuleSymbol type, ImplementsDirectiveNode directive)
    {
        var name = directive.NameExpression switch
        {
            SimpleNameExpressionNode simple => simple.IdentifierName,
            MemberAccessExpressionNode access => access.Member.IdentifierName,
            _ => null,
        };

        // the lifecycle interface every class implements is not one a directive names.
        var implemented = type.ImplementedInterfaces
            .Where(candidate => candidate.Uri.AbsoluteUri != ClassLifecycleInterface.Interface.Uri.AbsoluteUri)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (implemented is null)
        {
            return null;
        }

        var named = type.ImplementedInterfaceNames.Count(written => string.Equals(SimpleNameOf(written), implemented.Name, StringComparison.OrdinalIgnoreCase));
        var invalid = named > 1
            || implemented.Uri.AbsoluteUri == type.Uri.AbsoluteUri
            || (type.GetProperty(SymbolProperties.Extensible) && !type.IsDocument)
            || ImplementsSemantics.InterfaceMembersOf(implemented).Any(member => member.Name.Contains('_'));
        return (implemented, invalid);
    }

    private static string SimpleNameOf(string written) => written[(written.LastIndexOf('.') + 1)..];

    // a member of a class that is in its interface: a procedure that says it is public or says nothing, a variable that says it is Public.
    private static bool IsPublic(VBTypeMemberSymbol member)
        => ImplementsSemantics.KindOf(member) is not null
            ? member.AccessModifier is AccessModifier.Public or AccessModifier.Implicit
            : member.Kind == SymbolKindExt.Field && member.AccessModifier is AccessModifier.Public;

    private sealed partial class Writer(RenameContext context, string newLine, string todo, string handlerTodo, Func<SourceLocation, bool>? isDead)
    {
        // the members of the class as it compiles: one declared only in branches of #If blocks that are not live is none of them (MS-VBAL §3.4), and one
        // declared in several is the declaration of its live branch. Every member is, when which branches are live is not known.
        private IReadOnlyList<VBTypeMemberSymbol> MembersOf(VBClassModuleSymbol type)
            => isDead is null ? type.Members : [.. type.Members.Select(member => member.LiveDeclaration(isDead)).OfType<VBTypeMemberSymbol>()];

        // the procedures the class needs to implement the interface, or why they are not written.
        public (IReadOnlyList<ImplementationStub> Stubs, ImplementationRefusal? Refusal) Implementations(VBClassModuleSymbol type, VBClassModuleSymbol implemented)
        {
            var stubs = new List<ImplementationStub>();
            foreach (var member in ImplementsSemantics.InterfaceMembersOf(implemented))
            {
                var name = $"{implemented.Name}_{member.Name}";
                var named = MembersOf(type).Where(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
                var isVariable = member.Kind == SymbolKindExt.Field;

                // a name the class has for something no accessor of this member is (a variable, a Sub for a Function) is one its compile error reports: the
                // procedure is not written beside it.
                var accessors = isVariable ? new[] { MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet } : [ImplementsSemantics.KindOf(member)!.Value];
                var siblings = ImplementsSemantics.InterfaceMembersOf(implemented)
                    .Where(other => other.Kind != SymbolKindExt.Field && string.Equals(other.Name, member.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(other => ImplementsSemantics.KindOf(other)!.Value);
                accessors = [.. accessors.Concat(siblings).Distinct()];
                if (named.Any(candidate => ImplementsSemantics.KindOf(candidate) is not { } kind || !accessors.Contains(kind)))
                {
                    continue;
                }

                foreach (var kind in ImplementsSemantics.RequiredKindsOf(member))
                {
                    if (named.Any(candidate => ImplementsSemantics.KindOf(candidate) == kind))
                    {
                        continue;
                    }

                    if (SignatureOf(member, kind) is not { } signature)
                    {
                        return ([], new(ImplementationRefusalKind.NotWritable, $"{implemented.Name}.{member.Name}"));
                    }

                    var forwarded = Forwarded(MembersOf(type), member, kind, name, signature);
                    var body = forwarded ?? [$"    Err.Raise 5 '{todo}"];
                    stubs.Add(new ImplementationStub(name, kind, Procedure(kind, name, signature, body), forwarded is not null));
                }
            }

            return (stubs, null);
        }

        // the handlers of the events of a variable's class that the class does not handle, each to be written.
        public IReadOnlyList<ImplementationStub> Handlers(VBClassModuleSymbol type, VBTypeMemberSymbol variable, VBClassModuleSymbol source)
        {
            var stubs = new List<ImplementationStub>();
            foreach (var handled in source.Events.Where(handled => type.FindEventHandler(variable, handled) is null))
            {
                var name = $"{variable.Name}_{handled.Name}";
                if (MembersOf(type).Any(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                    || SignatureOf(handled, MemberKind.Procedure) is not { } signature)
                {
                    continue;
                }

                stubs.Add(new ImplementationStub(name, MemberKind.Procedure, Procedure(MemberKind.Procedure, name, signature, [$"    Exit Sub '{handlerTodo}"]), Forwards: false));
            }

            return stubs;
        }

        private string Procedure(MemberKind kind, string name, Signature signature, IReadOnlyList<string> body)
        {
            var end = kind switch
            {
                MemberKind.Procedure => "End Sub",
                MemberKind.Function => "End Function",
                _ => "End Property",
            };

            var header = $"Private {ProcedureKeywords.Of(kind)} {name}{signature.TypeHint}({string.Join(", ", signature.Parameters.Select(parameter => parameter.Text))}){signature.AsClause}";
            return string.Join(newLine, [header, .. body, end]);
        }

        // ---- signatures ----

        // the member's parameters and type, as the procedure of a kind that implements it declares them; null when one of them cannot be written.
        private Signature? SignatureOf(VBTypeMemberSymbol member, MemberKind kind)
        {
            if (member.Kind == SymbolKindExt.Field)
            {
                var typeName = VariableTypeOf(member);
                return kind is MemberKind.PropertyGet
                    ? new Signature([], string.Empty, $" As {typeName}", IsObject(member.ResolvedType), member.ResolvedType is VBVariantType)
                    : new Signature([new Parameter(ValueName(member), $"ByVal {ValueName(member)} As {typeName}", IsParamArray: false)], string.Empty, string.Empty,
                        IsObject(member.ResolvedType), member.ResolvedType is VBVariantType);
            }

            return DeclarationOf(member, kind) is { } declared ? FromSource(member, declared.Node, declared.Text) : FromSymbol(member);
        }

        // the value parameter of a property that implements a variable: "value", unless that is the variable's own name.
        private static string ValueName(VBTypeMemberSymbol variable) => string.Equals(variable.Name, "value", StringComparison.OrdinalIgnoreCase) ? "newValue" : "value";

        // the declaration of a member of a module of the workspace, and the text of that module.
        private (SyntaxNode Node, TextLines Text)? DeclarationOf(VBTypeMemberSymbol member, MemberKind kind)
        {
            if (context.PartOf(member) is not { } module)
            {
                return null;
            }

            var declaredKind = member is VBEventMemberSymbol ? MemberKind.Event : kind;
            var candidates = module.Syntax.Children.OfType<MemberDeclarationNode>()
                .Where(node => node.MemberKind == declaredKind && string.Equals(node.Name, member.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var node = candidates.FirstOrDefault(candidate => candidate.NameRange?.Start == member.SelectionRange.Start) ?? candidates.FirstOrDefault();
            return node is null ? null : (node, TextLines.Of(module.Source));
        }

        // the parameters and the type as the member's declaration writes them, each parameter's passing written out.
        private static Signature FromSource(VBTypeMemberSymbol member, SyntaxNode declaration, TextLines text)
        {
            var parameters = declaration.Children.OfType<ParameterDeclarationNode>().Select(parameter =>
            {
                var written = new List<string>();
                if (parameter.IsOptional)
                {
                    written.Add("Optional");
                }

                written.Add(parameter.IsParamArray ? "ParamArray" : Passing(parameter.ParameterKind));
                var declared = $"{parameter.Name}{parameter.TypeHint}{(parameter.IsArray ? "()" : string.Empty)}";
                if (parameter.Children.OfType<AsTypeExpressionNode>().FirstOrDefault() is { } asType)
                {
                    declared += $" As {TypeNameOf(asType)}";
                }

                if (parameter.Children.OfType<ExpressionNode>().FirstOrDefault(child => child is not AsTypeExpressionNode) is { } value)
                {
                    declared += $" = {OneLine(text.Of(value.Location.Range))}";
                }

                written.Add(declared);
                return new Parameter(parameter.Name, string.Join(" ", written), parameter.IsParamArray);
            }).ToArray();

            var returned = declaration.Children.Where(child => child is not ParameterDeclarationNode).OfType<AsTypeExpressionNode>().FirstOrDefault();
            var asClause = returned is null ? string.Empty : $" As {TypeNameOf(returned)}{(returned.IsArrayDef ? "()" : string.Empty)}";
            var typeHint = (declaration as MemberDeclarationNode)?.TypeHint ?? string.Empty;
            return new Signature(parameters, typeHint, asClause, IsObject(member.ResolvedType), member.ResolvedType is VBVariantType);
        }

        // the parameters and the type as the symbols of a library's member say them; null when an Optional parameter has a default, which they do not write.
        private static Signature? FromSymbol(VBTypeMemberSymbol member)
        {
            var symbols = member switch
            {
                VBEventMemberSymbol handled => handled.Parameters,
                _ => ParametersOf(member),
            };

            var parameters = new List<Parameter>();
            foreach (var parameter in symbols.Where(parameter => parameter.Name != "Me"))
            {
                if (parameter.IsOptional && parameter.DefaultValue is not null)
                {
                    return null;
                }

                var isParamArray = parameter is ParamArrayParameterSymbol;
                var array = parameter.ResolvedType is VBArrayType ? "()" : string.Empty;
                var written = $"{(parameter.IsOptional ? "Optional " : string.Empty)}{(isParamArray ? "ParamArray" : Passing(parameter.ParameterKind))} {parameter.Name}{array} As {NameOfType(parameter.ResolvedType)}";
                parameters.Add(new Parameter(parameter.Name, written, isParamArray));
            }

            var asClause = member is VBFunctionMemberSymbol && member.ResolvedType is not VBVoidType ? $" As {NameOfType(member.ResolvedType)}" : string.Empty;
            return new Signature(parameters, string.Empty, asClause, IsObject(member.ResolvedType), member.ResolvedType is VBVariantType);
        }

        private static IEnumerable<VBParameterSymbol> ParametersOf(VBTypeMemberSymbol member) => member switch
        {
            VBProcedureMemberSymbol procedure => procedure.Parameters,
            VBFunctionMemberSymbol function => function.Parameters,
            _ => [],
        };

        private static string Passing(ParameterKind kind) => kind is ParameterKind.ExplicitByVal or ParameterKind.ImplicitByVal ? "ByVal" : "ByRef";

        // a type as its As clause writes it, qualified or not: the interface and the class are of one project, where it names the same type.
        private static string TypeNameOf(AsTypeExpressionNode asType)
            => asType.FixedLength is not null ? "String" : asType.QualifierName is { } qualifier ? $"{qualifier}.{asType.TypeName}" : asType.TypeName;

        // the type of a variable of the interface, as its declaration writes it; the name of its type when it has no As clause.
        private string VariableTypeOf(VBTypeMemberSymbol variable)
        {
            var declared = context.PartOf(variable)?.Syntax.Children.OfType<VariableDeclarationNode>()
                .FirstOrDefault(node => node.NameRange?.Start == variable.SelectionRange.Start || string.Equals(node.Name, variable.Name, StringComparison.OrdinalIgnoreCase));
            return declared?.ChildNodes().OfType<AsTypeExpressionNode>().FirstOrDefault() is { } asType ? TypeNameOf(asType) : NameOfType(variable.ResolvedType);
        }

        private static string NameOfType(VBType type) => type switch
        {
            VBArrayType { ItemType: { } item } => NameOfType(item),
            VBFixedStringType => "String",
            _ => type.Name,
        };

        private static bool IsObject(VBType type) => type is VBClassType or VBObjectType;

        // a default value written over several lines, on one.
        private static string OneLine(string text) => LineContinuation().Replace(text, " ").Trim();

        [GeneratedRegex(@"\s+_\r?\n\s*")]
        private static partial Regex LineContinuation();

        // ---- forwarding ----

        // the body of a procedure that implements an interface member by calling the class's own public member of its name: of the same kind, the same
        // parameters and the same type; null when the class has no such member, or when a call cannot stand for it.
        private static IReadOnlyList<string>? Forwarded(
            IReadOnlyList<VBTypeMemberSymbol> members, VBTypeMemberSymbol member, MemberKind kind, string name, Signature signature)
        {
            if (signature.Parameters.Any(parameter => parameter.IsParamArray || string.Equals(parameter.Name, member.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var own = members.Where(candidate => IsPublic(candidate) && string.Equals(candidate.Name, member.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var arguments = signature.Parameters.Select(parameter => parameter.Name).ToArray();
            if (member.Kind == SymbolKindExt.Field)
            {
                // a variable of the class of the same type, or its own property of the same type with no index.
                var field = own.FirstOrDefault(candidate => candidate.Kind == SymbolKindExt.Field && SameType(candidate.ResolvedType, member.ResolvedType));
                var property = own.FirstOrDefault(candidate => ImplementsSemantics.KindOf(candidate) == kind
                    && ParametersOf(candidate).Count(parameter => parameter.Name != "Me") == (kind is MemberKind.PropertyGet ? 0 : 1)
                    && SameType(TypeCarried(candidate, kind), member.ResolvedType));
                if (field is null && (property is null || (kind is MemberKind.PropertyGet && signature.IsVariant)))
                {
                    return null;
                }

                return kind switch
                {
                    MemberKind.PropertyGet when signature.IsVariant => [$"    If IsObject({member.Name}) Then", $"        Set {name} = {member.Name}", "    Else", $"        {name} = {member.Name}", "    End If"],
                    MemberKind.PropertyGet => [$"    {(signature.IsObject ? "Set " : string.Empty)}{name} = {member.Name}"],
                    MemberKind.PropertyLet => [$"    {member.Name} = {arguments[^1]}"],
                    _ => [$"    Set {member.Name} = {arguments[^1]}"],
                };
            }

            if (own.FirstOrDefault(candidate => ImplementsSemantics.KindOf(candidate) == kind) is not { } target
                || ParameterLists.Incompatibility(ParameterLists.Of(target), ParameterLists.Of(member), optionalsAndDefaults: true) is not null
                || (kind is MemberKind.Function or MemberKind.PropertyGet && (!SameType(target.ResolvedType, member.ResolvedType) || signature.IsVariant)))
            {
                return null;
            }

            var list = arguments.Length == 0 ? string.Empty : $"({string.Join(", ", arguments)})";
            return kind switch
            {
                MemberKind.Procedure => [arguments.Length == 0 ? $"    {member.Name}" : $"    {member.Name} {string.Join(", ", arguments)}"],
                MemberKind.Function or MemberKind.PropertyGet => [$"    {(signature.IsObject ? "Set " : string.Empty)}{name} = {member.Name}{list}"],
                MemberKind.PropertyLet => [$"    {member.Name}{Indexed(arguments)} = {arguments[^1]}"],
                _ => [$"    Set {member.Name}{Indexed(arguments)} = {arguments[^1]}"],
            };
        }

        // the index arguments of a property's Let or Set: every parameter but the value.
        private static string Indexed(string[] arguments) => arguments.Length <= 1 ? string.Empty : $"({string.Join(", ", arguments[..^1])})";

        // the type a property carries: what a Get returns, and what the value parameter of a Let or a Set takes.
        private static VBType? TypeCarried(VBTypeMemberSymbol property, MemberKind kind)
            => kind is MemberKind.PropertyGet ? property.ResolvedType : ParametersOf(property).LastOrDefault()?.ResolvedType;

        private static bool SameType(VBType? left, VBType right) => left is not null && left is not VBUnknownType && right is not VBUnknownType && ParameterLists.SameType(left, right);
    }

    // a parameter of a procedure that is written: its name, and its declaration.
    private sealed record class Parameter(string Name, string Text, bool IsParamArray);

    // what a procedure that is written declares after its name.
    private sealed record class Signature(IReadOnlyList<Parameter> Parameters, string TypeHint, string AsClause, bool IsObject, bool IsVariant);
}
