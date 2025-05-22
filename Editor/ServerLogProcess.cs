using UnityEditor;
using System.Diagnostics;
using System;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

// Processes the logs when the server is running in silent mode ///

namespace NorthernRogue.CCCP.Editor {

public class ServerLogProcess
{
    // Constants
    public const string WslCombinedLogPath = "/tmp/spacetime.log";
    
    // Log file processes
    private Process tailProcess;
    private Process databaseLogProcess;
    
    // Log contents
    private string silentServerCombinedLog = "";
    private string databaseLogContent = "";
    private string cachedDatabaseLogContent = ""; // Add cached version of database logs
    
    // Added for performance - accumulated logs between SessionState updates
    private StringBuilder moduleLogAccumulator = new StringBuilder();
    private StringBuilder databaseLogAccumulator = new StringBuilder();
    private DateTime lastSessionStateUpdateTime = DateTime.MinValue;
    private const double sessionStateUpdateInterval = 1.0; // Update SessionState at most once per second
    
    // Session state keys
    private const string SessionKeyCombinedLog = "ServerWindow_SilentCombinedLog";
    private const string SessionKeyDatabaseLog = "ServerWindow_DatabaseLog";
    private const string SessionKeyCachedDatabaseLog = "ServerWindow_CachedDatabaseLog"; // Add session key for cached logs
    private const string SessionKeyDatabaseLogRunning = "ServerWindow_DatabaseLogRunning";
    private const string PrefsKeyPrefix = "CCCP_"; // Same prefix as ServerWindow
    
    // Settings
    public static bool debugMode = false;
    private bool clearModuleLogAtStart = false;
    private bool clearDatabaseLogAtStart = false;
    private string userName = "";
    
    // Logging delegate for output
    private Action<string, int> logCallback;
    
    // Callbacks
    private Action onDatabaseLogUpdated;
    private Action onModuleLogUpdated;
    
    // Timer for tail process check
    private double lastTailCheckTime = 0;
    private const double tailCheckInterval = 10.0;
    
    // Server running info
    private bool serverRunning = false;
    private string moduleName = "";
    private string serverDirectory = "";
    
    // Reference to the CMD processor for executing commands
    private ServerCMDProcess cmdProcessor;

    // Thread-safe queue and processing fields
    private readonly ConcurrentQueue<string> databaseLogQueue = new ConcurrentQueue<string>();
    private readonly object logLock = new object();
    private CancellationTokenSource processingCts;
    private Task processingTask;
    private const int PROCESS_INTERVAL_MS = 100; // Process queued logs every 100ms
    private const int BUFFER_SIZE = 300000; // Equals to around 700kB
    private const int TARGET_SIZE = 50000;
    private volatile bool isProcessing = false;
    
    #region SSH Log Methods for Custom Server
    
    // Path for remote server logs
    public const string CustomServerCombinedLogPath = "/var/log/spacetimedb/spacetimedb.log";
    
    // SSH process variables
    private Process sshTailProcess;
    private Process sshDatabaseLogProcess;
      // SSH details
    private string sshUser = "";
    private string sshHost = "";
    private string sshKeyPath = "";
    private bool isCustomServer = false;
    private string remoteSpacetimePath = "spacetime"; // Default path
    
    // Configure SSH details for custom server log capture
    public void ConfigureSSH(string sshUser, string sshHost, string sshKeyPath, bool isCustomServer)
    {
        this.sshUser = sshUser;
        this.sshHost = sshHost;
        this.sshKeyPath = sshKeyPath;
        this.isCustomServer = isCustomServer;
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Configured SSH: User={sshUser}, Host={sshHost}, KeyPath={sshKeyPath}, IsCustomServer={isCustomServer}");
          // Find spacetime path asynchronously
        EditorApplication.delayCall += async () => 
        {
            try
            {
                remoteSpacetimePath = await FindRemoteSpacetimePathAsync();
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Remote spacetime path set to: {remoteSpacetimePath}");
                
                // Test the connection and spacetime command
                bool testResult = await TestSSHAndSpacetimeAsync();
                if (!testResult)
                {
                    logCallback("Warning: Could not verify SpacetimeDB on the remote server. Log capture may not work correctly.", -2);
                    logCallback("Check that you can SSH to the server and that spacetime is installed and in PATH.", -2);
                }
            }
            catch (Exception ex) 
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error finding remote spacetime path: {ex.Message}");
                remoteSpacetimePath = "spacetime"; // Default fallback
                logCallback("Error establishing SSH connection. Check SSH credentials and server details.", -1);
            }
        };
    }
      // Start SSH-based log tailing for custom server
    public async void StartSSHLogging()
    {
        // Clean up any leftover tail processes on the remote server before starting new ones
        KillRemoteTailProcesses();
        
        // Clear logs if needed
        if (clearModuleLogAtStart)
        {
            ClearSSHModuleLogFile();
        }
        
        if (clearDatabaseLogAtStart)
        {
            ClearSSHDatabaseLog();
        }
        
        // Start SSH-based log tailing processes
        StopSSHTailingLogs(); // Make sure no previous tail process is running
        
        // Ensure we have found the spacetime path
        // If not previously found, try to find it now
        if (remoteSpacetimePath == "spacetime")
        {
            try
            {
                remoteSpacetimePath = await FindRemoteSpacetimePathAsync();
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Found spacetime path before starting logs: {remoteSpacetimePath}");
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error finding spacetime path: {ex.Message}");
            }
        }
          // Small delay to ensure log files are ready
        System.Threading.Thread.Sleep(200);
        
        // Check if the spacetimedb service is running
        bool serviceRunning = false;
        try 
        {
            serviceRunning = await CheckSpacetimeDBServiceStatus();
            if (serviceRunning && debugMode) 
                logCallback("SpacetimeDB service is running, will use journalctl for logs", 1);
            else if (debugMode)
                logCallback("SpacetimeDB service not detected, falling back to log file", 0);
        }
        catch (Exception ex)
        {
            if (debugMode) 
                UnityEngine.Debug.LogError($"[ServerLogProcess] Error checking service status: {ex.Message}");
        }
        
        // Try to use journalctl for service logs first if service is running
        Process serviceLogProcess = serviceRunning ? StartSSHServiceLogProcess() : null;
        
        if (serviceLogProcess != null)
        {
            // Successfully started service log monitoring
            sshTailProcess = serviceLogProcess;
            if (debugMode) logCallback("Using journalctl to monitor spacetimedb.service logs", 1);
        }
        else 
        {
            // Fall back to log file tailing if journalctl didn't work
            if (debugMode) logCallback("Falling back to log file tailing", 0);
            
            // Start module log tailing
            sshTailProcess = StartSSHTailingLogFile(CustomServerCombinedLogPath, (line) => {
                const int maxLogLength = 75000;
                const int trimToLength = 50000;
                if (silentServerCombinedLog.Length > maxLogLength)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory silent SSH log from {silentServerCombinedLog.Length} chars.");
                    silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncated SSH log length: {silentServerCombinedLog.Length}");
                }
                
                string formattedLine = FormatServerLogLine(line);
                moduleLogAccumulator.Append(formattedLine).Append("\n");
                
                // Update SessionState periodically
                UpdateSessionStateIfNeeded();
                
                // Notify of log update
                onModuleLogUpdated?.Invoke();
            });
        }
          // Start database log tailing
        StartSSHDatabaseLogProcess();
        
