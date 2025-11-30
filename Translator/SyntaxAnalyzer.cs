using System;
using System.Collections.Generic;
using System.Linq;
using Lexer;

namespace Parser;

/// <summary>
/// Синтаксический анализатор для подмножества языка C++.
/// Преобразует последовательность токенов в абстрактное синтаксическое дерево (AST).
/// Поддерживает проверку типов, управление областями видимости и диагностику const.
/// </summary>
public sealed class SyntaxAnalyzer
{
    private readonly List<Token> _tokenList;
    private int _pos;
    private Token _lookahead;
    private bool _inErrorRecovery = false;
    public readonly ScopeManager _scopes = new();
    public List<(int Line, int Col, string Message)> Errors { get; } = new();

    /// <summary>
    /// Инициализирует синтаксический анализатор с заданным списком токенов.
    /// Фильтрует пробелы и комментарии из потока токенов.
    /// </summary>
    public SyntaxAnalyzer(IEnumerable<Token> tokens)
    {
        if (tokens == null) throw new ArgumentNullException(nameof(tokens));
        _tokenList = tokens
            .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comment)
            .ToList();
        _pos = 0;
        _lookahead = _pos < _tokenList.Count
            ? _tokenList[_pos]
            : new Token(TokenType.EndOfFile, string.Empty, -1, -1);
    }

    // ========== Основные операции со потоком токенов ==========

    /// <summary>
    /// Переходит к следующему токену в потоке.
    /// </summary>
    private void MoveNext()
    {
        if (_pos < _tokenList.Count)
            _pos++;
        _lookahead = _pos < _tokenList.Count
            ? _tokenList[_pos]
            : new Token(TokenType.EndOfFile, string.Empty, -1, -1);
    }

    /// <summary>
    /// Проверяет, совпадает ли текущий токен заданному типу и лексеме.
    /// </summary>
    private bool Is(TokenType type, string? lexeme = null) =>
        _lookahead.Type == type &&
        (lexeme == null || string.Equals(_lookahead.Lexeme, lexeme, StringComparison.Ordinal));

    /// <summary>
    /// Принимает текущий токен и переходит к следующему.
    /// </summary>
    private Token Consume()
    {
        var t = _lookahead;
        MoveNext();
        return t;
    }

    /// <summary>
    /// Проверяет, что текущий токен имеет заданный тип и лексему.
    /// Если нет, регистрирует ошибку.
    /// </summary>
    private Token Expect(TokenType type, string? lexeme, string message)
    {
        if (Is(type, lexeme))
            return Consume();
        if (!_inErrorRecovery)
        {
            _inErrorRecovery = true;
            Errors.Add((_lookahead.Line, _lookahead.Column, message));
        }
        return _lookahead;
    }

    /// <summary>
    /// Пропускает токены до точки с запятой или нового ключевого слова.
    /// Используется для восстановления после ошибок.
    /// </summary>
    private void SkipUntilSemicolonOrNewStatement()
    {
        while (!IsAtEnd() &&
               _lookahead.Type != TokenType.Semicolon &&
               !IsKeywordLike(_lookahead.Lexeme))
        {
            MoveNext();
        }
        if (Is(TokenType.Semicolon))
            Consume();
    }

    /// <summary>
    /// Проверяет, является ли строка началом нового ключевого слова или оператора.
    /// </summary>
    private bool IsKeywordLike(string lexeme)
    {
        string[] keywords = {
            "int", "float", "bool", "char", "string", "const", "void", "double", "long", "short",
            "if", "else", "for", "while", "do", "return", "break",
            "continue", "switch", "case", "default", "new", "delete"
        };
        return keywords.Contains(lexeme);
    }

    /// <summary>
    /// Проверяет, достигнут ли конец потока токенов.
    /// </summary>
    private bool IsAtEnd() => _lookahead.Type == TokenType.EndOfFile;

    /// <summary>
    /// Пропускает токены до точки с запятой (включительно).
    /// </summary>
    private void SkipToSemicolon()
    {
        while (_lookahead.Type != TokenType.Semicolon &&
               _lookahead.Type != TokenType.EndOfFile &&
               _lookahead.Type != TokenType.RBrace &&
               _lookahead.Type != TokenType.RParen)
        {
            MoveNext();
        }
        if (Is(TokenType.Semicolon))
            Consume();
    }

    /// <summary>
    /// Пропускает токены до одного из целевых типов.
    /// </summary>
    private void SkipTo(TokenType target1, TokenType target2 = TokenType.EndOfFile)
    {
        while (_lookahead.Type != target1 && _lookahead.Type != target2 &&
               _lookahead.Type != TokenType.LBrace && _lookahead.Type != TokenType.RBrace)
        {
            MoveNext();
        }
    }

    // ========== Точка входа ==========

    /// <summary>
    /// Начинает синтаксический анализ программы.
    /// Это главная точка входа для синтаксического анализатора.
    /// </summary>
    public ProgramNode? ParseProgram()
    {
        try
        {
            return ParseCppProgram();
        }
        catch (Exception ex)
        {
            Errors.Add((-1, -1, $"Критическая ошибка парсера: {ex.Message}"));
            return null;
        }
    }

    // ========== Программа C++ ==========

    /// <summary>
    /// Парсит полную программу на C++, содержащую директивы препроцессора,
    /// объявления переменных, определения функций и другие конструкции.
    /// </summary>
    private ProgramNode ParseCppProgram()
    {
        var stmts = new List<AstNode>();
        while (_lookahead.Type != TokenType.EndOfFile)
        {
            // Пропускаем директивы препроцессора
            if (_lookahead.Type == TokenType.Preprocessor)
            {
                Consume();
                while (Is(TokenType.Identifier) || Is(TokenType.Operator, "<") || Is(TokenType.Operator, ">"))
                    Consume();
                continue;
            }

            if (_lookahead.Type == TokenType.Semicolon)
            {
                Consume();
                continue;
            }

            if (_lookahead.Type == TokenType.RBrace)
            {
                Consume();
                continue;
            }

            // Обработка using namespace
            if (Is(TokenType.Keyword, "using"))
            {
                ParseUsingNamespace();
                continue;
            }

            var stmt = ParseCppStatementOrDeclaration();
            if (stmt != null)
            {
                stmts.Add(stmt);
                _inErrorRecovery = false;
            }
            else if (_lookahead.Type != TokenType.EndOfFile)
            {
                SkipToSemicolon();
                _inErrorRecovery = false;
            }
        }
        return new ProgramNode(stmts);
    }

    /// <summary>
    /// Парсит конструкцию "using namespace".
    /// Обрабатывает как простые названия пространств имён, так и KEYWORD токены (для std).
    /// </summary>
    private void ParseUsingNamespace()
    {
        Consume(); // "using"
        if (Is(TokenType.Keyword, "namespace"))
        {
            Consume(); // "namespace"
            // Обрабатываем имя пространства как Identifier ИЛИ Keyword
            if (_lookahead.Type == TokenType.Identifier || (_lookahead.Type == TokenType.Keyword && _lookahead.Lexeme == "std"))
            {
                var namespaceName = Consume().Lexeme;
            }
            else
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    "Ожидалось имя пространства имён после 'namespace'."));
            }

            if (!Is(TokenType.Semicolon))
                Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась ';' после using namespace."));
            else
                Consume();
        }
        else
        {
            // Возможно, что-то вроде "using std::cout;"
            while (!Is(TokenType.Semicolon) && _lookahead.Type != TokenType.EndOfFile)
                Consume();
            if (Is(TokenType.Semicolon))
                Consume();
        }
    }

    // ========== Объявления и определения ==========

    /// <summary>
    /// Парсит объявление переменной, объявление const, или определение функции.
    /// Анализирует модификаторы (const), тип, имя и ветвит по типу объявления.
    /// </summary>
    private AstNode? ParseCppStatementOrDeclaration()
    {
        bool isConst = false;
        int constLine = -1, constCol = -1;

        if (Is(TokenType.Keyword, "const"))
        {
            var constToken = Consume();
            isConst = true;
            constLine = constToken.Line;
            constCol = constToken.Column;
        }

        if (!IsCppType(_lookahead))
        {
            if (isConst)
                Errors.Add((constLine, constCol, "Ожидался тип после 'const'."));
            return ParseCppStatement();
        }

        var typeTok = Consume();
        SkipCppTemplateArguments();

        // Обработка указателей (int*, int**, и т.д.)
        string pointerPrefix = "";
        while (Is(TokenType.Operator, "*"))
        {
            pointerPrefix += "*";
            Consume();
        }

        if (_lookahead.Type != TokenType.Identifier)
        {
            Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидался идентификатор после типа."));
            return null;
        }

        var nameTok = Consume();

        // Ветвление: если ( после имени — это функция, иначе переменная
        if (Is(TokenType.LParen))
        {
            if (isConst)
                Errors.Add((constLine, constCol, "'const' не может использоваться для объявления функции."));
            return ParseCppFunctionDeclaration(typeTok, nameTok, pointerPrefix);
        }
        else
        {
            return ParseCppVarDeclTail(typeTok, nameTok, isConst, pointerPrefix);
        }
    }

    /// <summary>
    /// Проверяет, является ли токен типом данных C++.
    /// </summary>
    private bool IsCppType(Token tok)
    {
        if (tok.Type == TokenType.Keyword)
        {
            string[] types = {
                "int", "float", "double", "char", "bool", "void",
                "long", "short", "unsigned", "signed", "auto"
            };
            if (Array.Exists(types, t => t == tok.Lexeme))
                return true;
        }

        if (tok.Type == TokenType.Identifier && (tok.Lexeme == "vector" || tok.Lexeme == "string"))
            return true;

        return false;
    }

    /// <summary>
    /// Пропускает аргументы шаблона, например &lt; int, double &gt;.
    /// </summary>
    private void SkipCppTemplateArguments()
    {
        if (!Is(TokenType.Operator, "<"))
            return;

        int depth = 0;
        while (_lookahead.Type != TokenType.EndOfFile)
        {
            if (Is(TokenType.Operator, "<"))
            {
                depth++;
                Consume();
            }
            else if (Is(TokenType.Operator, ">"))
            {
                depth--;
                Consume();
                if (depth == 0)
                    break;
            }
            else
            {
                Consume();
            }
        }
    }

    /// <summary>
    /// Парсит остаток объявления переменной после прочтения типа и имени.
    /// Обрабатывает инициализацию, массивы и проверку типов.
    /// </summary>
    private AstNode ParseCppVarDeclTail(Token typeTok, Token nameTok, bool isConst, string pointerPrefix)
    {
        string varName = nameTok.Lexeme;
        string fullType = typeTok.Lexeme + pointerPrefix;

        // Обработка массивов: int arr[5]
        string arrayPostfix = "";
        while (Is(TokenType.LBracket))
        {
            Consume(); // [
            arrayPostfix += "[";

            if (!Is(TokenType.RBracket))
            {
                var sizeExpr = ParseCppExpr();
                arrayPostfix += "]";
            }
            else
            {
                arrayPostfix += "]";
            }

            if (!Is(TokenType.RBracket))
                Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась ']' в объявлении массива."));
            else
                Consume();
        }

        fullType += arrayPostfix;

        // Объявляем переменную в текущей области видимости
        _scopes.Declare(
            varName,
            kind: "var",
            type: fullType,
            isConst: isConst,
            line: nameTok.Line,
            column: nameTok.Column,
            errors: Errors);

        var idNode = new IdentifierNode(varName, nameTok.Line, nameTok.Column);
        var typeNode = new IdentifierNode(typeTok.Lexeme + pointerPrefix, typeTok.Line, typeTok.Column);

        AstNode? init = null;

        // Обработка инициализации (=)
        if (Is(TokenType.Operator, "="))
        {
            Consume();
            init = ParseCppExpr();

            // Проверка совместимости типов
            string exprType = GetExpressionType(init);
            if (!AreTypesCompatible(fullType, exprType))
            {
                Errors.Add((nameTok.Line, nameTok.Column,
                    $"Ошибка типов: несовместимые типы в инициализации {varName} ({fullType}) = ... ({exprType})"));
            }

            // Отмечаем переменную как инициализированную
            var entry = _scopes.Lookup(varName);
            if (entry != null)
                entry.IsInitialized = true;
        }

        // Константы должны быть инициализированы
        if (isConst && init == null)
        {
            Errors.Add((nameTok.Line, nameTok.Column,
                $"Константная переменная '{varName}' должна быть инициализирована."));
        }

        // Проверка точки с запятой
        if (!Is(TokenType.Semicolon))
        {
            Errors.Add((_lookahead.Line, _lookahead.Column,
                $"Ожидалась ';' после объявления переменной '{varName}' — обнаружена '{_lookahead.Lexeme}'"));
            SkipToSemicolon();
        }
        else
        {
            Consume();
        }

        var assign = new AssignNode(idNode, "=", init ?? new LiteralNode("void", "", nameTok.Line, nameTok.Column));
        var declNode = new BinaryNode("decl", typeNode, assign);
        return declNode;
    }

    /// <summary>
    /// Парсит объявление переменной в инициализации цикла for БЕЗ проверки точки с запятой.
    /// Точка с запятой обрабатывается отдельно в ParseCppForStatement().
    /// </summary>
    private AstNode ParseCppVarDeclForInit(Token typeTok, Token nameTok, bool isConst, string pointerPrefix)
    {
        string varName = nameTok.Lexeme;
        string fullType = typeTok.Lexeme + pointerPrefix;

        // Обработка массивов
        string arrayPostfix = "";
        while (Is(TokenType.LBracket))
        {
            Consume();
            arrayPostfix += "[";
            if (!Is(TokenType.RBracket))
            {
                var sizeExpr = ParseCppExpr();
                arrayPostfix += "]";
            }
            else
            {
                arrayPostfix += "]";
            }

            if (!Is(TokenType.RBracket))
                Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась ']' в объявлении массива."));
            else
                Consume();
        }

        fullType += arrayPostfix;

        _scopes.Declare(
            varName,
            kind: "var",
            type: fullType,
            isConst: isConst,
            line: nameTok.Line,
            column: nameTok.Column,
            errors: Errors);

        var idNode = new IdentifierNode(varName, nameTok.Line, nameTok.Column);
        var typeNode = new IdentifierNode(typeTok.Lexeme + pointerPrefix, typeTok.Line, typeTok.Column);

        AstNode? init = null;

        if (Is(TokenType.Operator, "="))
        {
            Consume();
            init = ParseCppExpr();

            string exprType = GetExpressionType(init);
            if (!AreTypesCompatible(fullType, exprType))
            {
                Errors.Add((nameTok.Line, nameTok.Column,
                    $"Ошибка типов: несовместимые типы в инициализации {varName} ({fullType}) = ... ({exprType})"));
            }

            var entry = _scopes.Lookup(varName);
            if (entry != null)
                entry.IsInitialized = true;
        }

        if (isConst && init == null)
        {
            Errors.Add((nameTok.Line, nameTok.Column,
                $"Константная переменная '{varName}' должна быть инициализирована."));
        }

        var assign = new AssignNode(idNode, "=", init ?? new LiteralNode("void", "", nameTok.Line, nameTok.Column));
        var declNode = new BinaryNode("decl", typeNode, assign);
        return declNode;
    }

    /// <summary>
    /// Парсит определение функции, включая тип возврата, имя, параметры и тело.
    /// </summary>
    private AstNode ParseCppFunctionDeclaration(Token typeToken, Token nameToken, string pointerPrefix)
    {
        // Объявляем функцию в текущей области видимости
        _scopes.Declare(
            nameToken.Lexeme,
            kind: "func",
            type: typeToken.Lexeme + pointerPrefix,
            isConst: false,
            line: nameToken.Line,
            column: nameToken.Column,
            errors: Errors);

        var funcEntry = _scopes.Lookup(nameToken.Lexeme);
        if (funcEntry != null)
            funcEntry.IsInitialized = false; // Функции не инициализируются

        var typeNode = new IdentifierNode(typeToken.Lexeme + pointerPrefix, typeToken.Line, typeToken.Column);
        var nameNode = new IdentifierNode(nameToken.Lexeme, nameToken.Line, nameToken.Column);

        // Пропускаем параметры до закрывающей скобки
        Consume(); // (
        int depth = 1;
        while (_lookahead.Type != TokenType.EndOfFile && depth > 0)
        {
            if (_lookahead.Type == TokenType.LParen) depth++;
            else if (_lookahead.Type == TokenType.RParen) depth--;
            if (depth > 0) Consume();
        }

        Expect(TokenType.RParen, null, "Ожидалась ')' в объявлении функции.");

        AstNode body;

        if (Is(TokenType.LBrace))
        {
            _scopes.EnterScope();
            body = ParseCppBlock();
            _scopes.ExitScope();
        }
        else
        {
            Errors.Add((nameToken.Line, nameToken.Column, "Предупреждение: отсутствует '{' после объявления функции — предполагается тело."));
            _scopes.EnterScope();
            var implicitStmts = new List<AstNode>();
            while (_lookahead.Type != TokenType.EndOfFile && !Is(TokenType.Keyword, "return"))
            {
                var stmt = ParseCppStatementOrDeclaration();
                if (stmt != null)
                    implicitStmts.Add(stmt);
                else
                    SkipToSemicolon();
            }

            if (Is(TokenType.Keyword, "return"))
                implicitStmts.Add(ParseCppStatement() ?? new LiteralNode("void", "", -1, -1));

            body = new ProgramNode(implicitStmts);
            _scopes.ExitScope();
        }

        return new BinaryNode("func-def", typeNode,
            new BinaryNode("func-params-body", nameNode, body));
    }

    // ========== Операторы ==========

    /// <summary>
    /// Парсит оператор: условный оператор (if/else), циклы (for, while, do-while),
    /// блоки кода, выражения и специальные операторы (return, break, continue).
    /// </summary>
    private AstNode? ParseCppStatement()
    {
        // Объявления
        if (Is(TokenType.Keyword, "const") || IsCppType(_lookahead))
            return ParseCppStatementOrDeclaration();

        // if
        if (Is(TokenType.Keyword, "if"))
            return ParseCppIfStatement();

        // while
        if (Is(TokenType.Keyword, "while"))
            return ParseCppWhileStatement();

        // do-while
        if (Is(TokenType.Keyword, "do"))
            return ParseCppDoWhileStatement();

        // for
        if (Is(TokenType.Keyword, "for"))
            return ParseCppForStatement();

        // Блок
        if (Is(TokenType.LBrace))
            return ParseCppBlock();

        // break
        if (Is(TokenType.Keyword, "break"))
        {
            var tok = Consume();
            if (!Is(TokenType.Semicolon))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    $"Ожидалась ';' после break — обнаружена '{_lookahead.Lexeme}'"));
                SkipToSemicolon();
            }
            else
            {
                Consume();
            }
            return new LiteralNode("keyword", "break", tok.Line, tok.Column);
        }

        // continue
        if (Is(TokenType.Keyword, "continue"))
        {
            var tok = Consume();
            if (!Is(TokenType.Semicolon))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    $"Ожидалась ';' после continue — обнаружена '{_lookahead.Lexeme}'"));
                SkipToSemicolon();
            }
            else
            {
                Consume();
            }
            return new LiteralNode("keyword", "continue", tok.Line, tok.Column);
        }

        // return
        if (Is(TokenType.Keyword, "return"))
        {
            var retTok = Consume();
            AstNode expr = Is(TokenType.Semicolon)
                ? new LiteralNode("void", string.Empty, retTok.Line, retTok.Column)
                : ParseCppExpr();

            if (!Is(TokenType.Semicolon))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    $"Ожидалась ';' после return — обнаружена '{_lookahead.Lexeme}'"));
                SkipToSemicolon();
            }
            else
            {
                Consume();
            }
            return new UnaryNode("return", expr);
        }

        // delete
        if (Is(TokenType.Keyword, "delete"))
        {
            var delTok = Consume();
            bool isArray = false;
            if (Is(TokenType.LBracket))
            {
                isArray = true;
                Consume();
                Expect(TokenType.RBracket, null, "Ожидалась ']' после delete.");
            }

            var expr = ParseCppExpr();
            if (!Is(TokenType.Semicolon))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    $"Ожидалась ';' после delete — обнаружена '{_lookahead.Lexeme}'"));
                SkipToSemicolon();
            }
            else
            {
                Consume();
            }
            return new UnaryNode(isArray ? "delete[]" : "delete", expr);
        }

        // Выражение
        var e = ParseCppExpr();
        if (!Is(TokenType.Semicolon))
        {
            Errors.Add((_lookahead.Line, _lookahead.Column,
                $"Ожидалась ';' после выражения — обнаружена '{_lookahead.Lexeme}'"));
            SkipToSemicolon();
        }
        else
        {
            Consume();
        }
        return new ExprStatementNode(e);
    }

    /// <summary>
    /// Парсит условный оператор if-else.
    /// Создаёт новую область видимости для каждой ветви.
    /// </summary>
    private AstNode ParseCppIfStatement()
    {
        Consume(); // if
        AstNode condition;

        if (Is(TokenType.LParen))
        {
            Consume();
            condition = ParseCppExpr();
            if (!Is(TokenType.RParen))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    $"Ожидалась ')' после условия if — обнаружена '{_lookahead.Lexeme}'"));
                SkipTo(TokenType.LBrace, TokenType.Semicolon);
            }
            else
            {
                Consume();
            }
        }
        else
        {
            Errors.Add((_lookahead.Line, _lookahead.Column,
                $"Ожидалась '(' после if — обнаружена '{_lookahead.Lexeme}'"));
            condition = ParseCppExpr();
            SkipTo(TokenType.LBrace, TokenType.Semicolon);
        }

        _scopes.EnterScope("if-then");
        var thenBranch = ParseCppStatementOrBlock();
        _scopes.ExitScope();

        AstNode? elseBranch = null;
        if (Is(TokenType.Keyword, "else"))
        {
            Consume();
            _scopes.EnterScope("if-else");
            elseBranch = ParseCppStatementOrBlock();
            _scopes.ExitScope();
        }

        return new BinaryNode("if", condition,
            new BinaryNode("then-else", thenBranch, elseBranch ?? new LiteralNode("void", string.Empty, -1, -1)));
    }

    /// <summary>
    /// Парсит циклический оператор while.
    /// Создаёт новую область видимости для тела цикла.
    /// </summary>
    private AstNode ParseCppWhileStatement()
    {
        Consume(); // while
        AstNode condition;

        if (Is(TokenType.LParen))
        {
            Consume();
            condition = ParseCppExpr();
            if (!Is(TokenType.RParen))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    $"Ожидалась ')' после условия while — обнаружена '{_lookahead.Lexeme}'"));
                SkipTo(TokenType.LBrace, TokenType.Semicolon);
            }
            else
            {
                Consume();
            }
        }
        else
        {
            Errors.Add((_lookahead.Line, _lookahead.Column,
                $"Ожидалась '(' после while — обнаружена '{_lookahead.Lexeme}'"));
            condition = ParseCppExpr();
            SkipTo(TokenType.LBrace, TokenType.Semicolon);
        }

        var body = ParseCppStatementOrBlock();
        return new BinaryNode("while", condition, body);
    }

    /// <summary>
    /// Парсит циклический оператор do-while.
    /// </summary>
    private AstNode ParseCppDoWhileStatement()
    {
        Consume(); // do
        var body = ParseCppStatementOrBlock();

        if (!Is(TokenType.Keyword, "while"))
            Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась 'while' в конце do-while."));
        else
            Consume();

        Expect(TokenType.LParen, null, "Ожидалась '(' после while.");
        var condition = ParseCppExpr();
        Expect(TokenType.RParen, null, "Ожидалась ')' в условии do-while.");
        Expect(TokenType.Semicolon, null, "Ожидалась ';' в конце do-while.");

        return new BinaryNode("do-while", condition, body);
    }

    /// <summary>
    /// Парсит циклический оператор for с поддержкой объявления переменной в инициализации.
    /// Правильно обрабатывает точки с запятой в заголовке for.
    /// </summary>
    private AstNode ParseCppForStatement()
    {
        Consume(); // for

        if (!Is(TokenType.LParen))
        {
            Errors.Add((_lookahead.Line, _lookahead.Column,
                $"Ожидалась '(' после for — обнаружена '{_lookahead.Lexeme}'"));
            return new LiteralNode("error", "for", _lookahead.Line, _lookahead.Column);
        }

        Consume();
        _scopes.EnterScope("for-init");

        AstNode init;
        if (Is(TokenType.Semicolon))
        {
            init = new LiteralNode("void", string.Empty, -1, -1);
            Consume();
        }
        else if (Is(TokenType.Keyword, "const") || IsCppType(_lookahead))
        {
            bool isConst = false;
            int constLine = -1, constCol = -1;
            if (Is(TokenType.Keyword, "const"))
            {
                var constToken = Consume();
                isConst = true;
                constLine = constToken.Line;
                constCol = constToken.Column;
            }

            if (IsCppType(_lookahead))
            {
                var typeTok = Consume();
                SkipCppTemplateArguments();
                string pointerPrefix = "";
                while (Is(TokenType.Operator, "*"))
                {
                    pointerPrefix += "*";
                    Consume();
                }

                if (_lookahead.Type == TokenType.Identifier)
                {
                    var nameTok = Consume();
                    init = ParseCppVarDeclForInit(typeTok, nameTok, isConst, pointerPrefix);
                }
                else
                {
                    Errors.Add((_lookahead.Line, _lookahead.Column,
                        "Ожидался идентификатор после типа."));
                    init = new LiteralNode("void", string.Empty, -1, -1);
                }
            }
            else
            {
                init = new LiteralNode("void", string.Empty, -1, -1);
            }

            if (!Is(TokenType.Semicolon))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    $"Ожидалась ';' в инициализации for — обнаружена '{_lookahead.Lexeme}'"));
            }
            else
            {
                Consume();
            }
        }
        else
        {
            init = ParseCppExpr();
            if (!Is(TokenType.Semicolon))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    $"Ожидалась ';' в инициализации for — обнаружена '{_lookahead.Lexeme}'"));
            }
            else
            {
                Consume();
            }
        }

        AstNode cond;
        if (Is(TokenType.Semicolon))
        {
            cond = new LiteralNode("void", string.Empty, -1, -1);
            Consume();
        }
        else
        {
            cond = ParseCppExpr();
            if (!Is(TokenType.Semicolon))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    $"Ожидалась ';' в условии for — обнаружена '{_lookahead.Lexeme}'"));
            }
            else
            {
                Consume();
            }
        }

        AstNode incr = Is(TokenType.RParen)
            ? new LiteralNode("void", string.Empty, -1, -1)
            : ParseCppExpr();

        if (!Is(TokenType.RParen))
        {
            Errors.Add((_lookahead.Line, _lookahead.Column,
                $"Ожидалась ')' в конце for — обнаружена '{_lookahead.Lexeme}'"));
        }
        else
        {
            Consume();
        }

        var body = ParseCppStatementOrBlock();
        _scopes.ExitScope();

        return new BinaryNode(
            "for",
            new BinaryNode("for-header", init, new BinaryNode("for-cond", cond, incr)),
            body);
    }

    /// <summary>
    /// Парсит блок кода, заключённый в фигурные скобки.
    /// Создаёт новую область видимости для содержимого блока.
    /// </summary>
    private AstNode ParseCppBlock()
    {
        if (!Is(TokenType.LBrace))
            return new LiteralNode("error", string.Empty, _lookahead.Line, _lookahead.Column);

        Consume(); // '{'
        _scopes.EnterScope("block");
        var stmts = new List<AstNode>();

        while (_lookahead.Type != TokenType.RBrace && _lookahead.Type != TokenType.EndOfFile)
        {
            if (_lookahead.Type == TokenType.Semicolon)
            {
                Consume();
                continue;
            }

            var stmt = ParseCppStatement();
            if (stmt != null)
                stmts.Add(stmt);
            else
                SkipToSemicolon();
        }

        if (!Is(TokenType.RBrace))
        {
            Errors.Add((_lookahead.Line, _lookahead.Column,
                $"Ожидалась '}}' для закрытия блока — обнаружена '{_lookahead.Lexeme}'"));
        }
        else
        {
            Consume();
        }

        _scopes.ExitScope();
        return new ProgramNode(stmts);
    }

    /// <summary>
    /// Парсит оператор или блок, проверяя наличие блока.
    /// </summary>
    private AstNode ParseCppStatementOrBlock()
    {
        if (Is(TokenType.LBrace))
            return ParseCppBlock();
        return ParseCppStatement() ?? new LiteralNode("void", string.Empty, _lookahead.Line, _lookahead.Column);
    }

    // ========== Выражения ==========

    /// <summary>
    /// Парсит выражение, начиная с присваивания (приоритет наименьший).
    /// </summary>
    private AstNode ParseCppExpr() => ParseCppAssignment();

    /// <summary>
    /// Парсит выражение присваивания с проверкой const и совместимости типов.
    /// </summary>
    private AstNode ParseCppAssignment()
    {
        var left = ParseCppLogicalOr();

        if (_lookahead.Type == TokenType.Operator && _lookahead.Lexeme == "=")
        {
            var op = Consume();
            var right = ParseCppAssignment();

            if (left is IdentifierNode idLeft)
            {
                var leftEntry = _scopes.Lookup(idLeft.Name);
                if (leftEntry?.IsConst == true)
                {
                    Errors.Add((op.Line, op.Column, $"Ошибка: нельзя присвоить значение константной переменной '{idLeft.Name}'"));
                }
                else if (leftEntry != null)
                {
                    string leftType = leftEntry.Type.ToLower();
                    string rightType = GetExpressionType(right);
                    if (!AreTypesCompatible(leftType, rightType))
                    {
                        Errors.Add((op.Line, op.Column, $"Ошибка типов: несовместимые типы в присваивании {idLeft.Name} ({leftType}) = ... ({rightType})"));
                    }
                    leftEntry.IsInitialized = true;
                }
            }
            return new AssignNode(left, "=", right);
        }

        return left;
    }

    /// <summary>
    /// Парсит логическое ИЛИ (||).
    /// </summary>
    private AstNode ParseCppLogicalOr()
    {
        var left = ParseCppLogicalAnd();
        while (_lookahead.Type == TokenType.Operator && _lookahead.Lexeme == "||")
        {
            var op = Consume();
            var right = ParseCppLogicalAnd();
            left = new BinaryNode(op.Lexeme, left, right);
        }
        return left;
    }

    /// <summary>
    /// Парсит логическое И (&&).
    /// </summary>
    private AstNode ParseCppLogicalAnd()
    {
        var left = ParseCppEquality();
        while (_lookahead.Type == TokenType.Operator && _lookahead.Lexeme == "&&")
        {
            var op = Consume();
            var right = ParseCppEquality();
            left = new BinaryNode(op.Lexeme, left, right);
        }
        return left;
    }

    /// <summary>
    /// Парсит выражения равенства (==, !=).
    /// </summary>
    private AstNode ParseCppEquality()
    {
        var left = ParseCppRelational();
        while (_lookahead.Type == TokenType.Operator &&
               (_lookahead.Lexeme == "==" || _lookahead.Lexeme == "!="))
        {
            var op = Consume();
            var right = ParseCppRelational();
            left = new BinaryNode(op.Lexeme, left, right);
        }
        return left;
    }

    /// <summary>
    /// Парсит выражения сравнения (&lt;, &gt;, &lt;=, &gt;=, &lt;&lt;, &gt;&gt;).
    /// </summary>
    private AstNode ParseCppRelational()
    {
        var left = ParseCppAdditive();
        while (_lookahead.Type == TokenType.Operator &&
               (_lookahead.Lexeme == "<" || _lookahead.Lexeme == ">" ||
                _lookahead.Lexeme == "<=" || _lookahead.Lexeme == ">=" ||
                _lookahead.Lexeme == "<<" || _lookahead.Lexeme == ">>"))
        {
            var op = Consume();
            var right = ParseCppAdditive();
            left = new BinaryNode(op.Lexeme, left, right);
        }
        return left;
    }

    /// <summary>
    /// Парсит выражения сложения и вычитания (+, -).
    /// </summary>
    private AstNode ParseCppAdditive()
    {
        var left = ParseCppMultiplicative();
        while (_lookahead.Type == TokenType.Operator &&
               (_lookahead.Lexeme == "+" || _lookahead.Lexeme == "-"))
        {
            var op = Consume();
            var right = ParseCppMultiplicative();
            left = new BinaryNode(op.Lexeme, left, right);
        }
        return left;
    }

    /// <summary>
    /// Парсит выражения умножения, деления и модуля (*, /, %).
    /// </summary>
    private AstNode ParseCppMultiplicative()
    {
        var left = ParseCppUnary();
        while (_lookahead.Type == TokenType.Operator &&
               (_lookahead.Lexeme == "*" || _lookahead.Lexeme == "/" || _lookahead.Lexeme == "%"))
        {
            var op = Consume();
            var right = ParseCppUnary();
            left = new BinaryNode(op.Lexeme, left, right);
        }
        return left;
    }

    /// <summary>
    /// Парсит унарные выражения (++, --, !, -, +).
    /// </summary>
    private AstNode ParseCppUnary()
    {
        if (_lookahead.Type == TokenType.Operator &&
            (_lookahead.Lexeme == "+" || _lookahead.Lexeme == "-" ||
             _lookahead.Lexeme == "!" || _lookahead.Lexeme == "++" ||
             _lookahead.Lexeme == "--"))
        {
            var op = Consume();
            var operand = ParseCppUnary();
            return new UnaryNode(op.Lexeme, operand);
        }

        return ParseCppPostfix();
    }

    /// <summary>
    /// Парсит постфиксные выражения (++, --, [], функция).
    /// </summary>
    private AstNode ParseCppPostfix()
    {
        var expr = ParseCppPrimary();

        // Постфиксные ++ и --
        while (_lookahead.Type == TokenType.Operator &&
               (_lookahead.Lexeme == "++" || _lookahead.Lexeme == "--"))
        {
            var op = Consume();
            if (expr is IdentifierNode idExpr)
            {
                var entry = _scopes.Lookup(idExpr.Name);
                if (entry?.IsConst == true)
                {
                    Errors.Add((op.Line, op.Column, $"Ошибка: нельзя инкрементировать/декрементировать константную переменную '{idExpr.Name}'"));
                }
                else if (entry != null)
                {
                    entry.IsInitialized = true;
                }
            }
            expr = new BinaryNode(op.Lexeme + "-post", expr, new LiteralNode("void", "", op.Line, op.Column));
        }

        // Доступ к элементам массива []
        while (Is(TokenType.LBracket))
        {
            Consume();
            var index = ParseCppExpr();
            if (!Is(TokenType.RBracket))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась ']' после индекса массива."));
            }
            else
            {
                Consume();
            }
            expr = new BinaryNode("[]", expr, index);
        }

        return expr;
    }

    /// <summary>
    /// Парсит первичные выражения: литералы, идентификаторы, new, скобки, инициализаторы.
    /// Обрабатывает встроенные std идентификаторы (cout, cin, endl) как KEYWORD токены.
    /// </summary>
    private AstNode ParseCppPrimary()
    {
        // new
        if (Is(TokenType.Keyword, "new"))
        {
            var newTok = Consume();
            if (!IsCppType(_lookahead))
            {
                Errors.Add((newTok.Line, newTok.Column, "Ожидался тип после 'new'."));
                return new LiteralNode("error", "new", newTok.Line, newTok.Column);
            }

            var typeTok = Consume();
            AstNode? size = null;
            if (Is(TokenType.LBracket))
            {
                Consume();
                size = ParseCppExpr();
                if (!Is(TokenType.RBracket))
                {
                    Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась ']' после размера массива в new."));
                }
                else
                {
                    Consume();
                }
            }

            var typeNode = new IdentifierNode(typeTok.Lexeme, typeTok.Line, typeTok.Column);
            if (size != null)
                return new BinaryNode("new-array", typeNode, size);
            return new UnaryNode("new", typeNode);
        }

        // Скобки (выражение)
        if (Is(TokenType.LParen))
        {
            Consume();
            var expr = ParseCppExpr();
            if (!Is(TokenType.RParen))
                Errors.Add((_lookahead.Line, _lookahead.Column,
                    "Ожидалась закрывающая скобка ')' в выражении."));
            else
                Consume();
            return expr;
        }

        // Инициализатор массива {...}
        if (Is(TokenType.LBrace))
        {
            var braceStart = Consume();
            var initList = new List<AstNode>();
            while (!Is(TokenType.RBrace) && _lookahead.Type != TokenType.EndOfFile)
            {
                var elem = ParseCppExpr();
                initList.Add(elem);
                if (Is(TokenType.Comma))
                {
                    Consume();
                }
                else if (!Is(TokenType.RBrace))
                {
                    break;
                }
            }

            if (!Is(TokenType.RBrace))
            {
                Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась '}' в инициализаторе массива."));
            }
            else
            {
                Consume();
            }

            return new ProgramNode(initList);
        }

        // Идентификаторы и встроенные std ключевые слова
        if (_lookahead.Type == TokenType.Identifier || _lookahead.Type == TokenType.Keyword)
        {
            string[] stdKeywords = {
                "cout", "cin", "cerr", "clog", "endl", "flush", "ws",
                "string", "vector", "map", "set", "list"
            };

            if (_lookahead.Type == TokenType.Keyword && stdKeywords.Contains(_lookahead.Lexeme))
            {
                var t = Consume();
                return new IdentifierNode(t.Lexeme, t.Line, t.Column);
            }

            if (_lookahead.Type == TokenType.Identifier)
            {
                var t = Consume();
                _scopes.Require(t.Lexeme, t.Line, t.Column, Errors);
                return new IdentifierNode(t.Lexeme, t.Line, t.Column);
            }
        }

        // Числовые литералы
        if (_lookahead.Type == TokenType.Number)
        {
            var t = Consume();
            return new LiteralNode("number", t.Lexeme, t.Line, t.Column);
        }

        // Строковые литералы
        if (_lookahead.Type == TokenType.StringLiteral)
        {
            var t = Consume();
            return new LiteralNode("string", t.Lexeme, t.Line, t.Column);
        }

        // Символьные литералы
        if (_lookahead.Type == TokenType.CharLiteral)
        {
            var t = Consume();
            return new LiteralNode("char", t.Lexeme, t.Line, t.Column);
        }

        // Булевы литералы
        if (_lookahead.Type == TokenType.BoolLiteral)
        {
            var t = Consume();
            return new LiteralNode("bool", t.Lexeme, t.Line, t.Column);
        }

        Errors.Add((_lookahead.Line, _lookahead.Column,
            $"Ожидалось выражение, получено '{_lookahead.Lexeme}'."));
        var errTok = Consume();
        return new LiteralNode("error", errTok.Lexeme, errTok.Line, errTok.Column);
    }

    // ========== Диагностика типов ==========

    /// <summary>
    /// Определяет тип выражения на основе его структуры и значений.
    /// </summary>
    private string GetExpressionType(AstNode? node)
    {
        if (node == null) return "unknown";
        return node switch
        {
            LiteralNode lit => GetLiteralType(lit),
            IdentifierNode id => GetIdentifierType(id),
            BinaryNode bin => GetBinaryType(bin),
            UnaryNode un => GetUnaryType(un),
            ProgramNode => "array",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Определяет тип литерального значения.
    /// </summary>
    private string GetLiteralType(LiteralNode lit)
    {
        return lit.Kind switch
        {
            "number" => IsFloatLiteral(lit.Value) ? "double" : "int",
            "bool" => "bool",
            "string" => "string",
            "char" => "char",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Определяет тип идентификатора по его объявлению в таблице идентификаторов.
    /// </summary>
    private string GetIdentifierType(IdentifierNode id)
    {
        var entry = _scopes.Lookup(id.Name);
        return entry?.Type.ToLower() ?? "undeclared";
    }

    /// <summary>
    /// Определяет тип бинарного выражения на основе оператора и операндов.
    /// </summary>
    private string GetBinaryType(BinaryNode bin)
    {
        string leftType = GetExpressionType(bin.Left);
        string rightType = GetExpressionType(bin.Right);

        if (bin.Op == "new-array")
            return "pointer";

        if (bin.Op == "[]")
            return "element-type";

        if (IsComparisonOp(bin.Op))
            return "bool";

        if (IsArithmeticOp(bin.Op))
        {
            if (leftType == "double" || rightType == "double")
                return "double";
            if (leftType == "float" || rightType == "float")
                return "float";
            return "int";
        }

        return leftType;
    }

    /// <summary>
    /// Определяет тип унарного выражения на основе оператора.
    /// </summary>
    private string GetUnaryType(UnaryNode un)
    {
        if (un.Op == "!" || un.Op == "new")
            return "bool";
        return GetExpressionType(un.Operand);
    }

    /// <summary>
    /// Проверяет, является ли оператор оператором сравнения.
    /// </summary>
    private bool IsComparisonOp(string op) =>
        op == "==" || op == "!=" || op == "<" || op == ">" || op == "<=" || op == ">=";

    /// <summary>
    /// Проверяет, является ли оператор арифметическим оператором.
    /// </summary>
    private bool IsArithmeticOp(string op) =>
        op == "+" || op == "-" || op == "*" || op == "/" || op == "%";

    /// <summary>
    /// Проверяет совместимость двух типов при присваивании.
    /// </summary>
    private bool AreTypesCompatible(string targetType, string sourceType)
    {
        if (targetType == sourceType)
            return true;

        if (sourceType == "void" || sourceType == "unknown" || sourceType == "undeclared")
            return true;

        string target = targetType.ToLower().Replace("*", "");
        string source = sourceType.ToLower().Replace("*", "");

        if (target == source)
            return true;

        // Преобразования типов
        if ((target == "double" || target == "float") &&
            (source == "int" || source == "double" || source == "float"))
            return true;

        if (target == "int" && source == "int")
            return true;

        return false;
    }

    /// <summary>
    /// Проверяет, является ли строка вещественным числом.
    /// </summary>
    private bool IsFloatLiteral(string value) =>
        value.Contains(".") || value.ToLower().Contains("f") || value.ToLower().Contains("e");
}
