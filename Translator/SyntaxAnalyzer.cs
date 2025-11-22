// ===================== SyntaxAnalyzer.cs =====================

using System;
using System.Collections.Generic;
using System.Linq;
using Lexer;

namespace Parser;

///
/// Синтаксический анализатор упрощённого C++ с поддержкой таблицы идентификаторов, областей видимости, диагностики типов и const.
/// Поддерживает префиксные (++i, --i) и постфиксные (i++, i--) операторы инкремента/декремента.
/// NEW: Поддержка указателей (int*), массивов (arr[5]), инициализации {...}, новых конструкций.
///
public sealed class SyntaxAnalyzer
{
    private readonly List<Token> _tokenList;
    private int _pos;
    private Token _lookahead;
    private bool _inErrorRecovery = false;
    private readonly ScopeManager _scopes = new();

    public List<(int Line, int Col, string Message)> Errors { get; } = new();

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

        InitializeStdNamespace();
    }

    private void InitializeStdNamespace()
    {
        var stdIdentifiers = new[]
        {
            ("cout", "ostream"),
            ("cin", "istream"),
            ("cerr", "ostream"),
            ("endl", "manipulator"),
            ("string", "type"),
            ("vector", "type"),
            ("map", "type"),
            ("set", "type"),
            ("list", "type")
        };

        foreach (var (name, type) in stdIdentifiers)
        {
            _scopes.Declare(name, "std", type, false, -1, -1, Errors);
        }
    }

    private void MoveNext()
    {
        if (_pos < _tokenList.Count)
            _pos++;
        _lookahead = _pos < _tokenList.Count
            ? _tokenList[_pos]
            : new Token(TokenType.EndOfFile, string.Empty, -1, -1);
    }

    private bool Is(TokenType type, string? lexeme = null) =>
        _lookahead.Type == type &&
        (lexeme == null || string.Equals(_lookahead.Lexeme, lexeme, StringComparison.Ordinal));

    private Token Consume()
    {
        var t = _lookahead;
        MoveNext();
        return t;
    }

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

    private void SkipTo(TokenType target1, TokenType target2 = TokenType.EndOfFile)
    {
        while (_lookahead.Type != target1 && _lookahead.Type != target2 &&
               _lookahead.Type != TokenType.LBrace && _lookahead.Type != TokenType.RBrace)
        {
            MoveNext();
        }
    }

    // =====================================================================
    // ТОЧКА ВХОДА
    // =====================================================================

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

    // =====================================================================
    // C++ ПРОГРАММА
    // =====================================================================

    private ProgramNode ParseCppProgram()
    {
        var stmts = new List<AstNode>();
        while (_lookahead.Type != TokenType.EndOfFile)
        {
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

    private void ParseUsingNamespace()
    {
        Consume(); // "using"
        if (Is(TokenType.Keyword, "namespace"))
        {
            Consume(); // "namespace"
            if (_lookahead.Type == TokenType.Identifier)
            {
                var namespaceName = Consume().Lexeme;
            }

            if (!Is(TokenType.Semicolon))
                Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась ';' после using."));
            else
                Consume();
        }
    }

    // =====================================================================
    // ОБЪЯВЛЕНИЯ И ФУНКЦИИ
    // =====================================================================

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
            {
                Errors.Add((constLine, constCol, "Ожидался тип после 'const'."));
            }

            return ParseCppStatement();
        }

        var typeTok = Consume();
        SkipCppTemplateArguments();

        // NEW: Handle pointer declarators (int*, int**, etc.)
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

        // Branch: if ( after name — function, else variable
        if (Is(TokenType.LParen))
        {
            if (isConst)
            {
                Errors.Add((constLine, constCol, "'const' не может использоваться для объявления функции."));
            }

            return ParseCppFunctionDeclaration(typeTok, nameTok, pointerPrefix);
        }
        else
        {
            return ParseCppVarDeclTail(typeTok, nameTok, isConst, pointerPrefix);
        }
    }

    private bool IsCppType(Token tok)
    {
        if (tok.Type == TokenType.Keyword)
        {
            string[] types =
            {
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

    // NEW: Improved variable declaration with array and pointer support
    private AstNode ParseCppVarDeclTail(Token typeTok, Token nameTok, bool isConst, string pointerPrefix)
    {
        string varName = nameTok.Lexeme;
        string fullType = typeTok.Lexeme + pointerPrefix;

        // Handle array declarators: int arr[5]
        string arrayPostfix = "";
        while (Is(TokenType.LBracket))
        {
            Consume(); // [
            arrayPostfix += "[";

            // Parse array size if present
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

        // Declare variable
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

            // Type check
            string exprType = GetExpressionType(init);

            if (!AreTypesCompatible(fullType, exprType))
            {
                Errors.Add((nameTok.Line, nameTok.Column,
                    $"Ошибка типов: несовместимые типы в инициализации {varName} ({fullType}) = ... ({exprType})"));
            }

            // Mark as initialized
            var entry = _scopes.Lookup(varName);
            if (entry != null)
                entry.IsInitialized = true;
        }

        // Const requires init
        if (isConst && init == null)
        {
            Errors.Add((nameTok.Line, nameTok.Column, $"Константная переменная '{varName}' должна быть инициализирована."));
        }

        SkipToSemicolon();

        var assign = new AssignNode(idNode, "=", init ?? new LiteralNode("void", "", nameTok.Line, nameTok.Column));
        var declNode = new BinaryNode("decl", typeNode, assign);

        return declNode;
    }

    private AstNode ParseCppFunctionDeclaration(Token typeToken, Token nameToken, string pointerPrefix)
    {
        // Declare function
        _scopes.Declare(
            nameToken.Lexeme,
            kind: "func",
            type: typeToken.Lexeme + pointerPrefix,
            isConst: false,
            line: nameToken.Line,
            column: nameToken.Column,
            errors: Errors);

        var typeNode = new IdentifierNode(typeToken.Lexeme + pointerPrefix, typeToken.Line, typeToken.Column);
        var nameNode = new IdentifierNode(nameToken.Lexeme, nameToken.Line, nameToken.Column);

        // Params (skip to ))
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
            {
                implicitStmts.Add(ParseCppStatement() ?? new LiteralNode("void", "", -1, -1));
            }

            body = new ProgramNode(implicitStmts);
            _scopes.ExitScope();
        }

        return new BinaryNode("func-def", typeNode,
            new BinaryNode("func-params-body", nameNode, body));
    }

    // =====================================================================
    // ОПЕРАТОРЫ
    // =====================================================================

    private AstNode? ParseCppStatement()
    {
        if (Is(TokenType.Keyword, "const") || IsCppType(_lookahead))
            return ParseCppStatementOrDeclaration();

        if (Is(TokenType.Keyword, "if"))
            return ParseCppIfStatement();

        if (Is(TokenType.Keyword, "while"))
            return ParseCppWhileStatement();

        if (Is(TokenType.Keyword, "do"))
            return ParseCppDoWhileStatement();

        if (Is(TokenType.Keyword, "for"))
            return ParseCppForStatement();

        if (Is(TokenType.LBrace))
            return ParseCppBlock();

        if (Is(TokenType.Keyword, "break"))
        {
            var tok = Consume();
            Expect(TokenType.Semicolon, null, "Ожидалась ';' после break.");
            return new LiteralNode("keyword", "break", tok.Line, tok.Column);
        }

        if (Is(TokenType.Keyword, "continue"))
        {
            var tok = Consume();
            Expect(TokenType.Semicolon, null, "Ожидалась ';' после continue.");
            return new LiteralNode("keyword", "continue", tok.Line, tok.Column);
        }

        if (Is(TokenType.Keyword, "return"))
        {
            var retTok = Consume();
            AstNode expr = Is(TokenType.Semicolon)
                ? new LiteralNode("void", string.Empty, retTok.Line, retTok.Column)
                : ParseCppExpr();
            Expect(TokenType.Semicolon, null, "Ожидалась ';' после return.");
            return new UnaryNode("return", expr);
        }

        // NEW: delete[] support
        if (Is(TokenType.Keyword, "delete"))
        {
            var delTok = Consume();
            bool isArray = false;
            if (Is(TokenType.LBracket))
            {
                isArray = true;
                Consume(); // [
                Expect(TokenType.RBracket, null, "Ожидалась ']' после delete.");
            }
            var expr = ParseCppExpr();
            Expect(TokenType.Semicolon, null, "Ожидалась ';' после delete.");
            return new UnaryNode(isArray ? "delete[]" : "delete", expr);
        }

        var e = ParseCppExpr();
        SkipToSemicolon();
        return new ExprStatementNode(e);
    }

    private AstNode ParseCppIfStatement()
    {
        Consume(); // if
        AstNode condition;

        if (Is(TokenType.LParen))
        {
            Consume();
            condition = ParseCppExpr();
            Expect(TokenType.RParen, null, "Ожидалась ')' в условии if.");
        }
        else
        {
            Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась '(' после if — парсинг условия без скобок."));
            condition = ParseCppExpr();
            SkipTo(TokenType.LBrace, TokenType.Semicolon);
        }

        _scopes.EnterScope();
        var thenBranch = ParseCppStatementOrBlock();
        _scopes.ExitScope();

        AstNode? elseBranch = null;
        if (Is(TokenType.Keyword, "else"))
        {
            Consume();
            _scopes.EnterScope();
            elseBranch = ParseCppStatementOrBlock();
            _scopes.ExitScope();
        }

        return new BinaryNode("if", condition,
            new BinaryNode("then-else", thenBranch, elseBranch ?? new LiteralNode("void", string.Empty, -1, -1)));
    }

    private AstNode ParseCppWhileStatement()
    {
        Consume(); // while
        AstNode condition;

        if (Is(TokenType.LParen))
        {
            Consume();
            condition = ParseCppExpr();
            Expect(TokenType.RParen, null, "Ожидалась ')' в условии while.");
        }
        else
        {
            Errors.Add((_lookahead.Line, _lookahead.Column, "Ожидалась '(' после while — парсинг условия без скобок."));
            condition = ParseCppExpr();
            SkipTo(TokenType.LBrace, TokenType.Semicolon);
        }

        var body = ParseCppStatementOrBlock();
        return new BinaryNode("while", condition, body);
    }

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

    private AstNode ParseCppForStatement()
    {
        Consume(); // for
        Expect(TokenType.LParen, null, "Ожидалась '(' после for.");

        _scopes.EnterScope();

        AstNode init;
        if (Is(TokenType.Semicolon))
        {
            init = new LiteralNode("void", string.Empty, -1, -1);
            Consume();
        }
        else if (IsCppType(_lookahead) || Is(TokenType.Keyword, "const"))
        {
            init = ParseCppStatementOrDeclaration() ?? new LiteralNode("void", string.Empty, -1, -1);
            while (!Is(TokenType.Semicolon) && _lookahead.Type != TokenType.EndOfFile)
                Consume();
            if (Is(TokenType.Semicolon)) Consume();
        }
        else
        {
            init = ParseCppExpr();
            while (!Is(TokenType.Semicolon) && _lookahead.Type != TokenType.EndOfFile)
                Consume();
            if (Is(TokenType.Semicolon)) Consume();
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
            while (!Is(TokenType.Semicolon) && !Is(TokenType.RParen) && _lookahead.Type != TokenType.EndOfFile)
                Consume();
            if (Is(TokenType.Semicolon)) Consume();
        }

        AstNode incr = Is(TokenType.RParen)
            ? new LiteralNode("void", string.Empty, -1, -1)
            : ParseCppExpr();

        while (!Is(TokenType.RParen) && _lookahead.Type != TokenType.EndOfFile)
            Consume();

        Expect(TokenType.RParen, null, "Ожидалась ')' после заголовка for.");

        var body = ParseCppStatementOrBlock();

        _scopes.ExitScope();

        return new BinaryNode(
            "for",
            new BinaryNode("for-header", init, new BinaryNode("for-cond", cond, incr)),
            body);
    }

    private AstNode ParseCppBlock()
    {
        if (!Is(TokenType.LBrace))
            return new LiteralNode("error", string.Empty, _lookahead.Line, _lookahead.Column);

        Consume(); // '{'
        _scopes.EnterScope();

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

        Expect(TokenType.RBrace, null, "Ожидалась '}'.");
        _scopes.ExitScope();

        return new ProgramNode(stmts);
    }

    private AstNode ParseCppStatementOrBlock()
    {
        if (Is(TokenType.LBrace))
            return ParseCppBlock();
        return ParseCppStatement() ?? new LiteralNode("void", string.Empty, _lookahead.Line, _lookahead.Column);
    }

    // =====================================================================
    // ВЫРАЖЕНИЯ
    // =====================================================================

    private AstNode ParseCppExpr() => ParseCppAssignment();

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

    private AstNode ParseCppPostfix()
    {
        var expr = ParseCppPrimary();

        // Postfix ++ --
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

        // Array access []
        while (Is(TokenType.LBracket))
        {
            Consume(); // [
            var index = ParseCppExpr();
            Expect(TokenType.RBracket, null, "Ожидалась ']' после индекса массива.");
            expr = new BinaryNode("[]", expr, index);
        }

        return expr;
    }

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
                Expect(TokenType.RBracket, null, "Ожидалась ']' после размера массива в new.");
            }

            var typeNode = new IdentifierNode(typeTok.Lexeme, typeTok.Line, typeTok.Column);

            if (size != null)
                return new BinaryNode("new-array", typeNode, size);
            return new UnaryNode("new", typeNode);
        }

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

        // Array initialization {...}
        if (Is(TokenType.LBrace))
        {
            var braceStart = Consume(); // {
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

            Expect(TokenType.RBrace, null, "Ожидалась '}' в инициализаторе массива.");
            return new ProgramNode(initList);
        }

        if (_lookahead.Type == TokenType.Identifier)
        {
            var t = Consume();
            _scopes.Require(t.Lexeme, t.Line, t.Column, Errors);
            return new IdentifierNode(t.Lexeme, t.Line, t.Column);
        }

        if (_lookahead.Type == TokenType.Number)
        {
            var t = Consume();
            return new LiteralNode("number", t.Lexeme, t.Line, t.Column);
        }

        if (_lookahead.Type == TokenType.StringLiteral)
        {
            var t = Consume();
            return new LiteralNode("string", t.Lexeme, t.Line, t.Column);
        }

        if (_lookahead.Type == TokenType.CharLiteral)
        {
            var t = Consume();
            return new LiteralNode("char", t.Lexeme, t.Line, t.Column);
        }

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

    // =====================================================================
    // ДИАГНОСТИКА ТИПОВ
    // =====================================================================

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

    private string GetIdentifierType(IdentifierNode id)
    {
        var entry = _scopes.Lookup(id.Name);
        return entry?.Type.ToLower() ?? "undeclared";
    }

    private string GetBinaryType(BinaryNode bin)
    {
        string leftType = GetExpressionType(bin.Left);
        string rightType = GetExpressionType(bin.Right);

        // NEW: For new-array: new int[3] returns int* (pointer)
        if (bin.Op == "new-array")
        {
            if (bin.Left is IdentifierNode typeId)
                return typeId.Name + "*";
            return "pointer";
        }

        // NEW: For array access "[]"
        if (bin.Op == "[]")
        {
            return "int";
        }

        if (IsArithmeticOperator(bin.Op))
        {
            if (leftType == "double" || rightType == "double" || leftType == "float" || rightType == "float")
                return "double";

            if (leftType == "int" || rightType == "int" || leftType == "short" || rightType == "short" ||
                leftType == "long" || rightType == "long")
                return "int";

            return "unknown";
        }

        if (IsLogicalOperator(bin.Op))
        {
            return "bool";
        }

        if (IsComparisonOperator(bin.Op))
        {
            return "bool";
        }

        return "unknown";
    }

    private string GetUnaryType(UnaryNode un)
    {
        if (un.Op == "new")
            return un.Operand is IdentifierNode id ? id.Name + "*" : "pointer";

        if (un.Op == "delete[]" || un.Op == "delete")
            return "void";

        return GetExpressionType(un.Operand);
    }

    private bool AreTypesCompatible(string leftType, string rightType)
    {
        // Normalize types for comparison
        string left = leftType.ToLower();
        string right = rightType.ToLower();

        // Exact match
        if (left == right)
            return true;

        // NEW: Array initialization compatibility
        // int arr[5] = {...} means leftType="int[]" and rightType="array"
        if ((left.Contains("[") && left.Contains("]")) && right == "array")
            return true;

        // NEW: Pointer compatibility with new-array
        // int* ptr = new int[3] means leftType="int*" and rightType="int*"
        if (left.Contains("*") && right.Contains("*"))
            return true;

        // Integer types: int, short, long are mutually compatible
        if ((left == "int" || left == "short" || left == "long") &&
            (right == "int" || right == "short" || right == "long"))
            return true;

        if (leftType == "double" || leftType == "float")
        {
            return rightType == "int" || rightType == "short" || rightType == "long"
                || rightType == "double" || rightType == "float";
        }

        if (leftType == "bool")
        {
            return rightType == "bool";
        }

        if (leftType == "string" || leftType == "char")
        {
            return rightType == leftType;
        }

        if (leftType.Contains("*") || rightType.Contains("*"))
        {
            return leftType == rightType;
        }

        if (leftType.Contains("*") && rightType == "array")
            return true;

        return false;
    }

    private static bool IsArithmeticOperator(string op)
    {
        return op == "+" || op == "-" || op == "*" || op == "/" || op == "%" || op == "<<" || op == ">>";
    }

    private static bool IsLogicalOperator(string op)
    {
        return op == "&&" || op == "||";
    }

    private static bool IsComparisonOperator(string op)
    {
        return op == "==" || op == "!=" || op == "<" || op == ">" || op == "<=" || op == ">=";
    }

    private static bool IsFloatLiteral(string value)
    {
        return value.Contains('.') || value.Contains('e') || value.Contains('E');
    }
}