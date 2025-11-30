using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Lexer;

/// <summary>
/// Перечисление всех типов токенов, которые может распознать лексический анализатор.
/// </summary>
public enum TokenType
{
    // Базовые типы токенов
    Identifier,     // Идентификаторы: переменные, функции и т.д.
    Number,         // Числовые литералы: целые, вещественные, шестнадцатеричные, двоичные
    StringLiteral,  // Строковые литералы в двойных кавычках
    CharLiteral,    // Символьные литералы в одинарных кавычках
    BoolLiteral,    // Булевы значения: true, false
    Keyword,        // Зарезервированные ключевые слова языка

    // Операторы и разделители
    Operator,       // Математические и логические операторы
    LParen,         // Открывающая круглая скобка (
    RParen,         // Закрывающая круглая скобка )
    LBrace,         // Открывающая фигурная скобка {
    RBrace,         // Закрывающая фигурная скобка }
    LBracket,       // Открывающая квадратная скобка [
    RBracket,       // Закрывающая квадратная скобка ]
    Semicolon,      // Точка с запятой ;
    Comma,          // Запятая ,
    Dot,            // Точка .
    Colon,          // Двоеточие :
    Arrow,          // Стрелка ->
    DoubleColon,    // Двойное двоеточие ::

    // Служебные типы
    Preprocessor,   // Директивы препроцессора (#include, #define и т.д.)
    Comment,        // Комментарии: однострочные и многострочные
    Whitespace,     // Пробелы, табуляции, переводы строк
    EndOfFile,      // Конец входного потока
    Unknown         // Неизвестные символы (ошибка)
}

/// <summary>
/// Структура, представляющая один токен с типом, лексемой и позицией в исходном коде.
/// </summary>
public readonly record struct Token(TokenType Type, string Lexeme, int Line, int Column);

/// <summary>
/// Класс, определяющий грамматику языка программирования.
/// Содержит все правила для распознавания различных типов токенов.
/// </summary>
public class LanguageGrammar
{
    /// <summary>
    /// Набор ключевых слов языка (if, for, while, int и т.д.).
    /// </summary>
    public HashSet<string> Keywords { get; set; }

    /// <summary>
    /// Набор булевых литералов (обычно true и false).
    /// </summary>
    public HashSet<string> BooleanLiterals { get; set; }

    /// <summary>
    /// Префикс однострочного комментария (обычно "//").
    /// </summary>
    public string LineCommentStart { get; set; }

    /// <summary>
    /// Начало многострочного комментария (обычно "/*").
    /// </summary>
    public string BlockCommentStart { get; set; }

    /// <summary>
    /// Конец многострочного комментария (обычно "*/").
    /// </summary>
    public string BlockCommentEnd { get; set; }

    /// <summary>
    /// Символ, обозначающий директиву препроцессора (обычно '#').
    /// </summary>
    public char PreprocessorChar { get; set; }

    /// <summary>
    /// Символ-разделитель для строковых литералов (обычно '"').
    /// </summary>
    public char StringDelimiter { get; set; }

    /// <summary>
    /// Символ-разделитель для символьных литералов (обычно '\'').
    /// </summary>
    public char CharDelimiter { get; set; }

    /// <summary>
    /// Символ для экранирования специальных символов (обычно '\\').
    /// </summary>
    public char EscapeChar { get; set; }

    /// <summary>
    /// Список двухсимвольных операторов, упорядоченный по приоритету.
    /// Более специфичные операторы должны быть в начале.
    /// </summary>
    public List<string> TwoCharOperators { get; set; }

    /// <summary>
    /// Набор односимвольных операторов, которые могут быть началом двухсимвольных.
    /// </summary>
    public HashSet<char> SingleCharOperators { get; set; }

