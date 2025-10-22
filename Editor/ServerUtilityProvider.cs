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
    #region Color Management

    /// <summary>
    /// Centralized static color manager for consistent UI theming across all server windows.
    /// All colors are stored as HTML hex strings and cached as Color objects for efficiency.
    /// Colors are initialized lazily on first access, ensuring minimal startup overhead.
    /// </summary>
    public static class ColorManager
    {
        private const string WINDOW_TOGGLE = "#99FF99";              // Light green for active toggles
        private const string SUBTITLE_TEXT = "#6E6E6E";              // Neutral gray for subtitles
        private const string AUTOSCROLL_ENABLED = "#808080";         // Medium gray when enabled
        private const string AUTOSCROLL_DISABLED = "#575757";        // Dark gray when disabled
        private const string CLEAR_BUTTON = "#808080";               // Medium gray for clear button
        private const string BUTTON_TEXT = "#FFFFFF";                // White for button text
        private const string BUTTON_DISABLED = "#C0C0C0";            // Light gray when disabled
        private const string CONNECTED_TEXT = "#4CFF4C";             // Bright green for connected
        private const string PROCESSING = "#00FF00";                 // Bright green for processing
        private const string RECOMMENDED = "#00FF00";                // Bright green for recommended
        private const string WARNING = "#FFA500";                    // Orange for warnings
        private const string HIDDEN = "#808080";                     // Gray for hidden elements
        private const string DEBUG = "#30C099";                      // Cyan for debug mode
        private const string LINE_DARK = "#262626";                  // Very dark for separators (pro skin)
        private const string LINE_LIGHT = "#999999";                 // Light gray for separators (light skin)
        private const string ACTIVE_TOOLBAR = "#00FF00";             // Bright green for active toolbar
        private const string INACTIVE_TOOLBAR = "#808080";           // Gray for inactive toolbar
        private const string VERSION_TEXT = "#6E6E6E";               // Neutral gray for version
        private const string HOVER_GREEN = "#00CC00";                // Darker green for hover states

        // Log Window Colors
        private const string CMD_BACKGROUND = "#1A1A1A";              // Very dark background for log console
        private const string CMD_TEXT = "#CCCCCC";                    // Light gray text for log console

        // Data Window Colors
        private const string TABLE_SELECTED = "#4DCC4D";            // Darker green for selected table
        private const string CLR_BUTTON = "#999999";               // Bright gray for clear button

        // Reducer Window Colors
        private const string RUN_BUTTON_NORMAL = "#33B333";         // Bright green for normal run button
        private const string RUN_BUTTON_HOVER = "#4DCC4D";          // Darker green for hover state
        private const string SEPARATOR = "#8080800D";                // Dark gray for separator lines

        // Status Message Colors
        private const string STATUS_NEUTRAL = "#A0A0A0";              // Neutral gray for status messages
        private const string STATUS_SUCCESS = "#4CFF4C";              // Bright green for success status
        private const string STATUS_ERROR = "#FF6B6B";                // Bright red for error status
        private const string STATUS_WARNING = "#FFA500";              // Orange for warning status
        private const string STATUS_INFO = "#87CEEB";                 // Sky blue for info status
        private const string STATUS_TIME = "#999999";                // Grey for timestamp text

        // Setup Window Colors
        private const string INSTALLED_TEXT = "#00BF00";             // Bright green for installed state
        private const string INSTALL_BUTTON_NORMAL = "#33B333";      // Green for install button normal state
        private const string INSTALL_BUTTON_HOVER = "#4DCC4D";       // Lighter green for install button hover
        private const string SECTION_HEADER = "#808080";             // Medium gray for section headers

        // Public static properties for accessing colors
        public static Color WindowToggle { get; private set; }
        public static Color SubtitleText { get; private set; }
        public static Color AutoscrollEnabled { get; private set; }
        public static Color AutoscrollDisabled { get; private set; }
        public static Color ClearButton { get; private set; }
        public static Color ButtonText { get; private set; }
        public static Color ButtonDisabled { get; private set; }
        public static Color ConnectedText { get; private set; }
        public static Color Processing { get; private set; }
        public static Color Recommended { get; private set; }
        public static Color Warning { get; private set; }
        public static Color Hidden { get; private set; }
        public static Color Debug { get; private set; }
        public static Color LineDark { get; private set; }
        public static Color LineLight { get; private set; }
        public static Color ActiveToolbar { get; private set; }
        public static Color InactiveToolbar { get; private set; }
        public static Color VersionText { get; private set; }
        public static Color HoverGreen { get; private set; }

        // Log Colors
        public static Color CmdBackground { get; private set; }
        public static Color CmdText { get; private set; }

        // Data Window Colors
        public static Color TableSelected { get; private set; }
        public static Color ClearDataButton { get; private set; }

        // Reducer Window Colors
        public static Color RunButtonNormal { get; private set; }
        public static Color RunButtonHover { get; private set; }
        public static Color Separator { get; private set; }

        // Status Message Colors
        public static Color StatusNeutral { get; private set; }
        public static Color StatusSuccess { get; private set; }
        public static Color StatusError { get; private set; }
        public static Color StatusWarning { get; private set; }
        public static Color StatusInfo { get; private set; }
        public static Color StatusTime { get; private set; }

        // Setup Window Colors
        public static Color InstalledText { get; private set; }
        public static Color InstallButtonNormal { get; private set; }
        public static Color InstallButtonHover { get; private set; }
        public static Color SectionHeader { get; private set; }

        // Cached color tracking
        private static bool _initialized = false;

        /// <summary>
        /// Initializes all colors from hex strings. Called automatically on first access via lazy initialization.
        /// This ensures colors are only parsed once and cached for the entire application lifetime.
        /// </summary>
        private static void Initialize()
        {
            if (_initialized)
                return;

            // Parse all hex colors to Color objects
            ColorUtility.TryParseHtmlString(WINDOW_TOGGLE, out var windowToggle);
            ColorUtility.TryParseHtmlString(SUBTITLE_TEXT, out var subtitleText);
            ColorUtility.TryParseHtmlString(AUTOSCROLL_ENABLED, out var autoscrollEnabled);
            ColorUtility.TryParseHtmlString(AUTOSCROLL_DISABLED, out var autoscrollDisabled);
            ColorUtility.TryParseHtmlString(CLEAR_BUTTON, out var clearButton);
            ColorUtility.TryParseHtmlString(BUTTON_TEXT, out var buttonText);
            ColorUtility.TryParseHtmlString(BUTTON_DISABLED, out var buttonDisabled);
            ColorUtility.TryParseHtmlString(CONNECTED_TEXT, out var connectedText);
            ColorUtility.TryParseHtmlString(PROCESSING, out var processing);
            ColorUtility.TryParseHtmlString(RECOMMENDED, out var recommended);
            ColorUtility.TryParseHtmlString(WARNING, out var warning);
            ColorUtility.TryParseHtmlString(HIDDEN, out var hidden);
            ColorUtility.TryParseHtmlString(DEBUG, out var debug);
            ColorUtility.TryParseHtmlString(LINE_DARK, out var lineDark);
            ColorUtility.TryParseHtmlString(LINE_LIGHT, out var lineLight);
            ColorUtility.TryParseHtmlString(ACTIVE_TOOLBAR, out var activeToolbar);
            ColorUtility.TryParseHtmlString(INACTIVE_TOOLBAR, out var inactiveToolbar);
            ColorUtility.TryParseHtmlString(VERSION_TEXT, out var versionText);
            ColorUtility.TryParseHtmlString(HOVER_GREEN, out var hoverGreen);

            // Parse new log and status colors
            ColorUtility.TryParseHtmlString(CMD_BACKGROUND, out var cmdBackground);
            ColorUtility.TryParseHtmlString(CMD_TEXT, out var cmdText);

            // Parse data window colors
            ColorUtility.TryParseHtmlString(TABLE_SELECTED, out var tableSelected);
            ColorUtility.TryParseHtmlString(CLR_BUTTON, out var clearDataButton);

            // Parse reducer window colors
            ColorUtility.TryParseHtmlString(RUN_BUTTON_NORMAL, out var runButtonNormal);
            ColorUtility.TryParseHtmlString(RUN_BUTTON_HOVER, out var runButtonHover);
            ColorUtility.TryParseHtmlString(SEPARATOR, out var separator);

            // Parse status colors
            ColorUtility.TryParseHtmlString(STATUS_NEUTRAL, out var statusNeutral);
            ColorUtility.TryParseHtmlString(STATUS_SUCCESS, out var statusSuccess);
            ColorUtility.TryParseHtmlString(STATUS_ERROR, out var statusError);
            ColorUtility.TryParseHtmlString(STATUS_WARNING, out var statusWarning);
            ColorUtility.TryParseHtmlString(STATUS_INFO, out var statusInfo);
            ColorUtility.TryParseHtmlString(STATUS_TIME, out var statusTime);

            // Parse setup window colors
            ColorUtility.TryParseHtmlString(INSTALLED_TEXT, out var installedText);
            ColorUtility.TryParseHtmlString(INSTALL_BUTTON_NORMAL, out var installButtonNormal);
            ColorUtility.TryParseHtmlString(INSTALL_BUTTON_HOVER, out var installButtonHover);
            ColorUtility.TryParseHtmlString(SECTION_HEADER, out var sectionHeader);

            // Cache the parsed colors
            WindowToggle = windowToggle;
            SubtitleText = subtitleText;
            AutoscrollEnabled = autoscrollEnabled;
            AutoscrollDisabled = autoscrollDisabled;
            ClearButton = clearButton;
            ButtonText = buttonText;
            ButtonDisabled = buttonDisabled;
            ConnectedText = connectedText;
            Processing = processing;
            Recommended = recommended;
            Warning = warning;
            Hidden = hidden;
            Debug = debug;
            LineDark = lineDark;
            LineLight = lineLight;
            ActiveToolbar = activeToolbar;
            InactiveToolbar = inactiveToolbar;
            VersionText = versionText;
            HoverGreen = hoverGreen;
            CmdBackground = cmdBackground;
            CmdText = cmdText;
            TableSelected = tableSelected;
            ClearDataButton = clearDataButton;
            RunButtonNormal = runButtonNormal;
            RunButtonHover = runButtonHover;
            Separator = separator;
            StatusNeutral = statusNeutral;
            StatusSuccess = statusSuccess;
            StatusError = statusError;
            StatusWarning = statusWarning;
            StatusInfo = statusInfo;
            StatusTime = statusTime;

            // Cache setup window colors
            InstalledText = installedText;
            InstallButtonNormal = installButtonNormal;
            InstallButtonHover = installButtonHover;
            SectionHeader = sectionHeader;

            _initialized = true;
        }

        /// <summary>
        /// Ensures colors are initialized before use. Called automatically by property accessors.
        /// </summary>
        public static void EnsureInitialized()
        {
            Initialize();
        }

        /// <summary>
        /// Gets color by name as a string (useful for runtime selection or debugging).
        /// </summary>
        public static Color GetColorByName(string colorName)
        {
            EnsureInitialized();
            
            return colorName switch
            {
                "WindowToggle" => WindowToggle,
                "SubtitleText" => SubtitleText,
                "AutoscrollEnabled" => AutoscrollEnabled,
                "AutoscrollDisabled" => AutoscrollDisabled,
                "ClearButton" => ClearButton,
                "ButtonText" => ButtonText,
                "ButtonDisabled" => ButtonDisabled,
                "ConnectedText" => ConnectedText,
                "Processing" => Processing,
                "Recommended" => Recommended,
                "Warning" => Warning,
                "Hidden" => Hidden,
                "Debug" => Debug,
                "LineDark" => LineDark,
                "LineLight" => LineLight,
                "ActiveToolbar" => ActiveToolbar,
                "InactiveToolbar" => InactiveToolbar,
                "VersionText" => VersionText,
                "HoverGreen" => HoverGreen,
                "CmdBackground" => CmdBackground,
                "CmdText" => CmdText,
                "TableSelected" => TableSelected,
                "ClearDataButton" => ClearDataButton,
                "RunButtonNormal" => RunButtonNormal,
                "RunButtonHover" => RunButtonHover,
                "Separator" => Separator,
                "StatusNeutral" => StatusNeutral,
                "StatusSuccess" => StatusSuccess,
                "StatusError" => StatusError,
                "StatusWarning" => StatusWarning,
                "StatusInfo" => StatusInfo,
                "StatusTime" => StatusTime,
                "InstalledText" => InstalledText,
                "InstallButtonNormal" => InstallButtonNormal,
                "InstallButtonHover" => InstallButtonHover,
                "SectionHeader" => SectionHeader,
                _ => Color.white,
            };
        }
    }

    #endregion

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

    /// <summary>
    /// Parses and extracts URLs from command output, specifically looking for login URLs
    /// </summary>
    /// <param name="output">The command output to parse</param>
    /// <returns>Array of extracted URLs</returns>
    public static string[] ExtractUrlsFromOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
            return new string[0];

        var urls = new System.Collections.Generic.List<string>();
        
        try
        {
            // Use regex to find URLs in the format: (http://...) or (https://...) or just http://... or https://...
            // This handles the specific case: "Opening https://spacetimedb.com/login/cli?token=... in your browser."
            string urlPattern = @"(?:Opening\s+)?(?:\(?)(https?://[^\s\)]+)(?:\)?)";
            var matches = System.Text.RegularExpressions.Regex.Matches(output, urlPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    string url = match.Groups[1].Value;
                    if (!string.IsNullOrEmpty(url) && !urls.Contains(url))
                    {
                        urls.Add(url);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error extracting URLs from output: {ex.Message}");
        }
        
        return urls.ToArray();
    }

    /// <summary>
    /// Opens a URL in the system's default browser
    /// </summary>
    /// <param name="url">The URL to open</param>
    /// <returns>True if successful, false otherwise</returns>
    public static bool OpenUrlInBrowser(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        try
        {
            // Use Unity's Application.OpenURL which works in both Editor and builds
            // But it must be called from the main thread, so use delayCall for thread safety
            UnityEditor.EditorApplication.delayCall += () => Application.OpenURL(url);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error opening URL {url}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Processes command output to automatically open any detected login URLs
    /// </summary>
    /// <param name="output">The command output to process</param>
    /// <returns>The processed output with URL opening status</returns>
    public static string ProcessOutputAndOpenUrls(string output)
    {
        if (string.IsNullOrEmpty(output))
            return output;

        string[] urls = ExtractUrlsFromOutput(output);
        string processedOutput = output;
        
        foreach (string url in urls)
        {
            // Check if this looks like a login URL (contains login/cli)
            if (url.Contains("login/cli"))
            {
                if (OpenUrlInBrowser(url))
                {
                    // Replace the "Opening ... in your browser" text with confirmation
                    string urlPattern = @"Opening\s+" + System.Text.RegularExpressions.Regex.Escape(url) + @"\s+in\s+your\s+browser";
                    processedOutput = System.Text.RegularExpressions.Regex.Replace(
                        processedOutput, 
                        urlPattern, 
                        $"Opening {url} in your browser... (URL opened automatically)", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    );
                    
                    // Also handle the case without "Opening" prefix
                    if (processedOutput.Contains(url) && !processedOutput.Contains("URL opened automatically"))
                    {
                        processedOutput = processedOutput.Replace(url, $"{url} (URL opened automatically)");
                    }
                }
            }
        }
        
        return processedOutput;
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

    #region Path Style Detection

    /// <summary>
    /// Determines if a path uses Windows style conventions (contains backslashes or drive letters)
    /// </summary>
    /// <param name="path">The path to check</param>
    /// <returns>True if path appears to be Windows style</returns>
    public static bool IsPathWindowsStyle(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        
        // Check for drive letter pattern (C:\, D:\, etc.)
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            return true;
        
        // Check for UNC path pattern (\\server\share)
        if (path.StartsWith("\\\\"))
            return true;
        
        // Check for backslashes
        if (path.Contains("\\"))
            return true;
        
        return false;
    }

    /// <summary>
    /// Determines if a path uses Unix style conventions (starts with / or ~)
    /// </summary>
    /// <param name="path">The path to check</param>
    /// <returns>True if path appears to be Unix style</returns>
    public static bool IsPathUnixStyle(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        
        // Check for absolute Unix path
        if (path.StartsWith("/"))
            return true;
        
        // Check for home directory shorthand
        if (path.StartsWith("~"))
            return true;
        
        return false;
    }

    /// <summary>
    /// Determines if a path is a WSL path (Unix path on Windows via WSL)
    /// </summary>
    /// <param name="path">The path to check</param>
    /// <returns>True if path appears to be a WSL path</returns>
    public static bool IsPathWSLStyle(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        
        // WSL paths typically start with /home/, /mnt/, or /root/
        if (path.StartsWith("/home/") || path.StartsWith("/mnt/") || path.StartsWith("/root/"))
            return true;
        
        return false;
    }

    /// <summary>
    /// Converts a WSL path to a Windows UNC path accessible from Windows
    /// </summary>
    /// <param name="wslPath">The WSL path to convert (e.g., /mnt/c/Users/username or /home/username)</param>
    /// <returns>Windows UNC path (e.g., \\wsl$\Debian\mnt\c\Users\username or C:\Users\username)</returns>
    public static string ConvertWSLPathToWindowsPath(string wslPath)
    {
        if (string.IsNullOrEmpty(wslPath))
            return wslPath;
        
        // If it's /mnt/x/... it's accessing Windows drives, convert directly
        if (wslPath.StartsWith("/mnt/"))
        {
            // /mnt/c/Users/... becomes C:\Users\...
            string pathWithoutMnt = wslPath.Substring(5); // Remove "/mnt/"
            if (pathWithoutMnt.Length > 1 && char.IsLetter(pathWithoutMnt[0]) && pathWithoutMnt[1] == '/')
            {
                string drive = char.ToUpper(pathWithoutMnt[0]).ToString();
                string rest = pathWithoutMnt.Substring(2).Replace('/', '\\');
                return $"{drive}:{rest}";
            }
        }
        
        // For /home/, /root/, and other paths, use \\wsl$\Debian\ access
        // This allows Windows access to the WSL filesystem
        string normalizedPath = wslPath.Replace('/', '\\');
        return $"\\\\wsl$\\Debian{normalizedPath}";
    }

    /// <summary>
    /// Gets the appropriate file path for the current platform and server directory configuration
    /// </summary>
    /// <param name="serverDirectory">The server directory path (may be Windows, WSL, or Unix style)</param>
    /// <param name="fileName">The file name to append</param>
    /// <returns>The appropriate file path for accessing the file on the current platform</returns>
    public static string GetPlatformSpecificFilePath(string serverDirectory, string fileName)
    {
        if (string.IsNullOrEmpty(serverDirectory) || string.IsNullOrEmpty(fileName))
            return null;
        
        string filePath;
        
        // Determine path style and current platform
        bool isWSLPath = IsPathWSLStyle(serverDirectory);
        bool isWindowsPath = IsPathWindowsStyle(serverDirectory);
        bool isUnixPath = IsPathUnixStyle(serverDirectory) && !isWSLPath;
        bool runningOnWindows = IsWindows();
        
        if (runningOnWindows && isWSLPath)
        {
            // Running on Windows, accessing WSL path - convert to Windows UNC path
            string windowsPath = ConvertWSLPathToWindowsPath(serverDirectory);
            filePath = Path.Combine(windowsPath, fileName);
        }
        else if (runningOnWindows && isWindowsPath)
        {
            // Running on Windows with Windows path - use as is
            filePath = Path.Combine(serverDirectory, fileName);
        }
        else if (!runningOnWindows && isUnixPath)
        {
            // Running on Unix (Mac/Linux) with Unix path - use forward slashes
            string normalizedDir = serverDirectory.TrimEnd('/');
            filePath = $"{normalizedDir}/{fileName}";
        }
        else if (!runningOnWindows && isWindowsPath)
        {
            // Running on Unix but have a Windows path - this is unusual, try to handle it
            // Replace backslashes with forward slashes
            string normalizedDir = serverDirectory.Replace('\\', '/').TrimEnd('/');
            filePath = $"{normalizedDir}/{fileName}";
        }
        else
        {
            // Fallback - try Path.Combine which should work on current platform
            filePath = Path.Combine(serverDirectory, fileName);
        }
        
        return filePath;
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

    #region Platform Detection

    /// <summary>
    /// Determines if the current operating system is Windows
    /// </summary>
    public static bool IsWindows()
    {
        return Application.platform == RuntimePlatform.WindowsEditor;
    }

    /// <summary>
    /// Determines if the current operating system is macOS
    /// </summary>
    public static bool IsMacOS()
    {
        return Application.platform == RuntimePlatform.OSXEditor;
    }

    /// <summary>
    /// Determines if the current operating system is Linux
    /// </summary>
    public static bool IsLinux()
    {
        return Application.platform == RuntimePlatform.LinuxEditor;
    }

    /// <summary>
    /// Gets the appropriate shell executable for the current platform
    /// </summary>
    /// <returns>Shell executable path (cmd.exe, bash, etc.)</returns>
    public static string GetShellExecutable()
    {
        if (IsWindows())
            return "cmd.exe";
        else if (IsMacOS() || IsLinux())
            return "/bin/bash";
        else
            return "sh"; // Fallback
    }

    /// <summary>
    /// Gets the appropriate shell argument prefix for the current platform
    /// </summary>
    /// <param name="command">The command to wrap</param>
    /// <returns>Shell arguments including the command</returns>
    public static string GetShellArguments(string command)
    {
        if (IsWindows())
            return $"/c {command}";
        else if (IsMacOS() || IsLinux())
            return $"-c \"{command}\"";
        else
            return $"-c \"{command}\""; // Fallback
    }

    /// <summary>
    /// Gets the appropriate ping command for the current platform
    /// </summary>
    /// <param name="hostname">The hostname to ping</param>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <returns>Tuple of (executable, arguments)</returns>
    public static (string executable, string arguments) GetPingCommand(string hostname, int timeoutMs = 1000)
    {
        if (IsWindows())
        {
            // Windows ping: -n (count) -w (timeout in ms)
            int timeoutSec = Mathf.Max(1, timeoutMs / 1000);
            return ("ping", $"{hostname} -n 1 -w {timeoutMs}");
        }
        else if (IsMacOS())
        {
            // macOS ping: -c (count) -W (timeout in ms)
            return ("ping", $"-c 1 -W {timeoutMs} {hostname}");
        }
        else if (IsLinux())
        {
            // Linux ping: -c (count) -W (timeout in ms)
            return ("ping", $"-c 1 -W {timeoutMs / 1000} {hostname}");
        }
        else
        {
            // Fallback to basic ping
            return ("ping", $"-c 1 {hostname}");
        }
    }

    /// <summary>
    /// Gets the command to check if a process is running on the current platform
    /// </summary>
    /// <param name="processName">Name of the process to check</param>
    /// <returns>Tuple of (executable, arguments)</returns>
    public static (string executable, string arguments) GetCheckProcessCommand(string processName)
    {
        if (IsWindows())
        {
            // Windows: tasklist command
            return ("tasklist", $"/FI \"IMAGENAME eq {processName}*\"");
        }
        else if (IsMacOS() || IsLinux())
        {
            // Unix-like: pgrep command
            return ("pgrep", $"-f {processName}");
        }
        else
        {
            return ("pgrep", $"-f {processName}"); // Fallback
        }
    }

    /// <summary>
    /// Gets the command to check if a file exists on the current platform
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <returns>Tuple of (executable, arguments)</returns>
    public static (string executable, string arguments) GetFileExistsCommand(string filePath)
    {
        if (IsWindows())
        {
            // Windows: test for file existence
            return ("cmd.exe", $"/c if exist \"{filePath}\" (echo EXISTS) else (echo NOT_EXISTS)");
        }
        else if (IsMacOS() || IsLinux())
        {
            // Unix-like: test command
            return ("test", $"-f {filePath} && echo EXISTS || echo NOT_EXISTS");
        }
        else
        {
            return ("test", $"-f {filePath} && echo EXISTS || echo NOT_EXISTS"); // Fallback
        }
    }

    /// <summary>
    /// Gets the command to check if an executable exists and is executable on the current platform
    /// </summary>
    /// <param name="executablePath">Path to the executable</param>
    /// <returns>Tuple of (executable, arguments)</returns>
    public static (string executable, string arguments) GetExecutableExistsCommand(string executablePath)
    {
        if (IsWindows())
        {
            // Windows: where or test for file
            return ("cmd.exe", $"/c if exist \"{executablePath}\" (echo EXISTS_AND_EXECUTABLE) else (echo NOT_EXECUTABLE)");
        }
        else if (IsMacOS() || IsLinux())
        {
            // Unix-like: test for executable
            return ("test", $"-x {executablePath} && echo EXISTS_AND_EXECUTABLE || echo NOT_EXECUTABLE");
        }
        else
        {
            return ("test", $"-x {executablePath} && echo EXISTS_AND_EXECUTABLE || echo NOT_EXECUTABLE"); // Fallback
        }
    }
    #endregion
}

} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
