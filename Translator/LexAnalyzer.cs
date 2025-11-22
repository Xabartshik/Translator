// ===================== LexAnalyzer.cs =====================
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexer;

/// <summary>
/// Типы токенов (упрощённый C++).
/// </summary>
public enum TokenType
{
    // Базовые токены
    Identifier,      // [a-zA-Z_][a-zA-Z0-9_]*
    Number,          // 123, 3.14, 0xFF, 0b101
    StringLiteral,   // "hello"
    CharLiteral,     // 'a', '\n'
    BoolLiteral,     // true, false
    Keyword,         // if, else, for, while, int, bool, ...

    // Операторы и разделители
    Operator,        // + - * / == != >= <= && || = < > ! & | ^ ~ << >> ? :
    LParen,          // (
    RParen,          // )
    LBrace,          // {
    RBrace,          // }
    LBracket,        // [
    RBracket,        // ]
    Semicolon,       // ;
    Comma,           // ,
    Dot,             // .
    Colon,           // :
    Arrow,           // ->
    DoubleColon,     // ::

    // Служебные токены
    Preprocessor,    // #include, #define ...
    Comment,         // // ...  или  /* ... */
    Whitespace,      // пробелы, табы, переводы строки (пропускается на верхнем уровне)
    EndOfFile,
    Unknown
}

/// <summary>
/// Один токен.
/// </summary>
public readonly record struct Token(TokenType Type, string Lexeme, int Line, int Column);

/// <summary>
/// Лексический анализатор для упрощённого C++.
/// </summary>
public sealed class LexAnalyzer
{
    private readonly string _input;
    private int _pos = 0;
    private int _line = 1;
    private int _column = 1;

    private const char EOF_CHAR = '\0';

    public List<(int Line, int Col, string Message)> Errors { get; } = new();

    public LexAnalyzer(string input)
    {
        _input = input ?? string.Empty;
    }

    public List<Token> Scan()
    {
        var tokens = new List<Token>();
        while (_pos < _input.Length)
        {
            var token = ScanToken();
            if (token.Type != TokenType.Unknown &&
                token.Type != TokenType.Whitespace &&
                token.Type != TokenType.Comment)
            {
                tokens.Add(token);
            }
        }

        tokens.Add(new Token(TokenType.EndOfFile, string.Empty, _line, _column));
        return tokens;
    }

    private Token ScanToken()
    {
        SkipWhitespace();
        if (_pos >= _input.Length)
            return new Token(TokenType.EndOfFile, string.Empty, _line, _column);

        char ch = _input[_pos];

        // Комментарии и препроцессор
        if (ch == '/' && PeekNext() == '/')
        {
            SkipLineComment();
            return new Token(TokenType.Comment, "//", _line, _column);
        }

        if (ch == '/' && PeekNext() == '*')
        {
            SkipBlockComment();
            return new Token(TokenType.Comment, "/* */", _line, _column);
        }

        if (ch == '#')
            return ScanPreprocessor();

        // Числа
        if (char.IsDigit(ch))
            return ScanNumber();

        // Строки и символы
        if (ch == '"')
            return ScanStringLiteral();

        if (ch == '\'')
            return ScanCharLiteral();

        // Идентификаторы / ключевые слова
        if (char.IsLetter(ch) || ch == '_')
            return ScanIdentifierOrKeyword();

        // Операторы / разделители
        return ScanOperatorOrDelimiter();
    }

