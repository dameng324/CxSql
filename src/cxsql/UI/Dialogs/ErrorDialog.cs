namespace CxSql.UI.Dialogs;

public static class ErrorDialog
{
    public static void Show(string title, string message, string? details = null)
    {
        Console.WriteLine();
        Console.WriteLine("+-- Error Details -------------------------------------------");
        Console.WriteLine($"| {title}");
        Console.WriteLine("+------------------------------------------------------------");
        Console.WriteLine($"| {message}");
        if (!string.IsNullOrWhiteSpace(details))
        {
            Console.WriteLine("+-- Details --------------------------------------------------");
            foreach (var line in details.Split(Environment.NewLine))
            {
                Console.WriteLine($"| {line}");
            }
        }

        Console.WriteLine("+-- Close Dialog (Esc)");
        WaitForEscape();
    }

    private static void WaitForEscape()
    {
        while (Console.ReadKey(intercept: true).Key != ConsoleKey.Escape)
        {
            Console.WriteLine("Use the visible Close Dialog action: Esc.");
        }
    }
}
