using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace TelemetryDashboard.Tests.Desktop.TestUtilities;

/// <summary>
/// Attribute to mark test methods that require Single-Threaded Apartment (STA) thread execution
/// for WPF UI components, ViewModels, or Visual Studio elements.
/// </summary>
/// <remarks>
/// This file moved out of the shared TestUtilities folder when the suite was split. Every helper
/// below calls <c>Thread.SetApartmentState</c>, which is annotated <c>[SupportedOSPlatform</c>
/// <c>("windows")]</c> and throws <c>PlatformNotSupportedException</c> everywhere else — the
/// platform-compatibility analyzer flagged all three call sites the moment the portable project
/// stopped targeting <c>net8.0-windows</c>. A helper that cannot run on Linux has no business
/// compiling into the suite whose job is to prove the backbone runs on Linux, so it lives here with
/// the rest of the desktop-only code instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class WpfTestFactAttribute : FactAttribute
{
    public WpfTestFactAttribute()
    {
    }
}

/// <summary>
/// Utility helper to execute synchronous or asynchronous test actions on an STA thread.
/// </summary>
public static class WpfTestHelper
{
    /// <summary>
    /// Executes the specified action on a dedicated STA thread.
    /// Re-throws any uncaught exception captured during execution.
    /// </summary>
    public static void RunOnStaThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Exception? capturedException = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(capturedException).Throw();
        }
    }

    /// <summary>
    /// Executes the specified asynchronous function on a dedicated STA thread.
    /// Re-throws any uncaught exception captured during execution.
    /// </summary>
    public static void RunOnStaThreadAsync(Func<Task> asyncFunc)
    {
        ArgumentNullException.ThrowIfNull(asyncFunc);

        Exception? capturedException = null;
        var thread = new Thread(() =>
        {
            try
            {
                asyncFunc().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(capturedException).Throw();
        }
    }

    /// <summary>
    /// Executes the specified asynchronous function returning a result on a dedicated STA thread.
    /// </summary>
    public static T RunOnStaThreadAsync<T>(Func<Task<T>> asyncFunc)
    {
        ArgumentNullException.ThrowIfNull(asyncFunc);

        T result = default!;
        Exception? capturedException = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = asyncFunc().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(capturedException).Throw();
        }

        return result;
    }
}
