using UnityEditor;
using System.Diagnostics;
using System;
using System.Text;

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
    private const string PrefsKeyPrefix = "ServerWindow_"; // Same prefix as ServerWindow
    
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
    
    #region Log Management Methods
    
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
            // Append to in-memory log, limit size, refresh window
            silentServerCombinedLog += line + "\n";
            // More aggressive truncation BEFORE saving to SessionState
            const int maxLogLength = 75000; // Keep last 75k chars
            const int trimToLength = 50000; // Trim down to 50k when limit exceeded
            if (silentServerCombinedLog.Length > maxLogLength)
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory silent log from {silentServerCombinedLog.Length} chars.");
                silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncated log length: {silentServerCombinedLog.Length}");
            }
            
            SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
            
            // Call the callback to update any UI
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
    
    #region Tail Process Management
    
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
                string errorPrefix = isError ? " [ERROR]" : "";
                
                // Return formatted output with timestamp at the start
                return $"{formattedTimestamp}{errorPrefix} {result}";
            }
        }
        
        // If no timestamp found or parsing failed, use current time
        string fallbackTimestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
        string errorSuffix = isError ? " [ERROR]" : "";
        return $"{fallbackTimestamp}{errorSuffix} {logLine}";
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
    
    #region Database Log Management
    
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
            
            // Counter for initial log entries we want to skip
            int initialLogsToSkip = clearDatabaseLogAtStart ? 10 : 0;
            int logLineCount = 0;
            
            // Handle output data received
            databaseLogProcess.OutputDataReceived += (sender, args) => {
                if (args.Data != null)
                {
                    EditorApplication.delayCall += () => {
                        try
                        {
                            // Skip first few logs if clearDatabaseLogAtStart is enabled
                            if (clearDatabaseLogAtStart && logLineCount < initialLogsToSkip)
                            {
                                logLineCount++;
                                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Skipping initial database log line {logLineCount}/{initialLogsToSkip}");
                                return;
                            }
                            
                            // Extract and format timestamp from the original log line
                            string line = FormatDatabaseLogLine(args.Data);
                            
                            // Append to in-memory log
                            databaseLogContent += line + "\n";
                            
                            // Also update the cached version
                            cachedDatabaseLogContent += line + "\n";
                            
                            // Add to accumulator
                            databaseLogAccumulator.AppendLine(line);
                            
                            // Limit the log size
                            const int maxLogLength = 75000;
                            const int trimToLength = 50000;
                            if (databaseLogContent.Length > maxLogLength)
                            {
                                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating database log from {databaseLogContent.Length} chars.");
                                databaseLogContent = "[... Log Truncated ...]\n" + databaseLogContent.Substring(databaseLogContent.Length - trimToLength);
                                cachedDatabaseLogContent = databaseLogContent; // Keep cache in sync
                            }
                            
                            // Update SessionState less frequently
                            UpdateSessionStateIfNeeded();
                            
                            // Notify subscribers regardless of SessionState update
                            if (onDatabaseLogUpdated != null)
                            {
                                onDatabaseLogUpdated();
                            }
                        }
                        catch (Exception ex)
                        {
                            if (debugMode) UnityEngine.Debug.LogError($"[Database Log Handler Error]: {ex}");
                        }
                    };
                }
            };
            
            // Handle error data received (Only enabled if debugMode is true)
            databaseLogProcess.ErrorDataReceived += (sender, args) => {
                if (args.Data != null && debugMode)
                {
                    EditorApplication.delayCall += () => {
                        try
                        {
                            // Don't skip error messages
                            string formattedLine = FormatDatabaseLogLine(args.Data, true);
                            
                            // Log to the console for visibility
                            if (debugMode) UnityEngine.Debug.LogError($"[Database Log Error] {args.Data}");
                            
                            // Append to both current and cached logs
                            databaseLogContent += formattedLine + "\n";
                            cachedDatabaseLogContent += formattedLine + "\n";
                            
                            // Store in SessionState
                            SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
                            SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
                            
                            // Notify subscribers
                            if (onDatabaseLogUpdated != null)
                            {
                                onDatabaseLogUpdated();
                            }
                        }
                        catch (Exception ex)
                        {
                            if (debugMode) UnityEngine.Debug.LogError($"[Database Log Error Handler Error]: {ex}");
                        }
                    };
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
} // Class
} // Namespace