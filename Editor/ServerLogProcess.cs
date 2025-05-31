using UnityEditor;
using UnityEngine;
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
    public static bool debugMode = false;
    
    // Constants
    public const string WslCombinedLogPath = "/tmp/spacetime.log";
    
    // Log file processes
    private Process tailProcess;
    private Process databaseLogProcess;
    
    // Log contents
    private string silentServerCombinedLog = "";
    private string cachedModuleLogContent = ""; // Add cached version of module logs
    private string databaseLogContent = "";
    private string cachedDatabaseLogContent = ""; // Add cached version of database logs
    
    // Added for performance - accumulated logs between SessionState updates
    private StringBuilder moduleLogAccumulator = new StringBuilder();
    private StringBuilder databaseLogAccumulator = new StringBuilder();
    private DateTime lastSessionStateUpdateTime = DateTime.MinValue;
    private const double sessionStateUpdateInterval = 1.0; // Update SessionState every 1 second
    
    // Session state keys
    private const string SessionKeyCombinedLog = "ServerWindow_SilentCombinedLog";
    private const string SessionKeyCachedModuleLog = "ServerWindow_CachedModuleLog"; // Add session key for cached module logs
    private const string SessionKeyDatabaseLog = "ServerWindow_DatabaseLog";
    private const string SessionKeyCachedDatabaseLog = "ServerWindow_CachedDatabaseLog"; // Add session key for cached logs
    private const string SessionKeyDatabaseLogRunning = "ServerWindow_DatabaseLogRunning";
    private const string PrefsKeyPrefix = "CCCP_"; // Same prefix as ServerWindow
    
    // Settings
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
    
    // Track when server was stopped to filter connection errors
    private DateTime serverStoppedTime = DateTime.MinValue;
    private const double serverStopGracePeriod = 10.0; // Ignore connection errors for 10 seconds after server stops
    
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

    // Helper classes for parsing SpacetimeDB JSON logs
    [System.Serializable]
    private class SpacetimeDbJsonLogEntry
    {
        public string timestamp;
        public string level;
        public SpacetimeDbJsonLogFields fields;
        public string target;
    }

    [System.Serializable]
    private class SpacetimeDbJsonLogFields    {
        public string message;
    }
    
    #region SSH Logging
    
    // MODULE LOGS: Read from journalctl for spacetimedb.service using --since timestamp
    // DATABASE LOGS: Read from journalctl for spacetimedb-logs.service (created by external script)
    //                The service runs "spacetime logs {module} -f" and logs to journalctl
    
    // Path for remote server logs (kept for fallback/reference)
    public const string CustomServerCombinedLogPath = "/var/log/spacetimedb/spacetimedb.log";
    
    // SSH details
    private string sshUser = "";
    private string sshHost = "";
    private string sshKeyPath = "";
    private bool isCustomServer = false;
    private string remoteSpacetimePath = "spacetime"; // Default path
    private DateTime lastModuleLogTimestamp = DateTime.MinValue;
    private DateTime lastDatabaseLogTimestamp = DateTime.MinValue;
    private double lastLogReadTime = 0;
    private const double logReadInterval = 1.0;
    private bool hasScheduledNextCheck = false;

    // Service names for journalctl
    private const string SpacetimeServiceName = "spacetimedb.service";
    private const string SpacetimeDatabaseLogServiceName = "spacetimedb-logs.service"; // Will be created by user's script
    
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
    
    // Start SSH-based log reading for custom server
    public async void StartSSHLogging()
    {
        // Clear logs if needed
        if (clearModuleLogAtStart)
        {
            ClearSSHModuleLogFile();
        }
        
        if (clearDatabaseLogAtStart)
        {
            ClearSSHDatabaseLog();
        }
          // Initialize timestamps to 1 hour ago to avoid getting massive historical logs
        lastModuleLogTimestamp = DateTime.UtcNow.AddHours(-1);
        lastDatabaseLogTimestamp = DateTime.UtcNow.AddHours(-1);
        
        // Ensure we have found the spacetime path
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
            }        }
          if (debugMode) logCallback("Started SSH periodic log reading", 1);
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
            
            // Also clear the in-memory log and cached version
            silentServerCombinedLog = "";
            cachedModuleLogContent = "";
            SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
            SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
            
            // Reset timestamp to start fresh
            lastModuleLogTimestamp = DateTime.UtcNow;
            
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
        
        // Reset timestamp to start fresh
        lastDatabaseLogTimestamp = DateTime.UtcNow;
        
        if (debugMode) logCallback("SSH database log cleared successfully", 1);
    }
    
    // Read module logs from journalctl periodically
    private async Task ReadSSHModuleLogsAsync()
    {
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath))
        {
            return;
        }
        
        try
        {
            // Format timestamp for journalctl --since parameter (correct format)
            string sinceTimestamp = lastModuleLogTimestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
              // Use journalctl to read logs since last timestamp
            string journalCommand = $"sudo journalctl -u {SpacetimeServiceName} --since \\\"{sinceTimestamp}\\\" --no-pager -o short-iso-precise";
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Reading SSH module logs since: {sinceTimestamp}");
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Full SSH command: ssh -i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{journalCommand}\"");
            
            Process readProcess = new Process();
            readProcess.StartInfo.FileName = "ssh";
            readProcess.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{journalCommand}\"";
            readProcess.StartInfo.UseShellExecute = false;
            readProcess.StartInfo.CreateNoWindow = true;
            readProcess.StartInfo.RedirectStandardOutput = true;
            readProcess.StartInfo.RedirectStandardError = true;
            
            readProcess.Start();
            
            string output = await readProcess.StandardOutput.ReadToEndAsync();
            string error = await readProcess.StandardError.ReadToEndAsync();
            
            readProcess.WaitForExit(5000);
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] SSH module logs - Output length: {output?.Length ?? 0}, Error: {error}");
            
            if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Split('\n');
                bool hasNewLogs = false;
                int lineCount = 0;
                DateTime latestTimestamp = lastModuleLogTimestamp;
                  foreach (string line in lines)
                {
                    if (!string.IsNullOrEmpty(line.Trim()) && !line.Trim().Equals("-- No entries --"))
                    {
                        string formattedLine = FormatServerLogLine(line.Trim());
                        moduleLogAccumulator.Append(formattedLine).Append("\n");
                        silentServerCombinedLog += formattedLine + "\n";
                        cachedModuleLogContent += formattedLine + "\n";
                        hasNewLogs = true;
                        lineCount++;
                          // Extract and track the actual timestamp from this log line
                        DateTime logTimestamp = ExtractTimestampFromJournalLine(line.Trim());
                        if (logTimestamp != DateTime.MinValue && logTimestamp > latestTimestamp)
                        {
                            latestTimestamp = logTimestamp;
                        }
                    }
                }
                
                if (hasNewLogs)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Read {lineCount} new SSH module log lines");
                    
                    // Always advance the timestamp to prevent infinite loops
                    DateTime timestampToUse;
                    
                    if (latestTimestamp > lastModuleLogTimestamp)
                    {
                        // Use the actual latest log timestamp if we successfully parsed one
                        timestampToUse = latestTimestamp.AddSeconds(1);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using parsed timestamp: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    else
                    {
                        // Fallback: advance by at least 1 second from the last query time to prevent infinite loops
                        timestampToUse = lastModuleLogTimestamp.AddSeconds(1);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No valid timestamps found, advancing by 1 second: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    
                    // Update the timestamp
                    lastModuleLogTimestamp = timestampToUse;
                    
                    // Manage log size
                    const int maxLogLength = 75000;
                    const int trimToLength = 50000;
                    if (silentServerCombinedLog.Length > maxLogLength)
                    {
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory silent SSH log from {silentServerCombinedLog.Length} chars.");
                        silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                        cachedModuleLogContent = "[... Log Truncated ...]\n" + cachedModuleLogContent.Substring(cachedModuleLogContent.Length - trimToLength);
                    }
                    
                    // Update SessionState immediately for SSH logs to ensure they appear
                    SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
                    SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
                    lastSessionStateUpdateTime = DateTime.Now;
                    
                    // Notify of log update
                    EditorApplication.delayCall += () => onModuleLogUpdated?.Invoke();
                }
                else 
                {
                    // Even if no new logs, advance timestamp slightly to prevent infinite queries
                    lastModuleLogTimestamp = lastModuleLogTimestamp.AddSeconds(0.5);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No new SSH module log lines found, advancing timestamp slightly to: {lastModuleLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
                }
            }
            else if (debugMode)
            {
                UnityEngine.Debug.Log("[ServerLogProcess] SSH module logs - No output received");
            }
            
            if (!string.IsNullOrEmpty(error))
            {
                if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] SSH module log read error: {error}");
                
                // If service doesn't exist, provide helpful message
                if (error.Contains("Unit " + SpacetimeServiceName + " could not be found"))
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] SpacetimeDB service not found - ensure SpacetimeDB is running as a systemd service");
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error reading SSH module logs: {ex.Message}");
        }
    }
    
    private async Task ReadSSHDatabaseLogsAsync()
    {
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath))
        {
            return;
        }
        
        if (string.IsNullOrEmpty(moduleName))
        {
            return; // Can't read database logs without module name
        }
        
        try
        {
            // Format timestamp for journalctl --since parameter (correct format)
            string sinceTimestamp = lastDatabaseLogTimestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            // Read from the database log service (assuming it will be created by user's script)
            string journalCommand = $"sudo journalctl -u {SpacetimeDatabaseLogServiceName} --since \\\"{sinceTimestamp}\\\" --no-pager -o short-iso-precise";
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Reading SSH database logs since: {sinceTimestamp}");
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Full Database SSH command: ssh -i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{journalCommand}\"");
            
            Process readProcess = new Process();
            readProcess.StartInfo.FileName = "ssh";
            readProcess.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{journalCommand}\"";
            readProcess.StartInfo.UseShellExecute = false;
            readProcess.StartInfo.CreateNoWindow = true;
            readProcess.StartInfo.RedirectStandardOutput = true;
            readProcess.StartInfo.RedirectStandardError = true;
            
            readProcess.Start();
            
            string output = await readProcess.StandardOutput.ReadToEndAsync();
            string error = await readProcess.StandardError.ReadToEndAsync();
            
            readProcess.WaitForExit(5000);
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] SSH database logs - Output length: {output?.Length ?? 0}, Error: {error}");
              if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Split('\n');
                bool hasNewLogs = false;
                int lineCount = 0;
                DateTime latestTimestamp = lastDatabaseLogTimestamp;                  
                
                foreach (string line in lines)
                {
                    if (!string.IsNullOrEmpty(line.Trim()) && !line.Trim().Equals("-- No entries --"))
                    {
                        string formattedLine = FormatDatabaseLogLine(line.Trim());
                        if (formattedLine != null) // Only process if line wasn't filtered out
                        {
                            databaseLogQueue.Enqueue(formattedLine);
                            hasNewLogs = true;
                            lineCount++;
                              // Extract and track the actual timestamp from this log line
                            DateTime logTimestamp = ExtractTimestampFromJournalLine(line.Trim());
                            if (logTimestamp != DateTime.MinValue && logTimestamp > latestTimestamp)
                            {
                                latestTimestamp = logTimestamp;
                            }
                        }
                    }
                }
                  if (hasNewLogs)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Read {lineCount} new SSH database log lines");
                    
                    // Always advance the timestamp to prevent infinite loops
                    DateTime timestampToUse;
                    
                    if (latestTimestamp > lastDatabaseLogTimestamp)
                    {
                        // Use the actual latest log timestamp if we successfully parsed one
                        timestampToUse = latestTimestamp.AddSeconds(1);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using parsed database timestamp: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    else
                    {
                        // Fallback: advance by at least 1 second from the last query time to prevent infinite loops
                        timestampToUse = lastDatabaseLogTimestamp.AddSeconds(1);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No valid database timestamps found, advancing by 1 second: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    
                    // Update the timestamp
                    lastDatabaseLogTimestamp = timestampToUse;
                    
                    // Notify of log update
                    EditorApplication.delayCall += () => onDatabaseLogUpdated?.Invoke();
                }
                else 
                {
                    // Even if no new logs, advance timestamp slightly to prevent infinite queries
                    lastDatabaseLogTimestamp = lastDatabaseLogTimestamp.AddSeconds(0.5);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No new SSH database log lines found, advancing timestamp slightly to: {lastDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
                }
            }
            else if (debugMode)
            {
                UnityEngine.Debug.Log("[ServerLogProcess] SSH database logs - No output received");
            }
            
            if (!string.IsNullOrEmpty(error))
            {
                // If service doesn't exist yet, that's expected until user creates it
                if (error.Contains("Unit " + SpacetimeDatabaseLogServiceName + " could not be found"))
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Database log service not found yet - this is expected until the service is created");
                }
                else if (debugMode)
                {
                    UnityEngine.Debug.LogWarning($"[ServerLogProcess] SSH database log read error: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error reading SSH database logs: {ex.Message}");
        }
    }    
    
    public void CheckSSHLogProcesses(double currentTime)
    {
        if (currentTime - lastLogReadTime > logReadInterval)
        {
            lastLogReadTime = currentTime;
            hasScheduledNextCheck = false; // Reset the scheduling flag
            
            if (serverRunning && isCustomServer)
            {
                EditorApplication.delayCall += async () =>
                {
                    try
                    {
                        await ReadSSHModuleLogsAsync();
                        await ReadSSHDatabaseLogsAsync();
                    }
                    catch (Exception ex)
                    {
                        if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error in CheckSSHLogProcesses: {ex.Message}");
                    }
                };
            }
            
            // Schedule the next check based on logReadInterval
            ScheduleNextSSHLogCheck();
        }
    }
    
    private void ScheduleNextSSHLogCheck()
    {
        if (!hasScheduledNextCheck && serverRunning && isCustomServer)
        {
            hasScheduledNextCheck = true;
            
            // Schedule the next check after logReadInterval seconds
            EditorApplication.delayCall += () =>
            {
                System.Threading.Tasks.Task.Delay((int)(logReadInterval * 1000)).ContinueWith(_ =>
                {
                    if (serverRunning && isCustomServer)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            CheckSSHLogProcesses(EditorApplication.timeSinceStartup);
                        };
                    }
                });
            };
        }
    }    
    
    public void StopSSHLogging()
    {
        if (debugMode) logCallback("Stopping SSH periodic log reading", 0);
        
        // Reset timestamps to 1 hour ago to avoid massive log dumps on restart
        lastModuleLogTimestamp = DateTime.UtcNow.AddHours(-1);
        lastDatabaseLogTimestamp = DateTime.UtcNow.AddHours(-1);
        lastLogReadTime = 0;
        hasScheduledNextCheck = false; // Reset scheduling flag to stop the chain
        
        SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
        
        if (debugMode) logCallback("SSH log reading stopped", 0);
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
        cachedModuleLogContent = SessionState.GetString(SessionKeyCachedModuleLog, ""); // Load cached module logs
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
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Configured with username: {this.userName}, moduleName: {this.moduleName}");
    }
    
    public void SetServerRunningState(bool isRunning)
    {
        serverRunning = isRunning;
        if (!isRunning)
        {
            serverStoppedTime = DateTime.Now;
        }
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Server running state set to: {isRunning}");
    }
    
    // Force refresh in-memory logs from SessionState - used when ServerOutputWindow gets focus
    public void ForceRefreshLogsFromSessionState()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Force refreshing logs from SessionState");
        
        // Load the most current data from SessionState
        string sessionModuleLog = SessionState.GetString(SessionKeyCombinedLog, "");
        string sessionDatabaseLog = SessionState.GetString(SessionKeyDatabaseLog, "");
        string sessionCachedModuleLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
        string sessionCachedDatabaseLog = SessionState.GetString(SessionKeyCachedDatabaseLog, "");
        
        // Update in-memory logs if SessionState has more recent/complete data
        if (!string.IsNullOrEmpty(sessionModuleLog) && sessionModuleLog.Length > silentServerCombinedLog.Length)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Refreshing silentServerCombinedLog from SessionState ({sessionModuleLog.Length} chars)");
            silentServerCombinedLog = sessionModuleLog;
        }
        
        if (!string.IsNullOrEmpty(sessionDatabaseLog) && sessionDatabaseLog.Length > databaseLogContent.Length)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Refreshing databaseLogContent from SessionState ({sessionDatabaseLog.Length} chars)");
            databaseLogContent = sessionDatabaseLog;
        }
        
        if (!string.IsNullOrEmpty(sessionCachedModuleLog) && sessionCachedModuleLog.Length > cachedModuleLogContent.Length)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Refreshing cachedModuleLogContent from SessionState ({sessionCachedModuleLog.Length} chars)");
            cachedModuleLogContent = sessionCachedModuleLog;
        }
        
        if (!string.IsNullOrEmpty(sessionCachedDatabaseLog) && sessionCachedDatabaseLog.Length > cachedDatabaseLogContent.Length)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Refreshing cachedDatabaseLogContent from SessionState ({sessionCachedDatabaseLog.Length} chars)");
            cachedDatabaseLogContent = sessionCachedDatabaseLog;
        }
        
        // Notify callbacks that logs have been updated
        onModuleLogUpdated?.Invoke();
        onDatabaseLogUpdated?.Invoke();
    }
    
    // Force SSH log refresh - triggers new journalctl commands immediately
    public async void ForceSSHLogRefresh()
    {
        if (debugMode) logCallback("Force refreshing SSH logs via journalctl...", 1);
        
        if (isCustomServer && serverRunning)
        {
            try
            {
                // Force immediate SSH log read
                await ReadSSHModuleLogsAsync();
                await ReadSSHDatabaseLogsAsync();
                
                if (debugMode) logCallback("SSH log force refresh completed", 1);
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error in ForceSSHLogRefresh: {ex.Message}");
                logCallback($"SSH log refresh failed: {ex.Message}", -1);
            }
        }
        else
        {
            if (debugMode) logCallback("SSH log refresh skipped - not a custom server or server not running", 0);
        }
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
        cachedModuleLogContent = "";
        SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
        SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
        
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
        // Return cached logs if server is not running, otherwise return current logs
        return serverRunning ? silentServerCombinedLog : cachedModuleLogContent;
    }
    
    public string GetDatabaseLogContent()
    {
        // Return cached logs if server is not running, otherwise return current logs
        return serverRunning ? databaseLogContent : cachedDatabaseLogContent;
    }
    
    public void StartLogging()
    {
        // Ensure background processing task is running before starting any logging processes
        if (!isProcessing || processingTask == null || processingTask.IsCompleted)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Ensuring background processing task is running before starting logging...");
            
            // Cancel existing task if it exists
            if (processingCts != null && !processingCts.IsCancellationRequested)
            {
                processingCts.Cancel();
            }
            
            // Create new cancellation token and restart the background task
            processingCts = new CancellationTokenSource();
            isProcessing = false; // Reset the flag so StartLogLimiter can restart
            StartLogLimiter();
            
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Background processing task ensured before starting logging");
        }
        
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
        // Cancel and cleanup background processing task
        if (processingCts != null)
        {
            processingCts.Cancel();
            try
            {
                processingTask?.Wait(1000);
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error waiting for processing task to complete: {ex.Message}");
            }
        }
        
        // Reset processing state
        isProcessing = false;
        
        // Create new cancellation token for next time
        processingCts = new CancellationTokenSource();
        
        // Stop the actual logging processes
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
            
            // Configure UTF-8 encoding to properly handle special characters from WSL
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

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
                            cachedModuleLogContent += formattedLine + "\n"; // Keep cached version in sync
                            
                            // Add to accumulator
                            moduleLogAccumulator.AppendLine(formattedLine);
                            
                            // Manage log size immediately
                            const int maxLogLength = 75000;
                            const int trimToLength = 50000;                            if (silentServerCombinedLog.Length > maxLogLength)
                            {
                                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory silent log from {silentServerCombinedLog.Length} chars.");
                                silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                                cachedModuleLogContent = "[... Log Truncated ...]\n" + cachedModuleLogContent.Substring(cachedModuleLogContent.Length - trimToLength);
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
            
            if (debugMode) logCallback($"Re-starting tail for {logPath}...", 0);            tailProcess = StartTailingLogFile(logPath, (line) => {
                silentServerCombinedLog += line + "\n";
                cachedModuleLogContent += line + "\n"; // Keep cached version in sync
                const int maxLogLength = 75000;
                const int trimToLength = 50000;
                if (silentServerCombinedLog.Length > maxLogLength)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess Tail Reload] Truncating in-memory silent log from {silentServerCombinedLog.Length} chars.");
                    silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                    cachedModuleLogContent = "[... Log Truncated ...]\n" + cachedModuleLogContent.Substring(cachedModuleLogContent.Length - trimToLength);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess Tail Reload] Truncated log length: {silentServerCombinedLog.Length}");
                }
                
                SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
                SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
                
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

        // Clean messageContent of any remaining ISO-style timestamps regardless of where they appear
        // This needs to happen AFTER we've extracted our timestamp prefix, to ensure we clean up SpacetimeDB timestamps in the content
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
            SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent); // Fix: Use correct cached content
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
            
            // Check for required parameters first
            if (string.IsNullOrEmpty(moduleName))
            {
                logCallback("ERROR: Module name is not configured, cannot start database log process", -1);
                if (debugMode) UnityEngine.Debug.LogError("[ServerLogProcess] StartDatabaseLogProcess failed: moduleName is null/empty");
                return;
            }
            
            if (string.IsNullOrEmpty(userName))
            {
                logCallback("ERROR: Username is not configured, cannot start database log process", -1);
                if (debugMode) UnityEngine.Debug.LogError("[ServerLogProcess] StartDatabaseLogProcess failed: userName is null/empty");
                return;
            }
            
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
            
            string wslArguments = $"-d Debian -u {userName} --exec bash -l -c \"{logCommand}\"";
            databaseLogProcess.StartInfo.Arguments = wslArguments;
            
            // Log the exact command being executed for debugging
            if (debugMode) 
            {
                logCallback($"Database log WSL command: wsl.exe {wslArguments}", 0);
                UnityEngine.Debug.Log($"[ServerLogProcess] Starting database log process with command: wsl.exe {wslArguments}");
            }
            databaseLogProcess.StartInfo.UseShellExecute = false;
            databaseLogProcess.StartInfo.CreateNoWindow = true;
            databaseLogProcess.StartInfo.RedirectStandardOutput = true;
            databaseLogProcess.StartInfo.RedirectStandardError = true;
            databaseLogProcess.EnableRaisingEvents = true;
            
            // Configure UTF-8 encoding to properly handle special characters from WSL
            databaseLogProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            databaseLogProcess.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            
            // Handle output data received
            databaseLogProcess.OutputDataReceived += (sender, args) => {
                if (args.Data != null)
                {
                    string line = FormatDatabaseLogLine(args.Data);
                    if (line != null) // Only enqueue if line wasn't filtered out
                    {
                        databaseLogQueue.Enqueue(line);
                    }
                }
            };
            
            // Handle error data received (Always enabled to catch startup errors)
            databaseLogProcess.ErrorDataReceived += (sender, args) => {
                if (args.Data != null)
                {
                    string formattedLine = FormatDatabaseLogLine(args.Data, true);
                    if (debugMode) logCallback($"Database Log Error: {args.Data}", -1);
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
            try
            {
                databaseLogProcess.Start();
                databaseLogProcess.BeginOutputReadLine();
                databaseLogProcess.BeginErrorReadLine();
                
                if (debugMode) logCallback($"Database log process started successfully (PID: {databaseLogProcess.Id}).", 1);
                
                // Ensure background processing task is running when we start a new database log process
                // This is critical after Unity domain reloads or WSL restarts
                if (!isProcessing || processingTask == null || processingTask.IsCompleted)
                {
                    if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Ensuring background processing task is running for new database log process...");
                    
                    // Cancel existing task if it exists
                    if (processingCts != null && !processingCts.IsCancellationRequested)
                    {
                        processingCts.Cancel();
                    }
                    
                    // Create new cancellation token and restart the background task
                    processingCts = new CancellationTokenSource();
                    isProcessing = false; // Reset the flag so StartLogLimiter can restart
                    StartLogLimiter();
                    
                    if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Background processing task ensured for database logging");
                }
                
                // Check if process exits immediately (common issue)
                System.Threading.Thread.Sleep(500); // Give it a moment to potentially fail
                if (databaseLogProcess.HasExited)
                {
                    int exitCode = databaseLogProcess.ExitCode;
                    if (debugMode) logCallback($"ERROR: Database log process exited immediately with code {exitCode}", -1);
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Database log process failed to start - exited with code {exitCode}");
                    databaseLogProcess = null;
                    SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
                    return;
                }
            }
            catch (Exception startEx)
            {
                logCallback($"ERROR: Failed to start database log process: {startEx.Message}", -1);
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Exception starting database log process: {startEx}");
                databaseLogProcess = null;
                SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
                return;
            }
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
        
        // Ensure the background processing task is running after domain reload
        // This is critical for processing the database log queue
        if (!isProcessing || processingTask == null || processingTask.IsCompleted)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Background processing task not running, restarting...");
            
            // Cancel existing task if it exists
            if (processingCts != null && !processingCts.IsCancellationRequested)
            {
                processingCts.Cancel();
            }
            
            // Create new cancellation token and restart the background task
            processingCts = new CancellationTokenSource();
            isProcessing = false; // Reset the flag so StartLogLimiter can restart
            StartLogLimiter();
            
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Background processing task restarted");
        }
    }
    
    // Helper method to extract and format timestamps from log lines
    private string FormatDatabaseLogLine(string logLine, bool isError = false)
    {
        if (string.IsNullOrEmpty(logLine))
            return logLine;        // Filter out specific error messages when not in debug mode
        if (!debugMode)
        {
            // Check for the specific error messages to filter out
            if (logLine.Contains("Error: error decoding response body") ||
                logLine.Contains("Caused by:") ||
                logLine.Contains("error reading a body from connection") ||
                logLine.Contains("unexpected EOF during chunk size line") ||
                logLine.Contains("error sending request for url") ||
                logLine.Contains("Connection refused") ||
                logLine.Contains("tcp connect error"))
            {
                return null; // Return null to indicate this line should be skipped
            }
        }
        
        // Also filter connection errors that occur within grace period after server stops
        if (serverStoppedTime != DateTime.MinValue && 
            (DateTime.Now - serverStoppedTime).TotalSeconds <= serverStopGracePeriod)
        {
            if (logLine.Contains("error sending request for url") ||
                logLine.Contains("Connection refused") ||
                logLine.Contains("tcp connect error") ||
                logLine.Contains("client error (Connect)"))
            {
                return null; // Filter out connection errors during grace period after server stop
            }
        }
        
        // Check if this is a journalctl format line (SSH logs)
        // Format 1: "May 29 20:16:31 LoreMagic spacetime[51367]: 2025-05-29T20:16:31.212054Z  INFO: src/lib.rs:140: Player 4 reconnected."
        // Format 2: "2025-05-29T20:32:45.845810+00:00 LoreMagic spacetime[74350]: 2025-05-29T20:16:31.212054Z  INFO: src/lib.rs:140: Player 4 reconnected."
        var journalMatch = System.Text.RegularExpressions.Regex.Match(logLine, @"^(\w{3}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+\w+\s+spacetime\[\d+\]:\s*(.*)$");
        var journalIsoMatch = System.Text.RegularExpressions.Regex.Match(logLine, @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+\+\d{2}:\d{2})\s+\w+\s+spacetime\[\d+\]:\s*(.*)$");
        
        if (journalMatch.Success || journalIsoMatch.Success)
        {
            // Extract the actual log content after the journalctl prefix
            string actualLogContent = journalMatch.Success ? journalMatch.Groups[2].Value.Trim() : journalIsoMatch.Groups[2].Value.Trim();
              // Now look for SpacetimeDB timestamp in the actual content
            var timestampMatch = System.Text.RegularExpressions.Regex.Match(actualLogContent, @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)");
            string timestampPrefix = "";
              if (timestampMatch.Success)
            {
                // Extract the timestamp
                string originalTimestamp = timestampMatch.Groups[1].Value;
                
                // Try to parse it as DateTimeOffset
                if (DateTimeOffset.TryParse(originalTimestamp, out DateTimeOffset dateTime))
                {
                    timestampPrefix = $"[{dateTime.ToString("yyyy-MM-dd HH:mm:ss")}]";
                }
            }
            
            // If we have a successfully parsed timestampPrefix, use it
            if (!string.IsNullOrEmpty(timestampPrefix))
            {
                // Remove the ISO timestamp from the actual log content
                string cleanedContent = actualLogContent;
                if (timestampMatch.Success)
                {
                    cleanedContent = actualLogContent.Replace(timestampMatch.Groups[1].Value, "").Trim();
                }
                
                string errorPrefix = isError ? " [DATABASE LOG ERROR]" : "";
                return $"{timestampPrefix}{errorPrefix} {cleanedContent}";
            }
            
            // If no timestamp found in the actual content, use the journalctl timestamp if available
            if (journalMatch.Success)
            {
                string journalTimestamp = journalMatch.Groups[1].Value;
                // Try to parse journalctl timestamp (format: "May 29 20:16:31")
                if (DateTime.TryParseExact($"{DateTime.Now.Year} {journalTimestamp}", "yyyy MMM d HH:mm:ss", 
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsedJournalTime))
                {
                    // Remove any ISO timestamp from the actual content as fallback
                    string cleanedContent = actualLogContent;
                    var isoTimestampMatch = System.Text.RegularExpressions.Regex.Match(actualLogContent, @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)");
                    if (isoTimestampMatch.Success)
                    {
                        cleanedContent = actualLogContent.Replace(isoTimestampMatch.Groups[1].Value, "").Trim();
                    }
                    
                    string formattedTimestamp = $"[{parsedJournalTime.ToString("yyyy-MM-dd HH:mm:ss")}]";
                    string errorPrefix = isError ? " [DATABASE LOG ERROR]" : "";
                    return $"{formattedTimestamp}{errorPrefix} {cleanedContent}";
                }
            }
            else if (journalIsoMatch.Success)
            {
                string journalIsoTimestamp = journalIsoMatch.Groups[1].Value;
                // Try to parse ISO journalctl timestamp
                if (DateTimeOffset.TryParse(journalIsoTimestamp, out DateTimeOffset parsedIsoTime))
                {
                    // Remove any ISO timestamp from the actual content as fallback
                    string cleanedContent = actualLogContent;
                    var isoTimestampMatch = System.Text.RegularExpressions.Regex.Match(actualLogContent, @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)");
                    if (isoTimestampMatch.Success)
                    {
                        cleanedContent = actualLogContent.Replace(isoTimestampMatch.Groups[1].Value, "").Trim();
                    }
                    
                    string formattedTimestamp = $"[{parsedIsoTime.ToString("yyyy-MM-dd HH:mm:ss")}]";
                    string errorPrefix = isError ? " [DATABASE LOG ERROR]" : "";
                    return $"{formattedTimestamp}{errorPrefix} {cleanedContent}";
                }
            }
            
            // Fallback for journalctl format
            // Remove any ISO timestamp from the actual content as final fallback
            string finalCleanedContent = actualLogContent;
            var finalIsoTimestampMatch = System.Text.RegularExpressions.Regex.Match(actualLogContent, @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)");
            if (finalIsoTimestampMatch.Success)
            {
                finalCleanedContent = actualLogContent.Replace(finalIsoTimestampMatch.Groups[1].Value, "").Trim();
            }
            
            string fallbackJournalTimestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
            string errorJournalSuffix = isError ? " [DATABASE LOG ERROR]" : "";
            return $"{fallbackJournalTimestamp}{errorJournalSuffix} {finalCleanedContent}";
        }

        // Fall back to existing timestamp extraction logic for non-journalctl format
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
        string finalFallbackTimestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
        string finalErrorSuffix = debugMode && isError ? " [DATABASE LOG ERROR]" : ""; // No suffix for normal logs
        return $"{finalFallbackTimestamp}{finalErrorSuffix} {logLine}";
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
                // Add a single newline at the end if content exists
                string newContent = batchedLogs.ToString();
                if (!string.IsNullOrEmpty(databaseLogContent) && !databaseLogContent.EndsWith("\n"))
                    databaseLogContent += "\n";
                databaseLogContent += newContent;
                
                if (!string.IsNullOrEmpty(cachedDatabaseLogContent) && !cachedDatabaseLogContent.EndsWith("\n"))
                    cachedDatabaseLogContent += "\n";
                cachedDatabaseLogContent += newContent;

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
                // Force immediate SessionState update for database logs (bypass rate limiting)
                SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
                SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
                
                if (onDatabaseLogUpdated != null)
                {
                    onDatabaseLogUpdated();
                }
            };
        }
    }
    #endregion

    private DateTime ExtractTimestampFromJournalLine(string line)
    {
        try
        {
            // journalctl -o short-iso-precise format: "2024-01-15T10:30:45.123456+00:00 hostname servicename[pid]: message"
            var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+[+-]\d{2}:\d{2})");
            if (match.Success)
            {
                if (DateTimeOffset.TryParse(match.Groups[1].Value, out DateTimeOffset parsed))
                {
                    return parsed.UtcDateTime;
                }
            }
            
            // Fallback: Try parsing other common journalctl timestamp formats
            // short-iso format: "2024-01-15T10:30:45+00:00"
            match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2})");
            if (match.Success)
            {
                if (DateTimeOffset.TryParse(match.Groups[1].Value, out DateTimeOffset parsed))
                {
                    return parsed.UtcDateTime;
                }
            }
            
            // Additional fallback for different possible formats
            // Look for any ISO 8601 timestamp in the line
            match = System.Text.RegularExpressions.Regex.Match(line, @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}))");
            if (match.Success)
            {
                if (DateTimeOffset.TryParse(match.Groups[1].Value, out DateTimeOffset parsed))
                {
                    return parsed.UtcDateTime;
                }
            }
            
            // Last resort: try to parse any timestamp-like pattern and assume it's recent
            match = System.Text.RegularExpressions.Regex.Match(line, @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})");
            if (match.Success)
            {
                if (DateTime.TryParse(match.Groups[1].Value, out DateTime parsed))
                {
                    // Convert to UTC assuming it's in the system timezone
                    return parsed.ToUniversalTime();
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Failed to parse timestamp from line: {line.Substring(0, Math.Min(50, line.Length))}... Error: {ex.Message}");
        }
        
        // If parsing fails, return DateTime.MinValue to indicate failure instead of current time
        // This prevents the fallback from causing infinite repetition
        return DateTime.MinValue;
    }

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