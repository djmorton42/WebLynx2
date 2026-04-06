using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WebLynx2;

public enum TcpChannelKind
{
    Clock,
    Results
}

public enum TcpChannelUiStatus
{
    NotListening,
    Listening,
    Connected
}

/// <summary>
/// Listens for FinishLynx clock and results TCP clients, logs UTF-16 payloads, and reports UI status.
/// </summary>
public sealed class FinishLynxTcpServer
{
    private readonly ReceivedDataFileLogger _logger;
    private readonly Action<TcpChannelKind, TcpChannelUiStatus> _onStatusChanged;
    private readonly int _bufferSize;

    private readonly object _gate = new();
    private readonly List<TcpClient> _clients = new();

    private CancellationTokenSource? _cts;
    private TcpListener? _clockListener;
    private TcpListener? _resultsListener;
    private bool _running;
    private int _clockConnections;
    private int _resultsConnections;

    public FinishLynxTcpServer(
        ReceivedDataFileLogger logger,
        Action<TcpChannelKind, TcpChannelUiStatus> onStatusChanged,
        int bufferSize = 65536)
    {
        _logger = logger;
        _onStatusChanged = onStatusChanged;
        _bufferSize = bufferSize;
    }

    public void Start(int clockPort, int resultsPort)
    {
        lock (_gate)
        {
            if (_running)
                throw new InvalidOperationException("The TCP server is already running.");

            var cts = new CancellationTokenSource();
            TcpListener? clockListener = null;
            TcpListener? resultsListener = null;

            try
            {
                // IPAddress.Any == 0.0.0.0 (all IPv4 interfaces)
                clockListener = new TcpListener(IPAddress.Any, clockPort);
                clockListener.Start();

                resultsListener = new TcpListener(IPAddress.Any, resultsPort);
                resultsListener.Start();

                _cts = cts;
                _clockListener = clockListener;
                _resultsListener = resultsListener;
                _running = true;
                _clockConnections = 0;
                _resultsConnections = 0;
            }
            catch
            {
                try
                {
                    clockListener?.Stop();
                }
                catch
                {
                    /* ignore */
                }

                try
                {
                    resultsListener?.Stop();
                }
                catch
                {
                    /* ignore */
                }

                cts.Dispose();
                throw;
            }
        }

        Raise(TcpChannelKind.Clock, TcpChannelUiStatus.Listening);
        Raise(TcpChannelKind.Results, TcpChannelUiStatus.Listening);

        var token = _cts!.Token;
        _ = AcceptLoopAsync(_clockListener!, TcpChannelKind.Clock, token);
        _ = AcceptLoopAsync(_resultsListener!, TcpChannelKind.Results, token);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        TcpListener? clock;
        TcpListener? results;
        List<TcpClient> toClose;

        lock (_gate)
        {
            if (!_running)
                return;

            _running = false;
            cts = _cts;
            clock = _clockListener;
            results = _resultsListener;
            _cts = null;
            _clockListener = null;
            _resultsListener = null;

            toClose = new List<TcpClient>(_clients);
            _clients.Clear();
            _clockConnections = 0;
            _resultsConnections = 0;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            /* ignore */
        }

        await Task.Delay(50).ConfigureAwait(false);

        foreach (var client in toClose)
        {
            try
            {
                if (client.Connected && client.Client is { } socket)
                {
                    socket.Shutdown(SocketShutdown.Both);
                    client.Close();
                }
                else
                {
                    client.Dispose();
                }
            }
            catch
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        try
        {
            clock?.Stop();
        }
        catch
        {
            /* ignore */
        }

        try
        {
            results?.Stop();
        }
        catch
        {
            /* ignore */
        }

        cts?.Dispose();

        Raise(TcpChannelKind.Clock, TcpChannelUiStatus.NotListening);
        Raise(TcpChannelKind.Results, TcpChannelUiStatus.NotListening);
    }

    private async Task AcceptLoopAsync(TcpListener listener, TcpChannelKind kind, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                RegisterClient(client, kind);
                _ = Task.Run(() => HandleClientAsync(client, kind, cancellationToken), cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void RegisterClient(TcpClient client, TcpChannelKind kind)
    {
        lock (_gate)
        {
            _clients.Add(client);
            if (kind == TcpChannelKind.Clock)
                _clockConnections++;
            else
                _resultsConnections++;
        }

        Raise(kind, TcpChannelUiStatus.Connected);
    }

    private void UnregisterClient(TcpClient client, TcpChannelKind kind)
    {
        lock (_gate)
        {
            if (!_clients.Remove(client))
                return;

            if (kind == TcpChannelKind.Clock)
                _clockConnections = Math.Max(0, _clockConnections - 1);
            else
                _resultsConnections = Math.Max(0, _resultsConnections - 1);

            if (!_running)
                return;

            if (kind == TcpChannelKind.Clock)
                Raise(
                    TcpChannelKind.Clock,
                    _clockConnections > 0 ? TcpChannelUiStatus.Connected : TcpChannelUiStatus.Listening);
            else
                Raise(
                    TcpChannelKind.Results,
                    _resultsConnections > 0 ? TcpChannelUiStatus.Connected : TcpChannelUiStatus.Listening);
        }
    }

    private async Task HandleClientAsync(TcpClient client, TcpChannelKind kind, CancellationToken cancellationToken)
    {
        var buffer = new byte[_bufferSize];
        try
        {
            using var stream = client.GetStream();
            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                var n = await stream
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (n == 0)
                    break;

                var chunk = new byte[n];
                buffer.AsSpan(0, n).CopyTo(chunk);

                if (kind == TcpChannelKind.Clock)
                    _logger.LogClock(chunk);
                else
                    _logger.LogResults(chunk);
            }
        }
        catch (OperationCanceledException)
        {
            /* expected on stop */
        }
        catch
        {
            /* connection errors */
        }
        finally
        {
            UnregisterClient(client, kind);
            try
            {
                client.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private void Raise(TcpChannelKind kind, TcpChannelUiStatus status) => _onStatusChanged(kind, status);
}
