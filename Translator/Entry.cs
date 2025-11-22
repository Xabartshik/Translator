// ===================== Entry.cs =====================

using System;
using System.Collections.Generic;

namespace Parser;

/// <summary>
/// Запись в таблице идентификаторов (с поддержкой областей видимости и затенения).
/// </summary>
public class Entry
{
    public string Name { get; init; } = string.Empty; // Имя идентификатора
    public string Kind { get; set; } = "var"; // var / func / param / type / std
    public string Type { get; set; } = "int"; // Имя типа (int, float, bool, ...)
    public int ScopeDepth { get; set; } = -1; // Глубина области (0=глобальная, 1=функция, 2=блок, ...)
    public Entry? Shadowed { get; set; } // Кого затеняет
    public bool IsInitialized { get; set; } = false; // Было ли присваивание
    public int LineDeclared { get; init; } // Строка объявления
    public int ColumnDeclared { get; init; } // Столбец объявления
    public bool IsConst { get; set; } = false; // Константная?

    public Entry(string name, string kind, string type, int line, int column)
    {
        Name = name;
        Kind = kind;
        Type = type;
        LineDeclared = line;
        ColumnDeclared = column;
    }

    public override string ToString() => $"{Name}:{Type} (kind={Kind}, depth={ScopeDepth}, const={IsConst}, init={IsInitialized})";
}

/// <summary>
/// Одна область видимости.
/// </summary>
public sealed class Scope
{
    public int Depth { get; }
    public Scope? OuterScope { get; }
    public Dictionary<string, Entry> Bindings { get; } = new();

    public Scope(int depth, Scope? outerScope)
    {
        Depth = depth;
        OuterScope = outerScope;
    }
}

/// <summary>
/// Менеджер областей видимости + таблица идентификаторов.
/// </summary>
public sealed class ScopeManager
{
    private readonly Stack<Scope> _scopeStack = new();

    public ScopeManager()
    {
        EnterScope();  // Global scope
    }

    public int CurrentDepth => _scopeStack.Peek().Depth;

    public void EnterScope()
    {
        var parent = _scopeStack.Count > 0 ? _scopeStack.Peek() : null;
        var scope = new Scope(_scopeStack.Count, parent);
        _scopeStack.Push(scope);
    }

    public void ExitScope()
    {
        if (_scopeStack.Count > 1)
            _scopeStack.Pop();
    }

    public Entry? Lookup(string name)
    {
        var scope = _scopeStack.Peek();
        while (scope != null)
        {
            if (scope.Bindings.TryGetValue(name, out var entry))
                return entry;
            scope = scope.OuterScope;
        }
        return null;
    }

    public Entry Declare(string name, string kind, string type, bool isConst, int line, int column, IList<(int, int, string)> errors)
    {
        var current = _scopeStack.Peek();
        if (current.Bindings.ContainsKey(name))
        {
            errors.Add((line, column, $"Повторное объявление идентификатора '{name}' в данной области видимости."));
            return current.Bindings[name];
        }

        var shadowed = Lookup(name);
        var entry = new Entry(name, kind, type, line, column)
        {
            ScopeDepth = current.Depth,
            Shadowed = shadowed,
            IsConst = isConst,
            IsInitialized = kind == "std" || kind == "type"  // NEW: Std and types (built-in) initialized
        };
        current.Bindings[name] = entry;
        return entry;
    }

    /// <summary>
    /// NEW: Skip IsInitialized check for std/type (cout, endl, int etc. "always ready")
    /// </summary>
    public Entry? Require(string name, int line, int column, IList<(int, int, string)> errors)
    {
        var entry = Lookup(name);
        if (entry == null)
        {
            errors.Add((line, column, $"Использование необъявленного идентификатора '{name}'."));
            return null;
        }
        // NEW: Skip init check for built-ins
        if (!entry.IsInitialized && entry.Kind != "std" && entry.Kind != "type")
        {
            errors.Add((line, column, $"Использование неинициализированной переменной '{name}'."));
        }
        return entry;
    }
}
