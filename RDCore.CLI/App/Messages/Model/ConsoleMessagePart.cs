using System.Globalization;

namespace RDCore.CLI.App.Messages.Model;

public abstract record class ConsoleMessagePart(MessagePart Part, string Value) 
{
    public static int ParseConfigColor(string value, ConsoleColor fallback = ConsoleColor.Gray) =>
        int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexValue) ? hexValue :
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue) ? intValue :
        (int)Enum.Parse<ConsoleColor>(value, ignoreCase: true); // FormatException otherwise
}
