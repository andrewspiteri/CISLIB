using System.Text;
using CIS.Text;

namespace CIS.RuleEngine;

internal sealed class RuleWildcard
{
    private const int MaximumOperations = RuleBounds.MaximumStringBytes * RuleBounds.MaximumStringBytes;

    private readonly Token[] _tokens;

    private RuleWildcard(Token[] tokens) => _tokens = tokens;

    internal static RuleWildcard Compile(string pattern)
    {
        if (!IsBoundedScalarText(pattern))
        {
            throw new FormatException("Use a wildcard pattern of at most 512 UTF-8 bytes with valid Unicode scalars.");
        }

        List<Token> tokens = new(pattern.Length);
        bool escaping = false;
        foreach (Rune scalar in pattern.EnumerateRunes())
        {
            if (escaping)
            {
                if (scalar.Value is not ('*' or '?' or '\\'))
                {
                    throw new FormatException("A wildcard escape must precede an asterisk, question mark or backslash.");
                }

                tokens.Add(new Token(TokenKind.Literal, scalar));
                escaping = false;
                continue;
            }

            switch (scalar.Value)
            {
                case '\\':
                    escaping = true;
                    break;
                case '*':
                    tokens.Add(new Token(TokenKind.Many));
                    break;
                case '?':
                    tokens.Add(new Token(TokenKind.One));
                    break;
                default:
                    tokens.Add(new Token(TokenKind.Literal, scalar));
                    break;
            }
        }

        if (escaping)
        {
            throw new FormatException("A wildcard pattern cannot end in an escape.");
        }

        return new RuleWildcard(tokens.ToArray());
    }

    internal bool IsMatch(string text)
    {
        if (!IsBoundedScalarText(text))
        {
            throw new ArgumentException("Use input of at most 512 UTF-8 bytes with valid Unicode scalars.", nameof(text));
        }

        Span<Rune> scalars = stackalloc Rune[text.Length];
        int length = 0;
        foreach (Rune scalar in text.EnumerateRunes())
        {
            scalars[length++] = scalar;
        }

        if (_tokens.Length * length > MaximumOperations)
        {
            throw new InvalidOperationException("The wildcard comparison exceeds its operation limit.");
        }

        // Each column records whether the pattern prefix matches that many input scalars.
        Span<bool> previous = stackalloc bool[length + 1];
        Span<bool> current = stackalloc bool[length + 1];
        previous.Clear();
        previous[0] = true;

        foreach (Token token in _tokens)
        {
            current[0] = token.Kind == TokenKind.Many && previous[0];
            for (int column = 1; column <= length; column++)
            {
                current[column] = token.Kind == TokenKind.Many
                    ? previous[column] || current[column - 1]
                    : previous[column - 1] &&
                        (token.Kind == TokenKind.One || token.Literal == scalars[column - 1]);
            }

            Span<bool> swap = previous;
            previous = current;
            current = swap;
        }

        return previous[length];
    }

    private static bool IsBoundedScalarText(string text) =>
        UnicodeScalarText.IsValid(text, RuleBounds.MaximumStringBytes, allowControls: true) &&
        Encoding.UTF8.GetByteCount(text) <= RuleBounds.MaximumStringBytes;

    private enum TokenKind
    {
        Literal,
        One,
        Many
    }

    private readonly record struct Token(TokenKind Kind, Rune Literal = default);
}
