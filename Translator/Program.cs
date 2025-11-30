using Lexer;
using Parser;
using System;
using System.IO;

class Program
{
    /// <summary>
    /// Запускает анализ с выводом результатов в файл вместо консоли.
    /// </summary>
    /// <param name="source">Исходный код для анализа.</param>
    /// <param name="title">Название анализа (используется как имя файла).</param>
    /// <param name="outputPath">Путь к выходному файлу (если null, генерируется автоматически).</param>
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

        // 1. Лексический анализ (преобразование кода в токены)
        var lex = new LexAnalyzer(source, LanguageGrammarFactory.CreateCppGrammar());
        var tokens = lex.Scan();

        // 2. Синтаксический анализ (построение дерева разбора)
        var parser = new SyntaxAnalyzer(tokens);
        var ast = parser.ParseProgram();

        // 3. Вывод дерева разбора в файл
        if (ast != null)
            AstPrinter.PrintDeepTreeToFile(ast);

        // 4. Диагностика ошибок (лексические и синтаксические)
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

    /// <summary>
    /// Запускает анализ с выводом результатов в консоль.
    /// </summary>
    /// <param name="source">Исходный код для анализа.</param>
    /// <param name="title">Название анализа.</param>
    public static void Run(string source, string title)
    {
        Console.WriteLine($"=== {title} [C++] ===");

        // 1. Лексический анализ (преобразование кода в токены)
        var lex = new LexAnalyzer(source, LanguageGrammarFactory.CreateCppGrammar());
        var tokens = lex.Scan();

        // 2. Синтаксический анализ (построение дерева разбора)
        var parser = new SyntaxAnalyzer(tokens);
        var ast = parser.ParseProgram();

        // 3. Вывод дерева разбора в консоль
        if (ast != null)
            AstPrinter.PrintDeepTree(ast);

        // 4. Вывод таблицы глобальных идентификаторов
        parser._scopes.PrintScopeTree();

        // 5. Диагностика ошибок (лексические и синтаксические)
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

    /// <summary>
    /// Точка входа программы.
    /// Обрабатывает командную строку для анализа файла или запускает встроенные тесты.
    /// </summary>
    /// <param name="args">Аргументы командной строки: [путь_к_файлу] [tofile]</param>
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
            Console.WriteLine("║ СИНТАКСИЧЕСКИЙ АНАЛИЗАТОР (C++) ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

            // Тестовый пример: анализ константных переменных и области видимости
            string cpp_const_test =
                "#include <iostream>\n" +
                "using namespace std;\n" +
                "\n" +
                "int main() {\n" +
                " const int c1 = 5;\n" +
                " \n" +
                " cout << \"Константа c1: \" << c1 << endl;\n" +
                " \n" +
                " c1 = 10;\n" +
                " \n" +
                " { \n" +
                " const int c2 = 15;\n" +
                " cout << \"Локальная const c2: \" << c2 << endl;\n" +
                " \n" +
                " c2 = 20;\n" +
                " \n" +
                " cout << \"Внешняя const: \" << c1 << endl;\n" +
                " }\n" +
                " \n" +
                " cout << \"После блока: c1 = \" << c1 << endl;\n" +
                " c2 = 25;\n" +
                " \n" +
                " for (const int i = 0; i < 3; i++) {\n" +
                " cout << \"Const i в for: \" << i << endl;\n" +
                " }\n" +
                " \n" +
                " if (true) {\n" +
                " const int if_c = 30;\n" +
                " cout << \"Const в if: \" << if_c << endl;\n" +
                " if_c = 35;\n" +
                " } else {\n" +
                " const int else_c = 40;\n" +
                " cout << \"Const в else: \" << else_c << endl;\n" +
                " else_c = 45;\n" +
                " }\n" +
                " \n" +
                " int var = 50;\n" +
                " { const int var = 60; }\n" +
                " var = 55;\n" +
                " \n" +
                " int sum = 10.3;\n" +
                " \n" +
                " return 0;\n" +
                "}\n";

            string cpp_declarations =
                "int x = 5; float y = 3.14f; bool flag = true; " +
                "for (int i = 0; i < 10; i = i + 1) { x = x + i; }";

            string cpp_err =
                "int x = 1 + ; bool f = && x; z = 10;";

            // Запуск основного теста
            Run(cpp_const_test, "C++ простой");

            Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║ Для анализа файла используйте: ║");
            Console.WriteLine("║ > Program.exe <файл> [tofile] ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        }
    }
}
