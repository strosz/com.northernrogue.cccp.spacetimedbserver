using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace NorthernRogue.CCCP.Editor {

/// <summary>
/// Provides utility and converter methods for the SpacetimeDB Server Manager
/// </summary>
public static class ServerUtilityProvider
{
    #region URL and Network Utilities

    /// <summary>
    /// Extracts hostname from a URL by removing protocol, port, and path
    /// </summary>
    /// <param name="url">The URL to extract hostname from</param>
    /// <returns>The hostname portion of the URL</returns>
    public static string ExtractHostname(string url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;
            
        string hostname = url;
        
        // Remove protocol if present
        if (hostname.StartsWith("http://")) 
            hostname = hostname.Substring(7);
        else if (hostname.StartsWith("https://")) 
            hostname = hostname.Substring(8);
        
        // Remove path and port if present
        int colonIndex = hostname.IndexOf(':');
        if (colonIndex > 0) 
            hostname = hostname.Substring(0, colonIndex);
            
        int slashIndex = hostname.IndexOf('/');
        if (slashIndex > 0) 
            hostname = hostname.Substring(0, slashIndex);
            
        return hostname;
    }

    /// <summary>
    /// Extracts port number from a URL
    /// </summary>
    /// <param name="url">The URL to extract port from</param>
    /// <returns>Port number if found and valid, -1 otherwise</returns>
    public static int ExtractPortFromUrl(string url)
    {
        try
        {
            // Look for the port pattern ":number/" or ":number" at the end
            int colonIndex = url.LastIndexOf(':');
            if (colonIndex != -1 && colonIndex < url.Length - 1)
            {
                // Find the end of the port number (either / or end of string)
                int endIndex = url.IndexOf('/', colonIndex);
                if (endIndex == -1) endIndex = url.Length;
                
                // Extract the port substring
                string portStr = url.Substring(colonIndex + 1, endIndex - colonIndex - 1);
                
                // Try to parse the port
                if (int.TryParse(portStr, out int port) && port > 0 && port < 65536)
                {
                    return port;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error extracting port from URL: {ex.Message}");
        }
        
        return -1; // Invalid or no port found
    }

    #endregion

    #region Path Utilities

    /// <summary>
    /// Gets the relative path for client directory in SpacetimeDB format
    /// </summary>
    /// <param name="clientDirectory">The client directory path</param>
    /// <param name="serverMode">Optional server mode to handle path differently (e.g., "DockerServer")</param>
    /// <returns>Relative path formatted for SpacetimeDB</returns>
    public static string GetRelativeClientPath(string clientDirectory, string serverMode = null)
    {
        // Default path if nothing else works
        string defaultPath = "../Assets/Scripts/Server";
        
        if (string.IsNullOrEmpty(clientDirectory))
        {
            return defaultPath;
        }
        
        try
        {
            // Normalize path to forward slashes
            string normalizedPath = clientDirectory.Replace('\\', '/');
            
            // Docker mode: Use /unity mount point instead of ../Assets
            if (!string.IsNullOrEmpty(serverMode) && serverMode.Equals("DockerServer", StringComparison.OrdinalIgnoreCase))
            {
                // Find the "Assets" directory in the path
                int assetsIndex = normalizedPath.IndexOf("Assets/");
                if (assetsIndex < 0)
                {
                    assetsIndex = normalizedPath.IndexOf("Assets");
                }
                
                if (assetsIndex >= 0)
                {
                    // Extract from "Assets" to the end and use /unity/ mount point
                    string dockerPath = "/unity/" + normalizedPath.Substring(assetsIndex);
                    
                    // Add quotes if path contains spaces
                    if (dockerPath.Contains(" "))
                    {
                        return $"\"{dockerPath}\"";
                    }
                    return dockerPath;
                }
                
                // Fallback to /unity/Assets for Docker
                return "/unity/Assets";
            }
            
            // WSL/Custom mode: Use ../Assets relative path
            // If the path already starts with "../Assets", use it directly
            if (normalizedPath.StartsWith("../Assets"))
            {
                return normalizedPath;
            }
            
            // Find the "Assets" directory in the path
            int assetsIndexNormal = normalizedPath.IndexOf("Assets/");
            if (assetsIndexNormal < 0)
            {
                assetsIndexNormal = normalizedPath.IndexOf("Assets");
            }
            
            if (assetsIndexNormal >= 0)
            {
                // Extract from "Assets" to the end and prepend "../"
                string relativePath = "../" + normalizedPath.Substring(assetsIndexNormal);
                
                // Ensure it has proper structure
                if (!relativePath.Contains("/"))
                {
                    relativePath += "/";
                }
                
                // Add quotes if path contains spaces
                if (relativePath.Contains(" "))
                {
                    return $"\"{relativePath}\"";
                }
                return relativePath;
            }
            
            // If no "Assets" in path, just return default
            return defaultPath;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in path handling: {ex.Message}");
            return defaultPath;
        }
    }

    #endregion

    #region File System Utilities

    /// <summary>
    /// Recursively copies directory and all its contents
    /// </summary>
    /// <param name="sourceDir">Source directory path</param>
    /// <param name="destDir">Destination directory path</param>
    public static void CopyDirectory(string sourceDir, string destDir)
    {
        // Get all files from the source
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, true);
        }
        
        // Copy subdirectories recursively
        foreach (string directory in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(directory);
            string destSubDir = Path.Combine(destDir, dirName);
            Directory.CreateDirectory(destSubDir);
            CopyDirectory(directory, destSubDir);
        }
    }

    #endregion

    #region String Processing Utilities

    /// <summary>
    /// Formats SpacetimeDB login info output with color highlighting
    /// </summary>
    /// <param name="output">Raw login info output</param>
    /// <returns>Formatted output with color tags</returns>
    public static string FormatLoginInfoOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
            return output;
        
        // Color to use for login ID and auth token - Color(0.3f, 0.8f, 0.3f) = #4CCC4C
        string colorTag = "#4CCC4C";
        
        string formattedOutput = output;
        
        // Format login ID line: "You are logged in as <username>"
        string loginPattern = @"(You are logged in as\s+)([^\r\n]+)";
        formattedOutput = System.Text.RegularExpressions.Regex.Replace(
            formattedOutput, 
            loginPattern, 
            $"$1<color={colorTag}>$2</color>", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        // Format auth token line: "Your auth token (don't share this!) is <token>"
        string tokenPattern = @"(Your auth token \(don't share this!\) is\s+)([^\r\n]+)";
        formattedOutput = System.Text.RegularExpressions.Regex.Replace(
            formattedOutput, 
            tokenPattern, 
            $"$1<color={colorTag}>$2</color>", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        return formattedOutput;
    }

    /// <summary>
    /// Filters out non-functional error messages from SpacetimeDB generate command
    /// </summary>
    /// <param name="error">Raw error output</param>
    /// <returns>Filtered error output</returns>
    public static string FilterGenerateErrors(string error)
    {
        if (string.IsNullOrEmpty(error))
            return error;

        // Filter out the formatting error that doesn't affect functionality
        var lines = error.Split('\n');
        var filteredLines = new System.Collections.Generic.List<string>();
        
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            // Skip the specific formatting error that confuses users but doesn't affect functionality
            if (trimmedLine.Contains("Could not format generated files: No such file or directory (os error 2)"))
            {
                continue;
            }
            
            // Keep all other error lines
            if (!string.IsNullOrWhiteSpace(trimmedLine))
            {
                filteredLines.Add(line);
            }
        }
        
        return string.Join("\n", filteredLines).Trim();
    }

    #endregion

    #region UI Utilities

    /// <summary>
    /// Gets status icon for boolean values
    /// </summary>
    /// <param name="status">Status to represent</param>
    /// <returns>Checkmark for true, circle for false</returns>
    public static string GetStatusIcon(bool status)
    {
        return status ? "✓" : "○";
    }

    /// <summary>
    /// Checks if a specific EditorWindow type is currently open
    /// </summary>
    /// <typeparam name="T">EditorWindow type to check</typeparam>
    /// <returns>True if window is open</returns>
    public static bool IsWindowOpen<T>() where T : EditorWindow
    {
        return EditorWindow.HasOpenInstances<T>();
    }

    /// <summary>
    /// Closes all instances of a specific EditorWindow type
    /// </summary>
    /// <typeparam name="T">EditorWindow type to close</typeparam>
    public static void CloseWindow<T>() where T : EditorWindow
    {
        T[] windows = UnityEngine.Resources.FindObjectsOfTypeAll<T>();
        if (windows != null && windows.Length > 0)
        {
            foreach (T window in windows)
            {
                window.Close();
            }
        }
    }

    #endregion
}

} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
