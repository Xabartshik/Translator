// ===================== Ast.cs =====================
using System;
using System.Collections.Generic;
using System.IO;

namespace Parser;

// Базовый узел
public abstract record AstNode;

// Корень - набор операторов/выражений
public record ProgramNode(IReadOnlyList<AstNode> Children) : AstNode;

// Оператор вида "expr;"
public record ExprStatementNode(AstNode Expr) : AstNode;

// Присваивание: left op right ("=" для C++)
public record AssignNode(AstNode Left, string Op, AstNode Right) : AstNode;

// Бинарный оператор: +, -, *, /, &&, ||, ==, < и т.п.
public record BinaryNode(string Op, AstNode Left, AstNode Right) : AstNode;

// Унарный оператор: !, -, +, ++, --, return и т.п.
public record UnaryNode(string Op, AstNode Operand) : AstNode;

// Идентификатор
public record IdentifierNode(string Name, int Line, int Column) : AstNode;

// Литерал (число, строка, char, bool)
public record LiteralNode(string Kind, string Value, int Line, int Column) : AstNode;

public static class AstPrinter
{
    public const string DefaultTreeFileName = "ast-tree.txt";

    public static void PrintDeepTree(AstNode root)
    {
        if (root == null)
        {
            Console.WriteLine("");
            return;
        }

        int maxDepth = GetDepth(root);
        Console.WriteLine("Дерево разбора:");
        PrintNode(root, prefix: string.Empty, isLast: true, depth: 1, maxDepth, Console.Out);
    }

    public static void PrintDeepTreeToFile(AstNode root, string? filePath = null)
    {
        filePath = string.IsNullOrWhiteSpace(filePath) ? DefaultTreeFileName : filePath;
        using var writer = new StreamWriter(filePath, append: false);

        if (root == null)
        {
            writer.WriteLine("");
            return;
        }

        int maxDepth = GetDepth(root);
        writer.WriteLine("Дерево разбора:");
        PrintNode(root, prefix: string.Empty, isLast: true, depth: 1, maxDepth, writer);
    }

    private static int GetDepth(AstNode node)
    {
        if (node == null) return 0;
        var children = GetChildren(node);
        if (children.Count == 0) return 1;
        int max = 0;
        foreach (var ch in children)
            max = Math.Max(max, GetDepth(ch));
        return 1 + max;
    }

    private static void PrintNode(
        AstNode node,
        string prefix,
        bool isLast,
        int depth,
        int maxDepth,
        TextWriter writer)
    {
        var children = GetChildren(node);

        if (children.Count == 0)
        {
            int extraLevels = maxDepth - depth;
            int dashCount = 2 + extraLevels * 4;
            writer.Write(prefix);
            writer.Write(isLast ? "└" : "├");
            writer.Write(new string('─', dashCount));
            writer.Write(" ");
            writer.WriteLine(NodeLabel(node));
            return;
        }

        writer.Write(prefix);
        writer.Write(isLast ? "└── " : "├── ");
        writer.WriteLine(NodeLabel(node));

        string childPrefix = prefix + (isLast ? "   " : "│  ");
        for (int i = 0; i < children.Count; i++)
        {
            bool childIsLast = (i == children.Count - 1);
            PrintNode(children[i], childPrefix, childIsLast, depth + 1, maxDepth, writer);
        }
    }

    private static List<AstNode> GetChildren(AstNode node)
    {
        var list = new List<AstNode>();
        switch (node)
        {
            case ProgramNode p:
                list.AddRange(p.Children);
                break;
            case ExprStatementNode es:
                list.Add(es.Expr);
                break;
            case AssignNode a:
                list.Add(a.Left);
                list.Add(a.Right);
                break;
            case BinaryNode b:
                list.Add(b.Left);
                list.Add(b.Right);
                break;
            case UnaryNode u:
                list.Add(u.Operand);
                break;
                // IdentifierNode / LiteralNode — листья
        }
        return list;
    }

    private static string NodeLabel(AstNode node) =>
        node switch
        {
            ProgramNode => "Program",
            ExprStatementNode => "ExprStmt",
            AssignNode a => $"Assign({a.Op})",
            BinaryNode b => $"Bin({b.Op})",
            UnaryNode u => $"Un({u.Op})",
            IdentifierNode id => $"Id({id.Name})",
            LiteralNode lit => $"{lit.Kind}({lit.Value})",
            _ => node.GetType().Name
        };


}
