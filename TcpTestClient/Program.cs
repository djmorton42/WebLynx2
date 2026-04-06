using System.Net;
using System.Net.Sockets;
using System.Text;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: TcpTestClient <ip> <port>");
    Console.Error.WriteLine("Example: TcpTestClient 127.0.0.1 8080");
    return 1;
}

if (!IPAddress.TryParse(args[0], out var ip))
{
    Console.Error.WriteLine($"Invalid IP address: {args[0]}");
    return 1;
}

if (!int.TryParse(args[1], out var port) || port is < 1 or > 65535)
{
    Console.Error.WriteLine($"Invalid port: {args[1]}");
    return 1;
}

using var quit = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    quit.Cancel();
};

Console.WriteLine($"Connecting to {ip}:{port}… (press any key or Ctrl+C to exit)");
using var client = new TcpClient();

try
{
    await client.ConnectAsync(ip, port);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Connect failed: {ex.Message}");
    return 1;
}

Console.WriteLine("Connected. Sending UTF-16 test line every second.");

await using var stream = client.GetStream();
var token = quit.Token;

try
{
    while (!token.IsCancellationRequested)
    {
        if (TryConsumeAnyKey())
            break;

        var line = $"WebLynx TcpTestClient {DateTimeOffset.UtcNow:O}\r\n";
        var payload = Encoding.Unicode.GetBytes(line);
        await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);

        if (!await WaitUpToOneSecondAllowingKeyAsync(token))
            break;
    }
}
catch (OperationCanceledException)
{
    /* Ctrl+C */
}

try
{
    client.Client?.Shutdown(SocketShutdown.Both);
}
catch
{
    /* ignore */
}

Console.WriteLine("Disconnected. Goodbye.");
return 0;

static bool TryConsumeAnyKey()
{
    if (!Console.KeyAvailable)
        return false;

    _ = Console.ReadKey(intercept: true);
    return true;
}

static async Task<bool> WaitUpToOneSecondAllowingKeyAsync(CancellationToken token)
{
    const int slices = 20;
    for (var i = 0; i < slices && !token.IsCancellationRequested; i++)
    {
        if (TryConsumeAnyKey())
            return false;

        await Task.Delay(50, token);
    }

    return !token.IsCancellationRequested;
}
