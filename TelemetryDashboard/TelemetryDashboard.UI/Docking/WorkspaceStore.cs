using System;
using System.IO;

namespace TelemetryDashboard.UI.Docking;

/// <summary>
/// Where the operator's panel arrangement lives between sessions.
/// </summary>
/// <remarks>
/// Every part of this existed and none of it was connected. <c>LayoutManager</c> could serialise
/// the dock and read it back, <c>WorkspaceManager</c> could write that to a file and load it, and
/// <c>WorkspaceProfile</c> had a field named <c>LayoutXml</c> to carry it — and no code in the
/// application called any of them. An operator who arranged the window found it back the way it
/// shipped at every launch, and the tests that covered this were passing against a
/// <c>WorkspaceLayoutState</c> declared inside the test file.
/// <para>
/// One file per installation, beside the other preferences. The arrangement is a property of the
/// person sitting at the machine rather than of the machine, so it does not belong next to the
/// executable — which on a plant PC is often a directory the operator cannot write to.
/// </para>
/// </remarks>
public static class WorkspaceStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TelemetryDashboard", "workspace.xml");

    /// <summary>Reads the stored arrangement, or null when there is not one to read.</summary>
    /// <remarks>
    /// Null rather than a default profile, so the caller can tell "nothing saved yet" from "saved
    /// and empty". The first means leave the arrangement the XAML declares alone.
    /// </remarks>
    public static WorkspaceProfile? Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return null;

        try
        {
            WorkspaceProfile profile = new WorkspaceManager().LoadWorkspaceProfile(path);
            return string.IsNullOrWhiteSpace(profile.LayoutXml) ? null : profile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Writes the arrangement. Returns why it could not be written, or null.</summary>
    public static string? Save(WorkspaceProfile profile, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        path ??= DefaultPath;

        try
        {
            new WorkspaceManager().SaveWorkspaceProfile(profile, path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>Forgets the stored arrangement, so the next start uses the one XAML declares.</summary>
    public static bool Clear(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
