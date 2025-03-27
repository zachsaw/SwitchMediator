namespace Mediator.Switch.SourceGenerator.Extensions;

public static class StringExtensions
{
    public static string ToLowerFirst(this string str) =>
        string.IsNullOrEmpty(str) ? str : char.ToLower(str[0]) + str.Substring(1);

    public static string DropGenerics(this string str)
    {
        var index = str.IndexOf('<');
        return index < 0 ? str : str.Substring(0, index);
    }

    public static string ToVariableName(this string str) =>
        str.Replace('+', '_').Replace('.', '_');
}