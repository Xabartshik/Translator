using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Parser;

namespace FlowchartGen;

/// <summary>
/// Генератор Mermaid блок-схем по ГОСТ 19.701-90 (ЕСПД).
/// Обходит AST и строит диаграмму потока управления в формате Mermaid.
/// </summary>
public sealed class ASTToMermaid
{
    private int _nodeCounter = 0;
    private readonly StringBuilder _mermaid = new();
    private readonly List<string> _pendingNodes = new(); // хвосты веток, которые нужно слить
    private readonly Dictionary<string, int> _decisionEdgeCount = new(); // сколько рёбер уже вышло из ромба

    /// <summary>
    /// Контекст цикла: условие и хвосты выхода (для break и ветки "Нет").
    /// </summary>
    private class LoopContext
    {
        public string ConditionNode { get; set; } = string.Empty;
        public List<string> ExitTails { get; } = new();
    }

    private readonly Stack<LoopContext> _loopStack = new(); // стек активных циклов для break/continue

    public string Generate(AstNode? root, string graphName = "Flowchart")
    {
        if (root == null)
            return "flowchart TD\n Start([...])";

        _nodeCounter = 0;
        _mermaid.Clear();
        _pendingNodes.Clear();
        _decisionEdgeCount.Clear();
        _loopStack.Clear();

        // Инициализация с настройками Mermaid
        _mermaid.AppendLine("%%{init: {'flowchart': {");
        _mermaid.AppendLine(" 'curve': 'linear',");
        _mermaid.AppendLine(" 'nodeSpacing': 80,");
        _mermaid.AppendLine(" 'rankSpacing': 100,");
        _mermaid.AppendLine(" 'diagramPadding': 40,");
        _mermaid.AppendLine(" 'defaultRenderer': 'elk'");
        _mermaid.AppendLine("}}}%%");
        _mermaid.AppendLine("flowchart TD");

        // Начальный и конечный элементы
        string startNode = NewNode("Начало", "terminator");
        string lastNode = startNode;

        if (root is ProgramNode program)
            lastNode = ProcessStatements(program.Children, startNode);
        else
            lastNode = ProcessNode(root, startNode);

        // Если после последнего оператора ещё висят хвосты (после if/else/циклов), подключаем их к последнему узлу
        foreach (var pending in _pendingNodes)
            AddEdge(pending, lastNode);
        _pendingNodes.Clear();

        string endNode = NewNode("Конец", "terminator");
        AddEdge(lastNode, endNode);

        return _mermaid.ToString();
    }

    // ================== Общий обход AST ==================

    private string ProcessStatements(IReadOnlyList<AstNode> statements, string prevNode)
    {
        string current = prevNode;
        foreach (var stmt in statements)
            current = ProcessNode(stmt, current);
        return current;
    }

    private string ProcessNode(AstNode node, string prevNode)
    {
        return node switch
        {
            // Ветвление: if / else
            BinaryNode { Op: "if" } ifNode => ProcessIf(ifNode, prevNode),

            // Циклы
            BinaryNode { Op: "while" } whileNode => ProcessWhile(whileNode, prevNode),
            BinaryNode { Op: "do-while" } dwNode => ProcessDoWhile(dwNode, prevNode),
            BinaryNode { Op: "for" } forNode => ProcessFor(forNode, prevNode),

            // Объявление переменной
            BinaryNode { Op: "decl" } declNode => ProcessDeclaration(declNode, prevNode),

            // Определение функции
            BinaryNode { Op: "func-def" } funcNode => ProcessFunction(funcNode, prevNode),

            // Возврат из функции
            UnaryNode { Op: "return" } retNode => ProcessReturn(retNode, prevNode),

            // break / continue
            LiteralNode { Kind: "keyword", Value: "break" } => ProcessBreak(prevNode),
            LiteralNode { Kind: "keyword", Value: "continue" } => ProcessContinue(prevNode),

            // Блок кода — НЕ создаём отдельный узел, а обрабатываем содержимое
            ProgramNode block => ProcessStatements(block.Children, prevNode),

            // Оператор-выражение
            ExprStatementNode es => ProcessExpression(es.Expr, prevNode),

            // Присваивание
            AssignNode assign => ProcessAssignment(assign, prevNode),

            // Прочее выражение
            _ => ProcessGenericExpression(node, prevNode)
        };
    }

