using System;
using System.IO;
using TelemetryDashboard.Core.Security;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Enrols the credential <c>--credential</c> demands, on a machine with no desktop shell.
/// </summary>
/// <remarks>
/// Without this the flag is unusable where it matters most. The only way to produce a credential
/// file was the WPF screen lock's enrolment, which runs on Windows only — so a Linux or macOS
/// operator could be told to pass <c>--credential</c> and had no way to create the file. A feature
/// reachable only from the platform the feature exists to get away from is not a feature.
/// <para>
/// The password is read from standard input rather than taken as an argument. An argument is in the
/// shell history, in the process list while it runs, and in any log that records the command —
/// three places a password should not be, for the convenience of not typing a pipe.
/// </para>
/// <para>
/// Nothing about the password is stored. The file holds the salted PBKDF2 derivation
/// <see cref="PasswordCredential"/> produces, which is why the file may be copied to the host that
/// needs it without the password travelling with it.
/// </para>
/// </remarks>
internal static class CredentialCommand
{
    public const string Verb = "credential";

    public static bool Matches(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        string? output = null;
        bool force = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                    if (i + 1 >= args.Length) return Fail("--out needs a file to write.");
                    output = args[++i];
                    break;

                case "--force":
                    force = true;
                    break;

                case "--help" or "-h" or "-?" or "/?":
                    Console.Out.Write(Usage);
                    return 0;

                default:
                    return Fail($"unknown argument '{args[i]}'. Run '{Verb} --help'.");
            }
        }

        if (output is null) return Fail("no output file. Use --out <file>.");

        if (File.Exists(output) && !force)
        {
            // Overwriting a credential silently would lock every host already using it out of
            // itself, with nothing to say what changed.
            return Fail($"'{output}' already exists. Replace it with --force if that is the intent.");
        }

        Console.Error.Write("Password (input is read from stdin, not echoed by this process): ");
        string? password = Console.ReadLine();
        Console.Error.WriteLine();

        if (string.IsNullOrEmpty(password)) return Fail("no password was read from standard input.");

        PasswordCredential credential;
        try
        {
            credential = PasswordCredential.Create(password);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        if (CredentialFile.Save(output, credential) is { } problem) return Fail(problem);

        Console.WriteLine($"Wrote {output}.");
        Console.WriteLine("It holds a salted PBKDF2 derivation, not the password, so it may be copied");
        Console.WriteLine("to the host that needs it. Start that host with --credential " + output);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"telemetry-host {Verb}: {message}");
        return Program.ExitUsage;
    }

    private const string Usage =
        """
        telemetry-host credential — write the credential file the console will demand.

        Usage:
          echo 'my-password' | telemetry-host credential --out console.cred
          telemetry-host --credential console.cred --serial COM3

        The password is read from standard input, never from an argument: an argument is in the
        shell history, in the process list while it runs, and in any log that records the command.

          --out <file>   Where to write the credential. Required.
          --force        Replace an existing file. Every host using the old one stops accepting
                         the old password, so this is deliberate rather than default.

        """;
}
