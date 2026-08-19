using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TelemetryDashboard.UI.Services;

public class DropResult
{
    public bool Success { get; set; }
    public string ActionType { get; set; } = string.Empty; // "LoadWorkspace", "Load3DModel", "ImportDataSession", "Rejected"
    public string ErrorMessage { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public class DragDropHandler
{
    private static readonly HashSet<string> WorkspaceExts = new(StringComparer.OrdinalIgnoreCase) { ".workspace" };
    private static readonly HashSet<string> ModelExts = new(StringComparer.OrdinalIgnoreCase) { ".obj", ".stl" };
    private static readonly HashSet<string> SessionExts = new(StringComparer.OrdinalIgnoreCase) { ".mat", ".csv" };

    public bool CanAcceptFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string ext = Path.GetExtension(filePath);
        return WorkspaceExts.Contains(ext) || ModelExts.Contains(ext) || SessionExts.Contains(ext);
    }

    public string? SelectPrimaryFile(string[] files)
    {
        if (files == null || files.Length == 0) return null;
        var validFiles = files.Where(CanAcceptFile).ToList();
        if (!validFiles.Any()) return null;

        return validFiles.FirstOrDefault(f => WorkspaceExts.Contains(Path.GetExtension(f)))
            ?? validFiles.FirstOrDefault(f => ModelExts.Contains(Path.GetExtension(f)))
            ?? validFiles.FirstOrDefault(f => SessionExts.Contains(Path.GetExtension(f)))
            ?? validFiles.First();
    }

    public DropResult ProcessDroppedFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new DropResult
            {
                Success = false,
                ActionType = "Rejected",
                ErrorMessage = "File does not exist or access denied",
                FilePath = filePath ?? string.Empty
            };
        }

        try
        {
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    return new DropResult
                    {
                        Success = false,
                        ActionType = "Rejected",
                        ErrorMessage = "Empty file dropped",
                        FilePath = filePath
                    };
                }
            }
            else
            {
                return new DropResult
                {
                    Success = false,
                    ActionType = "Rejected",
                    ErrorMessage = "File does not exist or access denied",
                    FilePath = filePath
                };
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (!CanAcceptFile(filePath))
            {
                return new DropResult
                {
                    Success = false,
                    ActionType = "Rejected",
                    ErrorMessage = "Unsupported file extension",
                    FilePath = filePath
                };
            }

            if ((ext == ".obj" || ext == ".stl") && !Validate3DModel(filePath, ext))
            {
                return new DropResult
                {
                    Success = false,
                    ActionType = "Rejected",
                    ErrorMessage = "Corrupted 3D model file",
                    FilePath = filePath
                };
            }

            string actionType = ext switch
            {
                ".workspace" => "LoadWorkspace",
                ".obj" or ".stl" => "Load3DModel",
                ".mat" or ".csv" => "ImportDataSession",
                _ => "Rejected"
            };

            return new DropResult
            {
                Success = actionType != "Rejected",
                ActionType = actionType,
                FilePath = filePath
            };
        }
        catch (Exception ex)
        {
            return new DropResult
            {
                Success = false,
                ActionType = "Rejected",
                ErrorMessage = ex.Message,
                FilePath = filePath
            };
        }
    }

    private static bool Validate3DModel(string filePath, string ext)
    {
        try
        {
            if (ext == ".stl")
            {
                var bytes = File.ReadAllBytes(filePath);
                if (bytes.Length < 84)
                {
                    string text = Encoding.ASCII.GetString(bytes).Trim();
                    if (!text.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
            else if (ext == ".obj")
            {
                string text = File.ReadAllText(filePath);
                if (!text.Contains("v ") && !text.Contains("#") && !text.Contains("f "))
                {
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
