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
#include <iostream>
using namespace std;

int main() {
    int x = 0;
    int n = 10;
    
    while (x < n) {
        if (x % 2 == 0) {
            cout << x << endl;
        }
        x++;
    }
    
    return 0;
}
";

        GenerateFlowchart(sourceCode2, "While_Loop", "while_loop.dot");

        // Пример 3: Условный оператор if-else
        string sourceCode3 = @"
// break и continue
#include <iostream>
using namespace std;

int main() {
    int i;
    i = 0;
    while (i < 10) {
        if (i == 3) {
            i = i + 1;
            continue;
        }
        if (i == 7) {
            break;
        }
        cout << i << endl;
        i = i + 1;
    }
    return 0;
}
";

        GenerateFlowchart(sourceCode3, "test_break_continue", "test_break_continue.dot");

        // Пример 4: Цикл for
        string sourceCode4 = @"
// Функция и вызов
#include <iostream>
using namespace std;

int square(int x) {
    return x * x;
}

int main() {
    int n;
    n = 5;
    int result;
    result = square(n);
    cout << result << endl;
    return 0;
}
";

        GenerateFlowchart(sourceCode4, "func", "func.dot");

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
