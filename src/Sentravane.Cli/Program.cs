using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Sentravane.Shared.IPC;

namespace Sentravane.Cli;

public static class Program
{
    private const string PipeName = "sentravane-sensor";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var commandStr = args[0].ToLowerInvariant();
        IpcCommand? command = commandStr switch
        {
            "status" => IpcCommand.GetStatus,
            "canaries" => IpcCommand.ListCanaries,
            "isolate" => IpcCommand.QuarantineEndpoint,
            "release" or "unquarantine" => IpcCommand.UnquarantineEndpoint,
            "diag" or "diagnostics" => IpcCommand.TriggerDiagnostics,
            _ => null
        };

        if (command == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[!] Unknown command: '{args[0]}'");
            Console.ResetColor();
            PrintUsage();
            return 1;
        }

        return await SendIpcCommandAsync(command.Value, args.Length > 1 ? args[1] : null);
    }

    private static async Task<int> SendIpcCommandAsync(IpcCommand command, string? args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            Console.WriteLine($"[*] Connecting to Sentravane Sensor pipe ({PipeName})...");
            await client.ConnectAsync(3000);

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var request = new IpcRequest
            {
                Command = command,
                Arguments = args
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            var responseLine = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(responseLine))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[!] Received empty response from sensor.");
                Console.ResetColor();
                return 1;
            }

            var response = JsonSerializer.Deserialize<IpcResponse>(responseLine);
            if (response == null)
            {
                Console.WriteLine(responseLine);
                return 0;
            }

            if (response.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] {response.Message}");
                Console.ResetColor();

                if (!string.IsNullOrWhiteSpace(response.PayloadJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(response.PayloadJson);
                        Console.WriteLine(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    catch
                    {
                        Console.WriteLine(response.PayloadJson);
                    }
                }
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] Error: {response.Message}");
                Console.ResetColor();
                return 1;
            }
        }
        catch (TimeoutException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[-] Connection timed out. Ensure 'SentravaneSensor' service is running with Administrator privileges.");
            Console.ResetColor();
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[-] Access Denied. The Sentravane IPC pipe requires Administrator privileges. Run PowerShell/Terminal as Administrator.");
            Console.ResetColor();
            return 1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[-] Failed to communicate with Sentravane Sensor: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
Sentravane Fleet Sensor CLI
Usage: sentravane <command> [arguments]

Commands:
  status                Display sensor state, armed canaries count, and endpoint registration
  canaries              List all active canary decoy file tripwires monitored by the sensor
  isolate               Immediately trigger dynamic WFP network quarantine
  release               Release dynamic WFP network quarantine
  diag                  Run local sensor diagnostics
");
    }
}
