using UnityEngine;
using UnityEditor;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

// Handles the methods related to managing a custom server for SpacetimeDB ///

namespace NorthernRogue.CCCP.Editor {

public class ServerCustomProcess
{
    // Constants
    private const string PrefsKeyPrefix = "CCCP_";
    
    // Callback delegate for logging messages
    private Action<string, int> logCallback;
    
    // Debug mode flag
    public static bool debugMode = false;
    
    // SSH connection information
    private string sshUserName;
    private string customServerUrl;
    private int customServerPort;
    private string customServerAuthToken;
    private string sshPrivateKeyPath = ""; // Path to the SSH private key file
    
    // SSH connection status
    private bool isConnected = false;
    
    // Service mode flag
    private bool serviceMode = false;
    
    // Session status tracking
    private bool sessionActive = false;
    private double lastSessionCheckTime = 0;
    private const double sessionCheckInterval = 5;
    
    // Status cache to avoid repeated SSH calls
    private double lastStatusCacheTime = 0;
    private const double statusCacheTimeout = 10; // Status valid for 10 seconds
    public bool cachedServerRunningStatus = false;
    
    // Command result cache to reduce load
    private Dictionary<string, (double timestamp, bool success, string output, string error)> commandCache = 
        new Dictionary<string, (double timestamp, bool success, string output, string error)>();
    private const double commandCacheTimeout = 10; // Cache command results for 10 seconds

    // Background connection check
    private Process backgroundCheckProcess = null;
    
    // Constructor
    public ServerCustomProcess(Action<string, int> logCallback, bool debugMode = false)
    {
        this.logCallback = logCallback;
        ServerCustomProcess.debugMode = debugMode;
    }
    
    // Automatically load settings from EditorPrefs
    public void LoadSettings()
    {
        sshUserName = EditorPrefs.GetString(PrefsKeyPrefix + "SSHUserName", "");
        sshPrivateKeyPath = EditorPrefs.GetString(PrefsKeyPrefix + "SSHPrivateKeyPath", "");
        serviceMode = EditorPrefs.GetBool(PrefsKeyPrefix + "ServiceMode", true);
        
        // Get server URL for SSH connection (hostname only)
        string serverUrl = EditorPrefs.GetString(PrefsKeyPrefix + "CustomServerURL", "");
        
        // Extract server address from URL
        if (!string.IsNullOrEmpty(serverUrl))
        {
            Uri uri;
            if (Uri.TryCreate(serverUrl, UriKind.Absolute, out uri))
            {
                customServerUrl = uri.Host;
            }
            else
            {
                // Fallback to the URL itself if parsing fails
                customServerUrl = serverUrl.Replace("http://", "").Replace("https://", "").Split(':')[0];
            }
        }
        
        // Use SSH default port 22 instead of the server application port
        customServerPort = 22; // Default SSH port
        
        // Store the SpacetimeDB server port separately
        int spacetimeDbPort = EditorPrefs.GetInt(PrefsKeyPrefix + "CustomServerPort", 3000);
        
        // Get auth token
        customServerAuthToken = EditorPrefs.GetString(PrefsKeyPrefix + "CustomServerAuthToken", "");
        
        if (debugMode) Log($"SSH settings loaded: User={sshUserName}, Host={customServerUrl}:{customServerPort}, KeyPath={sshPrivateKeyPath} (SpacetimeDB on port {spacetimeDbPort})", 0);
    }
    
    // Log wrapper method
    private void Log(string message, int level)
    {
        logCallback?.Invoke(message, level);
    }

    #region SSH Start
            
