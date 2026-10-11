using System.Diagnostics;
using System.Text;

namespace SimpleGit11.GitEditor;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] is not ("sequence" or "message"))
            {
                return 1;
            }

            string prefix = Environment.GetEnvironmentVariable("SIMPLEGIT_EDITOR_SESSION")
                ?? throw new InvalidOperationException("The editor session is missing.");
            int parentId = int.Parse(Environment.GetEnvironmentVariable("SIMPLEGIT_EDITOR_HOST")!);
            using Process host = Process.GetProcessById(parentId);
            string id = Guid.NewGuid().ToString("N");
            string request = prefix + ".request";
            string response = prefix + "." + id + ".response";
            string content = await File.ReadAllTextAsync(args[1], Encoding.UTF8);
            string temporary = request + ".tmp";
            await File.WriteAllTextAsync(temporary, id + "\n" + args[0] + "\n" + content, new UTF8Encoding(false));
            File.Move(temporary, request, overwrite: true);

            while (!File.Exists(response))
            {
                if (host.HasExited)
                {
                    return 1;
                }

                await Task.Delay(100);
            }

            string result = await File.ReadAllTextAsync(response, Encoding.UTF8);
            if (!result.StartsWith("1\n", StringComparison.Ordinal))
            {
                return 1;
            }

            await File.WriteAllTextAsync(args[1], result[2..], new UTF8Encoding(false));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
