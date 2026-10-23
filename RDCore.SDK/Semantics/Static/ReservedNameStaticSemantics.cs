using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The functions of the standard library whose names are <em>reserved names</em> (<strong>MS-VBAL §3.3.5.2</strong>), which MS-VBA parses with the
/// arguments they take: <c>Abs</c>, <c>Fix</c>, <c>Int</c>, <c>Sgn</c>, <c>Len</c>, <c>LenB</c>, the conversion functions, and <c>String</c>.
/// </summary>
/// <remarks>
/// <para>
/// MS-VBAL §3.3.5.2 says a reserved name is used "as if it was a normal program defined entity", and is silent about one written without its arguments.
/// MS-VBA refuses it as a syntax error (recorded in Excel): <c>Len</c>, <c>Len()</c>, <c>Abs(1, 2)</c> and <c>String(2)</c> are each "Syntax error" - and
/// not "Argument not optional", which a function of the library that has no reserved name (<c>CDec()</c>) is.
/// </para>
/// <para>
/// A name qualified by its library (<c>VBA.Len</c>) is a member like any other, and a named argument is left to the rules of arguments.
/// </para>
/// </remarks>
public static class ReservedNameStaticSemantics
{
    // how many arguments each takes, which is the one way MS-VBA parses a call of it.
    private static readonly ImmutableDictionary<string, int> _arities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Abs"] = 1, ["CBool"] = 1, ["CByte"] = 1, ["CCur"] = 1, ["CDate"] = 1, ["CDbl"] = 1, ["CInt"] = 1, ["CLng"] = 1, ["CLngLng"] = 1, ["CLngPtr"] = 1,
        ["CSng"] = 1, ["CStr"] = 1, ["CVar"] = 1, ["CVErr"] = 1, ["Fix"] = 1, ["Int"] = 1, ["Len"] = 1, ["LenB"] = 1, ["Sgn"] = 1, ["String"] = 2,
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The syntax error of a call of a reserved-name function with other than the arguments it takes, or <see langword="null"/>.
    /// </summary>
    /// <param name="call">An index expression: the call, when its callee is one of the functions.</param>
    public static VBCompileErrorInfo? CallError(IndexExpressionNode call)
    {
        if (call.Callee is not SimpleNameExpressionNode { IdentifierName: var name } || !_arities.TryGetValue(name, out var arity)
            || call.Arguments.Any(argument => argument is NamedArgumentNode))
        {
            return null;
        }

        var given = call.Arguments.Count(argument => argument is not MissingArgumentNode);
        return given == arity && given == call.Arguments.Length
            ? null
            : VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, call.Location, string.Format(Exceptions.VBCompileError_ReservedNameArguments_Verbose, name, arity));
    }

    /// <summary>
    /// The syntax errors of the reserved-name functions a module names without an argument list: one for each.
    /// </summary>
    /// <param name="module">The parsed module.</param>
    public static ImmutableArray<VBCompileErrorInfo> CheckSyntax(ModuleNode module)
    {
        var nodes = module.Descendants().ToList();
        // a callee is written with its arguments, and checked with them; a member of what qualifies it (`VBA.LenB`) is one like any other.
        var callees = nodes.OfType<IndexExpressionNode>().Select(call => (object)call.Callee)
            .Concat(nodes.OfType<MemberAccessExpressionNode>().Select(access => (object)access.Member))
            .ToHashSet(ReferenceEqualityComparer.Instance);
        return
        [
            .. nodes.OfType<SimpleNameExpressionNode>()
                .Where(name => _arities.TryGetValue(name.IdentifierName, out _) && !callees.Contains(name))
                .Select(name => VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, name.Location,
                    string.Format(Exceptions.VBCompileError_ReservedNameArguments_Verbose, name.IdentifierName, _arities[name.IdentifierName]))),
        ];
    }
}
