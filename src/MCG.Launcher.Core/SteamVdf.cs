using System.Text;

namespace MCG.Launcher.Core;

// Steam's manifests use Valve KeyValues, not JSON. Keep parsing separate from disk discovery.
public sealed class VdfNode
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Value(string key) => Values.GetValueOrDefault(key, "");
}

public static class SteamVdf
{
    public static VdfNode Parse(string text)
    {
        if (text.Length > 8 * 1024 * 1024) throw new FormatException("Manifesto Steam muito grande.");
        var position = 0;
        return ReadNode(false, 0);

        VdfNode ReadNode(bool nested, int depth)
        {
            if (depth > 64) throw new FormatException("Manifesto Steam com profundidade inválida.");
            var node = new VdfNode();
            while (Next() is { } key)
            {
                if (key == "}")
                {
                    if (!nested) throw new FormatException("Fechamento inesperado.");
                    return node;
                }
                if (key == "{") throw new FormatException("Chave ausente.");
                var value = Next() ?? throw new FormatException("Valor ausente.");
                if (value == "{") node.Children[key] = ReadNode(true, depth + 1);
                else if (value == "}") throw new FormatException("Valor inválido.");
                else node.Values[key] = value;
            }
            if (nested) throw new FormatException("Manifesto incompleto.");
            return node;
        }

        string? Next()
        {
            while (position < text.Length)
            {
                if (char.IsWhiteSpace(text[position]) || text[position] == '\uFEFF') { position++; continue; }
                if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
                {
                    while (position < text.Length && text[position] != '\n') position++;
                    continue;
                }
                break;
            }
            if (position >= text.Length) return null;
            var first = text[position++];
            if (first is '{' or '}') return first.ToString();
            if (first != '"')
            {
                var start = position - 1;
                while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not '{' and not '}') position++;
                return text[start..position];
            }
            var builder = new StringBuilder();
            while (position < text.Length)
            {
                var character = text[position++];
                if (character == '"') return builder.ToString();
                if (character == '\\' && position < text.Length && text[position] is '\\' or '"')
                    character = text[position++];
                builder.Append(character);
            }
            throw new FormatException("Texto sem fechamento.");
        }
    }
}
