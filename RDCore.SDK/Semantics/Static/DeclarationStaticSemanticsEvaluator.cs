using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;
using System.Collections.Immutable;
using System.Globalization;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The static semantics of what a module declares, apart from what its procedures' bodies say: the declarations themselves.
/// </summary>
/// <remarks>
/// A module whose every procedure is valid is not a valid module: a name declared twice, an event that is wrong, an interface that is not
/// implemented are all errors of a declaration, which no body shows. They are found here, once for the module, and a module model holds them
/// next to the models of its procedures (<see cref="ModuleSemanticModel.DeclarationErrors"/>).
/// <list type="bullet">
/// <item>A name is declared once in the scope of a module (<strong>MS-VBAL §5.2</strong>, <see cref="VBCompileErrorId.AmbiguousName"/>). That is a rule of
/// the declarations as they are written, and not of the symbols they become (<see cref="CheckSyntax"/>).</item>
/// <item>A declared type is a name that resolves to a type (<see cref="VBCompileErrorId.UserDefinedTypeNotDefined"/>): of a variable, a constant of the
/// module, a parameter, a function's or a property's result, and a local variable. A name that resolves to a procedural module or a project does not: neither
/// is a type (<strong>MS-VBAL §5.6.16.7</strong>, <see cref="VBCompileErrorId.ModuleIsNotAValidType"/>, <see cref="VBCompileErrorId.AutomationTypeNotSupported"/>
/// for a library's module, <see cref="VBCompileErrorId.ExpectedUserDefinedTypeNotProject"/>). Of a field of a user-defined type and of a local constant, only that much is checked: a procedural module is
/// refused there, and a name that does not resolve is not. Whether a name resolves can only be told once everything the declaration can see is defined,
/// which is why this rule is one a caller asks for (<see cref="DeclarationRules.DeclaredTypes"/>).</item>
/// <item>What a module exposes (<see cref="PublicMemberSemantics"/>): a public member is not declared with a private type, and a class module exposes no
/// constant, array, user-defined type or <c>Declare</c>. A rule of the declared types too, and asked for with them.</item>
/// <item>A class module is also checked for what it declares about events (<see cref="ClassModuleEventSemantics"/>) and for what its
/// <c>Implements</c> directives require (<see cref="ImplementsSemantics"/>).</item>
/// </list>
/// </remarks>
public static class DeclarationStaticSemanticsEvaluator
{
    /// <summary>
    /// The most names a module declares at module level - variables, constants, procedures, properties, <c>Declare</c> statements, enumerations and
    /// user-defined types, all together, a property once whatever its accessors - before it is <see cref="VBCompileErrorId.OutOfMemory"/>.
    /// </summary>
    /// <remarks>
    /// MS-VBAL sets no limit. MS-VBA compiles a module of 10,800 such names and not one of 11,000, whatever their kind or the length of their names, and
    /// reports "Out of memory" (recorded in Excel). RD-VBA's cap is a round number below that, by the author's ruling: a module RD-VBA compiles is one
    /// MS-VBA compiles.
    /// </remarks>
    public const int MaxModuleNames = 10_000;

