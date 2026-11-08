using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Serialization;

namespace RDCore.Tests.Model.AST;

/// <summary>
/// The flat tree writes a node a node holds as its index where the node is held by a property declared as a node, or as an array, an
/// <see cref="ImmutableArray{T}"/> or an <see cref="IEnumerable{T}"/> of nodes (<see cref="SyntaxNodeJson"/>). A node held any other way - by a property declared
/// as <see cref="object"/> or as an interface a node implements, or in a <see cref="List{T}"/> - would be written nested again, or not at all.
/// </summary>
[TestClass]
public sealed class FlatSyntaxTreeShapeTests
{
    private static readonly Type[] NodeTypes = [.. new[] { typeof(SyntaxNode).Assembly, typeof(ModuleParser).Assembly }
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => !type.IsAbstract && typeof(SyntaxNode).IsAssignableFrom(type))];

    [TestMethod]
    public void EveryWrittenPropertyOfANode_HoldsNodesAsTheFlatTreeWritesThem()
    {
        var problems = new List<string>();
        var seen = new HashSet<Type>();
        var pending = new Queue<Type>(NodeTypes);
        while (pending.TryDequeue(out var type))
        {
            if (!seen.Add(type))
            {
                continue;
            }

            foreach (var property in WrittenProperties(type))
            {
                var declared = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (Problem(declared) is { } problem)
                {
                    problems.Add($"{type.Name}.{property.Name} ({declared.Name}): {problem}");
                }
                else if (WrittenInside(declared) is { } inside)
                {
                    pending.Enqueue(inside);
                }
            }
        }

        Assert.IsTrue(seen.Count > NodeTypes.Length, "The walk looks into the records a node holds too.");
        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    // the properties System.Text.Json writes a type with: its public instance properties that can be read, but those it is told to ignore always.
    private static IEnumerable<PropertyInfo> WrittenProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always });

    // what is wrong with a property declared as a type, if anything.
    private static string? Problem(Type declared)
    {
        if (MayBeANodeUnseen(declared))
        {
            return "a node it holds is held as something other than a node";
        }

        if (ElementOf(declared) is { } element)
        {
            if (MayBeANodeUnseen(element))
            {
                return "a node it holds is held as something other than a node";
            }

            if (typeof(SyntaxNode).IsAssignableFrom(element) && !IsFlatSequence(declared))
            {
                return "a sequence of nodes other than an array, an ImmutableArray or an IEnumerable, which is written nested";
            }
        }

        return null;
    }

    // a type a value of which may be a node, which is not declared as one: object, or an interface a node implements.
    private static bool MayBeANodeUnseen(Type type)
        => type == typeof(object) || (type.IsInterface && ElementOf(type) is null && NodeTypes.Any(type.IsAssignableFrom));

    private static bool IsFlatSequence(Type type)
        => type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(ImmutableArray<>) || definition == typeof(IEnumerable<>)));

    // what a sequence holds: the value of a dictionary, the element of anything else.
    private static Type? ElementOf(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return null;
        }

        var element = type.IsArray ? type.GetElementType()
            : type.GetInterfaces().Append(type).FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];
        return element is { IsGenericType: true } pair && pair.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ? pair.GetGenericArguments()[1] : element;
    }

    // the type of the platform's own whose properties a value declared as a type is written with: a record a node holds, or the records of a sequence. A type
    // with a converter of its own (a VBTypedValue) is written by it, not by its properties.
    private static Type? WrittenInside(Type declared)
    {
        var type = ElementOf(declared) ?? declared;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.Namespace?.StartsWith("RDCore", StringComparison.Ordinal) == true && !typeof(SyntaxNode).IsAssignableFrom(type) && !type.IsEnum
            && type.GetCustomAttribute<JsonConverterAttribute>(inherit: true) is null
            ? type
            : null;
    }
}