    // Start a persistent SSH session
    public async Task<bool> StartSession()
    {
        LoadSettings();
        
        if (isConnected)
        {
            Log($"SSH session already active on {customServerUrl}:{customServerPort}", 0);
            return true;
        }
        
        if (string.IsNullOrEmpty(sshUserName) || string.IsNullOrEmpty(customServerUrl))
        {
            Log("SSH username or server address not configured", -1);
            return false;
        }
        
        // Validate SSH connection parameters
        if (customServerPort <= 0 || customServerPort > 65535)
        {
            Log($"Invalid SSH port: {customServerPort}, using default port 22", -2);
            customServerPort = 22;
        }
        
        // Verify connection first with a simple command
        if (debugMode) Log("Attempting SSH connection verification...", 0);
        bool connectionVerified = await VerifyConnection();
        
        if (connectionVerified)
        {
            Log("SSH connected successfully!", 1);
            isConnected = true;
            sessionActive = true;
            lastSessionCheckTime = UnityEditor.EditorApplication.timeSinceStartup;
            
            // Clear command cache
            commandCache.Clear();

            // Check SpacetimeDB version on custom server
            _ = CheckSpacetimeDBVersionCustom();
            
            return true;
        }
        else
        {
            Log("Failed to verify SSH connection. Please check your credentials and ensure SSH service is enabled on your target server.", -1);
            isConnected = false;
            sessionActive = false;
            return false;
        }
    }
    
    // Stop the SSH session
    public void StopSession()
    {
        isConnected = false;
        sessionActive = false;
        
        // Clear any background processes
        CancelBackgroundChecks();
        
        // Clear caches
        commandCache.Clear();
        
        Log("SSH session terminated", 0);
    }
    
    // Cancel any background processes
    private void CancelBackgroundChecks()
    {
        try
        {
            if (backgroundCheckProcess != null && !backgroundCheckProcess.HasExited)
            {
                try { backgroundCheckProcess.Kill(); } catch { }
            }
            backgroundCheckProcess = null;
        }
        catch (Exception ex)
        {
            if (debugMode) Log($"Error canceling background check: {ex.Message}", -1);
        }
    }
    
    // Verify if the SSH connection is working
    private async Task<bool> VerifyConnection()
    {
        try
        {
            // Use a very basic test command with short timeout
            Log("Testing SSH connection with " + sshUserName + " to " + customServerUrl + " with simple test command...", 0);
            var result = await RunSimpleCommandAsync("echo CONNECTION_TEST_OK", 3000);
            
            if (result.success && result.output.Contains("CONNECTION_TEST_OK"))
            {
                if (debugMode) Log("SSH connection verified successfully!", 1);
                return true;
            }
            else
            {
                if (debugMode) Log($"SSH verification failed: {result.error}", -1);
                return false;
            }
        }
        catch (Exception ex)
        {
            if (debugMode) Log($"SSH verification exception: {ex.Message}", -1);
            return false;
        }
    }
    
    // Run a command process using SSH key
    private async Task<(bool success, string output, string error)> RunSimpleCommandAsync(string command, int timeoutMs = 5000)
    {
        try 
        {
            if (string.IsNullOrEmpty(sshPrivateKeyPath))
            {
                if (debugMode) Log("SSH Private Key Path is not set. Please specify the path to your private key file.", -1);
                return (false, "", "SSH Private Key Path not set");
            }
            
            if (!File.Exists(sshPrivateKeyPath))
            {
                Log($"SSH Private Key file not found at: {sshPrivateKeyPath}", -1);
                return (false, "", "SSH Private Key file not found");
            }
            
            // Check cache first to avoid repeated calls
            string cacheKey = $"{command}";
            if (commandCache.TryGetValue(cacheKey, out var cachedResult))
            {
                double currentTime = EditorApplication.timeSinceStartup;
                if (currentTime - cachedResult.timestamp < commandCacheTimeout)
                {
                    if (debugMode) Log($"Using cached command result for: {command}", 0);
                    return (cachedResult.success, cachedResult.output, cachedResult.error);
                }
                else
                {
                    // Remove expired cache entry
                    commandCache.Remove(cacheKey);
                }
            }

            if (debugMode) Log($"Running SSH command: {command} using key {sshPrivateKeyPath}", 0);
            
            Process process = new Process();
            process.StartInfo.FileName = "ssh";
            process.StartInfo.Arguments = $"-o StrictHostKeyChecking=no -o BatchMode=yes -o ConnectTimeout=3 -p {customServerPort} -i \"{sshPrivateKeyPath}\" {sshUserName}@{customServerUrl} \"{command}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            
            // Capture output
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            
            process.OutputDataReceived += (sender, args) => {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    outputBuilder.AppendLine(args.Data);
                    if (debugMode) Log($"SSH Output: {args.Data}", 0);
                }
            };
            
            process.ErrorDataReceived += (sender, args) => {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    // Ignore specific warnings
                    if (!args.Data.Contains("Pseudo-terminal will not be allocated"))
                    {
                        errorBuilder.AppendLine(args.Data);
                        if (debugMode) Log($"SSH Error: {args.Data}", 0);
                    }
                }
            };
            
