using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Parser;

namespace FlowchartGen;

///
/// Генератор Mermaid блок-схем по ГОСТ 19.701-90 (ЕСПД).
/// Обходит AST и строит диаграмму потока управления в формате Mermaid.
///
/// Элементы ГОСТ 19.701-90:
/// - Овал ([...]) : начало/конец
/// - Прямоугольник ["..."] : действие, операция
/// - Параллелограмм [/.../ или \.../] : ввод/вывод данных
/// - Ромб {...} : проверка условия
/// - Стрелки с подписями "Да"/"Нет" для развилок
/// - Ортогональные углы (linear curve) для чёткой геометрии
///
public sealed class ASTToMermaid
{
    private int _nodeCounter = 0;
    private readonly StringBuilder _mermaid = new();
    private List<string> _pendingNodes = new(); // Узлы, ожидающие подключения
    private readonly Dictionary<string, int> _decisionEdgeCount = new(); // исходящие рёбра из decision-узлов

    ///
    /// Сгенерировать Mermaid-код блок-схемы для заданного AST в соответствии с ГОСТ 19.701-90.
    /// Использует linear curve (прямые стрелки), ELK renderer, оптимальный spacing.
    ///
    public string Generate(AstNode? root, string graphName = "Flowchart")
    {
        if (root == null)
            return "flowchart TD\n    Start([...])";

        _nodeCounter = 0;
        _mermaid.Clear();
        _pendingNodes.Clear();
        _decisionEdgeCount.Clear();

        // Инициализация с настройками Mermaid
        _mermaid.AppendLine("%%{init: {'flowchart': {");
        _mermaid.AppendLine("  'curve': 'linear',");
        _mermaid.AppendLine("  'nodeSpacing': 80,");
        _mermaid.AppendLine("  'rankSpacing': 100,");
        _mermaid.AppendLine("  'diagramPadding': 40,");
        _mermaid.AppendLine("  'defaultRenderer': 'elk'");
        _mermaid.AppendLine("}}}%%");
        _mermaid.AppendLine("flowchart-elk TD");

        // Начальный и конечный элементы
        string startNode = NewNode("Начало", "terminator");
        string lastNode = startNode;

        if (root is ProgramNode program)
            lastNode = ProcessStatements(program.Children, startNode);
        else
            lastNode = ProcessNode(root, startNode);

        // Подключаем все отложенные узлы к концу
        foreach (var pendingNode in _pendingNodes)
        {
            AddEdge(pendingNode, lastNode);
        }
        _pendingNodes.Clear();

        string endNode = NewNode("Конец", "terminator");
        AddEdge(lastNode, endNode);

        // Добавляем стиль для subgraph
        _mermaid.AppendLine("\n    classDef loopGroup fill:#1a2a3a,stroke:#4a7c9e,stroke-width:2px,color:#fff;");

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

    ///
    /// if (cond) thenBranch else elseBranch
    /// Условие — ромб (decision).
    /// ИСПРАВЛЕНО: Возвращаем обе ветки в список _pendingNodes, 
    /// чтобы следующий узел подключился к обеим сразу.
    ///
    private string ProcessIf(BinaryNode ifNode, string prevNode)
    {
        string condLabel = GetExpressionLabel(ifNode.Left);
        string condNode = NewNode(condLabel, "decision"); // ромб
        AddEdge(prevNode, condNode);

        var branches = ifNode.Right as BinaryNode; // then-else
        var thenBranch = branches?.Left;
        var elseBranch = branches?.Right;

        // Проверяем наличие веток
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

        // Оба выхода существуют — объединяем через pending
        if (hasThen && hasElse && thenTarget != null && elseTarget != null)
        {
            _pendingNodes.Add(thenTarget);
            _pendingNodes.Add(elseTarget);
            return thenTarget;
        }

        if (hasThen && thenTarget != null)
            return thenTarget;

        if (hasElse && elseTarget != null)
            return elseTarget;

        return condNode;
    }

    ///
    /// while (cond) body
    /// Условие цикла — ромб (decision).
    ///
    private string ProcessWhile(BinaryNode whileNode, string prevNode)
    {
        string condLabel = GetExpressionLabel(whileNode.Left);
        string condNode = NewNode(condLabel, "decision");
        AddEdge(prevNode, condNode);

        string bodyLast = ProcessNode(whileNode.Right, condNode);
        AddEdge(bodyLast, condNode); // назад к условию

        return condNode;
    }

    ///
    /// do { body } while (cond);
    ///
    private string ProcessDoWhile(BinaryNode doWhileNode, string prevNode)
    {
        string bodyLast = ProcessNode(doWhileNode.Right, prevNode);
        string condLabel = GetExpressionLabel(doWhileNode.Left);
        string condNode = NewNode(condLabel, "decision");
        AddEdge(bodyLast, condNode);
        AddEdge(condNode, prevNode, "Да"); // назад к телу (явная метка)

        return condNode;
    }

    ///
    /// for (init; cond; incr) body
    /// Очищенный вариант: "int i = 1" → "i = 1", "i++post" → "i++"
    ///
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

        // инициализация — очищаем текст от типа переменной
        string initLabel = CleanForLabel(GetExpressionLabel(init));
        string initNode = NewNode(initLabel, "process");
        AddEdge(prevNode, initNode);

        // условие цикла
        string condLabel = cond != null ? GetExpressionLabel(cond) : "true";
        string condNode = NewNode(condLabel, "decision");
        AddEdge(initNode, condNode);

        // тело цикла
        string bodyLast = ProcessNode(body, condNode);

        // изменение параметров — очищаем от постфикса
        if (incr != null && incr is not LiteralNode { Kind: "void" })
        {
            string incrLabel = CleanForLabel(GetExpressionLabel(incr));
            string incrNode = NewNode(incrLabel, "process");
            AddEdge(bodyLast, incrNode);
            AddEdge(incrNode, condNode);
        }
        else
        {
            AddEdge(bodyLast, condNode);
        }

        return condNode;
    }

