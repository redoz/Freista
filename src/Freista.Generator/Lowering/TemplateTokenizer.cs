using System.Collections.Generic;
using System.Text;

namespace Freista.Generator.Lowering;

/// <summary>Splits a display-name template into literal runs and <c>{placeholder}</c> tokens.</summary>
internal static class TemplateTokenizer
{
    public readonly record struct Token(string Text, bool IsPlaceholder);

    public static List<Token> Tokenize(string template)
    {
        var tokens = new List<Token>();
        var literal = new StringBuilder();

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end > i)
                {
                    if (literal.Length > 0)
                    {
                        tokens.Add(new Token(literal.ToString(), false));
                        literal.Clear();
                    }

                    tokens.Add(new Token(template.Substring(i + 1, end - i - 1).Trim(), true));
                    i = end;
                    continue;
                }
            }

            literal.Append(c);
        }

        if (literal.Length > 0)
        {
            tokens.Add(new Token(literal.ToString(), false));
        }

        return tokens;
    }
}
