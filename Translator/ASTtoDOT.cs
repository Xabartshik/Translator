using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Parser;

namespace FlowchartGen;

/// <summary>
/// Генератор блок-схем по ГОСТ 19.701-90 (ЕСПД).
/// Обходит AST и строит граф потока управления.
///
/// Элементы ГОСТ 19.701-90:
/// - Овал (terminator): начало/конец
/// - Прямоугольник (process): действие, операция
/// - Параллелограмм (io): ввод/вывод данных
/// - Ромб (decision): проверка условия
/// - Шестиугольник (loop-cond): подготовка/условие/изменение цикла
/// - Прямоугольник со сдвоенной линией (predefined-process): вызов подпрограммы
/// - Документ (document): обработка документа
/// - Стрелки с подписями "да"/"нет" для развилок
/// </summary>
public sealed class FlowchartGeneratorGOST
{
    private int _nodeCounter = 0;
    private readonly StringBuilder _dot = new();

    /// <summary>
    /// Сгенерировать DOT-код блок-схемы для заданного AST в соответствии с ГОСТ 19.701-90.
    /// </summary>
    public string Generate(AstNode? root, string graphName = "Flowchart")
    {
        if (root == null)
            return "digraph G { }";

        _nodeCounter = 0;
        _dot.Clear();

        // Заголовок графа
        _dot.AppendLine($"digraph {graphName} {{");
        _dot.AppendLine(" rankdir=TB;");

        // Общие атрибуты графа.
        // Используем splines=ortho для прямых углов на стрелках
        _dot.AppendLine(" graph [");
        _dot.AppendLine(" bgcolor=white,");
        _dot.AppendLine(" splines=ortho,");
        _dot.AppendLine(" nodesep=0.6,");
        _dot.AppendLine(" ranksep=0.8,");
        _dot.AppendLine(" margin=0.5");
        _dot.AppendLine(" ];");

        // Атрибуты узлов
        _dot.AppendLine(" node [");
        _dot.AppendLine(" fontname=\"Arial\",");
        _dot.AppendLine(" fontsize=10,");
        _dot.AppendLine(" color=black,");
        _dot.AppendLine(" style=solid,");
        _dot.AppendLine(" width=1.0,");
        _dot.AppendLine(" height=0.6");
        _dot.AppendLine(" ];");

        // Атрибуты рёбер
        _dot.AppendLine(" edge [");
        _dot.AppendLine(" color=black,");
        _dot.AppendLine(" arrowsize=1.0,");
        _dot.AppendLine(" fontname=\"Arial\",");
        _dot.AppendLine(" fontsize=9");
        _dot.AppendLine(" ];");
        _dot.AppendLine();

        // Начальный и конечный элементы
        string startNode = NewNode("Начало", "terminator");
        string lastNode = startNode;

        if (root is ProgramNode program)
            lastNode = ProcessStatements(program.Children, startNode);
        else
            lastNode = ProcessNode(root, startNode);

        string endNode = NewNode("Конец", "terminator");
        AddEdge(lastNode, endNode);

        _dot.AppendLine("}");
        return _dot.ToString();
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

            // Блок кода
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

    /// <summary>
    /// if (cond) thenBranch else elseBranch
    /// Условие — ромб (решение).
    /// БЕЗ промежуточных соединителей — стрелки идут напрямую!
    /// </summary>
    private string ProcessIf(BinaryNode ifNode, string prevNode)
    {
        string condLabel = GetExpressionLabel(ifNode.Left);
        string condNode = NewNode(condLabel, "decision"); // ромб
        AddEdge(prevNode, condNode);

        var branches = ifNode.Right as BinaryNode; // then-else
        var thenBranch = branches?.Left;
        var elseBranch = branches?.Right;

        // Определяем, куда выходит "да" и "нет"
        string thenTarget = thenBranch != null && thenBranch is not LiteralNode { Kind: "void" }
            ? ProcessNode(thenBranch, condNode)
            : null;

        string elseTarget = elseBranch != null && elseBranch is not LiteralNode { Kind: "void" }
            ? ProcessNode(elseBranch, condNode)
            : null;

        // Если обе ветки есть — они самостоятельно обработают выход
        if (thenTarget != null || elseTarget != null)
        {
            if (thenTarget != null)
                AddEdge(condNode, thenTarget, "да");
            if (elseTarget != null)
                AddEdge(condNode, elseTarget, "нет");

            // Возвращаем последний обработанный узел
            return thenTarget ?? elseTarget ?? condNode;
        }

        // Если нет веток — просто идём дальше
        return condNode;
    }

    /// <summary>
    /// while (cond) body
    /// Условие цикла — шестиугольник.
    /// БЕЗ промежуточных соединителей.
    /// </summary>
    private string ProcessWhile(BinaryNode whileNode, string prevNode)
    {
        string condLabel = GetExpressionLabel(whileNode.Left);
        string condNode = NewNode(condLabel, "loop-cond");
        AddEdge(prevNode, condNode);

        string bodyLast = ProcessNode(whileNode.Right, condNode);
        AddEdge(bodyLast, condNode); // назад к условию

        // Возвращаем сам узел условия как "выход" цикла
        return condNode;
    }

    /// <summary>
    /// do { body } while (cond);
    /// БЕЗ промежуточных соединителей.
    /// </summary>
    private string ProcessDoWhile(BinaryNode doWhileNode, string prevNode)
    {
        string bodyLast = ProcessNode(doWhileNode.Right, prevNode);

        string condLabel = GetExpressionLabel(doWhileNode.Left);
        string condNode = NewNode(condLabel, "loop-cond");
        AddEdge(bodyLast, condNode);

        AddEdge(condNode, prevNode, "да"); // назад к телу

        // Возвращаем условие цикла
        return condNode;
    }

    /// <summary>
    /// for (init; cond; incr) body
    /// БЕЗ промежуточных соединителей.
    /// </summary>
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

        // подготовка цикла (инициализация)
        string initLabel = GetExpressionLabel(init);
        string initNode = NewNode(initLabel, "loop-cond");
        AddEdge(prevNode, initNode);

        // условие цикла
        string condLabel = cond != null ? GetExpressionLabel(cond) : "true";
        string condNode = NewNode(condLabel, "loop-cond");
        AddEdge(initNode, condNode);

        // тело цикла — обрабатываем напрямую
        string bodyLast = ProcessNode(body, condNode);

        // изменение параметров
        if (incr != null && incr is not LiteralNode { Kind: "void" })
        {
            string incrLabel = GetExpressionLabel(incr);
            string incrNode = NewNode(incrLabel, "loop-cond");
            AddEdge(bodyLast, incrNode);
            AddEdge(incrNode, condNode);
        }
        else
        {
            AddEdge(bodyLast, condNode);
        }

        // Возвращаем условие как точку выхода
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
        string label =
            assign.Right is LiteralNode { Kind: "void" }
                ? $"{typeNode.Name} {varName}"
                : $"{typeNode.Name} {varName} = {GetExpressionLabel(assign.Right)}";

        string shape = IsIoExpression(assign.Right) ? "io" : "process";
        string nodeId = NewNode(label, shape);
        AddEdge(prevNode, nodeId);

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
        string funcId = NewNode(label, "predefined-process");
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
        AddEdge(prevNode, nodeId);

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
        AddEdge(prevNode, nodeId);

        return nodeId;
    }

