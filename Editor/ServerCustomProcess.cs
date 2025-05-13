using UnityEngine;
using UnityEditor;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace NorthernRogue.CCCP.Editor
{
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
        private bool settingsLoaded = false;
        
        // Session management
        private bool isConnected = false;
        
        // Session status tracking
        private bool sessionActive = false;
        private double lastSessionCheckTime = 0;
        private const double sessionCheckInterval = 10.0; // Reduced frequency - check every 10 seconds
        
        // Status cache to avoid repeated SSH calls
        private double lastStatusCacheTime = 0;
        private const double statusCacheTimeout = 20.0; // Status valid for 20 seconds
        private bool cachedServerRunningStatus = false;
        
        // Command result cache to reduce load
        private Dictionary<string, (double timestamp, bool success, string output, string error)> commandCache = 
            new Dictionary<string, (double timestamp, bool success, string output, string error)>();
        private const double commandCacheTimeout = 10.0; // Cache command results for 10 seconds

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
            settingsLoaded = true;
        }
        
        // Log wrapper method
        private void Log(string message, int level)
        {
            logCallback?.Invoke(message, level);
        }

        #region SSH Connection
        
        // Ensure settings are loaded before performing operations
        private void EnsureSettingsLoaded()
        {
            if (!settingsLoaded)
            {
                LoadSettings();
            }
        }
        
        // Start a persistent SSH session
        public bool StartSession()
        {
            EnsureSettingsLoaded();
            
            if (isConnected)
            {
                Log("SSH session already active", 0);
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
            bool connectionVerified = VerifyConnection();
            
            if (connectionVerified)
            {
                Log("SSH connection established successfully", 1);
                isConnected = true;
                sessionActive = true;
                lastSessionCheckTime = UnityEditor.EditorApplication.timeSinceStartup;
                
                // Clear command cache
                commandCache.Clear();
                
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
        private bool VerifyConnection()
        {
            try
            {
                // Use a very basic test command with short timeout
                Log("Testing SSH connection with simple test command...", 0);
                var result = RunSimpleCommand("echo CONNECTION_TEST_OK", 3000);
                
                if (result.success && result.output.Contains("CONNECTION_TEST_OK"))
                {
                    Log("SSH connection verified successfully!", 1);
                    return true;
                }
                else
                {
                    Log($"SSH verification failed: {result.error}", -1);
                    
                    // Provide helpful troubleshooting advice
                    Log("Try these troubleshooting steps:", 0);
                    Log("1. Verify your SSH username and private key are correct", 0);
                    Log("2. Confirm SSH server is running: sudo systemctl status ssh", 0);
                    Log("3. Test manual SSH: ssh -i private_key username@host", 0);
                    Log("4. Ensure private key permissions are correct", 0);
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"SSH verification exception: {ex.Message}", -1);
                return false;
            }
        }
        
        // Run a command process using SSH key
        private (bool success, string output, string error) RunSimpleCommand(string command, int timeoutMs = 5000)
        {
            try 
            {
                if (string.IsNullOrEmpty(sshPrivateKeyPath))
                {
                    Log("SSH Private Key Path is not set. Please specify the path to your private key file.", -1);
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
                bool finished = process.WaitForExit(timeoutMs);
                
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
                bool success = process.ExitCode == 0 || (!string.IsNullOrEmpty(output) && string.IsNullOrEmpty(error));
                
                if (success)
                {
                    if (debugMode) Log("SSH command executed successfully", 1);
                }
                else
                {
                    if (debugMode) Log($"SSH command failed with exit code: {process.ExitCode}. Error: {error}", -1);
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
            EnsureSettingsLoaded();
            
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
        public async Task<(bool success, string output, string error)> RunSpacetimeDBCommandAsync(string command, int timeoutMs = 5000)
        {
            // Use the full path to spacetime executable
            string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";
            
            // Check if command already starts with "spacetime" and remove it
            string trimmedCommand = command.Trim();
            if (trimmedCommand.StartsWith("spacetime ", StringComparison.OrdinalIgnoreCase))
            {
                trimmedCommand = trimmedCommand.Substring("spacetime ".Length);
            }
            
            string spacetimeCmd = $"{spacetimePath} {trimmedCommand}";
            return await RunCustomCommandAsync(spacetimeCmd, timeoutMs);
        }
        
        // Execute a command on the custom server
        public async Task<(bool success, string output, string error)> RunCustomCommandAsync(string command, int timeoutMs = 5000)
        {
            EnsureSettingsLoaded();
            
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
            var result = RunSimpleCommand(command, timeoutMs);
            
            // Wrap the synchronous result in a completed task to match the async signature
            return await Task.FromResult(result);
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
                    return cachedResult.success && cachedResult.output.Contains("FOUND");
                }
            }
            
            // Construct the expected path
            string expectedPath = $"/home/{sshUserName}/.local/bin/spacetime";
            string command = $"test -f {expectedPath} && echo FOUND || echo NOT_FOUND";
            
            var result = await RunCustomCommandAsync(command, 3000);
            
            // Cache the result with longer timeout
            commandCache["check_spacetimedb_installed"] = (EditorApplication.timeSinceStartup, result.success, result.output, result.error);
            
            // Check if the output contains the success marker
            bool installed = result.success && result.output.Contains("FOUND");
            
            if (installed)
            {
                Log("SpacetimeDB executable found on the remote server.", 1);
            }
            else if (!result.success) // Only log error if first-time check
            {
                Log($"SpacetimeDB executable NOT found at {expectedPath}. Make sure it's installed correctly and accessible.", -2);
                if (!string.IsNullOrEmpty(result.error))
                {
                    Log($"SSH Error during check: {result.error}", -1);
                }
            }
            
            return installed;
        }
        
        // Check if the SpacetimeDB server is running - optimized with caching
        public async Task<bool> CheckServerRunning()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            
            // Use cached result if recent enough
            if (currentTime - lastStatusCacheTime < statusCacheTimeout)
            {
                return cachedServerRunningStatus;
            }
            
            // Run actual check if cache expired
            var result = await RunCustomCommandAsync("ps aux | grep spacetime | grep -v grep", 3000);
            bool running = result.success && result.output.Contains("spacetime");
            
            // Update cache
            cachedServerRunningStatus = running;
            lastStatusCacheTime = currentTime;
            
            return running;
        }
        
        // Get SpacetimeDB version
        public async Task<string> GetSpacetimeDBVersion()
        {
            // Check cache first
            if (commandCache.TryGetValue("spacetime_version", out var cachedResult))
            {
                double currentTime = EditorApplication.timeSinceStartup;
                if (currentTime - cachedResult.timestamp < 60.0) // Longer cache for version (60s)
                {
                    return cachedResult.output.Trim();
                }
            }
            
            // Use the full path to spacetime
            string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";
            
            var result = await RunCustomCommandAsync($"{spacetimePath} --version", 3000);
            
            if (result.success && !string.IsNullOrEmpty(result.output))
            {
                string version = result.output.Trim();
                
                // Cache the result
                commandCache["spacetime_version"] = (EditorApplication.timeSinceStartup, true, version, "");
                
                Log($"SpacetimeDB version: {version}", 1);
                return version;
            }
            else
            {
                return "Unknown";
            }
        }
        
        // Start SpacetimeDB server on the remote machine
        public async Task<bool> StartCustomServer()
        {
            // Check if server is already running - using cached status if available
            if (await CheckServerRunning())
            {
                Log("SpacetimeDB server is already running", 1);
                return true;
            }
            
            // Use the full path to spacetime executable
            string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";
            
            // Start the server
            Log("Starting SpacetimeDB server on remote machine...", 0);
            var result = await RunCustomCommandAsync($"{spacetimePath} start", 5000);
            
            if (result.success)
            {
                // Force status cache update
                cachedServerRunningStatus = true;
                lastStatusCacheTime = EditorApplication.timeSinceStartup;
                
                Log("SpacetimeDB server started successfully on remote machine", 1);
                return true;
            }
            else
            {
                Log($"Failed to start SpacetimeDB server: {result.error}", -1);
                return false;
            }
        }
        
        // Stop SpacetimeDB server on the remote machine
        public async Task<bool> StopCustomServer()
        {
            // Check if server is running - using cached status if available
            if (!await CheckServerRunning())
            {
                Log("SpacetimeDB server is not running", 0);
                return true;
            }
            
            // Use the full path to spacetime executable
            string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";
            
            // Stop the server
            Log("Stopping SpacetimeDB server on remote machine...", 0);
            var result = await RunCustomCommandAsync($"{spacetimePath} server stop", 5000);
            
            if (result.success)
            {
                // Force status cache update
                cachedServerRunningStatus = false;
                lastStatusCacheTime = EditorApplication.timeSinceStartup;
                
                // Clear command cache as commands will likely return different results now
                commandCache.Clear();
                
                Log("SpacetimeDB server stopped successfully on remote machine", 1);
                return true;
            }
            else
            {
                Log($"Failed to stop SpacetimeDB server: {result.error}", -1);
                return false;
            }
        }
        #endregion

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
            return sessionActive;
        }

        // Only update session status occasionally to avoid lag
        public void UpdateSessionStatusIfNeeded()
        {
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
            // Cancel any existing background check
            CancelBackgroundChecks();
            
            try
            {
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
                
                // Handle exit and output separately to avoid blocking
                string output = "";
                
                pingProcess.OutputDataReceived += (sender, args) => {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        output = args.Data.Trim();
                    }
                };
                
                pingProcess.BeginOutputReadLine();
                
                // Use shorter timeout
                bool exited = pingProcess.WaitForExit(2000); // 2 second timeout
                
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
        }
    }
} 