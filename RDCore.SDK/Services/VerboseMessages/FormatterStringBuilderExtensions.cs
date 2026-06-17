using System.Text;

namespace RDCore.SDK.Services.VerboseMessages;

public static class FormatterStringBuilderExtensions
{
    /// <summary>
    /// Appends the default line terminator to the current formatted string.
    /// </summary>
    public static StringBuilder WithNewLine(this StringBuilder builder) 
        => builder.AppendLine();

    /// <summary>
    /// Appends a specified number of spaces to the current formatted string.
    /// </summary>
    public static StringBuilder WithWhitespace(this StringBuilder builder, int count = 1) 
        => builder.Append(' ', count);

    /// <summary>
    /// Prepends the specified content with a number of specified spaces, then appends the indented content to the current formatted string.
    /// </summary>
    public static StringBuilder WithIndent(this StringBuilder builder, string content, int indent = 4) 
        => builder.WithWhitespace(indent).Append(content);

    /// <summary>
    /// Appends the specified icon/string, then an additional whitespace postfix.
    /// </summary>
    /// <remarks>
    /// Optionally conditional to support the more expressive <c>WithIconIf</c>. 
    /// </remarks>
    public static StringBuilder WithIcon(this StringBuilder builder, string icon, bool condition = true) 
        => condition ? builder.Append(icon).WithWhitespace() : builder;

    /// <summary>
    /// Parameterizes a <c>WithIcon</c> extension call if the provided <c>condition</c> is <c>true</c>.
    /// </summary>
    public static StringBuilder WithIconIf(this StringBuilder builder, bool condition, string icon) => builder.WithIcon(icon, condition);

    /// <summary>
    /// Appends the specified <c>value</c> if the specified <c>condition</c> is <c>true</c>.
    /// </summary>
    public static StringBuilder WithConditional(this StringBuilder builder, bool condition, string value)
    {
        if (condition)
        {
            builder.Append(value);
        }
        return builder;
    }

    /// <summary>
    /// Runs the provided <c>Func&lt;StringBuilder, StringBuilder&gt;</c> delegate to append <em>conditional</em> content.
    /// </summary>
    public static StringBuilder WithConditional(this StringBuilder builder, bool condition, Func<StringBuilder, StringBuilder> conditional) 
        => condition ? conditional(builder) : builder;

    /// <summary>
    /// Adds the specified <em>enclosed</em> content between the specified <c>prefix</c> and <c>postfix</c> strings.
    /// </summary>
    public static StringBuilder WithEnclosed(this StringBuilder builder, string prefix, string enclosed, string postfix) 
        => builder.Append(prefix).Append(enclosed).Append(postfix);

    /// <summary>
    /// Runs the provided <c>Func&lt;StringBuilder, StringBuilder&gt;</c> delegate <em>enclosed</em> between the specified <c>prefix</c> and <c>postfix</c> strings.
    /// </summary>
    public static StringBuilder WithEnclosed(this StringBuilder builder, string prefix, Func<StringBuilder, StringBuilder> enclosed, string postfix)
    {
        builder.Append(prefix);
        return enclosed(builder).Append(postfix);
    }

    /// <summary>
    /// Conditionally adds the specified <em>enclosed</em> content between the specified <c>prefix</c> and <c>postfix</c> strings.
    /// </summary>
    public static StringBuilder WithEnclosedIf(this StringBuilder builder, bool show, string prefix, string enclosed, string postfix)
        => show ? builder.WithEnclosed(prefix, enclosed, postfix) : builder;

    /// <summary>
    /// Conditionally runs the provided <c>Func&lt;StringBuilder, StringBuilder&gt;</c> delegate <em>enclosed</em> between the specified <c>prefix</c> and <c>postfix</c> strings.
    /// </summary>
    public static StringBuilder WithEnclosedIf(this StringBuilder builder, bool show, string prefix, Func<StringBuilder, StringBuilder> enclosed, string postfix)
        => show ? builder.WithEnclosed(prefix, enclosed, postfix) : builder;
}