    private string ProcessGenericExpression(AstNode expr, string prevNode)
    {
        string label = GetExpressionLabel(expr);

        if (string.IsNullOrWhiteSpace(label))
            return prevNode;

        string shape = IsIoExpression(expr) ? "io" : "process";
        string nodeId = NewNode(label, shape);
        AddEdge(prevNode, nodeId);

        return nodeId;
    }

    // ================== Вспомогательные функции ==================

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

        string shapeAttr = type switch
        {
            "terminator" => "shape=ellipse",
            "process" => "shape=box",
            "io" => "shape=parallelogram",
            "decision" => "shape=diamond",
            "loop-cond" => "shape=hexagon",
            "predefined-process" => "shape=component",
            "document" => "shape=note",
            _ => "shape=box"
        };

        string labelAttr = (!string.IsNullOrEmpty(label) && type != "connector")
            ? $", label=\"{EscapeLabel(label)}\""
            : ", label=\"\"";

        _dot.AppendLine($" {nodeId} [{shapeAttr}{labelAttr}];");

        return nodeId;
    }

    private void AddEdge(string from, string to, string label = "")
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            return;

        // Для "да"/"нет" используем taillabel для подписи у начала стрелки
        if (label == "да" || label == "нет")
        {
            string escaped = EscapeLabel(label);
            string tailPort = label == "да" ? ":e" : ":w";
            _dot.AppendLine(
                $" {from}{tailPort} -> {to} [taillabel=\"{escaped}\", labeldistance=0.3, labelangle=0];");
            return;
        }

        string attrs = "";
        if (!string.IsNullOrEmpty(label))
            attrs = $" [taillabel=\"{EscapeLabel(label)}\", labeldistance=0.3, labelangle=0]";

        _dot.AppendLine($" {from} -> {to}{attrs};");
    }

    private string EscapeLabel(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("&", "&")
            .Replace("<", "<")
            .Replace(">", ">");
    }
}