        // Service log monitoring was already attempted earlier
        // No need to call StartSSHServiceLogProcess() again since sshTailProcess is already set
    }
    
    // Clear the module log file on the remote server
    public void ClearSSHModuleLogFile()
    {
        if (debugMode) logCallback("Clearing SSH log file...", 0);
        
        try
        {
            // Create a process to clear the log file via SSH
            Process clearProcess = new Process();
            clearProcess.StartInfo.FileName = "ssh";
            clearProcess.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"sudo truncate -s 0 {CustomServerCombinedLogPath}\"";
            clearProcess.StartInfo.UseShellExecute = false;
            clearProcess.StartInfo.CreateNoWindow = true;
            clearProcess.StartInfo.RedirectStandardOutput = true;
            clearProcess.StartInfo.RedirectStandardError = true;
            
            clearProcess.Start();
            clearProcess.WaitForExit(5000); // Wait up to 5 seconds
            
            // Also clear the in-memory log
            silentServerCombinedLog = "";
            SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
            
            if (debugMode) logCallback("SSH log file cleared successfully", 1);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error clearing SSH log file: {ex.Message}", -1);
        }
    }
    
    // Clear the database log for SSH
    public void ClearSSHDatabaseLog()
    {
        if (debugMode) logCallback("Clearing SSH database log...", 0);
        
        databaseLogContent = "";
        cachedDatabaseLogContent = "";
        SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
        SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
        
        if (debugMode) logCallback("SSH database log cleared successfully", 1);
    }
    
    // Start tailing a log file via SSH
    private Process StartSSHTailingLogFile(string remotePath, Action<string> onNewLine)
    {
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath))
        {
            if (debugMode) logCallback("SSH connection details incomplete", -1);
            return null;
        }
        
        if (debugMode) logCallback($"Starting SSH tail for {remotePath}", 0);
        
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "ssh";
            // Use sudo to access system logs and tail -F for robustness
            string tailCommand = $"sudo tail -F -n +1 {remotePath}";
            process.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{tailCommand}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.EnableRaisingEvents = true;
            
            process.OutputDataReceived += (sender, args) => {
                if (args.Data != null)
                {
                    if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] SSH Tail Raw Output");
                    EditorApplication.delayCall += () => {
                        try {
                            // Pass data to callback
                            onNewLine(args.Data);
                        }
                        catch (Exception ex) {
                            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Exception in SSH tail output handler: {ex.Message}");
                        }
                    };
                }
            };
            
            process.ErrorDataReceived += (sender, args) => {
                if (args.Data != null)
                {
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] SSH Tail Error: {args.Data}");
                    EditorApplication.delayCall += () => {
                        try {
                            // Format as error and pass to callback
                            string formattedLine = FormatServerLogLine(args.Data, true);
                            onNewLine(formattedLine);
                        }
                        catch (Exception ex) {
                            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Exception in SSH tail error handler: {ex.Message}");
                        }
                    };
                }
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            if (debugMode) logCallback("Started SSH log tailing process", 1);
            return process;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error starting SSH log tailing: {ex.Message}", -1);
            return null;
        }
    }
      // Stop all SSH tailing processes
    private void StopSSHTailingLogs()
    {
        if (sshTailProcess != null)
        {
            if (debugMode) logCallback("Stopping SSH tail process", 0);
            
            try
            {
                if (!sshTailProcess.HasExited)
                {
                    sshTailProcess.Kill();
                    sshTailProcess.WaitForExit(1000); // Give it time to exit properly
                    sshTailProcess.Close();
                }
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error stopping SSH tail process: {ex.Message}");
            }
            
            sshTailProcess = null;
        }
        
        // Also kill any leftover remote tail processes to prevent accumulation
        KillRemoteTailProcesses();
        
        if (debugMode) logCallback("SSH tail process stopped", 0);
    }
    
    // Start process to monitor database logs via spacetime logs
    private void StartSSHDatabaseLogProcess()
    {
        try
        {
            if (debugMode) logCallback("Starting SSH database logs process...", 0);
            
            // Mark state in SessionState
            SessionState.SetBool(SessionKeyDatabaseLogRunning, true);
            
            // Check for required parameters
            if (string.IsNullOrEmpty(moduleName))
            {
                if (debugMode) logCallback($"Module name is not set, cannot start SSH database log", -1);
                return;
            }

            // Create a new process to run the spacetime logs command via SSH
            sshDatabaseLogProcess = new Process();
            sshDatabaseLogProcess.StartInfo.FileName = "ssh";            
            
            // Build the command to run spacetime logs with the module name using discovered path
            // Wrap in a login shell to ensure proper environment is loaded
            string logCommand = $"bash -l -c '{remoteSpacetimePath} logs {moduleName} -f'";
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Running remote command: {logCommand}");
            sshDatabaseLogProcess.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{logCommand}\"";
            sshDatabaseLogProcess.StartInfo.UseShellExecute = false;
            sshDatabaseLogProcess.StartInfo.CreateNoWindow = true;
            sshDatabaseLogProcess.StartInfo.RedirectStandardOutput = true;
            sshDatabaseLogProcess.StartInfo.RedirectStandardError = true;
            sshDatabaseLogProcess.EnableRaisingEvents = true;
            
            // Clear existing logs when starting new process
            databaseLogContent = "";
            cachedDatabaseLogContent = "";
            SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
            SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
            
            sshDatabaseLogProcess.OutputDataReceived += (sender, args) => {
                if (args.Data != null)
                {
                    if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] SSH Database Log Raw Output");
                    
                    // Format the line immediately
                    string formattedLine = FormatDatabaseLogLine(args.Data);
                    
                    // Update logs directly instead of using queue
                    lock (logLock)
                    {
                        databaseLogContent += formattedLine + "\n";
                        cachedDatabaseLogContent += formattedLine + "\n";
                        
                        // Check if we need to truncate
                        if (databaseLogContent.Length > BUFFER_SIZE)
                        {
                            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating database log from {databaseLogContent.Length} chars.");
                            string truncatedContent = "[... Log Truncated ...]\n" + 
                                databaseLogContent.Substring(databaseLogContent.Length - TARGET_SIZE);
                            databaseLogContent = truncatedContent;
                            cachedDatabaseLogContent = truncatedContent;
                        }
                    }
                    
                    // Update SessionState and notify UI
                    EditorApplication.delayCall += () => {
                        SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
                        SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
                        onDatabaseLogUpdated?.Invoke();
                    };
                }
            };
            
            sshDatabaseLogProcess.ErrorDataReceived += (sender, args) => {
                if (args.Data != null)
                {                    
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] SSH Database Log Error: {args.Data}");
                    
                    // Special handling for common error messages
                    string errorMessage = args.Data;
                    if (errorMessage.Contains("command not found"))
                    {
                        logCallback($"ERROR: spacetime command not found on the remote server. Check that SpacetimeDB is installed and in PATH.", -1);
                        errorMessage = "spacetime command not found on remote server. Check installation.";
                    }
                    else if (errorMessage.Contains("Permission denied"))
                    {
                        logCallback($"ERROR: Permission denied accessing spacetime on remote server.", -1);
                    }
                    
                    // Format and add error message directly
                    string formattedLine = FormatDatabaseLogLine($"ERROR: {errorMessage}", true);
                    
                    lock (logLock)
                    {
                        databaseLogContent += formattedLine + "\n";
                        cachedDatabaseLogContent += formattedLine + "\n";
                    }
                    
                    // Update SessionState and notify UI
                    EditorApplication.delayCall += () => {
                        SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
                        SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
                        onDatabaseLogUpdated?.Invoke();
                    };
                }
            };
            
            sshDatabaseLogProcess.Start();
            sshDatabaseLogProcess.BeginOutputReadLine();
            sshDatabaseLogProcess.BeginErrorReadLine();
            
            if (debugMode) logCallback("Started SSH database log process", 1);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error starting SSH database log process: {ex.Message}", -1);
            SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
        }
    }
    
    // Start process to monitor spacetimedb service logs via journalctl
    private Process StartSSHServiceLogProcess()
    {
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath))
        {
            if (debugMode) logCallback("SSH connection details incomplete", -1);
            return null;
        }
        
        if (debugMode) logCallback("Starting SSH service log monitoring via journalctl", 0);
        
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "ssh";            // Use journalctl to get logs since the last service start, then follow new logs
            // The -f flag follows the log in real-time
            // The -u flag specifies the service unit name
            // The --no-pager flag prevents pagination
            // Use short-iso-precise format for more robust timestamp parsing
            // Use -n 100 to limit initial output to last 100 lines (since we're filtering for session start)
            string journalCommand = "sudo journalctl -f -u spacetimedb.service --no-pager -o short-iso-precise -n 100";
            
            process.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{journalCommand}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.EnableRaisingEvents = true;
            
            // For tracking the current session
            bool foundSessionStart = false;
            string sessionStartMarker = "Started spacetimedb.service";
            
            process.OutputDataReceived += (sender, args) => {
                if (args.Data != null)
                {
                    if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] SSH Service Log Raw Output: " + args.Data);
                    EditorApplication.delayCall += () => {
                        try {
                            string line = args.Data.Trim();
                            
                            // If we have a new session start marker, clear previous log and start fresh
                            if (line.Contains(sessionStartMarker))
                            {
                                if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] New spacetimedb service session detected, clearing previous logs");
                                
                                // Clear the existing log when we find a session start marker
                                moduleLogAccumulator.Clear();
                                silentServerCombinedLog = "";
                                foundSessionStart = true;
                            }
                            
                            // If we're still waiting to find the session start marker, skip this line
                            if (!foundSessionStart && !line.Contains(sessionStartMarker))
                            {
                                // Skip lines until we find a session start message
                                return;
                            }
                            
                            if (!string.IsNullOrEmpty(line))
                            {
                                // Format the log line with the timestamp and message
                                string formattedLine = FormatServerLogLine(line);
                                
                                // Update both the accumulator and the combined log
                                moduleLogAccumulator.Append(formattedLine).Append("\n");
                                silentServerCombinedLog += formattedLine + "\n";
                                
                                // Manage log size
                                const int maxLogLength = 75000;
                                const int trimToLength = 50000;
                                if (silentServerCombinedLog.Length > maxLogLength)
                                {
                                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory silent log from {silentServerCombinedLog.Length} chars.");
                                    silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                                }
                                
                                // Update SessionState periodically
                                UpdateSessionStateIfNeeded();
                                
                                // Notify of log update
                                onModuleLogUpdated?.Invoke();
                            }
                        }
                        catch (Exception ex) {
                            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Exception in SSH service log output handler: {ex.Message}");
                        }
                    };
                }
            };
            
            process.ErrorDataReceived += (sender, args) => {
                if (args.Data != null)
                {                    
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] SSH Service Log Error: {args.Data}");
                    
                    // Special handling for common service log errors
                    string errorMessage = args.Data;
                    if (errorMessage.Contains("No entries"))
                    {
                        if (debugMode) logCallback("No service log entries found. Service may be newly started.", 0);
                    }
                    else if (errorMessage.Contains("Failed to") || errorMessage.Contains("error"))
                    {
                        logCallback($"Service log error: {errorMessage}", -1);
                    }
                    
                    EditorApplication.delayCall += () => {
                        try {
                            // Only process errors if we've already found the session start or if this is about session start
                            if (foundSessionStart || errorMessage.Contains(sessionStartMarker))
                            {
                                // Format as error and pass to module log
                                string formattedLine = FormatServerLogLine($"[SERVICE ERROR] {errorMessage}", true);
                                
                                // Update both the accumulator and the combined log
                                moduleLogAccumulator.Append(formattedLine).Append("\n");
                                silentServerCombinedLog += formattedLine + "\n";
                                
                                // Update SessionState periodically
                                UpdateSessionStateIfNeeded();
                                
                                // Notify of log update
                                onModuleLogUpdated?.Invoke();
                            }
                        }
                        catch (Exception ex) {
                            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Exception in SSH service log error handler: {ex.Message}");
                        }
                    };
                }
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            if (debugMode) logCallback("Started SSH service log monitoring process", 1);
            return process;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error starting SSH service log monitoring: {ex.Message}", -1);
            return null;
        }
    }
      // Stop SSH database log process
    private void StopSSHDatabaseLogProcess()
    {
        if (sshDatabaseLogProcess != null)
        {
            if (debugMode) logCallback("Stopping SSH database log process", 0);
            
            try
            {
                if (!sshDatabaseLogProcess.HasExited)
                {
                    sshDatabaseLogProcess.Kill();
                    sshDatabaseLogProcess.WaitForExit(1000); // Give it time to exit properly
                    sshDatabaseLogProcess.Close();
                }
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error stopping SSH database log process: {ex.Message}");
            }
            
            sshDatabaseLogProcess = null;
            SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
            
            // Also try to kill any leftover database log processes on the remote server
            try
            {
                if (!string.IsNullOrEmpty(sshUser) && !string.IsNullOrEmpty(sshHost) && !string.IsNullOrEmpty(sshKeyPath) && !string.IsNullOrEmpty(moduleName))
                {
                    if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Cleaning up remote database log processes");
                    
                    Process cleanupProcess = new Process();
                    cleanupProcess.StartInfo.FileName = "ssh";
                    // Find and kill all spacetime logs processes for this module
                    string killCommand = $"sudo pkill -f '{remoteSpacetimePath} logs {moduleName}'";
                    cleanupProcess.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{killCommand}\"";
                    cleanupProcess.StartInfo.UseShellExecute = false;
                    cleanupProcess.StartInfo.CreateNoWindow = true;
                    
                    cleanupProcess.Start();
                    cleanupProcess.WaitForExit(2000); // Wait up to 2 seconds
                }
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error cleaning up remote database log processes: {ex.Message}");
            }
            
            if (debugMode) logCallback("SSH database log process stopped", 0);
        }
    }
    
    // Method to check SSH log processes and restart if needed
    public void CheckSSHLogProcesses(double currentTime)
    {
        if (currentTime - lastTailCheckTime > tailCheckInterval)
        {
            lastTailCheckTime = currentTime;
            
            if (serverRunning && isCustomServer)
            {
                CheckSSHTailProcess();
                CheckSSHDatabaseLogProcess();
            }
        }
    }
    
    private void CheckSSHTailProcess()
    {
        if (sshTailProcess == null && serverRunning && isCustomServer)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] SSH tail process needs restart");
            AttemptSSHTailRestartAfterReload();
        }
        else if (sshTailProcess != null && (sshTailProcess.HasExited || !serverRunning || !isCustomServer))
        {
            if (!serverRunning || !isCustomServer)
            {
                StopSSHTailingLogs();
            }
            else if (sshTailProcess.HasExited)
            {
                AttemptSSHTailRestartAfterReload();
            }
        }
    }
    
    private void CheckSSHDatabaseLogProcess()
    {
        if (sshDatabaseLogProcess == null && serverRunning && isCustomServer)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] SSH database log process needs restart");
            AttemptSSHDatabaseLogRestartAfterReload();
        }
        else if (sshDatabaseLogProcess != null && (sshDatabaseLogProcess.HasExited || !serverRunning || !isCustomServer))
        {
            if (!serverRunning || !isCustomServer)
            {
                StopSSHDatabaseLogProcess();
            }
            else if (sshDatabaseLogProcess.HasExited)
            {
                AttemptSSHDatabaseLogRestartAfterReload();
            }
        }
    }
      // Attempt to restart SSH tail process after domain reload
    public void AttemptSSHTailRestartAfterReload()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Attempting to restart SSH tail process");
        
        if (serverRunning && isCustomServer && sshTailProcess == null)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Starting SSH tail process after reload");
            
            // Clean up any leftover processes first
            KillRemoteTailProcesses();
            
            string remotePath = CustomServerCombinedLogPath;
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using remote path: {remotePath}");
            
            sshTailProcess = StartSSHTailingLogFile(remotePath, (line) => {
                // Logic is same as StartSSHLogging
                const int maxLogLength = 75000;
                const int trimToLength = 50000;
                if (silentServerCombinedLog.Length > maxLogLength)
                {
                    silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                }
                
                string formattedLine = FormatServerLogLine(line);
                moduleLogAccumulator.Append(formattedLine).Append("\n");
                
                // Update SessionState periodically
                UpdateSessionStateIfNeeded();
                
                // Notify of log update
                onModuleLogUpdated?.Invoke();
            });
            
            if (sshTailProcess != null)
            {
                if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] SSH tail process restarted successfully");
            }
            else
            {
                if (debugMode) UnityEngine.Debug.LogError("[ServerLogProcess] SSH tail restart FAILED. StartSSHTailingLogFile returned NULL.");
            }
        }
        else
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Not restarting SSH tail. ServerRunning: {serverRunning}, IsCustomServer: {isCustomServer}, TailProcess: {(sshTailProcess != null ? "exists" : "null")}");
        }
    }
      // Attempt to restart SSH database log process after domain reload
    public void AttemptSSHDatabaseLogRestartAfterReload()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Attempting to restart SSH database log process");
        
        if (serverRunning && isCustomServer && (sshDatabaseLogProcess == null || sshDatabaseLogProcess.HasExited))
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Starting SSH database log process after reload");
            
            // First kill any leftover processes
            if (!string.IsNullOrEmpty(sshUser) && !string.IsNullOrEmpty(sshHost) && !string.IsNullOrEmpty(sshKeyPath) && !string.IsNullOrEmpty(moduleName))
            {
                try
                {
                    Process cleanupProcess = new Process();
                    cleanupProcess.StartInfo.FileName = "ssh";
                    string killCommand = $"sudo pkill -f '{remoteSpacetimePath} logs {moduleName}'";
                    cleanupProcess.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{killCommand}\"";
                    cleanupProcess.StartInfo.UseShellExecute = false;
                    cleanupProcess.StartInfo.CreateNoWindow = true;
                    
                    cleanupProcess.Start();
                    cleanupProcess.WaitForExit(2000);
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error cleaning up before restart: {ex.Message}");
                }
            }
            
            StartSSHDatabaseLogProcess();
        }
    }
    
    // Stop all SSH logging processes
    public void StopSSHLogging()
    {
        StopSSHTailingLogs();
        StopSSHDatabaseLogProcess();
        // Clean up any leftover tail processes on the remote server
        KillRemoteTailProcesses();
    }
    
    // Kill all leftover tail processes on the remote server
    private void KillRemoteTailProcesses()
    {
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath))
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] SSH connection details incomplete, cannot kill remote processes");
            return;
        }
        
        try
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Cleaning up remote tail processes");
            
            // Create a process to kill all tail processes on the remote server
            Process cleanupProcess = new Process();
            cleanupProcess.StartInfo.FileName = "ssh";
            // Find and kill all tail processes related to our log file
            string killCommand = $"sudo pkill -f 'tail -F -n \\+1 {CustomServerCombinedLogPath}'";
            cleanupProcess.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{killCommand}\"";
            cleanupProcess.StartInfo.UseShellExecute = false;
            cleanupProcess.StartInfo.CreateNoWindow = true;
            cleanupProcess.StartInfo.RedirectStandardOutput = true;
            cleanupProcess.StartInfo.RedirectStandardError = true;
            
            cleanupProcess.Start();
            cleanupProcess.WaitForExit(3000); // Wait up to 3 seconds
            
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Remote tail processes cleanup completed");
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error cleaning up remote tail processes: {ex.Message}");
        }
    }
    
    #endregion
    
    public ServerLogProcess(
        Action<string, int> logCallback, 
        Action onModuleLogUpdated,
        Action onDatabaseLogUpdated,
        ServerCMDProcess cmdProcessor = null,
        bool debugMode = false)
    {
        this.logCallback = logCallback;
        this.onModuleLogUpdated = onModuleLogUpdated;
        this.onDatabaseLogUpdated = onDatabaseLogUpdated;
        this.cmdProcessor = cmdProcessor;
        ServerLogProcess.debugMode = debugMode;
        
        // Load username from EditorPrefs
        this.userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Initialized with username from EditorPrefs: {this.userName}");
        
        // Load log content from session state
        silentServerCombinedLog = SessionState.GetString(SessionKeyCombinedLog, "");
        databaseLogContent = SessionState.GetString(SessionKeyDatabaseLog, "");
        cachedDatabaseLogContent = SessionState.GetString(SessionKeyCachedDatabaseLog, ""); // Load cached logs

        processingCts = new CancellationTokenSource();
        StartLogLimiter();
    }
    
    public void Configure(string moduleName, string serverDirectory, bool clearModuleLogAtStart, bool clearDatabaseLogAtStart, string userName)
    {
        this.moduleName = moduleName;
        this.serverDirectory = serverDirectory;
        this.clearModuleLogAtStart = clearModuleLogAtStart;
        this.clearDatabaseLogAtStart = clearDatabaseLogAtStart;
        this.userName = userName;
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Configured with username: {this.userName}");
    }
    
    public void SetServerRunningState(bool isRunning)
    {
        serverRunning = isRunning;
    }
    
    #region Log Methods
    
    public void ClearModuleLogFile()
    {
        if (debugMode) logCallback("Clearing log file...", 0);
        string logPath = WslCombinedLogPath;
        string clearLogCommand = $"truncate -s 0 {logPath}";
        
        if (cmdProcessor != null)
        {
            cmdProcessor.RunWslCommandSilent(clearLogCommand);
        }
        
        // Also clear the in-memory log
        silentServerCombinedLog = "";
        SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
        
        // Notify that the log has been cleared
        if (debugMode) logCallback("Log file cleared successfully", 1);
    }
    
    public void ClearDatabaseLog()
    {
        if (debugMode) logCallback("Clearing database log...", 0);
        
        // Clear both the in-memory database log content and the cache
        databaseLogContent = "";
        cachedDatabaseLogContent = "";
        SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
        SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
        
        // Notify that the log has been cleared
        if (debugMode) logCallback("Database log cleared successfully", 1);
    }
    
    public string GetModuleLogContent()
    {
        return silentServerCombinedLog;
    }
    
    public string GetDatabaseLogContent()
    {
        // Return cached logs if server is not running, otherwise return current logs
        return serverRunning ? databaseLogContent : cachedDatabaseLogContent;
    }
    
    public void StartLogging()
    {
        // Clear logs if needed
        if (clearModuleLogAtStart)
        {
            ClearModuleLogFile();
        }
        
        if (clearDatabaseLogAtStart)
        {
            ClearDatabaseLog();
        }
        
        // Start tailing the log file
        StopTailingLogs(); // Make sure no previous tail process is running
        string logPath = WslCombinedLogPath;
        
        // Give a tiny delay for the file to potentially be created/truncated
        System.Threading.Thread.Sleep(200);
        
        tailProcess = StartTailingLogFile(logPath, (line) => {
            const int maxLogLength = 75000;
            const int trimToLength = 50000;
            if (silentServerCombinedLog.Length > maxLogLength)
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory silent log from {silentServerCombinedLog.Length} chars.");
                silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncated log length: {silentServerCombinedLog.Length}");
            }
            
            SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
            
            if (onModuleLogUpdated != null)
            {
                onModuleLogUpdated();
            }
        });
        
        // Start the database log process
        StartDatabaseLogProcess();
    }
    
    public void StopLogging()
    {
        processingCts?.Cancel();
        try
        {
            processingTask?.Wait(1000);
        }
        catch (Exception) { }
        
        processingCts = new CancellationTokenSource();
        StopTailingLogs();
        StopDatabaseLogProcess();
    }
    
    public void CheckLogProcesses(double currentTime)
    {
        // Check processes periodically
        if (currentTime - lastTailCheckTime > tailCheckInterval)
        {
            lastTailCheckTime = currentTime;
            
            // Check tail process
            CheckTailProcess();
            
            // Check database log process
            CheckDatabaseLogProcess();
        }
    }
    
    private void CheckTailProcess()
    {
        if (tailProcess == null && serverRunning)
        {
            if (debugMode) UnityEngine.Debug.LogWarning("[ServerLogProcess] Tail process is NULL while server is supposed to be running!");
            AttemptTailRestartAfterReload();
        }
        else if (tailProcess != null)
        {
            try
            {
                if (tailProcess.HasExited)
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Tail process HAS EXITED (Code: {tailProcess.ExitCode}) while server running!");
                    if (serverRunning)
                    {
                        AttemptTailRestartAfterReload();
                    }
                }
            }
            catch (InvalidOperationException)
            {
                if (debugMode) UnityEngine.Debug.LogWarning("[ServerLogProcess] Tail process check failed (InvalidOperationException - likely killed or inaccessible).");
                tailProcess = null;
                if (serverRunning)
                {
                    AttemptTailRestartAfterReload();
                }
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error checking tail process: {ex.Message}");
            }
        }
    }
    
    private void CheckDatabaseLogProcess()
    {
        if (databaseLogProcess == null && serverRunning)
        {
            if (debugMode) logCallback("Database log process not running, attempting to restart...", 0);
            StartDatabaseLogProcess();
        }
        else if (databaseLogProcess != null)
        {
            try
            {
                if (databaseLogProcess.HasExited)
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Database log process HAS EXITED (Code: {databaseLogProcess.ExitCode}) while server running!");
                    if (serverRunning)
                    {
                        StartDatabaseLogProcess();
                    }
                }
            }
            catch (InvalidOperationException)
            {
                if (debugMode) UnityEngine.Debug.LogWarning("[ServerLogProcess] Database log process check failed (InvalidOperationException - likely killed or inaccessible).");
                databaseLogProcess = null;
                if (serverRunning)
                {
                    StartDatabaseLogProcess();
                }
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error checking database log process: {ex.Message}");
            }
        }
    }
    
    #endregion
    
    #region Tail Processes
    
    private void StopTailingLogs()
    {
        if (tailProcess != null)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Attempting to stop tail process (PID: {(tailProcess.Id)}). HasExited={tailProcess.HasExited}");
            if (!tailProcess.HasExited)
            {
                try
                {
                    if (debugMode) logCallback("Stopping tail process...", 0);
                    tailProcess.Kill();
                    if (debugMode) logCallback("Tail process stopped.", 0);
                    tailProcess.WaitForExit(500); // Give it a moment to exit cleanly
                }
                catch (InvalidOperationException ioEx)
                {
                    // Process may have already exited between the check and Kill()
                    logCallback($"Info stopping tail process: {ioEx.Message}", 0);
                }
                catch (Exception ex)
                {
                    logCallback($"Error stopping tail process: {ex.Message}", -1);
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error: {ex}");
                }
            }
            tailProcess = null; // Ensure reference is cleared
        }
        else
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] tailProcess was already null.");
        }
    }
    
    private Process StartTailingLogFile(string wslLogPath, Action<string> onNewLine)
    {
        if (debugMode) logCallback($"Attempting to start tailing {wslLogPath}...", 0);
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "wsl.exe";
            // Use tail -F for robustness, start from beginning (-n +1)
            string tailCommand = $"touch {wslLogPath} && tail -F -n +1 {wslLogPath}";
            process.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -l -c \"{tailCommand}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.EnableRaisingEvents = true;

            process.OutputDataReceived += (sender, args) => {
                if (args.Data != null)
                {
                    if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Tail Raw Output");
                    EditorApplication.delayCall += () => {
                        try {
                            // Format original timestamp if present, otherwise add one
                            string formattedLine = FormatServerLogLine(args.Data);
                            
                            // Update in-memory log immediately
                            silentServerCombinedLog += formattedLine + "\n";
                            
                            // Add to accumulator
                            moduleLogAccumulator.AppendLine(formattedLine);
                            
                            // Manage log size immediately
                            const int maxLogLength = 75000;
                            const int trimToLength = 50000;
                            if (silentServerCombinedLog.Length > maxLogLength)
                            {
                                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory silent log from {silentServerCombinedLog.Length} chars.");
                                silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                            }
                            
                            // Update SessionState less frequently
                            UpdateSessionStateIfNeeded();
                            
                            // Call callback for immediate UI update regardless of SessionState update
                            onNewLine(formattedLine);
                            
                            // Notify subscribers
                            if (onModuleLogUpdated != null)
                            {
                                onModuleLogUpdated();
                            }
                        }
                        catch (Exception ex) {
                            if (debugMode) UnityEngine.Debug.LogError($"[Tail Output Handler Error]: {ex}");
                        }
                    };
                }
            };
            
            process.ErrorDataReceived += (sender, args) => {
                if (args.Data != null) {
                    if (debugMode) UnityEngine.Debug.LogWarning("[ServerLogProcess] Tail Raw Error");
                    EditorApplication.delayCall += () => {
                        try {
                            // Format error lines with timestamp
                            string formattedLine = FormatServerLogLine(args.Data, true);
                            
                            // Special handling for "file truncated" messages - these are normal when log is cleared
                            if (args.Data.Contains("file truncated"))
                            {
                                if (debugMode) logCallback($"{formattedLine} - This is normal after clearing logs", 0);
                                // Don't add to main log to avoid confusion
                            }
                            else
                            {
                                // Other errors should still be logged as errors
                                logCallback($"{formattedLine}", -1); // Log tail errors
                                onNewLine($"{formattedLine} [TAIL ERROR]"); // Also add to main log
                                if (onModuleLogUpdated != null)
                                {
                                    onModuleLogUpdated();
                                }
                            }
                        }
                        catch (Exception ex) {
                            if (debugMode) UnityEngine.Debug.LogError($"[Tail Error Handler Error]: {ex}");
                        }
                    };
                }
            };
            
            process.Exited += (sender, e) => {
                EditorApplication.delayCall += () => {
                    try {
                        int exitCode = -1;
                        try { exitCode = process.ExitCode; } catch {}
                        
                        if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Tailing process exited unexpectedly (Code: {exitCode}). Attempting restart: {serverRunning}");
                        
                        // Auto-restart if server is supposed to be running
                        if (serverRunning)
                        {
                            if (debugMode) logCallback("Attempting to restart tail process...", 0);
                            System.Threading.Thread.Sleep(1000); 
                            var newTailProcess = StartTailingLogFile(wslLogPath, onNewLine);
                            
                            if (newTailProcess != null)
                            {
                                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Restart successful. New tail PID: {newTailProcess.Id}");
                            }
                            else
                            {
                                if (debugMode) UnityEngine.Debug.LogError("[ServerLogProcess] Restart FAILED. StartTailingLogFile returned NULL.");
                            }
                            tailProcess = newTailProcess;
                        }
                    }
                    catch (Exception ex) {
                        if (debugMode) UnityEngine.Debug.LogError($"[Tail Exited Handler Error]: {ex}");
                    }
                };
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (debugMode) logCallback($"Tailing process started (PID: {process.Id}).", 0);
            return process;
        }
        catch (Exception ex)
        {
            logCallback($"Error starting tail for {wslLogPath}: {ex.Message}", -1);
            return null;
        }
    }
    
    public void AttemptTailRestartAfterReload()
    {
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Attempting tail restart. Current state: serverRunning={serverRunning}, tailProcessIsNull={tailProcess == null}");

        if (serverRunning && tailProcess == null)
        {
            if (debugMode) logCallback("Domain reload detected. Attempting to re-attach tail process...", 0);
            string logPath = WslCombinedLogPath;
            
            if (debugMode) logCallback($"Re-starting tail for {logPath}...", 0);
            tailProcess = StartTailingLogFile(logPath, (line) => {
                silentServerCombinedLog += line + "\n";
                const int maxLogLength = 75000;
                const int trimToLength = 50000;
                if (silentServerCombinedLog.Length > maxLogLength)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess Tail Reload] Truncating in-memory silent log from {silentServerCombinedLog.Length} chars.");
                    silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess Tail Reload] Truncated log length: {silentServerCombinedLog.Length}");
                }
                
                SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
                
                if (onModuleLogUpdated != null)
                {
                    onModuleLogUpdated();
                }
            });

            if (tailProcess != null)
            {
                if (debugMode) logCallback($"Successfully re-attached tail process (PID: {tailProcess.Id}).", 1);
            }
            else
            {
                logCallback("Failed to re-attach tail process.", -1);
            }
        }
        else
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] AttemptTailRestartAfterReload called but conditions not met (serverRunning={serverRunning}, tailProcessIsNull={tailProcess == null})");
        }
    }
      // Helper method to format server log lines with consistent timestamps
    private string FormatServerLogLine(string logLine, bool isError = false)
    {
        if (string.IsNullOrEmpty(logLine))
            return logLine;

        string timestampPrefix = "";
        string messageContent = logLine;
        bool existingPrefixFound = false;

        // Check if line already starts with a [YYYY-MM-DD HH:MM:SS] timestamp
        var prefixMatch = System.Text.RegularExpressions.Regex.Match(logLine, @"^(\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\])(.*)");
        if (prefixMatch.Success)
        {
            timestampPrefix = prefixMatch.Groups[1].Value;
            messageContent = prefixMatch.Groups[2].Value.TrimStart();
            existingPrefixFound = true;
        }

        // Clean messageContent of any ISO-style timestamps regardless of where they appear
        // Handle pattern "2025-05-21T20:51:40.352732Z"
        messageContent = System.Text.RegularExpressions.Regex.Replace(
            messageContent,
            @"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z\b",
            "");
            
        // Handle pattern "2025-05-21T20:22:27.029473+00:00"
        messageContent = System.Text.RegularExpressions.Regex.Replace(
            messageContent,
            @"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\+|-)\d{2}:\d{2}\b",
            "");
            
        // Trim any resulting extra spaces
        messageContent = messageContent.Trim();

        // If we still don't have a timestamp prefix, try to find one in the original line
        if (!existingPrefixFound)
        {
            // Try journalctl format: "2025-05-21T20:22:27.029473+00:00 LoreMagic systemd[1]:"
            var journalMatch = System.Text.RegularExpressions.Regex.Match(
                logLine, 
                @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\+|-)\d{2}:\d{2})\s+(\S+)\s+(\S+)\[\d+\]:\s+(.*)");
            
            if (journalMatch.Success)
            {
                string originalTimestamp = journalMatch.Groups[1].Value;
                messageContent = journalMatch.Groups[4].Value;
                
                if (DateTimeOffset.TryParse(originalTimestamp, out DateTimeOffset dateTime))
                {
                    timestampPrefix = $"[{dateTime.ToString("yyyy-MM-dd HH:mm:ss")}]";
                }
            }
            else
            {
                // Try alternative format: "2025-05-01T20:29:22.528775Z DEBUG ..."
                var isoMatch = System.Text.RegularExpressions.Regex.Match(
                    logLine, 
                    @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z)\s+(\w+)\s+(.*)");
                
                if (isoMatch.Success)
                {
                    string originalTimestamp = isoMatch.Groups[1].Value;
                    string logLevel = isoMatch.Groups[2].Value;
                    string content = isoMatch.Groups[3].Value;
                    
                    if (DateTimeOffset.TryParse(originalTimestamp, out DateTimeOffset dateTime))
                    {
                        timestampPrefix = $"[{dateTime.ToString("yyyy-MM-dd HH:mm:ss")}]";
                        messageContent = $"{logLevel} {content}";
                    }
                }
                else
                {
                    // Try to find any ISO timestamp in the line
                    var anyIsoMatch = System.Text.RegularExpressions.Regex.Match(
                        logLine, 
                        @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|(?:\+|-)\d{2}:\d{2}))");
                    
                    if (anyIsoMatch.Success)
                    {
                        string originalTimestamp = anyIsoMatch.Groups[1].Value;
                        
                        if (DateTimeOffset.TryParse(originalTimestamp, out DateTimeOffset dateTime))
                        {
                            timestampPrefix = $"[{dateTime.ToString("yyyy-MM-dd HH:mm:ss")}]";
                            // Replace the timestamp in the original message
                            messageContent = logLine.Replace(originalTimestamp, "").Trim();
                        }
                    }
                }
            }
        }

        // If no timestamp could be determined, use current time as fallback
        if (string.IsNullOrEmpty(timestampPrefix))
        {
            timestampPrefix = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
        }

        string errorMarker = isError ? " [ERROR]" : "";
        return $"{timestampPrefix}{errorMarker} {messageContent}".TrimEnd();
    }
    
    public void StopTailProcessExplicitly()
    {
        logCallback("Editor quitting. Stopping tail process...", 0);
        if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] StopTailProcessExplicitly called.");
        StopTailingLogs();
    }
    
    // Helper method to update SessionState less frequently
    private void UpdateSessionStateIfNeeded()
    {
        TimeSpan timeSinceLastUpdate = DateTime.Now - lastSessionStateUpdateTime;
        if (timeSinceLastUpdate.TotalSeconds >= sessionStateUpdateInterval)
        {
            SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
            SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
            SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
            lastSessionStateUpdateTime = DateTime.Now;
            
            // Clear accumulators after update
            moduleLogAccumulator.Clear();
            databaseLogAccumulator.Clear();
        }
    }
    
    #endregion
    
    #region DatabaseLog
    
    private void StartDatabaseLogProcess()
    {
        try
        {
            if (debugMode) logCallback("Starting database logs process...", 0);
            
            // Mark state in SessionState
            SessionState.SetBool(SessionKeyDatabaseLogRunning, true);
            
            // Always ensure database log content is cleared when starting the process if clearDatabaseLogAtStart is true
            if (clearDatabaseLogAtStart)
            {
                ClearDatabaseLog();
            }
            
            // Create a new process to run the spacetime logs command
            databaseLogProcess = new Process();
            databaseLogProcess.StartInfo.FileName = "wsl.exe";
            
            // Build the command to run spacetime logs with the module name
            string logCommand = $"spacetime logs {moduleName} -f";
            
            // If server directory is specified, change to that directory first
            if (!string.IsNullOrEmpty(serverDirectory))
            {
                if (cmdProcessor != null)
                {
                    string wslPath = cmdProcessor.GetWslPath(serverDirectory);
                    logCommand = $"cd '{wslPath}' && {logCommand}";
                }
            }
            
            databaseLogProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -l -c \"{logCommand}\"";
            databaseLogProcess.StartInfo.UseShellExecute = false;
            databaseLogProcess.StartInfo.CreateNoWindow = true;
            databaseLogProcess.StartInfo.RedirectStandardOutput = true;
            databaseLogProcess.StartInfo.RedirectStandardError = true;
            databaseLogProcess.EnableRaisingEvents = true;
            
            // Handle output data received
            databaseLogProcess.OutputDataReceived += (sender, args) => {
                if (args.Data != null)
                {
                    string line = FormatDatabaseLogLine(args.Data);
                    databaseLogQueue.Enqueue(line);
                }
            };
            
            // Handle error data received (Only enabled if debugMode is true)
            databaseLogProcess.ErrorDataReceived += (sender, args) => {
                if (args.Data != null && debugMode)
                {
                    string formattedLine = FormatDatabaseLogLine(args.Data, true);
                    if (debugMode) UnityEngine.Debug.LogError($"[Database Log Error] {args.Data}");
                    databaseLogQueue.Enqueue(formattedLine);
                }
            };
            
            // Handle process exit
            databaseLogProcess.Exited += (sender, e) => {
                EditorApplication.delayCall += () => {
                    try
                    {
                        int exitCode = -1;
                        try { exitCode = databaseLogProcess.ExitCode; } catch {}
                        
                        if (debugMode) // If we want more information
                        {
                            UnityEngine.Debug.Log($"[ServerLogProcess] Database log process exited with code: {exitCode}");
                            
                            // Add a message to the log with current time formatted in the same way
                            string timestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
                            string stopMessage = $"\n{timestamp} [DATABASE LOG STOPPED]\n";
                            
                            // Update both current and cached logs
                            databaseLogContent += stopMessage;
                            cachedDatabaseLogContent += stopMessage;
                        }
                        
                        // Store in SessionState
                        SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
                        SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
                        
                        // Clear the process reference
                        databaseLogProcess = null;
                        
                        // Notify subscribers
                        if (onDatabaseLogUpdated != null)
                        {
                            onDatabaseLogUpdated();
                        }
                    }
                    catch (Exception ex)
                    {
                        if (debugMode) UnityEngine.Debug.LogError($"[Database Log Exit Handler Error]: {ex}");
                    }
                };
            };
            
            // Start the process
            databaseLogProcess.Start();
            databaseLogProcess.BeginOutputReadLine();
            databaseLogProcess.BeginErrorReadLine();
            
            if (debugMode) logCallback($"Database log process started (PID: {databaseLogProcess.Id}).", 0);
            
            // Add initial message to show logs are cleared
            /*if (clearDatabaseLogAtStart)
            {
                string timestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
                databaseLogContent = $"{timestamp} [DATABASE LOG STARTED - PREVIOUS LOGS CLEARED]\n";
                SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
            }*/
        }
        catch (Exception ex)
        {
            logCallback($"Error starting database log process: {ex.Message}", -1);
            databaseLogProcess = null;
            SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
        }
    }
    
    private void StopDatabaseLogProcess()
    {
        if (databaseLogProcess != null)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Stopping database log process (PID: {databaseLogProcess.Id}).");
            
            if (!databaseLogProcess.HasExited)
            {
                try
                {
                    databaseLogProcess.Kill();
                    databaseLogProcess.WaitForExit(500);
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error stopping database log process: {ex}");
                }
            }
            
            databaseLogProcess = null;
            SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
        }
    }
    
    public void AttemptDatabaseLogRestartAfterReload()
    {
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Checking database log process: running={serverRunning}, process={(databaseLogProcess == null ? "null" : databaseLogProcess.HasExited ? "exited" : "running")}");
        
        // Check if we should restart the database logs
        if (serverRunning && (databaseLogProcess == null || databaseLogProcess.HasExited))
        {
            if (debugMode) logCallback("Attempting to restart database log process after domain reload...", 0);
            StartDatabaseLogProcess();
        }
    }
    
    // Helper method to extract and format timestamps from log lines
    private string FormatDatabaseLogLine(string logLine, bool isError = false)
    {
        if (string.IsNullOrEmpty(logLine))
            return logLine;

        // Check for timestamp pattern like "2025-05-01T20:29:22.528775Z"
        var match = System.Text.RegularExpressions.Regex.Match(logLine, @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)");
        
        if (match.Success)
        {
            // Extract the timestamp
            string originalTimestamp = match.Groups[1].Value;
            
            // Try to parse it as DateTimeOffset
            if (DateTimeOffset.TryParse(originalTimestamp, out DateTimeOffset dateTime))
            {
                // Format it as [YYYY-MM-DD HH:MM:SS]
                string formattedTimestamp = $"[{dateTime.ToString("yyyy-MM-dd HH:mm:ss")}]";
                
                // Replace the original timestamp with the formatted one
                string result = logLine.Replace(originalTimestamp, "").Trim();
                
                // Add error indicator if needed
                string errorPrefix = isError ? " [DATABASE LOG ERROR]" : ""; // No prefix for normal logs
                
                // Return formatted output with timestamp at the start
                return $"{formattedTimestamp}{errorPrefix} {result}";
            }
        }
        
        // If no timestamp found or parsing failed, use current time
        string fallbackTimestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
        string errorSuffix = isError ? " [DATABASE LOG ERROR]" : ""; // No suffix for normal logs
        return $"{fallbackTimestamp}{errorSuffix} {logLine}";
    }
    #endregion

    #region Log Limiter

    private void StartLogLimiter() // Background log length processor
    {
        if (isProcessing) return;
        
        isProcessing = true;
        processingTask = Task.Run(async () => {
            try
            {
                while (!processingCts.Token.IsCancellationRequested)
                {
                    ProcessLogQueue();
                    await Task.Delay(PROCESS_INTERVAL_MS, processingCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Log processor error: {ex}");
            }
            finally
            {
                isProcessing = false;
            }
        }, processingCts.Token);
    }

    private void ProcessLogQueue()
    {
        if (databaseLogQueue.IsEmpty) return;

        StringBuilder batchedLogs = new StringBuilder();
        while (databaseLogQueue.TryDequeue(out string logLine))
        {
            batchedLogs.AppendLine(logLine);
        }

        if (batchedLogs.Length > 0)
        {
            lock (logLock)
            {
                databaseLogContent += batchedLogs.ToString();
                cachedDatabaseLogContent += batchedLogs.ToString();

                // Check if we need to truncate
                if (databaseLogContent.Length > BUFFER_SIZE)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating database log from {databaseLogContent.Length} chars. If this happens frequently, please fix your database error or raise the buffer size.");
                    string truncatedContent = "[... Log Truncated ...]\n" + 
                        databaseLogContent.Substring(databaseLogContent.Length - TARGET_SIZE);
                    databaseLogContent = truncatedContent;
                    cachedDatabaseLogContent = truncatedContent;
                }
            }

            // Update UI on main thread
            EditorApplication.delayCall += () => {
                UpdateSessionStateIfNeeded();
                if (onDatabaseLogUpdated != null)
                {
                    onDatabaseLogUpdated();
                }
            };
        }
    }
    
    #endregion

    // Find spacetime binary path on the remote server
    private async Task<string> FindRemoteSpacetimePathAsync()
    {
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath))
        {
            if (debugMode) UnityEngine.Debug.LogError("[ServerLogProcess] SSH connection details incomplete");
            return "spacetime"; // Default fallback
        }

        try
        {            Process process = new Process();
            process.StartInfo.FileName = "ssh";
            
            // Use a login shell to ensure PATH is fully loaded, similar to ServerInstallerWindow approach
            string findCommand = "bash -l -c 'which spacetime' 2>/dev/null";
            
            process.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{findCommand}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            
            process.Start();
            
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit(5000); // Wait up to 5 seconds
              string path = output.Trim();
            if (!string.IsNullOrEmpty(path))
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Found remote spacetime path: {path}");
                return path;
            }
            
            // If the first approach failed, try checking common installation locations
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] First path detection failed, trying alternative locations");
            
            // List of common spacetime installation paths to check
            string[] commonPaths = new string[] {
                "/usr/local/bin/spacetime",
                "/usr/bin/spacetime",
                "$HOME/.local/bin/spacetime",
                "$HOME/.local/share/spacetime/bin/spacetime-cli"
            };
            
            foreach (string commonPath in commonPaths)
            {
                // Use test -x to check if the file exists and is executable
                string checkCommand = $"test -x {commonPath} && echo {commonPath}";
                
                process = new Process();
                process.StartInfo.FileName = "ssh";
                process.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{checkCommand}\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                
                process.Start();
                string checkOutput = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit(5000);
                
                string foundPath = checkOutput.Trim();
                if (!string.IsNullOrEmpty(foundPath))
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Found spacetime at alternate location: {foundPath}");
                    return foundPath;
                }
            }
            
            if (debugMode) UnityEngine.Debug.LogWarning("[ServerLogProcess] Could not find spacetime in any standard location, falling back to default");
            logCallback("Warning: Could not find spacetime in PATH. Using default 'spacetime' command.", -2);
            logCallback("Hint: Make sure spacetime is installed and accessible in your PATH on the remote server.", -2);
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error finding spacetime path: {ex.Message}");
        }
        
        return "spacetime"; // Default fallback
    }

    // Test SSH connection and check spacetime availability
    private async Task<bool> TestSSHAndSpacetimeAsync()
    {
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath))
            return false;

        try
        {            Process process = new Process();
            process.StartInfo.FileName = "ssh";
            // Command to test connection and run a simple spacetime command with login shell
            string testCommand = $"echo 'Testing SSH connection' && bash -l -c '{remoteSpacetimePath} --version'";
            
            process.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{testCommand}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            
            process.Start();
            
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            process.WaitForExit(5000);
            
            bool success = process.ExitCode == 0 && output.Contains("spacetime");
            
            if (success)
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] SSH and spacetime test successful: {output}");
            }
            else
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] SSH test failed. Exit code: {process.ExitCode}, Output: {output}, Error: {error}");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error testing SSH: {ex.Message}");
            return false;
        }
    }

    // Check if spacetimedb service is running on the remote server
    public async Task<bool> CheckSpacetimeDBServiceStatus()
    {
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath))
            return false;

        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "ssh";
            // Check service status
            string checkCommand = "sudo systemctl is-active spacetimedb.service";
            
            process.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{checkCommand}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            
            process.Start();
            
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit(5000);
            
            bool isActive = output.Trim() == "active";
            
            if (debugMode) 
            {
                UnityEngine.Debug.Log($"[ServerLogProcess] SpacetimeDB service status: {output.Trim()}");
            }
            
            return isActive;
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error checking service status: {ex.Message}");
            return false;
        }
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games