    // ================== Операторы и выражения ==================

    private string ProcessDeclaration(BinaryNode declNode, string prevNode)
    {
        var typeNode = declNode.Left as IdentifierNode;
        var assign = declNode.Right as AssignNode;

        if (typeNode == null || assign == null)
            return prevNode;

        string varName = GetExpressionLabel(assign.Left);
        string label = assign.Right is LiteralNode { Kind: "void" }
            ? $"{typeNode.Name} {varName}"
            : $"{typeNode.Name} {varName} = {GetExpressionLabel(assign.Right)}";

        string shape = IsIoExpression(assign.Right) ? "io" : "process";
        string nodeId = NewNode(label, shape);

        if (_pendingNodes.Count > 0)
        {
            foreach (var pending in _pendingNodes)
                AddEdge(pending, nodeId);
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
        AddEdge(prevNode, funcId);

        if (body != null)
            return ProcessNode(body, funcId);

        return funcId;
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
            foreach (var pending in _pendingNodes)
                AddEdge(pending, nodeId);
            _pendingNodes.Clear();
        }
        else
        {
            AddEdge(prevNode, nodeId);
        }

        return nodeId;
    }

    private string ProcessBreak(string prevNode)
    {
        string nodeId = NewNode("break", "process");
        AddEdge(prevNode, nodeId);
        return nodeId;
    }

    private string ProcessContinue(string prevNode)
    {
        string nodeId = NewNode("continue", "process");
        AddEdge(prevNode, nodeId);
        return nodeId;
    }

    private string ProcessExpression(AstNode expr, string prevNode)
    {
        if (expr is AssignNode assign)
            return ProcessAssignment(assign, prevNode);
        return ProcessGenericExpression(expr, prevNode);
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
            foreach (var pending in _pendingNodes)
                AddEdge(pending, nodeId);
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
            foreach (var pending in _pendingNodes)
                AddEdge(pending, nodeId);
            _pendingNodes.Clear();
        }
        else
        {
            AddEdge(prevNode, nodeId);
        }

        return nodeId;
    }

    // ================== Вспомогательные функции ==================

    ///
    /// Очищает текст от технических деталей AST для цикла FOR
    /// Примеры:
    /// "int decl i = 1" → "i = 1"
    /// "i++post" → "i++"
    /// "++iprefix" → "++i"
    ///
    private string CleanForLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return label;

        // Удаляем "<type> decl " в начале
        label = System.Text.RegularExpressions.Regex.Replace(label, @"^\w+\s+decl\s+", "");

        // Удаляем служебные суффиксы пост/префиксных операций
        label = label.Replace("post", "");
        label = label.Replace("prefix", "");

        return label.Trim();
    }

    private string GetExpressionLabel(AstNode? node)
    {
        if (node == null)
            return string.Empty;

        return node switch
        {
            IdentifierNode id => id.Name,
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
                id.Name is "cin" or "cout" or "scanf" or "printf" or "ReadLine" or "WriteLine" or "read" or "write",

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

        _mermaid.AppendLine($"    {nodeId}{shapeAndLabel}");

        // регистрируем все decision-узлы для авто "Да"/"Нет"
        if (type == "decision")
            _decisionEdgeCount[nodeId] = 0;

        return nodeId;
    }

    private void AddEdge(string from, string to, string label = "")
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            return;

        // Если from — decision-узел, считаем исходящие рёбра и,
        // при отсутствии явной метки, подставляем "Да"/"Нет"
        if (_decisionEdgeCount.TryGetValue(from, out int count))
        {
            if (string.IsNullOrEmpty(label))
            {
                label = count == 0 ? "Да" : "Нет";
            }
            _decisionEdgeCount[from] = count + 1;
        }

        if (!string.IsNullOrEmpty(label))
        {
            _mermaid.AppendLine($"    {from} -->|{EscapeMermaidLabel(label)}| {to}");
        }
        else
        {
            _mermaid.AppendLine($"    {from} --> {to}");
        }
    }

    private string EscapeMermaidLabel(string text)
    {
        return text
            .Replace("\"", "'")
            .Replace("\n", " ")
            .Replace("[", "(")
            .Replace("]", ")")
            .Replace("{", "(")
            .Replace("}", ")")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