    private void SkipWhitespace()
    {
        while (_pos < _input.Length && char.IsWhiteSpace(_input[_pos]))
        {
            if (_input[_pos] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _pos++;
        }
    }

    private char Current =>
        _pos < _input.Length ? _input[_pos] : EOF_CHAR;

    private char PeekNext() =>
        _pos + 1 < _input.Length ? _input[_pos + 1] : EOF_CHAR;

    private char PeekAhead(int offset) =>
        _pos + offset < _input.Length ? _input[_pos + offset] : EOF_CHAR;

    private void Advance()
    {
        if (_pos < _input.Length)
        {
            if (_input[_pos] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _pos++;
        }
    }

    // ----- комментарии -----

    private void SkipLineComment()
    {
        Advance(); // '/'
        Advance(); // '/'
        while (_pos < _input.Length && _input[_pos] != '\n')
            Advance();
    }

    private void SkipBlockComment()
    {
        Advance(); // '/'
        Advance(); // '*'
        while (_pos < _input.Length)
        {
            if (_input[_pos] == '*' && PeekNext() == '/')
            {
                Advance(); // '*'
                Advance(); // '/'
                break;
            }
            Advance();
        }
    }

    // ----- препроцессор -----

    private Token ScanPreprocessor()
    {
        int startLine = _line;
        int startCol = _column;
        var sb = new StringBuilder();

        while (_pos < _input.Length && _input[_pos] != '\n')
        {
            sb.Append(_input[_pos]);
            Advance();
        }

        return new Token(TokenType.Preprocessor, sb.ToString(), startLine, startCol);
    }

    // ----- числа -----

    private Token ScanNumber()
    {
        int startLine = _line;
        int startCol = _column;
        var sb = new StringBuilder();

        // 0x... / 0b...
        if (Current == '0' && (PeekNext() == 'x' || PeekNext() == 'X'))
        {
            sb.Append(Current);
            Advance();
            sb.Append(Current);
            Advance();
            while (_pos < _input.Length && IsHexDigit(Current))
            {
                sb.Append(Current);
                Advance();
            }
            return new Token(TokenType.Number, sb.ToString(), startLine, startCol);
        }

        if (Current == '0' && (PeekNext() == 'b' || PeekNext() == 'B'))
        {
            sb.Append(Current);
            Advance();
            sb.Append(Current);
            Advance();
            while (_pos < _input.Length && (Current == '0' || Current == '1'))
            {
                sb.Append(Current);
                Advance();
            }
            return new Token(TokenType.Number, sb.ToString(), startLine, startCol);
        }

        // десятичные
        while (_pos < _input.Length && char.IsDigit(Current))
        {
            sb.Append(Current);
            Advance();
        }

        // вещественные
        if (Current == '.' && char.IsDigit(PeekNext()))
        {
            sb.Append(Current);
            Advance();
            while (_pos < _input.Length && char.IsDigit(Current))
            {
                sb.Append(Current);
                Advance();
            }
        }

        // суффиксы (f, u, l, ...)
        while (_pos < _input.Length && (char.IsLetter(Current) || Current == '_'))
        {
            sb.Append(Current);
            Advance();
        }

        return new Token(TokenType.Number, sb.ToString(), startLine, startCol);
    }

    private static bool IsHexDigit(char ch) =>
        char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');

    // ----- строки -----

    private Token ScanStringLiteral()
    {
        int startLine = _line;
        int startCol = _column;
        var sb = new StringBuilder();

        sb.Append(Current); // "
        Advance();

        while (_pos < _input.Length && Current != '"')
        {
            if (Current == '\\')
            {
                sb.Append(Current);
                Advance();
                if (_pos < _input.Length)
                {
                    sb.Append(Current);
                    Advance();
                }
            }
            else
            {
                sb.Append(Current);
                Advance();
            }
        }

        if (Current == '"')
        {
            sb.Append(Current);
            Advance();
        }
        else
        {
            Errors.Add((_line, _column, "Незакрытая строка"));
        }

        return new Token(TokenType.StringLiteral, sb.ToString(), startLine, startCol);
    }

    // ----- символьные литералы -----

    private Token ScanCharLiteral()
    {
        int startLine = _line;
        int startCol = _column;
        var sb = new StringBuilder();

        sb.Append(Current); // '
        Advance();

        if (Current == '\\') // escape-последовательность
        {
            sb.Append(Current);
            Advance();
            if (_pos < _input.Length)
            {
                sb.Append(Current);
                Advance();
            }
        }
        else if (Current != '\'' && Current != EOF_CHAR)
        {
            sb.Append(Current);
            Advance();
        }

        if (Current == '\'')
        {
            sb.Append(Current);
            Advance();
        }
        else
        {
            Errors.Add((_line, _column, "Незакрытый символьный литерал"));
        }

        return new Token(TokenType.CharLiteral, sb.ToString(), startLine, startCol);
    }

    // ----- идентификаторы / ключевые слова -----

    private Token ScanIdentifierOrKeyword()
    {
        int startLine = _line;
        int startCol = _column;
        var sb = new StringBuilder();

        while (_pos < _input.Length && (char.IsLetterOrDigit(Current) || Current == '_'))
        {
            sb.Append(Current);
            Advance();
        }

        string lexeme = sb.ToString();

        // Проверяем на булевы литералы
        if (lexeme == "true" || lexeme == "false")
            return new Token(TokenType.BoolLiteral, lexeme, startLine, startCol);

        if (IsKeyword(lexeme))
            return new Token(TokenType.Keyword, lexeme, startLine, startCol);

        return new Token(TokenType.Identifier, lexeme, startLine, startCol);
    }

    private static bool IsKeyword(string word)
    {
        // Набор ключевых слов C / C++
        var cppKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            // типы
            "int", "float", "double", "char", "bool", "void", "long", "short",
            "unsigned", "signed", "auto", "const", "static", "volatile",

            // управление потоком
            "if", "else", "switch", "case", "default", "break", "continue",
            "for", "while", "do", "return", "goto",

            // логические
            "and", "or", "not", "xor",

            // прочее
            "struct", "class", "union", "enum", "namespace", "using",
            "new", "delete", "template", "typename",
            "public", "private", "protected"
        };

        return cppKeywords.Contains(word);
    }

    // ----- операторы / разделители -----

    private Token ScanOperatorOrDelimiter()
    {
        int startLine = _line;
        int startCol = _column;
        char ch = Current;

        // двухсимвольные операторы
        if (ch == '=' && PeekNext() == '=')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, "==", startLine, startCol);
        }

