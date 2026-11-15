using RDCore.SDK.Model;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

// reports into a list rather than on the first error: a statement's operands are largely independent. An operand of unknown type is deferred, not rejected.
internal sealed class StatementOperandChecks(StaticEvaluationContext context)
{
    private readonly ImmutableArray<VBCompileErrorInfo>.Builder _errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

    public ImmutableArray<VBCompileErrorInfo> Errors => _errors.ToImmutable();

    public void Add(VBCompileErrorInfo error) => _errors.Add(error);

    public VBType? TypeOf(ExpressionNode expression)
    {
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context, expression);
        if (result.IsError)
        {
            _errors.Add(result.ErrorInfo!);
            return null;
        }

        return result.Result is null or VBUnknownType ? null : result.Result;
    }

    // MS-VBAL §2.2
    public void RequireScalar(ExpressionNode expression, VBType? type, string role)
    {
        if (type is VBArrayType or VBUserDefinedType)
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"{role} must have a scalar declared type, and this is {(type is VBArrayType ? "an array" : $"the user-defined type {type.Name}")}."));
        }
    }

    public void RequireStringOrVariant(ExpressionNode expression, VBType? type, string role)
    {
        if (type is not null && type is not (VBStringType or VBVariantType))
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"{role} must be declared as a String or a Variant, and this is a {type.Name}."));
        }
    }

    public void RequireNotAnObject(ExpressionNode expression, VBType? type, string role, bool throughUserDefinedTypes)
    {
        if (type is not null && IsObject(type, throughUserDefinedTypes))
        {
            _errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                $"{role} must not be declared as an Object or as a class{(throughUserDefinedTypes ? ", nor as a user-defined type that has one in it" : string.Empty)}, and this is a {type.Name}."));
        }
    }

    // the fields of a user-defined type are the type's as the composition has it now (CurrentTypes).
    private bool IsObject(VBType type, bool throughUserDefinedTypes) => type switch
    {
        VBObjectType or VBClassType => true,
        VBUserDefinedType udt when throughUserDefinedTypes
            => CurrentTypes.Of(context.Resolver, udt).Fields().Any(field => field.ResolvedType is { } fieldType && IsObject(fieldType, true)),
        _ => false,
    };

    // MS-VBAL §5.5.1.1
    public void RequireLetCoercible(ExpressionNode expression, VBType? type, VBType destination)
    {
        if (type is null)
        {
            return;
        }

        var result = LetCoercionStaticSemantics.Instance.DetermineDeclaredType(context, expression, type, destination);
        if (result.IsError)
        {
            _errors.Add(result.ErrorInfo!);
        }
    }

    // MS-VBAL §5.6.1, §5.6.10
    public void RequireVariable(ExpressionNode expression, string role, string? section = null)
        => Report(expression, NotAVariable(expression, LibraryFunctions.None), $"{role} must be a variable", section);

    // MS-VBAL §5.4.3.8
    public void RequireLetTarget(ExpressionNode expression, string role, string section)
        => Report(expression, NotAVariable(expression, LibraryFunctions.OfAClassObjectOrVariant), $"{role} must be a variable", section);

    public void RequireAssignmentTarget(ExpressionNode expression, string role, string section, bool isLet)
    {
        if (ConstantNamedBy(expression) is { } constant)
        {
            Report(expression, $"the constant {constant}", $"{role} must be a variable", section, VBCompileErrorId.AssignmentToConstantNotPermitted);
        }
        else if (FunctionNamedBy(expression, isLet ? FunctionTarget.Let : FunctionTarget.Set) is { } function)
        {
            RequireFunctionTarget(expression, function, isLet ? FunctionTarget.Let : FunctionTarget.Set);
        }
        else if (isLet)
        {
            RequireLetTarget(expression, role, section);
        }
        else
        {
            RequireVariable(expression, role, section);
        }
    }

    public void RequireErasable(ExpressionNode expression)
    {
        if (FunctionNamedBy(expression, FunctionTarget.Erase) is { } function)
        {
            RequireFunctionTarget(expression, function, FunctionTarget.Erase);
        }
    }

    // what a function named as a target is used for.
    private enum FunctionTarget
    {
        Let,
        Set,
        Erase,
    }

    // A function named - by its name, or qualified by its module - as the target of an assignment or an element of an Erase statement, outside its own body,
    // is a call of it with no argument: what it returns is what is assigned, through its default member, or erased (MS-VBAL §5.4.3.8, §5.4.3.9, §5.4.3.4).
    // So is a property of a module that has no accessor for the assignment, the library's (Now, Timer) as the workspace's.
    // What cannot be one is a compile error, as MS-VBA has it (observed in Excel): a Let of one whose declared type is neither a Variant nor an
    // object is "Function call on left-hand side of assignment must return Variant or Object" - "Can't assign to read-only property" for a property -
    // before what its parameters are (`Left$ = "x"`); one that takes an argument is "Argument not optional" (`Left = "x"`, and first for a Set and an
    // Erase: `Set Left$ = Nothing`); a Set of one that returns no object is "Invalid use of property", and so is a Set of the library's Err, which MS-VBA's
    // library declares a property; an Erase of one that cannot return an array is "Expected array". What passes is called when it runs: what it returns is
    // error 424 for a Let or a Set that has no object to assign through, and error 13 for an Erase that has no array.
    private void RequireFunctionTarget(ExpressionNode expression, VBReturningMemberSymbol function, FunctionTarget use)
    {
        var holdsAnything = function.ResolvedType is VBVariantType or VBUnknownType;
        var holdsAnObject = function.ResolvedType is VBObjectType or VBClassType;
        var takesAnArgument = function.Parameters.Any(parameter => parameter.Name != Tokens.Me && !parameter.IsOptional && parameter is not ParamArrayParameterSymbol);
        var isProperty = function is VBPropertyGetMemberSymbol;
        var what = $"the {(isProperty ? "property" : "function")} {function.Name}";

        var (id, requirement) = use switch
        {
            FunctionTarget.Let when !holdsAnything && !holdsAnObject && isProperty
                => (VBCompileErrorId.CantAssignToReadOnlyProperty, "A property assigned to must have a Property Let, or return a Variant or an object"),
            FunctionTarget.Let when !holdsAnything && !holdsAnObject
                => (VBCompileErrorId.FunctionCallOnLeftHandSideMustReturnVariantOrObject, "A function assigned to outside its own body must return a Variant or an object"),
            _ when takesAnArgument => (VBCompileErrorId.ArgumentNotOptional, "A function named as a target is called with no argument, and must take none"),
            FunctionTarget.Set when !holdsAnything && !holdsAnObject
                => (VBCompileErrorId.InvalidUseOfProperty, "A function Set-assigned to outside its own body must return a Variant or an object"),
            FunctionTarget.Set when function.ResolvedType is VBClassType && IsOfALibrary(function)
                => (VBCompileErrorId.InvalidUseOfProperty, "An object a library's function returns is not Set-assigned through it, the way a property's is not"),
            FunctionTarget.Erase when !holdsAnything => (VBCompileErrorId.ExpectedArray, "A function named in an Erase statement must return a Variant, which can hold an array"),
            _ => ((VBCompileErrorId?)null, string.Empty),
        };

        if (id is { } error)
        {
            Report(expression, what, requirement, use == FunctionTarget.Erase ? "5.4.3.4" : use == FunctionTarget.Let ? "5.4.3.8" : "5.4.3.9", error);
        }
    }

    // the function an expression names, by its name or qualified by its module, when it is not the procedure the expression is written in - whose name
    // there is its result; or the property of a module it names, when that property has no accessor for what is done with it: a Let assigns a property
    // that has a Property Let through it, and a Set one that has a Property Set. A class's own property, which is invoked on the object, is none of these.
    private VBReturningMemberSymbol? FunctionNamedBy(ExpressionNode expression, FunctionTarget use)
    {
        var symbol = expression switch
        {
            SimpleNameExpressionNode name => Named(name),
            MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
                => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol,
            _ => null,
        };

        return symbol switch
        {
            VBFunctionMemberSymbol function when function.Uri.AbsoluteUri != context.Scope.Uri.AbsoluteUri => function,
            VBPropertyGetMemberSymbol property when property.Uri.AbsoluteUri != context.Scope.Uri.AbsoluteUri
                && property.Parameters is not [{ Name: Tokens.Me }, ..] && !HasAccessorFor(property, use) => property,
            _ => null,
        };
    }

    // whether the module that declares a property declares the accessor an assignment of the kind goes through, of the same name.
    private bool HasAccessorFor(VBPropertyGetMemberSymbol property, FunctionTarget use)
        => use != FunctionTarget.Erase && context.Scope.SelfAndAncestors()
            .SelectMany(scope => scope.DeclaredAs(property.Name))
            .Any(declared => declared.ParentUri.AbsoluteUri == property.ParentUri.AbsoluteUri
                && (use == FunctionTarget.Let ? declared is VBPropertyLetMemberSymbol : declared is VBPropertySetMemberSymbol));

    // the name of the constant an expression is: a constant the code declares, by its name or through its module, or a member of an enumeration.
    private string? ConstantNamedBy(ExpressionNode expression) => expression switch
    {
        SimpleNameExpressionNode name when Named(name) is VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol => name.IdentifierName,
        MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
            => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol is VBConstantMemberSymbol or VBEnumConstMemberSymbol
                ? access.Member.IdentifierName
                : null,
        MemberAccessExpressionNode { Owner: { } owner } access
            when NameClassificationStaticSemantics.EnumerationOf(context, owner) is { } enumType
            => enumType.Members.FirstOrDefault(member => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase))?.Name,
        _ => null,
    };

    // MS-VBAL §5.4.3.4
    public void RequireNotAValue(ExpressionNode expression, string role, string section)
        => Report(expression, NotAVariable(expression, LibraryFunctions.Any), $"{role} must be a variable, a property or a function", section);

    // which of a library's functions an operand may be.
    private enum LibraryFunctions
    {
        // none: what is to be a variable is never a function.
        None,

        // one whose declared type is a class, Object or Variant: the target of a Let statement (MS-VBAL §5.4.3.8).
        OfAClassObjectOrVariant,

        // any: an element of an Erase statement (MS-VBAL §5.4.3.4).
        Any,
    }

    private void Report(ExpressionNode expression, string? what, string requirement, string? section, VBCompileErrorId id = VBCompileErrorId.VariableRequired)
    {
        if (what is not null)
        {
            _errors.Add(VBCompileErrorInfo.For(id, expression.Location,
                $"{requirement}, and this is {what}{(section is null ? string.Empty : $" (MS-VBAL §{section})")}."));
        }
    }

    // what an expression that is certainly not a variable is, in words; null for one that may be - and for a function of a library that will do.
    private string? NotAVariable(ExpressionNode expression, LibraryFunctions functions) => expression switch
    {
        LiteralExpressionNode => "a literal",
        VBOperatorExpression => "the result of an operator",
        SimpleNameExpressionNode name => NotAVariable(Named(name), functions),
        // MS-VBAL §5.6.12: a member of a project or a procedural module is the member it resolves to in the namespace.
        MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
            => NotAVariable(context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol, functions),
        // and a member of an Enum is one of its constants (MS-VBAL §5.2.3.4); a name that is none of them is not found, which is another error.
        MemberAccessExpressionNode { Owner: { } owner } access
            when NameClassificationStaticSemantics.EnumerationOf(context, owner) is { } enumType
            && enumType.Members.FirstOrDefault(member => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)) is { } constant
            => $"the constant {constant.Name}",
        _ => null,
    };

    // what a simple name is. MS-VBAL §5.6.10 matches a name by its identifier, and only then judges its type character: a typed name (Left$) whose
    // identifier is a declaration of the workspace - a variable, a parameter, a procedure - is that declaration, even where the name as written finds
    // the library's member named with the character. An identifier that names no one declaration (two modules declare it) gets no verdict here:
    // which of them it is, is not known, and the library's member is not what it names.
    private Symbol? Named(SimpleNameExpressionNode name)
    {
        var symbol = context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol;
        if (name.TypeHint is { Length: > 0 } && symbol is VBFunctionMemberSymbol && IsOfALibrary(symbol))
        {
            var identified = context.Resolver.ResolveValue(name.IdentifierName, ScopeKind.Local, context.Scope.Uri);
            if (identified.Symbol is { } declaration)
            {
                return IsOfALibrary(declaration) ? symbol : declaration;
            }

            if (!identified.IsUnbound)
            {
                return null;
            }
        }

        return symbol;
    }

    private static bool IsOfALibrary(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library) is not null;

    private static string? NotAVariable(Symbol? symbol, LibraryFunctions functions) => symbol switch
    {
        VBConstantMemberSymbol or VBLocalConstantSymbol or VBEnumConstMemberSymbol => $"the constant {symbol.Name}",
        VBFunctionMemberSymbol function when function.GetProperty(SymbolProperties.Library) is { } library && !Admits(functions, function)
            => $"the function {function.Name} of the {library} library",
        _ => null,
    };

    // whether a function of a library will do. A declared type that is not known yet (a library's class named before it is read) may be a class.
    private static bool Admits(LibraryFunctions functions, VBFunctionMemberSymbol function) => functions switch
    {
        LibraryFunctions.Any => true,
        LibraryFunctions.OfAClassObjectOrVariant => function.ResolvedType is VBClassType or VBObjectType or VBVariantType or VBUnknownType,
        _ => false,
    };
}
