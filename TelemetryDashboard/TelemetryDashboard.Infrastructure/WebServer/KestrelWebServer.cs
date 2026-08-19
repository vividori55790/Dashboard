using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>
/// Kestrel-hosted web endpoint for the telemetry console.
/// </summary>
/// <remarks>
/// Binds the preferred port when it is free and otherwise moves to the next available one, so a
/// second instance — or an unrelated process already holding 8080 — starts cleanly instead of
/// failing at launch. The chosen port is returned to the caller to display.
/// </remarks>
public sealed class KestrelWebServer : IAsyncDisposable
{
    private WebApplication? _application;

    public bool IsRunning { get; private set; }

    /// <summary>Port actually bound, or 0 when not running.</summary>
    public int BoundPort { get; private set; }

    /// <summary>How many ports past the preferred one to try before giving up.</summary>
    public int PortSearchRange { get; init; } = 50;

    /// <summary>Starts on <paramref name="preferredPort"/>, or the next free port, and returns it.</summary>
    public async Task<int> StartOnAvailablePortAsync(int preferredPort = 8080, CancellationToken cancellationToken = default)
    {
        if (IsRunning) return BoundPort;

        int port = FindAvailablePort(preferredPort, PortSearchRange);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); // the desktop shell owns operator-facing logging
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port));

        WebApplication app = builder.Build();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", port }));

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        _application = app;
        BoundPort = port;
        IsRunning = true;
        return port;
    }

    /// <summary>Stops the server. Safe to call when it was never started.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_application is null)
        {
            IsRunning = false;
            return;
        }

        try
        {
            await _application.StopAsync(cancellationToken).ConfigureAwait(false);
            await _application.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            // Already torn down.
        }
        finally
        {
            _application = null;
            IsRunning = false;
            BoundPort = 0;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    /// <summary>
    /// Returns the first free loopback port at or after <paramref name="preferred"/>.
    /// Falls back to an ephemeral port when the whole range is taken.
    /// </summary>
    internal static int FindAvailablePort(int preferred, int searchRange)
    {
        for (int candidate = Math.Max(1, preferred); candidate < Math.Max(1, preferred) + searchRange; candidate++)
        {
            if (candidate > 65535) break;
            if (IsPortFree(candidate)) return candidate;
        }

        // Let the OS assign anything it has.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int ephemeral = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return ephemeral;
    }

    private static bool IsPortFree(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