        if (ch == '!' && PeekNext() == '=')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, "!=", startLine, startCol);
        }

        if (ch == '<' && PeekNext() == '=')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, "<=", startLine, startCol);
        }

        if (ch == '>' && PeekNext() == '=')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, ">=", startLine, startCol);
        }

        if (ch == '&' && PeekNext() == '&')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, "&&", startLine, startCol);
        }

        if (ch == '|' && PeekNext() == '|')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, "||", startLine, startCol);
        }

        if (ch == '+' && PeekNext() == '+')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, "++", startLine, startCol);
        }

        if (ch == '-' && PeekNext() == '-')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, "--", startLine, startCol);
        }

        if (ch == '-' && PeekNext() == '>')
        {
            Advance(); Advance();
            return new Token(TokenType.Arrow, "->", startLine, startCol);
        }

        if (ch == ':' && PeekNext() == ':')
        {
            Advance(); Advance();
            return new Token(TokenType.DoubleColon, "::", startLine, startCol);
        }

        if (ch == '<' && PeekNext() == '<')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, "<<", startLine, startCol);
        }

        if (ch == '>' && PeekNext() == '>')
        {
            Advance(); Advance();
            return new Token(TokenType.Operator, ">>", startLine, startCol);
        }

        // одиночные символы
        Advance();
        return ch switch
        {
            '(' => new Token(TokenType.LParen, "(", startLine, startCol),
            ')' => new Token(TokenType.RParen, ")", startLine, startCol),
            '{' => new Token(TokenType.LBrace, "{", startLine, startCol),
            '}' => new Token(TokenType.RBrace, "}", startLine, startCol),
            '[' => new Token(TokenType.LBracket, "[", startLine, startCol),
            ']' => new Token(TokenType.RBracket, "]", startLine, startCol),
            ';' => new Token(TokenType.Semicolon, ";", startLine, startCol),
            ',' => new Token(TokenType.Comma, ",", startLine, startCol),
            '.' => new Token(TokenType.Dot, ".", startLine, startCol),
            ':' => new Token(TokenType.Colon, ":", startLine, startCol),
            '+' => new Token(TokenType.Operator, "+", startLine, startCol),
            '-' => new Token(TokenType.Operator, "-", startLine, startCol),
            '*' => new Token(TokenType.Operator, "*", startLine, startCol),
            '/' => new Token(TokenType.Operator, "/", startLine, startCol),
            '%' => new Token(TokenType.Operator, "%", startLine, startCol),
            '=' => new Token(TokenType.Operator, "=", startLine, startCol),
            '<' => new Token(TokenType.Operator, "<", startLine, startCol),
            '>' => new Token(TokenType.Operator, ">", startLine, startCol),
            '!' => new Token(TokenType.Operator, "!", startLine, startCol),
            '&' => new Token(TokenType.Operator, "&", startLine, startCol),
            '|' => new Token(TokenType.Operator, "|", startLine, startCol),
            '^' => new Token(TokenType.Operator, "^", startLine, startCol),
            '~' => new Token(TokenType.Operator, "~", startLine, startCol),
            '?' => new Token(TokenType.Operator, "?", startLine, startCol),
            _ => new Token(TokenType.Unknown, ch.ToString(), startLine, startCol)
        };
    }
}
