using FlowchartGen;
using Lexer;
using Parser;
using System;
using System.IO;

namespace FlowchartExample;

class Program
{
    static void Main(string[] args)
    {
        // Пример 1: Простая функция проверки строки (как на изображении)
        string sourceCode1 = @"
bool Proverka(const string& str) {
    if (str.empty()) {
        return true;
    }

    for (char c : str) {
        if (c < '0' || c > '9') {
            return false;
        }
    }

    return true;
}
";

        GenerateFlowchart(sourceCode1, "Proverka_Function", "proverka.dot");

        // Пример 2: Цикл while с условием
        string sourceCode2 = @"
int main() {
    int x = 10;
    while (x > 0) {
        x = x - 1;
    }
    return x;
}
";

        GenerateFlowchart(sourceCode2, "While_Loop", "while_loop.dot");

        // Пример 3: Условный оператор if-else
        string sourceCode3 = @"
int main() {
    int a = 5;
    int b = 10;
    int max;

    if (a > b) {
        max = a;
    } else {
        max = b;
    }

    return max;
}
";

        GenerateFlowchart(sourceCode3, "IfElse_Example", "if_else.dot");

        // Пример 4: Цикл for
        string sourceCode4 = @"
int main() {
    int sum = 0;
    for (int i = 1; i <= 10; i++) {
        sum = sum + i;
    }
    return sum;
}
";

        GenerateFlowchart(sourceCode4, "For_Loop", "for_loop.dot");

        Console.WriteLine("\n✅ Все блок-схемы успешно сгенерированы!");
        Console.WriteLine("\nДля создания изображений выполните:");
        Console.WriteLine("  dot -Tpng proverka.dot -o proverka.png");
        Console.WriteLine("  dot -Tpng while_loop.dot -o while_loop.png");
        Console.WriteLine("  dot -Tpng if_else.dot -o if_else.png");
        Console.WriteLine("  dot -Tpng for_loop.dot -o for_loop.png");
    }

    static void GenerateFlowchart(string sourceCode, string graphName, string outputFile)
    {
        try
        {
            // 1. Лексический анализ
            var grammar = LanguageGrammarFactory.CreateCppGrammar(); // C++-грамматика
            var lexer = new LexAnalyzer(sourceCode, grammar);
            var tokens = lexer.Scan();


            // 2. Синтаксический анализ
            var parser = new SyntaxAnalyzer(tokens);
            var ast = parser.ParseProgram();

            if (ast == null)
            {
                Console.WriteLine($"❌ Ошибка парсинга для {graphName}");
                return;
            }

            // 3. Генерация блок-схемы
            var generator = new ASTToMermaid();
            string dotCode = generator.Generate(ast, graphName);

            // 4. Сохранение в файл
            File.WriteAllText(outputFile, dotCode);

            Console.WriteLine($"✓ {outputFile} создан ({dotCode.Length} символов)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка при генерации {graphName}: {ex.Message}");
        }
    }
}