            // Start the process
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Wait for the process to complete with a timeout
            if (debugMode) Log("Waiting for SSH command to complete...", 0);
            // bool finished = process.WaitForExit(timeoutMs); // Original synchronous wait
            
            // Asynchronous wait
            bool finished = await Task.Run(() => process.WaitForExit(timeoutMs));

            if (!finished)
            {
                if (debugMode) Log("SSH command timed out", -1);
                try { process.Kill(); } catch { }
                return (false, "", "Command timed out");
            }
            
            string output = outputBuilder.ToString().Trim();
            string error = errorBuilder.ToString().Trim();
            
            if (debugMode) Log($"SSH command completed with exit code: {process.ExitCode}", 0);
            
            // Check for common error messages
            if (error.Contains("Permission denied") || output.Contains("Permission denied"))
            {
                if (debugMode) Log("SSH authentication failed - check private key permissions or setup on server.", -1);
                return (false, output, "SSH authentication failed (Permission denied)");
            }
            else if (error.Contains("Connection refused") || output.Contains("Connection refused"))
            {
                if (debugMode) Log("Connection refused - SSH server may not be running", -1);
                return (false, output, "Connection refused - SSH server may not be running");
            }
            
            // Consider success if exit code is 0 or if we have output without critical errors
            bool success = process.ExitCode == 0 || (!string.IsNullOrEmpty(output) && string.IsNullOrEmpty(error)) || command.Contains("spacetimedb-standalone");
            
            if (success)
            {
                if (debugMode) Log($"SSH command '{command}' executed successfully", 1);
            }
            else
            {
                if (debugMode) Log($"SSH command '{command}' failed with exit code: {process.ExitCode}. Error: {error}", -1);
            }
            
            // Cache the result
            commandCache[cacheKey] = (EditorApplication.timeSinceStartup, success, output, error);
            