    // ================== if / while / do-while / for ==================

    private string ProcessIf(BinaryNode ifNode, string prevNode)
    {
        string condLabel = GetExpressionLabel(ifNode.Left);
        string condNode = NewNode(condLabel, "decision"); // ромб условия

        // Вход в if: сначала из накопленных хвостов, затем из prevNode
        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, condNode);
            _pendingNodes.Clear();
        }
        AddEdge(prevNode, condNode);

        var branches = ifNode.Right as BinaryNode; // then-else
        var thenBranch = branches?.Left;
        var elseBranch = branches?.Right;

        bool hasThen = thenBranch != null && thenBranch is not LiteralNode { Kind: "void" };
        bool hasElse = elseBranch != null && elseBranch is not LiteralNode { Kind: "void" };

        string? thenTarget = null;
        string? elseTarget = null;

        // THEN-ветка
        if (hasThen)
        {
            if (thenBranch is ProgramNode thenBlock)
                thenTarget = ProcessStatements(thenBlock.Children, condNode);
            else
                thenTarget = ProcessNode(thenBranch, condNode);
        }

        // ELSE-ветка
        if (hasElse)
        {
            if (elseBranch is ProgramNode elseBlock)
                elseTarget = ProcessStatements(elseBlock.Children, condNode);
            else
                elseTarget = ProcessNode(elseBranch, condNode);
        }

        // if с then и else
        if (hasThen && hasElse && thenTarget != null && elseTarget != null)
        {
            _pendingNodes.Add(thenTarget);
            _pendingNodes.Add(elseTarget);
            return thenTarget;
        }

        // if только с THEN-веткой
        if (hasThen && thenTarget != null)
        {
            _pendingNodes.Add(thenTarget); // хвост THEN
            _pendingNodes.Add(condNode);   // ветка "Нет"
            return thenTarget;
        }

        // Теоретический случай: else без then
        if (hasElse && elseTarget != null)
        {
            _pendingNodes.Add(elseTarget);
            _pendingNodes.Add(condNode);
            return elseTarget;
        }

        // Пустой if
        return condNode;
    }

    private string ProcessWhile(BinaryNode whileNode, string prevNode)
    {
        string condLabel = GetExpressionLabel(whileNode.Left);
        string condNode = NewNode(condLabel, "decision");

        // Вход в while: из _pendingNodes и из prevNode
        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, condNode);
            _pendingNodes.Clear();
        }
        AddEdge(prevNode, condNode);

        var loopCtx = new LoopContext { ConditionNode = condNode };
        _loopStack.Push(loopCtx);

        // Тело цикла (ветка "Да")
        string bodyLast = ProcessNode(whileNode.Right, condNode);

        _loopStack.Pop();

        // Хвосты тела, которые продолжают цикл, возвращаем к условию
        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, condNode);
            _pendingNodes.Clear();
        }
        else if (!string.IsNullOrEmpty(bodyLast))
        {
            AddEdge(bodyLast, condNode);
        }

        // Выход из цикла — ветка "Нет" из condNode (подключим к следующему оператору)
        loopCtx.ExitTails.Add(condNode);

        // Все выходные хвосты (включая break) передаём дальше
        foreach (var t in loopCtx.ExitTails)
            _pendingNodes.Add(t);

        // На верхнем уровне while сам не меняет prevNode (следующий оператор подключится через _pendingNodes)
        return prevNode;
    }

    private string ProcessDoWhile(BinaryNode doWhileNode, string prevNode)
    {
        var loopCtx = new LoopContext();
        _loopStack.Push(loopCtx);

        // Сначала тело (вход — prevNode)
        string bodyLast = ProcessNode(doWhileNode.Right, prevNode);

        // Затем условие
        string condLabel = GetExpressionLabel(doWhileNode.Left);
        string condNode = NewNode(condLabel, "decision");
        loopCtx.ConditionNode = condNode;

        // Подключаем хвосты тела к условию
        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, condNode);
            _pendingNodes.Clear();
        }
        else if (!string.IsNullOrEmpty(bodyLast))
        {
            AddEdge(bodyLast, condNode);
        }

        _loopStack.Pop();

        // Да — повтор: к началу тела
        AddEdge(condNode, prevNode, "Да");

        // Нет — выход (подключим к следующему оператору)
        loopCtx.ExitTails.Add(condNode);

        foreach (var t in loopCtx.ExitTails)
            _pendingNodes.Add(t);

        return prevNode;
    }

    private string ProcessFor(BinaryNode forNode, string prevNode)
    {
        var header = forNode.Left as BinaryNode; // for-header
        var body = forNode.Right;

        if (header == null)
            return prevNode;

        var init = header.Left;
        var condIncr = header.Right as BinaryNode; // for-cond
        var cond = condIncr?.Left;
        var incr = condIncr?.Right;

        // init
        string initLabel = CleanForLabel(GetExpressionLabel(init));
        string initNode = NewNode(initLabel, "process");

        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, initNode);
            _pendingNodes.Clear();
        }
        AddEdge(prevNode, initNode);

        // condition
        string condLabel = cond != null ? GetExpressionLabel(cond) : "true";
        string condNode = NewNode(condLabel, "decision");
        AddEdge(initNode, condNode);

        var loopCtx = new LoopContext { ConditionNode = condNode };
        _loopStack.Push(loopCtx);

        // body
        string bodyLast = ProcessNode(body, condNode);

        _loopStack.Pop();

        // increment
        if (incr != null && incr is not LiteralNode { Kind: "void" })
        {
            string incrLabel = CleanForLabel(GetExpressionLabel(incr));
            string incrNode = NewNode(incrLabel, "process");

            if (_pendingNodes.Count > 0)
            {
                foreach (var p in _pendingNodes)
                    AddEdge(p, incrNode);
                _pendingNodes.Clear();
            }
            if (!string.IsNullOrEmpty(bodyLast))
                AddEdge(bodyLast, incrNode);

            AddEdge(incrNode, condNode);
        }
        else
        {
            if (_pendingNodes.Count > 0)
            {
                foreach (var p in _pendingNodes)
                    AddEdge(p, condNode);
                _pendingNodes.Clear();
            }
            if (!string.IsNullOrEmpty(bodyLast))
                AddEdge(bodyLast, condNode);
        }

        // Выход — cond "Нет"
        loopCtx.ExitTails.Add(condNode);

        foreach (var t in loopCtx.ExitTails)
            _pendingNodes.Add(t);

        return prevNode;
    }

    // ================== Операторы и выражения ==================

    private string ProcessDeclaration(BinaryNode declNode, string prevNode)
    {
        var typeNode = declNode.Left as IdentifierNode;
        var assign = declNode.Right as AssignNode;

        if (typeNode == null || assign == null)
            return prevNode;

        string varName = GetExpressionLabel(assign.Left);
        string label =
            assign.Right is LiteralNode { Kind: "void" }
            ? $"{typeNode.Name} {varName}"
            : $"{typeNode.Name} {varName} = {GetExpressionLabel(assign.Right)}";

        string shape = IsIoExpression(assign.Right) ? "io" : "process";
        string nodeId = NewNode(label, shape);

        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, nodeId);
            _pendingNodes.Clear();
        }
        else
        {
            AddEdge(prevNode, nodeId);
        }

        return nodeId;
    }

    private string ProcessFunction(BinaryNode funcNode, string prevNode)
    {
        var retType = funcNode.Left as IdentifierNode;
        var paramsBody = funcNode.Right as BinaryNode;
        var nameNode = paramsBody?.Left as IdentifierNode;
        var body = paramsBody?.Right;

        if (retType == null || nameNode == null)
            return prevNode;

        string label = $"{retType.Name} {nameNode.Name}()";
        string funcId = NewNode(label, "process");

        // Специальный случай: main — точка входа, должна вести к последнему оператору main
        if (nameNode.Name == "main")
        {
            AddEdge(prevNode, funcId);

            string last = funcId;
            if (body is ProgramNode mainBlock)
                last = ProcessStatements(mainBlock.Children, funcId);
            else if (body != null)
                last = ProcessNode(body, funcId);

            // Возвращаем последний узел тела main, чтобы "Конец" подключился к return
            return last;
        }

        // Остальные функции: рисуем, но основной поток не меняем
        AddEdge(prevNode, funcId);

        if (body is ProgramNode funcBlock)
            ProcessStatements(funcBlock.Children, funcId);
        else if (body != null)
            ProcessNode(body, funcId);

        return prevNode;
    }

    private string ProcessReturn(UnaryNode retNode, string prevNode)
    {
        string valueLabel = GetExpressionLabel(retNode.Operand);
        string label = string.IsNullOrWhiteSpace(valueLabel)
            ? "return"
            : $"return {valueLabel}";

        string shape = IsIoExpression(retNode.Operand) ? "io" : "process";
        string nodeId = NewNode(label, shape);

        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, nodeId);
            _pendingNodes.Clear();
        }
        else
        {
            AddEdge(prevNode, nodeId);
        }

        return nodeId;
    }

    /// <summary>
    /// break: прерывает цикл и подключается к "следующему после цикла" оператору.
    /// </summary>
    private string ProcessBreak(string prevNode)
    {
        if (_loopStack.Count == 0)
        {
            string nodeId = NewNode("break", "process");
            AddEdge(prevNode, nodeId);
            return nodeId;
        }

        var loopCtx = _loopStack.Peek();
        string breakNode = NewNode("break", "process");
        AddEdge(prevNode, breakNode);

        // Сохраняем хвост выхода; подключим после завершения цикла
        loopCtx.ExitTails.Add(breakNode);

        // Обрываем дальнейшую линейную связь в теле
        return "";
    }

    /// <summary>
    /// continue: переходит к проверке условия цикла.
    /// </summary>
    private string ProcessContinue(string prevNode)
    {
        if (_loopStack.Count == 0)
        {
            string nodeId = NewNode("continue", "process");
            AddEdge(prevNode, nodeId);
            return nodeId;
        }

        var loopCtx = _loopStack.Peek();
        string continueNode = NewNode("continue", "process");
        AddEdge(prevNode, continueNode);
        AddEdge(continueNode, loopCtx.ConditionNode);

        return "";
    }

    private string ProcessExpression(AstNode expr, string prevNode)
    {
        if (expr is AssignNode assign)
            return ProcessAssignment(assign, prevNode);

        // Вызов функции
        if (expr is BinaryNode binNode && binNode.Op == "()")
            return ProcessFunctionCall(binNode, prevNode);

        return ProcessGenericExpression(expr, prevNode);
    }

    private string ProcessFunctionCall(BinaryNode callNode, string prevNode)
    {
        string funcName = GetExpressionLabel(callNode.Left);
        string argsLabel = callNode.Right is not LiteralNode { Kind: "void" }
            ? GetExpressionLabel(callNode.Right)
            : "";

        string label = string.IsNullOrWhiteSpace(argsLabel)
            ? $"{funcName}()"
            : $"{funcName}({argsLabel})";

        string nodeId = NewNode(label, "process");

        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, nodeId);
            _pendingNodes.Clear();
        }
        else
        {
            AddEdge(prevNode, nodeId);
        }

        return nodeId;
    }

    private string ProcessAssignment(AssignNode assign, string prevNode)
    {
        string varName = GetExpressionLabel(assign.Left);
        string value = GetExpressionLabel(assign.Right);
        string label = $"{varName} {assign.Op} {value}";
        string shape = IsIoExpression(assign) ? "io" : "process";
        string nodeId = NewNode(label, shape);

        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, nodeId);
            _pendingNodes.Clear();
        }
        else
        {
            AddEdge(prevNode, nodeId);
        }

        return nodeId;
    }

    private string ProcessGenericExpression(AstNode expr, string prevNode)
    {
        string label = GetExpressionLabel(expr);
        if (string.IsNullOrWhiteSpace(label))
            return prevNode;

        string shape = IsIoExpression(expr) ? "io" : "process";
        string nodeId = NewNode(label, shape);

        if (_pendingNodes.Count > 0)
        {
            foreach (var p in _pendingNodes)
                AddEdge(p, nodeId);
            _pendingNodes.Clear();
        }
        else
        {
            AddEdge(prevNode, nodeId);
        }

        return nodeId;
    }

    // ================== Вспомогательные функции ==================

    private string CleanForLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return label;

        label = System.Text.RegularExpressions.Regex.Replace(label, @"^\w+\s+decl\s+", "");
        label = label.Replace("post", "");
        label = label.Replace("prefix", "");
        return label.Trim();
    }

    private string GetExpressionLabel(AstNode? node)
    {
        if (node == null)
            return string.Empty;

        // Постфиксные ++/--
        if (node is BinaryNode binPost &&
            (binPost.Op == "++-post" || binPost.Op == "---post") &&
            binPost.Left is IdentifierNode idNode &&
            binPost.Right is LiteralNode { Kind: "void" })
        {
            string op = binPost.Op.StartsWith("++", StringComparison.Ordinal) ? "++" : "--";
            return $"{idNode.Name} {op}";
        }

        // Вызов функции
        if (node is BinaryNode funcCall && funcCall.Op == "()")
        {
            string funcName = GetExpressionLabel(funcCall.Left);
            string args = GetExpressionLabel(funcCall.Right);
            return string.IsNullOrWhiteSpace(args) ? $"{funcName}()" : $"{funcName}({args})";
        }

        return node switch
        {
            IdentifierNode identifier => identifier.Name,
            LiteralNode lit => lit.Value,
            BinaryNode bin => $"{GetExpressionLabel(bin.Left)} {bin.Op} {GetExpressionLabel(bin.Right)}",
            UnaryNode un => $"{un.Op}{GetExpressionLabel(un.Operand)}",
            AssignNode assign => $"{GetExpressionLabel(assign.Left)} {assign.Op} {GetExpressionLabel(assign.Right)}",
            ExprStatementNode es => GetExpressionLabel(es.Expr),
            ProgramNode => "{...}",
            _ => node.GetType().Name
        };
    }

    private bool IsIoExpression(AstNode? node)
    {
        if (node == null) return false;

        return node switch
        {
            IdentifierNode id =>
                id.Name is "cin" or "cout" or "cerr" or "clog" or "scanf" or "printf" or "ReadLine" or "WriteLine" or "read" or "write",
            BinaryNode bin =>
                bin.Op is ">>" or "<<" ||
                IsIoExpression(bin.Left) || IsIoExpression(bin.Right),
            UnaryNode un =>
                IsIoExpression(un.Operand),
            AssignNode assign =>
                IsIoExpression(assign.Left) || IsIoExpression(assign.Right),
            ExprStatementNode es =>
                IsIoExpression(es.Expr),
            ProgramNode prog =>
                prog.Children.Any(IsIoExpression),
            _ => false
        };
    }

    private string NewNode(string label, string type)
    {
        string nodeId = $"n{_nodeCounter++}";
        string shapeAndLabel = type switch
        {
            "terminator" => $"([{EscapeMermaidLabel(label)}])",
            "process" => $"[\"{EscapeMermaidLabel(label)}\"]",
            "io" => $"[/{EscapeMermaidLabel(label)}/]",
            "decision" => $"{{{EscapeMermaidLabel(label)}}}",
            _ => $"[\"{EscapeMermaidLabel(label)}\"]"
        };

        _mermaid.AppendLine($" {nodeId}{shapeAndLabel}");

        if (type == "decision")
            _decisionEdgeCount[nodeId] = 0;

        return nodeId;
    }

    private void AddEdge(string from, string to, string label = "")
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            return;

        // Для ромба: авто-метки "Да"/"Нет", если метка не указана
        if (_decisionEdgeCount.TryGetValue(from, out int count))
        {
            if (string.IsNullOrEmpty(label))
            {
                label = count == 0 ? "Да" : "Нет";
            }
            _decisionEdgeCount[from] = count + 1;
        }

        if (!string.IsNullOrEmpty(label))
            _mermaid.AppendLine($" {from} -->|{EscapeMermaidLabel(label)}| {to}");
        else
            _mermaid.AppendLine($" {from} --> {to}");
    }

    private string EscapeMermaidLabel(string text)
    {
        return text
            .Replace("\"", "'")
            .Replace("\\n", " ")
            .Replace("[", "(")
            .Replace("]", ")")
            .Replace("{", "(")
            .Replace("}", ")")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