    /// <summary>
    /// Набор разделителей: скобки, точка с запятой, запятая и т.д.
    /// </summary>
    public HashSet<char> Delimiters { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр грамматики со значениями по умолчанию.
    /// </summary>
    public LanguageGrammar()
    {
        Keywords = new HashSet<string>(StringComparer.Ordinal);
        BooleanLiterals = new HashSet<string>(StringComparer.Ordinal);
        LineCommentStart = "//";
        BlockCommentStart = "/*";
        BlockCommentEnd = "*/";
        PreprocessorChar = '#';
        StringDelimiter = '"';
        CharDelimiter = '\'';
        EscapeChar = '\\';
        TwoCharOperators = new List<string>();
        SingleCharOperators = new HashSet<char>();
        Delimiters = new HashSet<char>();
    }
}

/// <summary>
/// Лексический анализатор, преобразующий исходный код в последовательность токенов.
/// Поддерживает различные грамматики через параметр LanguageGrammar.
/// </summary>
public sealed class LexAnalyzer
{
    private readonly string _input;
    private readonly LanguageGrammar _grammar;
    private int _pos = 0;
    private int _line = 1;
    private int _column = 1;
    private const char EOF_CHAR = '\0';

    /// <summary>
    /// Список ошибок, найденных при лексическом анализе.
    /// Каждая ошибка содержит строку, столбец и описание проблемы.
    /// </summary>
    public List<(int Line, int Col, string Message)> Errors { get; } = new();

    /// <summary>
    /// Инициализирует лексический анализатор с заданным входом и грамматикой.
    /// </summary>
    /// <param name="input">Исходный код для анализа.</param>
    /// <param name="grammar">Грамматика языка, которая определяет правила распознавания.</param>
    public LexAnalyzer(string input, LanguageGrammar grammar)
    {
        _input = input ?? string.Empty;
        _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
    }

