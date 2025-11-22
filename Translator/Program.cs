// ===================== Program.cs =====================
using Lexer;
using Parser;
using System;
using System.IO;

class Program
{
    public static void RunToFile(string source, string title, string? outputPath = null)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var safeTitle = string.Join("_",
                title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safeTitle))
                safeTitle = "analysis";
            outputPath = safeTitle + ".log";
        }

        using var writer = new StreamWriter(outputPath, append: false);
        writer.WriteLine($"=== {title} [C++] ===");

        // 1. Лексический анализ
        var lex = new LexAnalyzer(source);
        var tokens = lex.Scan();

        // 2. Синтаксический анализ (C++-подмножество)
        var parser = new SyntaxAnalyzer(tokens);
        var ast = parser.ParseProgram();

        // 3. Вывод дерева разбора в файл
        if (ast != null)
            AstPrinter.PrintDeepTreeToFile(ast);

        // 4. Диагностика ошибок
        if (lex.Errors.Count > 0 || parser.Errors.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("=== ОШИБКИ ===");

            foreach (var (line, col, msg) in lex.Errors)
                writer.WriteLine($"Лексическая ошибка в {line}:{col} - {msg}");

            foreach (var (line, col, msg) in parser.Errors)
                writer.WriteLine($"Синтаксическая ошибка в {line}:{col} - {msg}");
        }
        else
        {
            writer.WriteLine();
            writer.WriteLine("Синтаксический анализ: OK");
        }

        writer.WriteLine();
    }

    public static void Run(string source, string title)
    {
        Console.WriteLine($"=== {title} [C++] ===");

        var lex = new LexAnalyzer(source);
        var tokens = lex.Scan();

        var parser = new SyntaxAnalyzer(tokens);
        var ast = parser.ParseProgram();

        if (ast != null)
            AstPrinter.PrintDeepTree(ast);

        if (lex.Errors.Count > 0 || parser.Errors.Count > 0)
        {
            Console.WriteLine("\n=== ОШИБКИ ===");
            Console.ForegroundColor = ConsoleColor.Red;

            foreach (var (line, col, msg) in lex.Errors)
                Console.WriteLine($"Лексическая ошибка в {line}:{col} - {msg}");

            foreach (var (line, col, msg) in parser.Errors)
                Console.WriteLine($"Синтаксическая ошибка в {line}:{col} - {msg}");

            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nСинтаксический анализ: OK");
            Console.ResetColor();
        }

        Console.WriteLine();
    }

    static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            string filePath = args[0];

            bool toFile = args.Length > 1 &&
                          args[1].Equals("tofile", StringComparison.OrdinalIgnoreCase);

            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"ОШИБКА: Файл '{filePath}' не найден.");
                Console.ResetColor();
                return;
            }

            string source = File.ReadAllText(filePath);

            if (toFile)
                RunToFile(source, $"Файл: {filePath}");
            else
                Run(source, $"Файл: {filePath}");
        }
        else
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  СИНТАКСИЧЕСКИЙ АНАЛИЗАТОР (C++)                           ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            // Примеры C++
            string cpp_const_test =
                "#include <iostream>\n" +
                "using namespace std;\n" +
                "\n" +
                "int main() {\n" +
                "    const int c1 = 5;  // Константная переменная (неизменяемая)\n" +
                "    \n" +
                "    cout << \"Константа c1: \" << c1 << endl;  // OK: чтение const\n" +
                "    \n" +
                "    // Ошибка: попытка изменить const\n" +
                "    c1 = 10;  // Должна быть ошибка: cannot assign to constant variable 'c1'\n" +
                "    \n" +
                "    {  // Блок: локальная const\n" +
                "        const int c2 = 15;  // Локальная const\n" +
                "        cout << \"Локальная const c2: \" << c2 << endl;  // OK\n" +
                "        \n" +
                "        // Ошибка внутри блока\n" +
                "        c2 = 20;  // Ошибка: изменение локальной const\n" +
                "        \n" +
                "        // Тест: использование внешней const внутри блока (OK)\n" +
                "        cout << \"Внешняя const: \" << c1 << endl;\n" +
                "    }\n" +
                "    \n" +
                "    // После блока: c2 недоступна (scope), но c1 доступна\n" +
                "    cout << \"После блока: c1 = \" << c1 << endl;  // OK\n" +
                "    // c2 = 25;  // Двойная ошибка: необъявленная + const (если раскомментировать)\n" +
                "    \n" +
                "    // Тест в for: const в инициализации for\n" +
                "    for (const int i = 0; i < 3; i++) {  // const i локальна в for\n" +
                "        cout << \"Const i в for: \" << i << endl;  // OK: чтение\n" +
                "        // i++;  // Ошибка: cannot increment const 'i' (если раскомментировать)\n" +
                "    }\n" +
                "    \n" +
                "    // Тест в if: const в ветке\n" +
                "    if (true) {\n" +
                "        const int if_c = 30;  // Локальная const в if\n" +
                "        cout << \"Const в if: \" << if_c << endl;  // OK\n" +
                "        if_c = 35;  // Ошибка: изменение const в if\n" +
                "    } else {\n" +
                "        const int else_c = 40;  // В else\n" +
                "        cout << \"Const в else: \" << else_c << endl;  // OK (если false, но true — не выполнится)\n" +
                "        else_c = 45;  // Ошибка в else\n" +
                "    }\n" +
                "    // cout << if_c << endl;  // Ошибка: if_c вышла из scope\n" +
                "    \n" +
                "    // Смешанный тест: non-const и const с одинаковым именем (затенение)\n" +
                "    int var = 50;  // non-const\n" +
                "    { const int var = 60; }  // const var затеняет non-const (OK, разные типы/константность)\n" +
                "    var = 55;  // OK: присваивание non-const (внешней)\n" +
                "    \n" +
                "    // Диагностика типов (для полноты)\n" +
                "    int sum = 10.3;  // Ошибка типов: int = double\n" +
                "    \n" +
                "    return 0;\n" +
                "}\n";





            string cpp_declarations =
                "int x = 5; float y = 3.14f; bool flag = true; " +
                "for (int i = 0; i < 10; i = i + 1) { x = x + i; }";

            string cpp_err =
                "int x = 1 + ; bool f = && x; z = 10;"; // z не объявлена

            Run(cpp_const_test, "C++ простой");
            //Run(cpp_declarations, "C++ объявления и цикл for");
            //Run(cpp_err, "C++ с ошибками (в т.ч. таблица идентификаторов)");

            Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║ Для анализа файла используйте:                             ║");
            Console.WriteLine("║   > Program.exe <файл> [tofile]                            ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        }
    }
}
