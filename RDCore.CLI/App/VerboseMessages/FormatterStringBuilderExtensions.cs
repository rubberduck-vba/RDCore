using System.Text;

namespace RDCore.CLI.App.VerboseMessages;

public static class FormatterStringBuilderExtensions
{
    public static StringBuilder WithNewLine(this StringBuilder builder) 
        => builder.AppendLine();

    public static StringBuilder WithWhitespace(this StringBuilder builder, int count = 1) 
        => builder.Append(' ', count);

    public static StringBuilder WithIndent(this StringBuilder builder, string content, int indent = 4) 
        => builder.WithWhitespace(indent).Append(content);

    public static StringBuilder WithIcon(this StringBuilder builder, string icon, bool condition = true) 
        => condition ? builder.Append(icon).WithWhitespace() : builder;

    public static StringBuilder WithIconIf(this StringBuilder builder, bool condition, string icon) => builder.WithIcon(icon, condition);

    public static StringBuilder WithConditional(this StringBuilder builder, bool condition, string value)
    {
        if (condition)
        {
            builder.Append(value);
        }
        return builder;
    }

    public static StringBuilder WithConditional(this StringBuilder builder, bool condition, Func<StringBuilder, StringBuilder> conditional) 
        => condition ? conditional(builder) : builder;

    public static StringBuilder WithEnclosed(this StringBuilder builder, string prefix, string enclosed, string postfix) 
        => builder.Append(prefix).Append(enclosed).Append(postfix);

    public static StringBuilder WithEnclosed(this StringBuilder builder, string prefix, Func<StringBuilder, StringBuilder> enclosed, string postfix)
    {
        builder.Append(prefix);
        return enclosed(builder).Append(postfix);
    }

    public static StringBuilder WithEnclosedIf(this StringBuilder builder, bool show, string prefix, string enclosed, string postfix)
        => show ? builder.WithEnclosed(prefix, enclosed, postfix) : builder;

    public static StringBuilder WithEnclosedIf(this StringBuilder builder, bool show, string prefix, Func<StringBuilder, StringBuilder> enclosed, string postfix)
        => show ? builder.WithEnclosed(prefix, enclosed, postfix) : builder;
}