    /// <summary>
    /// Сканирует входную строку и возвращает список токенов.
    /// Пропускает пробелы и комментарии, добавляет токен EndOfFile в конец.
    /// </summary>
    /// <returns>Список распознанных токенов.</returns>
    public List<Token> Scan()
    {
        var tokens = new List<Token>();
        while (_pos < _input.Length)
        {
            var token = ScanToken();
            // Включаем только значащие токены, пропускаем пробелы и комментарии
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

    /// <summary>
    /// Сканирует один токен из текущей позиции.
    /// Определяет тип токена и возвращает соответствующий объект Token.
    /// </summary>
    private Token ScanToken()
    {
        SkipWhitespace();
        if (_pos >= _input.Length)
            return new Token(TokenType.EndOfFile, string.Empty, _line, _column);

        char ch = _input[_pos];

        // Проверяем однострочные комментарии
        if (ch == _grammar.LineCommentStart[0] &&
            _grammar.LineCommentStart.Length > 1 &&
            PeekNext() == _grammar.LineCommentStart[1])
        {
            SkipLineComment();
            return new Token(TokenType.Comment, _grammar.LineCommentStart, _line, _column);
        }

        // Проверяем многострочные комментарии
        if (ch == _grammar.BlockCommentStart[0] &&
            _grammar.BlockCommentStart.Length > 1 &&
            PeekNext() == _grammar.BlockCommentStart[1])
        {
            SkipBlockComment();
            return new Token(TokenType.Comment, _grammar.BlockCommentStart + _grammar.BlockCommentEnd, _line, _column);
        }

        // Проверяем директивы препроцессора
        if (ch == _grammar.PreprocessorChar)
            return ScanPreprocessor();

        // Проверяем числовые литералы
        if (char.IsDigit(ch))
            return ScanNumber();

        // Проверяем строковые литералы
        if (ch == _grammar.StringDelimiter)
            return ScanStringLiteral();

        // Проверяем символьные литералы
        if (ch == _grammar.CharDelimiter)
            return ScanCharLiteral();

        // Проверяем идентификаторы и ключевые слова
        if (char.IsLetter(ch) || ch == '_')
            return ScanIdentifierOrKeyword();

        // Проверяем разделители
        if (_grammar.Delimiters.Contains(ch))
            return ScanDelimiter();

        // Проверяем операторы
        if (_grammar.SingleCharOperators.Contains(ch))
            return ScanOperatorOrDelimiter();

        // Неизвестный символ
        int startLine = _line;
        int startCol = _column;
        Advance();
        return new Token(TokenType.Unknown, ch.ToString(), startLine, startCol);
    }

    /// <summary>
    /// Пропускает пробелы, табуляции и переводы строк, обновляя счётчики строк и столбцов.
    /// </summary>
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

    /// <summary>
    /// Возвращает текущий символ без перемещения позиции.
    /// </summary>
    private char Current =>
        _pos < _input.Length ? _input[_pos] : EOF_CHAR;

    /// <summary>
    /// Возвращает следующий символ без перемещения позиции.
    /// </summary>
    private char PeekNext() =>
        _pos + 1 < _input.Length ? _input[_pos + 1] : EOF_CHAR;

    /// <summary>
    /// Возвращает символ на заданное смещение вперёд без перемещения позиции.
    /// </summary>
    private char PeekAhead(int offset) =>
        _pos + offset < _input.Length ? _input[_pos + offset] : EOF_CHAR;

    /// <summary>
    /// Перемещает позицию на один символ вперёд, обновляя строку и столбец.
    /// </summary>
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

    // ========== Сканирование комментариев ==========

    /// <summary>
    /// Пропускает однострочный комментарий до конца строки.
    /// </summary>
    private void SkipLineComment()
    {
        while (_pos < _input.Length && _input[_pos] != '\n')
            Advance();
    }

    /// <summary>
    /// Пропускает многострочный комментарий, ища символ конца.
    /// </summary>
    private void SkipBlockComment()
    {
        // Пропускаем начало комментария
        foreach (var ch in _grammar.BlockCommentStart)
            Advance();

        // Ищем конец комментария
        while (_pos < _input.Length)
        {
            bool found = true;
            for (int i = 0; i < _grammar.BlockCommentEnd.Length; i++)
            {
                if (_pos + i >= _input.Length || _input[_pos + i] != _grammar.BlockCommentEnd[i])
                {
                    found = false;
                    break;
                }
            }
            if (found)
            {
                foreach (var ch in _grammar.BlockCommentEnd)
                    Advance();
                break;
            }
            Advance();
        }
    }

    // ========== Сканирование препроцессора ==========

    /// <summary>
    /// Сканирует директиву препроцессора, начинающуюся с символа препроцессора.
    /// Собирает всю строку как один токен.
    /// </summary>
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

    // ========== Сканирование чисел ==========

    /// <summary>
    /// Сканирует числовые литералы: целые числа, вещественные, шестнадцатеричные и двоичные.
    /// Поддерживает суффиксы типов (f, u, l и т.д.).
    /// </summary>
    private Token ScanNumber()
    {
        int startLine = _line;
        int startCol = _column;
        var sb = new StringBuilder();

        // Проверяем шестнадцатеричные числа (0x...)
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

        // Проверяем двоичные числа (0b...)
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

        // Целая часть десятичного числа
        while (_pos < _input.Length && char.IsDigit(Current))
        {
            sb.Append(Current);
            Advance();
        }

        // Дробная часть вещественного числа
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

        // Суффиксы типов (f, u, l и т.д.)
        while (_pos < _input.Length && (char.IsLetter(Current) || Current == '_'))
        {
            sb.Append(Current);
            Advance();
        }

        return new Token(TokenType.Number, sb.ToString(), startLine, startCol);
    }

    /// <summary>
    /// Проверяет, является ли символ шестнадцатеричной цифрой (0-9, a-f, A-F).
    /// </summary>
    private static bool IsHexDigit(char ch) =>
        char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');

    // ========== Сканирование строк ==========

    /// <summary>
    /// Сканирует строковой литерал, включая обработку escape-последовательностей.
    /// Отслеживает незакрытые строки как ошибки.
    /// </summary>
    private Token ScanStringLiteral()
    {
        int startLine = _line;
        int startCol = _column;
        var sb = new StringBuilder();
        sb.Append(Current);
        Advance();

        while (_pos < _input.Length && Current != _grammar.StringDelimiter)
        {
            if (Current == _grammar.EscapeChar)
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

        if (Current == _grammar.StringDelimiter)
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

    // ========== Сканирование символов ==========

    /// <summary>
    /// Сканирует символьный литерал, включая обработку escape-последовательностей.
    /// Отслеживает незакрытые литералы как ошибки.
    /// </summary>
    private Token ScanCharLiteral()
    {
        int startLine = _line;
        int startCol = _column;
        var sb = new StringBuilder();
        sb.Append(Current);
        Advance();

        if (Current == _grammar.EscapeChar)
        {
            sb.Append(Current);
            Advance();
            if (_pos < _input.Length)
            {
                sb.Append(Current);
                Advance();
            }
        }
        else if (Current != _grammar.CharDelimiter && Current != EOF_CHAR)
        {
            sb.Append(Current);
            Advance();
        }

        if (Current == _grammar.CharDelimiter)
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

    // ========== Сканирование идентификаторов и ключевых слов ==========

    /// <summary>
    /// Сканирует идентификаторы и ключевые слова.
    /// Проверяет, является ли идентификатор булевым литералом или ключевым словом.
    /// </summary>
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

        // Проверяем булевы литералы
        if (_grammar.BooleanLiterals.Contains(lexeme))
            return new Token(TokenType.BoolLiteral, lexeme, startLine, startCol);

        // Проверяем ключевые слова
        if (_grammar.Keywords.Contains(lexeme))
            return new Token(TokenType.Keyword, lexeme, startLine, startCol);

        return new Token(TokenType.Identifier, lexeme, startLine, startCol);
    }

    // ========== Сканирование разделителей ==========

    /// <summary>
    /// Сканирует разделители: скобки, точка с запятой, запятая, двоеточие и т.д.
    /// Обрабатывает двухсимвольные разделители (:: и ->).
    /// </summary>
    private Token ScanDelimiter()
    {
        int startLine = _line;
        int startCol = _column;
        char ch = Current;
        Advance();

        // Проверяем двухсимвольные разделители
        string twoChar = ch + (Current != EOF_CHAR ? Current.ToString() : "");
        if (ch == ':' && twoChar == "::")
        {
            Advance();
            return new Token(TokenType.DoubleColon, "::", startLine, startCol);
        }

        if (ch == '-' && twoChar == "->")
        {
            Advance();
            return new Token(TokenType.Arrow, "->", startLine, startCol);
        }

        // Односимвольные разделители
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
            _ => new Token(TokenType.Unknown, ch.ToString(), startLine, startCol)
        };
    }

    // ========== Сканирование операторов ==========

    /// <summary>
    /// Сканирует операторы, начиная с проверки двухсимвольных операторов,
    /// затем проверяя односимвольные операторы.
    /// </summary>
    private Token ScanOperatorOrDelimiter()
    {
        int startLine = _line;
        int startCol = _column;
        char ch = Current;

        // Пробуем двухсимвольные операторы
        foreach (var twoCharOp in _grammar.TwoCharOperators)
        {
            if (twoCharOp.Length == 2 && ch == twoCharOp[0] && PeekNext() == twoCharOp[1])
            {
                Advance();
                Advance();
                return new Token(TokenType.Operator, twoCharOp, startLine, startCol);
            }
        }

        // Односимвольный оператор
        Advance();
        return new Token(TokenType.Operator, ch.ToString(), startLine, startCol);
    }
}

/// <summary>
/// Фабрика для создания грамматик различных языков программирования.
/// Предоставляет статические методы для создания предконфигурированных грамматик.
/// </summary>
public static class LanguageGrammarFactory
{
    /// <summary>
    /// Создаёт грамматику для языка C++.
    /// Включает стандартные ключевые слова, операторы и встроенные идентификаторы std (cout, cin, endl и т.д.).
    /// </summary>
    /// <returns>Грамматика C++.</returns>
    public static LanguageGrammar CreateCppGrammar()
    {
        var grammar = new LanguageGrammar
        {
            Keywords = new HashSet<string>(StringComparer.Ordinal)
            {
                // Типы данных
                "int", "float", "double", "char", "bool", "void", "long", "short",
                "unsigned", "signed", "auto", "const", "static", "volatile",
                // Управление потоком выполнения
                "if", "else", "switch", "case", "default", "break", "continue",
                "for", "while", "do", "return", "goto",
                // Логические операторы
                "and", "or", "not", "xor",
                // Объектно-ориентированные конструкции
                "struct", "class", "union", "enum", "namespace", "using",
                "new", "delete", "template", "typename",
                "public", "private", "protected",
                // Встроенные идентификаторы std
                "cout", "cin", "cerr", "clog",
                "endl", "flush", "ws",
                "string", "vector", "map", "set", "list", "deque", "queue", "stack",
                "array", "pair", "tuple", "optional", "variant",
                "iostream", "iomanip", "algorithm", "numeric",
                "std"
            },
            BooleanLiterals = new HashSet<string>(StringComparer.Ordinal) { "true", "false" },
            LineCommentStart = "//",
            BlockCommentStart = "/*",
            BlockCommentEnd = "*/",
            PreprocessorChar = '#',
            StringDelimiter = '"',
            CharDelimiter = '\'',
            EscapeChar = '\\',
            // Двухсимвольные операторы (упорядочены по специфичности)
            TwoCharOperators = new List<string>
            {
                "==", "!=", "<=", ">=", "&&", "||",
                "<<", ">>", "->", "++", "--", "::"
            },
            // Односимвольные операторы
            SingleCharOperators = new HashSet<char>
            {
                '+', '-', '*', '/', '%', '=', '<', '>', '!', '&', '|', '^', '~', '?'
            },
            // Разделители
            Delimiters = new HashSet<char>
            {
                '(', ')', '{', '}', '[', ']', ';', ',', '.', ':', '-'
            }
        };
        return grammar;
    }

    /// <summary>
    /// Создаёт грамматику для языка Java.
    /// Включает стандартные ключевые слова и операторы Java.
    /// </summary>
    /// <returns>Грамматика Java.</returns>
    public static LanguageGrammar CreateJavaGrammar()
    {
        var grammar = new LanguageGrammar
        {
            Keywords = new HashSet<string>(StringComparer.Ordinal)
            {
                "abstract", "assert", "boolean", "break", "byte", "case", "catch",
                "char", "class", "const", "continue", "default", "do", "double",
                "else", "enum", "extends", "final", "finally", "float", "for",
                "goto", "if", "implements", "import", "instanceof", "int",
                "interface", "long", "native", "new", "package", "private",
                "protected", "public", "return", "short", "static", "strictfp",
                "super", "switch", "synchronized", "this", "throw", "throws",
                "transient", "try", "void", "volatile", "while"
            },
            BooleanLiterals = new HashSet<string>(StringComparer.Ordinal) { "true", "false" },
            LineCommentStart = "//",
            BlockCommentStart = "/*",
            BlockCommentEnd = "*/",
            PreprocessorChar = '@',
            StringDelimiter = '"',
            CharDelimiter = '\'',
            EscapeChar = '\\',
            TwoCharOperators = new List<string>
            {
                "==", "!=", "<=", ">=", "&&", "||",
                "<<", ">>", "->", "++", "--", "::"
            },
            SingleCharOperators = new HashSet<char>
            {
                '+', '-', '*', '/', '%', '=', '<', '>', '!', '&', '|', '^', '~', '?'
            },
            Delimiters = new HashSet<char>
            {
                '(', ')', '{', '}', '[', ']', ';', ',', '.', ':', '-'
            }
        };
        return grammar;
    }
}