    /// <summary>
    /// Checks everything <paramref name="module"/> declares.
    /// </summary>
    /// <param name="module">The module, as the session or the workspace composed it: a class module with its members.</param>
    /// <param name="members">The members declared by the module (<see cref="ISessionSymbols.MembersOf"/>).</param>
    /// <param name="resolver">What finds the classes the module's declarations name, as they are now.</param>
    /// <param name="rules">Which of the rules that depend on what else is defined are checked.</param>
    /// <returns>Every error found, in declaration order within each rule; empty when the declarations are valid.</returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(
        Symbol module, IReadOnlyList<VBTypeMemberSymbol> members, ISymbolResolver resolver, DeclarationRules rules = DeclarationRules.Default)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

        if (rules.HasFlag(DeclarationRules.DeclaredTypes))
        {
            CheckDeclaredTypes(module, members, resolver, errors);

            // which types a public member may be declared with depends on the types its declarations name, resolved.
            errors.AddRange(PublicMemberSemantics.Evaluate(module, members, resolver));

            // what a constant's value names - a constant, a variable, a function - is known once every name resolves.
            errors.AddRange(ConstantExpressionStaticSemantics.Evaluate(members, resolver));
        }

        if (module is VBClassModuleSymbol classModule)
        {
            errors.AddRange(ClassModuleEventSemantics.Evaluate(classModule, resolver));
            errors.AddRange(ImplementsSemantics.Evaluate(classModule, resolver));
        }

        return errors.ToImmutable();
    }

    /// <summary>
    /// Checks that the code of a form module declares no member with the name of one of the form's controls
    /// (<see cref="VBCompileErrorId.MemberAlreadyExistsInDesigner"/>): the form's designer already declares a field of the form's class of that name.
    /// </summary>
    /// <remarks>
    /// MS-VBA reports a variable, a constant, a procedure or a <c>Declare</c> of a control's name (Excel-measured, <c>oracle\s30</c> C09/C10). It compiles a
    /// <c>WithEvents</c> variable of a control's name and class (C17), which reads as though it shadowed the control and does not: RD-VBA refuses it with the
    /// same error, which is the error it should have been all along (the author's ruling).
    /// </remarks>
    /// <param name="module">The parsed module.</param>
    /// <param name="designer">What the module's designer declares (<see cref="ModuleParseResult.Designer"/>), or <c>null</c> when it is no form.</param>
    /// <returns>One error for each declaration of a control's name, at the declaration; empty when there is none, or no designer.</returns>
    public static ImmutableArray<VBCompileErrorInfo> CheckDesigner(ModuleNode module, FormDescriptor? designer)
    {
        if (designer is null)
        {
            return [];
        }

        var controls = designer.DeclaredControls().Select(control => control.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();
        foreach (var child in module.Children)
        {
            var name = child switch
            {
                VariableDeclarationNode variable => variable.Name,
                ConstantDeclarationNode constant => constant.Name,
                ExternalMemberDeclarationNode external => external.Name,
                // the name of a type is bound in another context than a value's (MS-VBAL §5.6.4), as CheckSyntax has it
                MemberDeclarationNode { MemberKind: not (MemberKind.UserDefinedTypeField or MemberKind.Enum or MemberKind.UserDefinedType) } member => member.Name,
                _ => string.Empty,
            };

            if (controls.Contains(name))
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.MemberAlreadyExistsInDesigner, child.SourceLocation,
                    string.Format(Exceptions.VBCompileError_MemberAlreadyExistsInDesigner_Verbose, name)));
            }
        }

        return errors.ToImmutable();
    }

    /// <summary>
    /// Checks that a name is declared once in the scope of a module (<strong>MS-VBAL §5.2</strong>, <see cref="VBCompileErrorId.AmbiguousName"/>), from
    /// what the module's source declares.
    /// </summary>
    /// <remarks>
    /// Read off the declarations of the syntax tree and not the symbols they become, because a symbol is one per identity: a name declared twice as the same
    /// kind of thing (<c>Public Total As Long</c> twice, a <c>Sub</c> of one name twice) is, by then, one symbol, and a rule that read symbols could only
    /// find the names that collide as different kinds. The <c>Get</c>, <c>Let</c> and <c>Set</c> accessors of a property are the one declaration of it; an
    /// event is checked for its own uniqueness by the rule of events, which says what it is that is declared twice; and a name declared in each branch of a
    /// <c>#If</c> block is declared once, whichever branch compiles.
    /// </remarks>
    /// <param name="module">The parsed module.</param>
    /// <param name="blocks">The <c>#If</c> blocks of the module, which say which declarations are alternatives of one another.</param>
    /// <returns>One error for each declaration of a name that is already declared, at the repeated declaration; empty when each is declared once.</returns>
    public static ImmutableArray<VBCompileErrorInfo> CheckSyntax(ModuleNode module, ConditionalCompilationBlocks blocks)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();
        var seen = new Dictionary<string, List<(MemberKind? Kind, SourceLocation Location)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in module.Children)
        {
            var (name, kind) = child switch
            {
                VariableDeclarationNode variable => (variable.Name, default(MemberKind?)),
                ConstantDeclarationNode constant => (constant.Name, default(MemberKind?)),
                ExternalMemberDeclarationNode external => (external.Name, default(MemberKind?)),
                MemberDeclarationNode { MemberKind: MemberKind.Event or MemberKind.UserDefinedTypeField } => (string.Empty, default(MemberKind?)),
                MemberDeclarationNode member => (member.Name, member.MemberKind),
                _ => (string.Empty, default(MemberKind?)),
            };

            if (name.Length == 0)
            {
                continue;
            }

            // the name of a type and the name of a value are bound in different contexts (MS-VBAL §5.6.4): an Enum and a property of the one name are
            // both declared once, and `Property Get PlayerType() As PlayerType` is how code says that.
            var key = (kind is MemberKind.Enum or MemberKind.UserDefinedType ? "type:" : "value:") + name;
            if (!seen.TryGetValue(key, out var declarations))
            {
                seen.Add(key, declarations = []);

                // RD-VBA compiles a module of at most MaxModuleNames names, and the one past them is "Out of memory", as MS-VBA has it of the names past its
                // own count (recorded in Excel: between 10,800 and 11,000).
                if (seen.Count == MaxModuleNames + 1)
                {
                    errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.OutOfMemory, child.SourceLocation,
                        string.Format(CultureInfo.CurrentCulture, Exceptions.VBCompileError_OutOfMemory_ModuleNames_Verbose, MaxModuleNames)));
                }
            }

            // MS-VBA names a name a module declares twice, whatever the two are - two variables, two procedures, a constant and a variable - by the
            // ambiguity it makes: "Ambiguous name detected" (Excel-verified); a procedure's own duplicates are "Duplicate declaration in current scope".
            if (declarations.Any(prior => !AreOneDeclaration(prior, (kind, child.SourceLocation), blocks)))
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.AmbiguousName, child.SourceLocation, $"'{name}' is declared more than once in this module."));
            }

            declarations.Add((kind, child.SourceLocation));
        }

        // MS-VBAL §5.3.1.4: `As Long()` - a type written with () - is what a Function, a Property Get or a Declare's Function returns, and nothing else is
        // declared with one: a variable, a constant, a field of a Type, a parameter or the elements of a ReDim are an array by the () after the name (MS-VBA:
        // a syntax error, recorded in Excel, oracle\s23 T01-T18).
        foreach (var declaration in module.Descendants().Where(node => node is not (MemberDeclarationNode { MemberKind: MemberKind.Function or MemberKind.PropertyGet
            or MemberKind.ExternalFunction } or ExternalMemberDeclarationNode)))
        {
            foreach (var asType in declaration.Children.OfType<AsTypeExpressionNode>().Where(asType => asType.IsArrayDef))
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, asType.Location,
                    string.Format(Exceptions.VBCompileError_ArrayTypeOnlyReturned_Verbose, asType.TypeName)));
            }
        }

        // an array parameter is passed by reference, and one declared ByVal is refused - of a procedure, a property, an event or a Declare alike (MS-VBA: "Array
        // argument must be ByRef", recorded in Excel, oracle\s23; RD-VBAL §5.3.1.5). A ParamArray is written with neither ByVal nor ByRef (MS-VBA: "Expected:
        // identifier", a syntax error).
        foreach (var parameter in module.Descendants().OfType<ParameterDeclarationNode>())
        {
            if (parameter is { IsParamArray: true, ParameterKind: ParameterKind.ExplicitByVal or ParameterKind.ExplicitByRef })
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, parameter.Location,
                    string.Format(Exceptions.VBCompileError_ParamArrayPassingMode_Verbose, parameter.Name)));
            }
            else if (parameter is { IsArray: true, ParameterKind: ParameterKind.ExplicitByVal })
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.ArrayArgumentMustBeByRef, parameter.Location,
                    string.Format(Exceptions.VBCompileError_ArrayArgumentMustBeByRef_Declaration_Verbose, parameter.Name)));
            }
        }

        // MS-VBAL §5.6.13.1: the last positional argument of a list is required (required-positional-argument), so a list does not end with an omitted one: `F(1, )`
        // and `S 1,` are syntax errors, for an Optional parameter too, as MS-VBA has them (recorded in Excel: oracle\ismissing Q03, Q05, Q07). `F()` is a list of
        // no argument, which the parser reads as one omitted argument alone.
        foreach (var arguments in module.Descendants().Select(node => node switch
        {
            IndexExpressionNode call => call.Arguments,
            CallStatementNode call => call.Arguments,
            _ => default,
        }).Where(arguments => !arguments.IsDefault && arguments is [_, _, ..] && arguments[^1] is MissingArgumentNode))
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, arguments[^1].Location, Exceptions.VBCompileError_TrailingOmittedArgument_Verbose));
        }

        return errors.ToImmutable();
    }

    private static bool AreOneDeclaration((MemberKind? Kind, SourceLocation Location) prior, (MemberKind? Kind, SourceLocation Location) next, ConditionalCompilationBlocks blocks)
        => blocks.AreAlternatives(prior.Location.Range.Start, next.Location.Range.Start)
            || (IsAccessor(prior.Kind) && IsAccessor(next.Kind) && prior.Kind != next.Kind);

    private static bool IsAccessor(MemberKind? kind) => kind is MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

    // MS-VBAL §5.6.4: a declared type is bound in the type binding context. A name that did not resolve where the declaration was read, which may have been
    // before what it names was defined, is resolved again as seen from the module: an error is a name that still does not, or one that is a procedural
    // module, which the declaration could not have read as a type either.
    private static void CheckDeclaredTypes(
        Symbol module, IReadOnlyList<VBTypeMemberSymbol> members, ISymbolResolver resolver, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        foreach (var member in members)
        {
            CheckDeclaredType(module, member, member.SelectionRange, member.ParentUri, resolver, errors);
            CheckAutoInstantiation(member, member.SelectionRange, member.ParentUri, errors);

            // a field of a user-defined type is the type's, not the module's, and comes with it. Only a procedural module is refused as its type: a name that
            // does not resolve is not checked there, and loads as it did before a field's type was checked at all.
            foreach (var field in member is VBUserDefinedTypeMemberSymbol { Members.IsDefault: false } userDefinedType ? userDefinedType.Members : [])
            {
                CheckDeclaredType(module, field, field.SelectionRange, member.ParentUri, resolver, errors, onlyAModule: true);
            }

            foreach (var parameter in ParametersOf(member))
            {
                CheckDeclaredType(module, parameter, parameter.SelectionRange, member.ParentUri, resolver, errors,
                    mayBeAny: member is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol);
            }

            // a local constant's type is refused only for a procedural module, as a field's is: a name that does not resolve loads, as it did when the
            // language server sent that name as no type at all.
            foreach (var local in LocalsOf(member))
            {
                CheckDeclaredType(module, local, local.SelectionRange, member.ParentUri, resolver, errors, onlyAModule: local is VBLocalConstantSymbol);
                CheckAutoInstantiation(local, local.SelectionRange, member.ParentUri, errors);
            }
        }
    }

    internal static ImmutableArray<VBParameterSymbol> ParametersOf(VBTypeMemberSymbol member) => member switch
    {
        VBReturningMemberSymbol returning => returning.Parameters,
        VBProcedureMemberSymbol procedure => procedure.Parameters,
        VBEventMemberSymbol declaredEvent => declaredEvent.Parameters,
        _ => [],
    };

    internal static ImmutableArray<BoundTypedSymbol> LocalsOf(VBTypeMemberSymbol member) => member switch
    {
        VBReturningMemberSymbol returning => returning.Locals,
        VBProcedureMemberSymbol procedure => procedure.Locals,
        _ => [],
    };

    // MS-VBAL §5.2.3.1.4: "The specified type of an <as-auto-object> element MUST be a named class", of which an object is created - of an array's, each
    // element's. MS-VBA reads the name of a type that is no class at all - a number, a String, a Boolean, a Date, Variant, Object - as a syntax error, and
    // calls one that is no class - a user-defined type, an enumeration - an invalid use of New (Excel-verified, oracle\s19 N01-N07, N30).
    private static void CheckAutoInstantiation(Symbol declared, SourceRange range, Uri document, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        if (!declared.GetProperty(SymbolProperties.AutoInstantiated) || declared is not ITypedSymbol { ResolvedType: var declaredType })
        {
            return;
        }

        var location = new SourceLocation(document, range);
        var type = declaredType is VBArrayType array ? array.ItemType : declaredType;
        if (type is VBNumericType or VBStringType or VBBooleanType or VBDateType or VBVariantType or VBObjectType)
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, location, $"New {type.Name}"));
            return;
        }

        if (type is VBUserDefinedType or VBEnumType)
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfNewKeyword, location,
                string.Format(Exceptions.VBCompileError_InvalidUseOfNewKeyword_NotAClass_Verbose, declared.Name, type.Name)));
            return;
        }

        // MS-VBAL §5.2.3.1.2: a WithEvents variable is assigned the object whose events it handles; it is not one that makes an object of its own (MS-VBA, Excel-
        // verified, oracle\s19 N28).
        if (declared.GetProperty(SymbolProperties.WithEvents))
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfNewKeyword, location,
                string.Format(Exceptions.VBCompileError_InvalidUseOfNewKeyword_WithEvents_Verbose, declared.Name)));
            return;
        }

        // a document module's objects are its host's to make, in its own project too: MS-VBA refuses `Dim s As New Sheet1` (oracle\s49 D07).
        if (type is VBClassType { Symbol.IsDocument: true } hosted)
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfNewKeyword, location,
                string.Format(Exceptions.VBCompileError_InvalidUseOfNewKeyword_DocumentModule_Verbose, hosted.Symbol.Name)));
            return;
        }

        // MS-VBAL §5.2.3.1.4: "The instancing mode of the specified type of an <as-auto-object> MUST NOT be Public Not Creatable unless that type is defined in
        // the same project" - a class of a library that cannot be created. MS-VBA reports it when the variable is first referred to, as "Class not registered"
        // (Excel-verified, oracle\s19 N26-N27); the specification says it is invalid, and so does RD-VBA, before the code runs.
        if (type is VBClassType { Symbol: var classModule } && classModule.GetProperty(SymbolProperties.Library) is { } library
            && !classModule.GetProperty(SymbolProperties.Creatable))
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfNewKeyword, location,
                string.Format(Exceptions.VBCompileError_InvalidUseOfNewKeyword_NotCreatable_Verbose, declared.Name, classModule.Name, library)));
        }
    }

    // onlyAModule: the declaration is refused only when its type is a procedural module, and a name that does not resolve is left alone.
    private static void CheckDeclaredType(
        Symbol module, Symbol declared, SourceRange range, Uri document, ISymbolResolver resolver, ImmutableArray<VBCompileErrorInfo>.Builder errors,
        bool mayBeAny = false, bool onlyAModule = false)
    {
        if (declared is not ITypedSymbol { ResolvedType: var type } || Unresolved(type) is not { } name
            // `As Any` is a type only of a parameter of an external procedure, which takes whatever it is given (MS-VBAL §5.2.3.5 leaves its parameters to the
            // implementation).
            || mayBeAny && string.Equals(name, VBTypeNames.VBAny, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (bound, _) = TypeNameClassifier.Classify(name, module.Uri, resolver);
        if (bound is TypeNameClass.Type || onlyAModule && bound is TypeNameClass.Unresolved)
        {
            return;
        }

        if (NoTypeError(bound, name, new SourceLocation(document, range)) is { } error)
        {
            errors.Add(error);
            return;
        }

        // a qualifier that is nothing this project can see is most likely a library that is not referenced: the error says so, where the name alone would not.
        var qualifier = name.LastIndexOf('.') is >= 0 and var split ? name[..split] : null;
        var verbose = qualifier is not null && resolver.ResolveQualifier(qualifier, ScopeKind.Global, module.Uri).Symbol is null
            ? string.Format(Exceptions.VBCompileError_DeclaredTypeQualifierNotResolved_Verbose, name, qualifier)
            : string.Format(Exceptions.VBCompileError_DeclaredTypeNotResolved_Verbose, name);

        errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.UserDefinedTypeNotDefined, new SourceLocation(document, range), verbose));
    }

    /// <summary>
    /// The error of a name written as a type that binds something the type binding context binds, and that is no type (<strong>MS-VBAL §5.6.10</strong>): a
    /// procedural module, a library's, or a project. <see langword="null"/> for a type and for a name that does not resolve.
    /// </summary>
    /// <remarks>
    /// MS-VBA's three errors, Excel-verified (oracle\s29): "A module is not a valid type", "Automation type not supported in Visual Basic", "Expected
    /// user-defined type, not project".
    /// </remarks>
    internal static VBCompileErrorInfo? NoTypeError(TypeNameClass bound, string name, SourceLocation location) => bound switch
    {
        TypeNameClass.ProceduralModule => VBCompileErrorInfo.For(VBCompileErrorId.ModuleIsNotAValidType, location,
            string.Format(Exceptions.VBCompileError_DeclaredTypeIsAProceduralModule_Verbose, name)),
        TypeNameClass.LibraryModule => VBCompileErrorInfo.For(VBCompileErrorId.AutomationTypeNotSupported, location,
            string.Format(Exceptions.VBCompileError_DeclaredTypeIsALibraryModule_Verbose, name)),
        TypeNameClass.Project => VBCompileErrorInfo.For(VBCompileErrorId.ExpectedUserDefinedTypeNotProject, location,
            string.Format(Exceptions.VBCompileError_DeclaredTypeIsAProject_Verbose, name)),
        _ => null,
    };

    // the name a declared type was written with, when it did not resolve; for an array, its element's.
    private static string? Unresolved(VBType type) => type switch
    {
        VBUnresolvedType unresolved => unresolved.DeclaredName,
        VBArrayType array => Unresolved(array.ItemType),
        _ => null,
    };
}

/// <summary>
/// The rules of <see cref="DeclarationStaticSemanticsEvaluator"/> that depend on what else is defined.
/// </summary>
[Flags]
public enum DeclarationRules
{
    /// <summary>
    /// The rules that depend on nothing but the module.
    /// </summary>
    Default = 0,

    /// <summary>
    /// A declared type is a name that resolves to a type. Only to be asked once everything the module can see is defined: a module that is defined
    /// before the one it names has a name that does not resolve yet.
    /// </summary>
    DeclaredTypes = 1,
}
