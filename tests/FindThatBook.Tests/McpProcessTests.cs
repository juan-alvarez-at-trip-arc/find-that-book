using System.Diagnostics;
using System.Text.Json;

namespace FindThatBook.Tests;

public class McpProcessTests
{
    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";
    private const string Initialized = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
    private const string ListTools = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

    [Fact]
    public async Task Built_server_writes_only_protocol_messages_to_stdout()
    {
        var lines = await RunServerAsync(null);

        Assert.Equal(2, lines.Count);
        foreach (var line in lines)
        {
            using var message = JsonDocument.Parse(line);
            Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
        }
        Assert.Contains("search_books", lines[1]);
    }

    [Fact]
    public async Task Built_server_ignores_an_appsettings_file_in_the_working_directory()
    {
        var directory = Directory.CreateTempSubdirectory("ftb-mcp-cwd-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "appsettings.json"), """{"Ai":{"Provider":"NotAProvider"}}""");

            var lines = await RunServerAsync(directory.FullName);

            Assert.Equal(2, lines.Count);
            Assert.Contains("search_books", lines[1]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<List<string>> RunServerAsync(string? workingDirectory)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var dll = Path.GetFullPath(Path.Combine(output.FullName, "..", "..", "..", "..", "..", "src", "FindThatBook.Mcp", "bin", output.Parent!.Name, output.Name, "FindThatBook.Mcp.dll"));
        Assert.True(File.Exists(dll), $"{dll} was not built. Build src/FindThatBook.Mcp first.");
        var start = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        using var process = Process.Start(start)!;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();

        var lines = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.StandardInput.WriteLineAsync(Initialize.AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var initialize = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (initialize is null) return lines;
            lines.Add(initialize);

            await process.StandardInput.WriteLineAsync(Initialized.AsMemory(), timeout.Token);
            await process.StandardInput.WriteLineAsync(ListTools.AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var tools = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (tools is not null) lines.Add(tools);
        }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }

        return lines;
    }
}