            return (success, output, error);
        }
        catch (Exception ex)
        {
            if (debugMode) Log($"Error executing SSH command: {ex.Message}", -1);
            return (false, "", $"Error executing command: {ex.Message}");
        }
    }
    
    // Check if the server is reachable via ping - now optimized to be more lightweight
    public async Task<bool> CheckServerReachable()
    {
        LoadSettings();
        
        if (string.IsNullOrEmpty(customServerUrl))
        {
            Log("Server address not configured", -1);
            return false;
        }
        
        try
        {
            // Only use a short timeout
            var process = new Process();
            process.StartInfo.FileName = "ping";
            process.StartInfo.Arguments = $"{customServerUrl} -n 1 -w 1000"; // 1 second timeout
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            
            // Wait a short time
            bool exited = process.WaitForExit(1500);
            if (!exited)
            {
                try { process.Kill(); } catch { }
                return false;
            }
            
            return process.ExitCode == 0 || output.Contains("Reply from") || output.Contains("bytes=");
        }
        catch (Exception)
        {
            return false;
        }
    }
    #endregion
    
    #region Commands

    // Helper function to run a SpacetimeDB command
    public async Task<(bool success, string output, string error)> RunSpacetimeDBCommandAsync(string command, int timeoutMs = 10000)
    {
        LoadSettings(); // Ensure SSH settings, including sshUserName, are current

        if (string.IsNullOrEmpty(sshUserName))
        {
            Log("SSH User Name is not configured. Cannot run SpacetimeDB command.", -1); // Log level -1 for error
            return (false, "", "SSH User Name not configured. Please set it in the server settings.");
        }

        // Use the full path to spacetime executable
        string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";
        
        // Check if command already starts with "spacetime" and remove it
        string trimmedCommand = command.Trim();
        if (trimmedCommand.StartsWith("spacetime ", StringComparison.OrdinalIgnoreCase))
        {
            trimmedCommand = trimmedCommand.Substring("spacetime ".Length);
        }
        
        string spacetimeCmd = $"{spacetimePath} {trimmedCommand}";
        if (debugMode) Log($"Running SpacetimeDB command on custom server: {spacetimeCmd}", 0);
        return await RunCustomCommandAsync(spacetimeCmd, timeoutMs);
    }
    

    // Execute a command on the custom server
    public async Task<(bool success, string output, string error)> RunCustomCommandAsync(string command, int timeoutMs = 5000)
    {
        LoadSettings();
        
        // Cache check for heavily used commands
        string cacheKey = $"{command}";
        if (commandCache.TryGetValue(cacheKey, out var cachedResult))
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - cachedResult.timestamp < commandCacheTimeout)
            {
                return (cachedResult.success, cachedResult.output, cachedResult.error);
            }
        }
        
        // Run the command with a shorter timeout
        var result = await RunSimpleCommandAsync(command, timeoutMs);
        
        // Wrap the synchronous result in a completed task to match the async signature
        return result;
    }

    // Check if SpacetimeDB is installed on the remote server
    public async Task<bool> CheckSpacetimeDBInstalled()
    {
        // Use cached result if available
        if (commandCache.TryGetValue("check_spacetimedb_installed", out var cachedResult))
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - cachedResult.timestamp < 60.0) // Cache spacetime install check longer (60s)
            {
                return cachedResult.success;
            }
        }
        
        // Construct the expected path
        string expectedPath = $"/home/{sshUserName}/.local/bin/spacetime";
        
        // Step 1: Check if the file exists AND is executable (not just if it exists)
        string checkFileCommand = $"test -x {expectedPath} && echo EXISTS_AND_EXECUTABLE || echo NOT_FOUND";
        var fileCheckResult = await RunCustomCommandAsync(checkFileCommand, 3000);
        
        if (!fileCheckResult.success || !fileCheckResult.output.Contains("EXISTS_AND_EXECUTABLE"))
        {
            if (debugMode) Log($"SpacetimeDB executable not found or not executable at {expectedPath}", 0);
            
            // Cache the negative result
            commandCache["check_spacetimedb_installed"] = (EditorApplication.timeSinceStartup, false, "", "SpacetimeDB not found");
            
            Log($"SpacetimeDB executable NOT found at {expectedPath}. Make sure it's installed correctly and accessible.", -2);
            return false;
        }
        
        // Step 2: Try to run the executable with --version to verify it's actually SpacetimeDB
        var versionResult = await RunCustomCommandAsync($"{expectedPath} --version", 3000);
        
        // Step 3: Additional validation - check the output format matches expected SpacetimeDB version output
        bool isValidSpacetimeDB = versionResult.success && 
                                    !string.IsNullOrEmpty(versionResult.output) &&
                                    (versionResult.output.Contains("SpacetimeDB") || 
                                    versionResult.output.Contains("spacetime") || 
                                    versionResult.output.Trim().StartsWith("v") ||
                                    System.Text.RegularExpressions.Regex.IsMatch(versionResult.output, @"\d+\.\d+\.\d+"));
        
        // Cache the result with longer timeout
        commandCache["check_spacetimedb_installed"] = (EditorApplication.timeSinceStartup, isValidSpacetimeDB, versionResult.output, versionResult.error);
        
        if (isValidSpacetimeDB)
        {
            Log($"Valid SpacetimeDB executable found on the remote server!", 1);
        }
        else
        {
            Log($"File exists at {expectedPath} but does not appear to be a valid SpacetimeDB executable.", -1);
            if (!string.IsNullOrEmpty(versionResult.error))
            {
                Log($"Error when checking version: {versionResult.error}", -1);
            }
        }
        
        return isValidSpacetimeDB;
    }
    #endregion

    #region Start Server
            
    public async Task<bool> StartCustomServer()
    {
        // Check if server is already running - using cached status if available
        if (await CheckServerRunning(true))
        {
            Log("SpacetimeDB server is already running", 1);
            return true;
        }
        LoadSettings(); // To know if we are in service mode or not
        
        if (serviceMode)
            {
                // Start the service using systemctl
                Log("Starting SpacetimeDB service...", 0);
                var result = await RunCustomCommandAsync("sudo systemctl start spacetimedb", 3000);

                if (!result.success)
                {
                    Log("Failed to start SpacetimeDB service. Please check service configuration.", -1);
                    return false;
                }
                else
                {
                    Log("SpacetimeDB service started successfully!", 1);

                    if (EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBLogsService", false))
                    {
                        // Start the custom logs service if configured
                        if (debugMode) Log("Starting custom SpacetimeDB logs service...", 0);
                        var resultLogService = await RunCustomCommandAsync("sudo systemctl start spacetimedb-logs", 3000);
                        
                        if (!resultLogService.success)
                        {
                            if (debugMode) Log("Failed to start SpacetimeDB logs service. Please check service configuration.", -1);
                            return false;
                        }
                    }
                    else
                    {
                        if (debugMode) Log("Custom SpacetimeDB logs service is not configured. Skipping.", 0);
                    }

                    cachedServerRunningStatus = true; // Update the value for CheckServerStatus in ServerManager
                    return true;
                }
            }
            else
            {
                // Use the full path to spacetime executable
                string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";

                // Start the server
                if (debugMode) Log("Starting SpacetimeDB server in server custom process ...", 0);
                await RunCustomCommandAsync($"{spacetimePath} start", 3000);
            }
        
        await Task.Delay(1000);

        await CheckServerRunning(true);
        
        if (cachedServerRunningStatus)
        {           
            Log($"Custom SpacetimeDB {(serviceMode ? "service" : "server")} started successfully!", 1);
            return true;
        }
        else
        {
            Log($"Failed to start SpacetimeDB {(serviceMode ? "service" : "server")}. Please start it manually.", -1);
            return false;
        }
    }
    
    // Stop SpacetimeDB server on the remote machine
    public async Task<bool> StopCustomServer()
    {
        // Check if server is running - using cached status if available
        if (!await CheckServerRunning(true))
        {
            Log("SpacetimeDB server is not running", 0);
            return true;
        }
        LoadSettings();

        if (serviceMode)
        {
            // Stop the service using systemctl
            Log("Stopping SpacetimeDB service...", 0);
            var result = await RunCustomCommandAsync("sudo systemctl stop spacetimedb", 3000);
            
            if (!result.success)
            {
                Log("Failed to stop SpacetimeDB service. Please check service configuration.", -1);
                return false;
            } else {
                if (debugMode) Log("SpacetimeDB service stopped successfully on remote machine.", 1);

                if (EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBLogsService", false))
                {
                    // Stop the custom logs service if configured
                    if (debugMode) Log("Stopping custom SpacetimeDB logs service...", 0);
                    var resultLogService = await RunCustomCommandAsync("sudo systemctl stop spacetimedb-logs", 3000);
                    
                    if (!resultLogService.success)
                    {
                        if (debugMode) Log("Failed to stop SpacetimeDB logs service. Please check service configuration.", -1);
                        return false;
                    }
                }
                else
                {
                    if (debugMode) Log("Custom SpacetimeDB logs service is not configured. Skipping.", 0);
                }
                
                commandCache.Clear();
                return true;
            }
        }
        else
        {
            Log("Stopping SpacetimeDB on remote machine...", 0);
            await RunCustomCommandAsync("pkill -9 -f spacetime", 3000);
        }
        
        await Task.Delay(1000);

        await CheckServerRunning(true);
        
        // Check if it's still running
        if (!cachedServerRunningStatus)
        {
            // Clear command cache as commands will likely return different results now
            commandCache.Clear();
            
            if (debugMode) Log($"SpacetimeDB {(serviceMode ? "service" : "server")} stopped successfully on remote machine", 1);
            return true;
        }
        else
        {
            Log($"Could not stop SpacetimeDB {(serviceMode ? "service" : "server")}. Please stop it manually.", -1);
            return false;
        }
    }

    // Add method to check service status
    public async Task<(bool success, string output, string error)> CheckServiceStatus()
    {
        LoadSettings();
        if (!serviceMode)
        {
            return (false, "", "Service mode is not enabled");
        }

        return await RunCustomCommandAsync("sudo systemctl status spacetimedb", 3000);
    }

    // Check if the SpacetimeDB server is running - optimized with caching
    public async Task<bool> CheckServerRunning(bool instantCheck)
    {
        double currentTime = EditorApplication.timeSinceStartup;
        if (!instantCheck) {

            if (currentTime - lastStatusCacheTime < statusCacheTimeout)
            {
                return cachedServerRunningStatus;
            }
        }
        
        // Run actual check if cache expired
        var result = await RunCustomCommandAsync("ps aux | grep spacetimedb-standalone | grep -v grep", 3000);
        bool running = result.success && result.output.Contains("spacetimedb-standalone");

        // Update cache
        cachedServerRunningStatus = running;
        lastStatusCacheTime = currentTime;
        
        return running;
    }
    #endregion

    #region Utility

    // Check SpacetimeDB version on custom server via SSH
    public async Task CheckSpacetimeDBVersionCustom()
    {
        if (debugMode) Log("Checking SpacetimeDB version on custom server...", 0);
        
        // Use the full path to spacetime executable
        string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";
        
        // Run the spacetime --version command via SSH
        var result = await RunCustomCommandAsync($"{spacetimePath} --version", 3000);
        
        if (string.IsNullOrEmpty(result.output))
        {
            if (debugMode) Log("Failed to get SpacetimeDB version from custom server", -1);
            return;
        }
        
        // Parse the version from output that looks like:
        // "spacetime Path: /home/mchat/.local/share/spacetime/bin/1.1.0/spacetimedb-cli
        // Commit: 
        // spacetimedb tool version 1.1.0; spacetimedb-lib version 1.1.0;"
        string version = "";
        
        // Try to find the version using regex pattern
        System.Text.RegularExpressions.Match match = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"spacetimedb tool version ([0-9]+\.[0-9]+\.[0-9]+)");

        if (match.Success && match.Groups.Count > 1)
        {
            version = match.Groups[1].Value;
            if (debugMode) Log($"Detected SpacetimeDB version on custom server: {version}", 1);

            // Save to EditorPrefs
            EditorPrefs.SetString(PrefsKeyPrefix + "SpacetimeDBVersionCustom", version);

            // Check if update is available by comparing with the latest version
            string latestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBLatestVersion", "");
            if (!string.IsNullOrEmpty(latestVersion) && version != latestVersion)
            {
                Log($"SpacetimeDB update available for custom server! Click on the update button in Commands. Current version: {version} and latest version: {latestVersion}", 1);
            }
        }
        else
        {
            if (debugMode) Log("Could not parse SpacetimeDB version from custom server output", -1);
        }
    }
    

    // Set SSH Private Key Path
    public void SetPrivateKeyPath(string path)
    {
        sshPrivateKeyPath = path;
        EditorPrefs.SetString(PrefsKeyPrefix + "SSHPrivateKeyPath", path);
        
        // Clear caches when changing key
        commandCache.Clear();
        
        if (debugMode) Log($"SSH Private Key Path updated to: {sshPrivateKeyPath}", 0);
    }

    // Check if the SSH session is currently active - just return cached value
    public bool IsSessionActive()
    {
        // Call UpdateSessionStatusIfNeeded to ensure the session status is up-to-date
        UpdateSessionStatusIfNeeded();
        return sessionActive;
    }

    // Only update session status occasionally to avoid lag
    public void UpdateSessionStatusIfNeeded()
    {
        // Quick validation to avoid unnecessary processing
        if (string.IsNullOrEmpty(sshPrivateKeyPath) || string.IsNullOrEmpty(customServerUrl) || string.IsNullOrEmpty(sshUserName))
        {
            sessionActive = false;
            return;
        }
        
        double currentTime = UnityEditor.EditorApplication.timeSinceStartup;
        if (currentTime - lastSessionCheckTime >= sessionCheckInterval)
        {
            lastSessionCheckTime = currentTime;
            
            // Don't run the status check directly - start it in the background
            EditorApplication.delayCall += StartBackgroundSessionCheck;
        }
    }
    
    // Start the session status check in the background
    private void StartBackgroundSessionCheck()
    {
        // Run the session check asynchronously to avoid freezing the UI
        Task.Run(async () => {
            try
            {
                // Cancel any existing background check
                CancelBackgroundChecks();
                
                // If we don't have connection parameters set, assume not connected
                if (string.IsNullOrEmpty(sshPrivateKeyPath) || string.IsNullOrEmpty(customServerUrl) || string.IsNullOrEmpty(sshUserName))
                {
                    sessionActive = false;
                    return;
                }

                // Run a very lightweight echo command with minimal timeout
                Process pingProcess = new Process();
                pingProcess.StartInfo.FileName = "ssh";
                pingProcess.StartInfo.Arguments = $"-i \"{sshPrivateKeyPath}\" -o BatchMode=yes -o ConnectTimeout=1 -o StrictHostKeyChecking=no {sshUserName}@{customServerUrl} echo ping";
                pingProcess.StartInfo.UseShellExecute = false;
                pingProcess.StartInfo.RedirectStandardOutput = true;
                pingProcess.StartInfo.RedirectStandardError = true;
                pingProcess.StartInfo.CreateNoWindow = true;
                
                // Store reference to background process
                backgroundCheckProcess = pingProcess;
                
                pingProcess.Start();
                
                // Handle output without blocking
                string output = "";
                pingProcess.OutputDataReceived += (sender, args) => {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        output = args.Data.Trim();
                    }
                };
                
                pingProcess.BeginOutputReadLine();
                
                // Use async waiting instead of blocking
                bool exited = await Task.Run(() => pingProcess.WaitForExit(1500)); // Reduced timeout to 1.5 seconds
                
                if (exited && pingProcess.ExitCode == 0)
                {
                    sessionActive = output == "ping";
                }
                else
                {
                    sessionActive = false;
                }
                
                backgroundCheckProcess = null;
            }
            catch (Exception)
            {
                sessionActive = false;
                backgroundCheckProcess = null;
            }
        });
    }
    #endregion

    #region Prerequisites

    // For simple prerequisites check
    public async void CheckPrerequisites(Action<bool, bool, bool, bool, bool> callback)
    {
        if (!IsSessionActive())
        {
            Log("Cannot check prerequisites: SSH session not active", -1);
            callback(false, false, false, false, false);
            return;
        }

        try
        {
            Log("Checking prerequisites on remote Debian machine...", 0);
            
            // Check Debian version (trixie)
            var debianCheckResult = await RunCustomCommandAsync("cat /etc/os-release | grep VERSION_CODENAME");
            bool hasTrixie = debianCheckResult.success && debianCheckResult.output.Contains("trixie");
            
            // Check curl
            var curlCheckResult = await RunCustomCommandAsync("which curl");
            bool hasCurl = curlCheckResult.success && curlCheckResult.output.Contains("/usr/bin/curl");
            
            // Check SpacetimeDB
            bool hasSpacetimeDB = await CheckSpacetimeDBInstalled();
            
            // Check PATH
            var pathCheckResult = await RunCustomCommandAsync("bash -l -c 'which spacetime'");
            bool hasSpacetimeDBPath = pathCheckResult.success && pathCheckResult.output.Contains("spacetime");
            
            // Check Rust
            var rustCheckResult = await RunCustomCommandAsync("bash -l -c 'which rustup'");
            bool hasRust = rustCheckResult.success && rustCheckResult.output.Contains("rustup");

            Log($"Prerequisites check complete. Trixie: {hasTrixie}, curl: {hasCurl}, SpacetimeDB: {hasSpacetimeDB}, SpacetimeDB Path: {hasSpacetimeDBPath}, Rust: {hasRust}", 0);
            
            callback(hasTrixie, hasCurl, hasSpacetimeDB, hasSpacetimeDBPath, hasRust);
        }
        catch (Exception ex)
        {
            Log($"Error checking prerequisites: {ex.Message}", -1);
            callback(false, false, false, false, false);
        }
    }
    #endregion

    #region SSH Commands
    
    // Run SSH commands in a visible PowerShell window
    // This is needed for commands requiring interactive user input (like password prompts)
    public async Task<bool> RunVisibleSSHCommand(string commands)
    {
        try
        {
            // Load SSH settings
            LoadSettings();
            
            // Extract hostname from URL if needed
            string serverHost = customServerUrl;
            if (serverHost.Contains("://"))
            {
                Uri uri = new Uri(serverHost);
                serverHost = uri.Host;
            }
            
            // Create a temporary script file to run the SSH commands
            string tempScriptPath = Path.Combine(Path.GetTempPath(), $"sshcommands_{DateTime.Now.Ticks}.ps1");
            
            // Prepare the PowerShell script content
            string scriptContent = 
                "# Show SSH command being executed\n" +
                $"Write-Host \"Connecting to {serverHost} as {sshUserName}...\" -ForegroundColor Cyan\n\n" +
                "# SSH command with proper escaping\n" +
                $"ssh -i \"{sshPrivateKeyPath}\" {sshUserName}@{serverHost} -t \"bash -c '{commands.Replace("'", "''")}'\"" +
                "\n\n" +
                "# Keep window open\n" +
                "Write-Host \"Press any key to close this window...\" -ForegroundColor Yellow\n" +
                "$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')\n";
            
            // Write the script to the temp file
            File.WriteAllText(tempScriptPath, scriptContent);
            
            if (debugMode) Log($"Created temporary PowerShell script at: {tempScriptPath}", 0);
            
            // Create process to run the PowerShell script
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                UseShellExecute = true, // Required for visible window
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
            
            process.StartInfo = startInfo;
            process.Start();
            
            if (debugMode) Log("Started PowerShell process for SSH command", 0);
            
            // Wait for the process to exit
            await Task.Run(() => {
                process.WaitForExit();
            });
            
            // Clean up the temporary script file
            try
            {
                if (File.Exists(tempScriptPath))
                {
                    File.Delete(tempScriptPath);
                    if (debugMode) Log("Cleaned up temporary script file", 0);
                }
            }
            catch (Exception ex)
            {
                // Ignore deletion errors
                if (debugMode) Log($"Failed to delete temp script (non-critical): {ex.Message}", 0);
            }
            
            Log(process.ExitCode == 0 
                ? "SSH command executed successfully" 
                : "SSH command returned non-zero exit code", 
                process.ExitCode == 0 ? 1 : -1);
                
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log($"Error running SSH command: {ex.Message}", -1);
            return false;
        }
    }
    #endregion
} // Class
} // Namespace