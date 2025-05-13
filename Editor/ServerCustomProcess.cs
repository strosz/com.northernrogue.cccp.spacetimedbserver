using UnityEngine;
using UnityEditor;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

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
        private Process persistentSshProcess;
        private bool isConnected = false;
        private bool useSessionMode = true;
        
        // Session status tracking
        private bool sessionActive = false;
        private double lastSessionCheckTime = 0;
        private const double sessionCheckInterval = 5.0; // Check session every 5 seconds
        
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
            
            if (isConnected && persistentSshProcess != null && !persistentSshProcess.HasExited)
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
            Log("SSH session terminated", 0);
        }
        
        // Verify if the SSH connection is working
        private bool VerifyConnection()
        {
            try
            {
                // Use a very basic test command
                Log("Testing SSH connection with simple test command...", 0);
                var result = RunSimpleCommand("echo CONNECTION_TEST_OK");
                
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
        
        // Run a simpler command process using SSH key
        private (bool success, string output, string error) RunSimpleCommand(string command)
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

                if (debugMode) Log($"Running SSH command: {command} using key {sshPrivateKeyPath}", 0);
                
                Process process = new Process();
                process.StartInfo.FileName = "ssh";
                process.StartInfo.Arguments = $"-o StrictHostKeyChecking=no -o BatchMode=yes -p {customServerPort} -i \"{sshPrivateKeyPath}\" {sshUserName}@{customServerUrl} \"{command}\"";
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
                bool finished = process.WaitForExit(15000);  // 15 seconds timeout
                
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
                
                return (success, output, error);
            }
            catch (Exception ex)
            {
                if (debugMode) Log($"Error executing SSH command: {ex.Message}", -1);
                return (false, "", $"Error executing command: {ex.Message}");
            }
        }
        
        // Run a single command (new SSH process each time)
        private (bool success, string output, string error) RunSingleCommand(string command, int timeoutMs)
        {
            if (debugMode) Log($"Executing command: {command}", 0);
            var result = RunSimpleCommand(command);
            if (debugMode)
            {
                if (result.success)
                {
                    Log($"Command succeeded with output: {result.output}", 0);
                }
                else
                {
                    Log($"Command failed with error: {result.error}", -1);
                }
            }
            return result;
        }
        
        // Check if the server is reachable via ping
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
                Log($"Checking if server {customServerUrl} is reachable...", 0);
                
                // Create a simple ping test process
                var process = new Process();
                process.StartInfo.FileName = "ping";
                process.StartInfo.Arguments = $"{customServerUrl} -n 1 -w 2000"; // 2 second timeout
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                
                // Wait up to 3 seconds for ping to complete
                bool exited = process.WaitForExit(3000);
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    Log($"Ping test timed out for {customServerUrl}", -1);
                    return false;
                }
                
                // Check for success indicators in the output
                bool isReachable = process.ExitCode == 0 || output.Contains("Reply from") || output.Contains("bytes=");
                
                Log($"Server ping test: {(isReachable ? "Reachable" : "Unreachable")}", isReachable ? 1 : -1);
                
                if (!isReachable && debugMode)
                {
                    Log($"Ping output: {output}", 0);
                }
                
                return isReachable;
            }
            catch (Exception ex)
            {
                Log($"Error checking server reachability: {ex.Message}", -1);
                return false;
            }
        }
        #endregion
        
        #region Commands
        
        // Execute a command on the custom server
        public async Task<(bool success, string output, string error)> RunCustomCommandAsync(string command, int timeoutMs = 30000)
        {
            EnsureSettingsLoaded();
            
            // Always use RunSimpleCommand for reliability with key-based auth for now.
            // The persistent session logic might need review if we re-enable it.
            if (debugMode) Log($"Executing remote command using RunSimpleCommand: {command}", 0);
            
            // Note: RunSimpleCommand currently uses a fixed timeout internally (15000ms).
            // We pass timeoutMs here, but it's not directly used by the simplified RunSimpleCommand.
            // Consider adding timeout parameter to RunSimpleCommand if needed later.
            var result = RunSimpleCommand(command);
            
            // Wrap the synchronous result in a completed task to match the async signature.
            return await Task.FromResult(result);
        }
        
        // Run command in the persistent session
        private async Task<(bool success, string output, string error)> RunCommandInSessionAsync(string command, int timeoutMs)
        {
            if (persistentSshProcess == null || persistentSshProcess.HasExited)
            {
                return (false, "", "No active SSH session");
            }
            
            try
            {
                // Create a unique marker to identify the end of command output
                string marker = $"CMD_COMPLETE_{Guid.NewGuid().ToString().Replace("-", "")}_EOC";
                
                // Setup capture for this command
                StringBuilder outputBuilder = new StringBuilder();
                StringBuilder errorBuilder = new StringBuilder();
                bool outputComplete = false;
                
                // Add event handlers for this specific command
                DataReceivedEventHandler outputHandler = null;
                outputHandler = new DataReceivedEventHandler((sender, e) => {
                    if (e.Data == null) return;
                    
                    if (e.Data.Contains(marker))
                    {
                        outputComplete = true;
                        return;
                    }
                    
                    outputBuilder.AppendLine(e.Data);
                    if (debugMode) Log($"CMD: {e.Data}", 0);
                });
                
                persistentSshProcess.OutputDataReceived += outputHandler;
                
                // Send the command followed by the marker
                persistentSshProcess.StandardInput.WriteLine(command);
                persistentSshProcess.StandardInput.WriteLine($"echo \"{marker}\"");
                persistentSshProcess.StandardInput.Flush();
                
                // Wait for command completion or timeout
                DateTime startTime = DateTime.Now;
                while (!outputComplete)
                {
                    if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                    {
                        persistentSshProcess.OutputDataReceived -= outputHandler;
                        return (false, outputBuilder.ToString(), "Command timed out");
                    }
                    
                    await Task.Delay(100);
                }
                
                // Clean up event handler
                persistentSshProcess.OutputDataReceived -= outputHandler;
                
                return (true, outputBuilder.ToString(), errorBuilder.ToString());
            }
            catch (Exception ex)
            {
                Log($"Error running command in SSH session: {ex.Message}", -1);
                return (false, "", ex.Message);
            }
        }
        
        // Helper function to run a SpacetimeDB command
        public async Task<(bool success, string output, string error)> RunSpacetimeDBCommandAsync(string command, int timeoutMs = 30000)
        {
            string spacetimeCmd = $"spacetime {command}";
            return await RunCustomCommandAsync(spacetimeCmd, timeoutMs);
        }
        
        // Check if SpacetimeDB is installed on the remote server
        public async Task<bool> CheckSpacetimeDBInstalled()
        {
            // Construct the expected path - adjust if username changes dynamically
            string expectedPath = $"/home/{sshUserName}/.local/bin/spacetime";
            string command = $"test -f {expectedPath} && echo FOUND || echo NOT_FOUND";
            
            if (debugMode) Log($"Checking for SpacetimeDB at: {expectedPath}", 0);
            
            var result = await RunCustomCommandAsync(command, 5000);
            
            // Check if the output contains the success marker
            bool installed = result.success && result.output.Contains("FOUND");
            
            if (debugMode) UnityEngine.Debug.Log($"Check SpacetimeDB Result: Success={result.success}, Output='{result.output.Trim()}', Error='{result.error.Trim()}', Detected={installed}");
            
            if (installed)
            {
                Log("SpacetimeDB executable found on the remote server.", 1);
            }
            else
            {
                Log($"SpacetimeDB executable NOT found at {expectedPath}. Make sure it's installed correctly and accessible.", -2);
                // Log error output if any
                if (!string.IsNullOrEmpty(result.error))
                {
                    Log($"SSH Error during check: {result.error}", -1);
                }
            }
            
            return installed;
        }
        
        // Check if the SpacetimeDB server is running
        public async Task<bool> CheckServerRunning()
        {
            var result = await RunCustomCommandAsync("ps aux | grep spacetime | grep -v grep", 5000);
            bool running = result.success && result.output.Contains("spacetime");
            
            if (debugMode)
            {
                Log($"SpacetimeDB server status check: {(running ? "Running" : "Not running")}", running ? 1 : 0);
            }
            
            return running;
        }
        
        // Get SpacetimeDB version
        public async Task<string> GetSpacetimeDBVersion()
        {
            // Use the full path to spacetime instead of relying on PATH
            string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";
            
            var result = await RunCustomCommandAsync($"{spacetimePath} --version", 5000);
            
            if (debugMode) Log($"Version command result: Output='{result.output.Trim()}', Error='{result.error.Trim()}', Success={result.success}", 0);
            
            if (result.success && !string.IsNullOrEmpty(result.output))
            {
                string version = result.output.Trim();
                Log($"SpacetimeDB version: {version}", 1);
                return version;
            }
            else
            {
                string errorMsg = string.IsNullOrEmpty(result.error) ? "Empty output returned" : result.error;
                Log($"Failed to get SpacetimeDB version: {errorMsg}", -1);
                return "Unknown";
            }
        }
        
        // Start SpacetimeDB server on the remote machine
        public async Task<bool> StartRemoteServer()
        {
            // Check if server is already running
            if (await CheckServerRunning())
            {
                Log("SpacetimeDB server is already running", 1);
                return true;
            }
            
            // Use the full path to spacetime executable
            string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";
            
            // Start the server
            Log("Starting SpacetimeDB server on remote machine...", 0);
            var result = await RunCustomCommandAsync($"{spacetimePath} server start", 10000);
            
            if (result.success)
            {
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
        public async Task<bool> StopRemoteServer()
        {
            // Check if server is running
            if (!await CheckServerRunning())
            {
                Log("SpacetimeDB server is not running", 0);
                return true;
            }
            
            // Use the full path to spacetime executable
            string spacetimePath = $"/home/{sshUserName}/.local/bin/spacetime";
            
            // Stop the server
            Log("Stopping SpacetimeDB server on remote machine...", 0);
            var result = await RunCustomCommandAsync($"{spacetimePath} server stop", 10000);
            
            if (result.success)
            {
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
        
        // Set session mode (persistent or single command)
        public void SetSessionMode(bool useSession)
        {
            if (useSessionMode != useSession)
            {
                if (useSessionMode && isConnected)
                {
                    // We're switching from session mode to single command mode, so close session
                    StopSession();
                }
                
                useSessionMode = useSession;
                Log($"SSH session mode set to: {(useSessionMode ? "Persistent" : "Single Command")}", 0);
            }
        }

        // Set SSH Private Key Path
        public void SetPrivateKeyPath(string path)
        {
            sshPrivateKeyPath = path;
            EditorPrefs.SetString(PrefsKeyPrefix + "SSHPrivateKeyPath", path);
            if (debugMode) Log($"SSH Private Key Path updated to: {sshPrivateKeyPath}", 0);
        }

        // Check if the SSH session is currently active

        // Only return the cached sessionActive value. Do NOT check status synchronously from UI.
        public bool IsSessionActive()
        {
            return sessionActive;
        }

        // Call this from a background update, not from UI drawing code.
        public void UpdateSessionStatusIfNeeded()
        {
            double currentTime = UnityEditor.EditorApplication.timeSinceStartup;
            if (currentTime - lastSessionCheckTime >= sessionCheckInterval)
            {
                CheckSessionStatus();
                lastSessionCheckTime = currentTime;
            }
        }

        private void CheckSessionStatus()
        {
            try
            {
                // If we don't have connection parameters set, assume not connected
                if (string.IsNullOrEmpty(sshPrivateKeyPath) || string.IsNullOrEmpty(customServerUrl) || string.IsNullOrEmpty(sshUserName))
                {
                    sessionActive = false;
                    return;
                }

                // Simple ping command to check if session is still active
                Process pingProcess = new Process();
                pingProcess.StartInfo.FileName = "ssh";
                pingProcess.StartInfo.Arguments = $"-i \"{sshPrivateKeyPath}\" -o BatchMode=yes -o ConnectTimeout=5 -o StrictHostKeyChecking=no {sshUserName}@{customServerUrl} echo ping";
                pingProcess.StartInfo.UseShellExecute = false;
                pingProcess.StartInfo.RedirectStandardOutput = true;
                pingProcess.StartInfo.RedirectStandardError = true;
                pingProcess.StartInfo.CreateNoWindow = true;
                
                pingProcess.Start();
                bool exited = pingProcess.WaitForExit(5000); // Wait up to 5 seconds
                
                if (exited && pingProcess.ExitCode == 0)
                {
                    string output = pingProcess.StandardOutput.ReadToEnd().Trim();
                    sessionActive = output == "ping";
                    if (debugMode && sessionActive) Log("SSH session is active", 0);
                }
                else
                {
                    sessionActive = false;
                    if (debugMode) Log("SSH session check failed", -1);
                }
            }
            catch (Exception ex)
            {
                sessionActive = false;
                if (debugMode) Log($"SSH session check error: {ex.Message}", -1);
            }
        }
    }
} 