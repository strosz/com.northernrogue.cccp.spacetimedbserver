using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using System;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

// Processes the logs when the server is running in silent mode ///
//test
namespace NorthernRogue.CCCP.Editor {

public class ServerLogProcess
{
    public static bool debugMode = false;
    
    // Log contents
    private string silentServerCombinedLog = "";
    private string cachedModuleLogContent = ""; // Add cached version of module logs
    private string databaseLogContent = "";
    private string cachedDatabaseLogContent = ""; // Add cached version of database logs
    
    // Session state keys
    private const string SessionKeyCombinedLog = "ServerWindow_SilentCombinedLog";
    private const string SessionKeyCachedModuleLog = "ServerWindow_CachedModuleLog"; // Add session key for cached module logs
    private const string SessionKeyDatabaseLog = "ServerWindow_DatabaseLog";
    private const string SessionKeyCachedDatabaseLog = "ServerWindow_CachedDatabaseLog"; // Add session key for cached logs
    private const string SessionKeyDatabaseLogRunning = "ServerWindow_DatabaseLogRunning";
    private const string SessionKeyDatabaseLogStartFresh = "ServerWindow_DatabaseLogStartFresh";
    private const string SessionKeyDatabaseLogFreshStartTime = "ServerWindow_DatabaseLogFreshStartTime";
    private const string SessionKeyModuleLogStartFresh = "ServerWindow_ModuleLogStartFresh";
    private const string SessionKeyModuleLogFreshStartTime = "ServerWindow_ModuleLogFreshStartTime";
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
    [Serializable]
    private class SpacetimeDbJsonLogEntry
    {
        public string timestamp;
        public string level;
        public SpacetimeDbJsonLogFields fields;
        public string target;
    }

    [Serializable]
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
    private const string SpacetimeDatabaseLogServiceName = "spacetimedb-logs.service";
    
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
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath) || !isCustomServer)
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

    public void SwitchModuleSSH(string newModuleName, bool clearDatabaseLogOnSwitch = true)
    {
        if (string.Equals(this.moduleName, newModuleName, StringComparison.OrdinalIgnoreCase))
        {
            if (debugMode) logCallback($"SSH: Module '{newModuleName}' is already active. No switch needed.", 0);
            return;
        }

        if (debugMode) logCallback($"Switching SSH database logs from module '{this.moduleName}' to '{newModuleName}'", 0);

        // Update the module name
        string oldModuleName = this.moduleName;
        this.moduleName = newModuleName;

        // Clear database logs if requested
        if (clearDatabaseLogOnSwitch)
        {
            ClearSSHDatabaseLog(); // This clears in-memory and SessionState for SSH logs

            // Add a separator message to indicate the switch
            string timestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
            string switchMessage = $"{timestamp} [MODULE SWITCHED - SSH] Logs for module '{oldModuleName}' stopped. Now showing logs for module: {newModuleName}\\n";
            
            databaseLogContent += switchMessage;
            cachedDatabaseLogContent += switchMessage;

            // Update SessionState immediately
            SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
            SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
        }

        // Brief pause to allow the old process to fully terminate and release resources
        System.Threading.Thread.Sleep(250); // 250ms delay

        // If the server is running, trigger an immediate refresh of SSH logs to pick up the new module's logs.
        if (serverRunning && isCustomServer)
        {
            EditorApplication.delayCall += async () => 
            {
                await ReadSSHDatabaseLogsAsync();
            };
        }

        // Notify of log update
        onDatabaseLogUpdated?.Invoke();
    }
    #endregion
    
    #region WSL Journalctl Logging
    
    // WSL MODULE LOGS: Read from journalctl for spacetimedb.service using --since timestamp
    // WSL DATABASE LOGS: Read from journalctl for spacetimedb-logs.service
    private DateTime lastWSLModuleLogTimestamp = DateTime.MinValue;
    private DateTime lastWSLDatabaseLogTimestamp = DateTime.MinValue;
    private double lastWSLLogReadTime = 0;
    private const double wslLogReadInterval = 5.0; // Increased from 1.0 to 5.0 seconds to reduce process spawning
    private bool hasScheduledNextWSLCheck = false;
    // Add process protection flags to prevent multiple concurrent processes
    private bool isReadingWSLModuleLogs = false;
    private bool isReadingWSLDatabaseLogs = false;
    private bool databaseLogStartFresh = false; // Track when we want to start fresh (no historical logs)
    private DateTime databaseLogFreshStartTime = DateTime.MinValue; // Track when fresh mode was initiated
    private bool moduleLogStartFresh = false; // Track when module logs should start fresh
    private DateTime moduleLogFreshStartTime = DateTime.MinValue; // Track when module fresh mode was initiated
    
    public void ConfigureWSL(bool isLocalServer)
    {
        // Ensure we're not in custom server mode for WSL
        this.isCustomServer = false;
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Configured WSL journalctl: IsLocalServer={isLocalServer}, IsCustomServer={isCustomServer}");
        
        // Initialize timestamps to 10 minutes ago to avoid getting massive historical logs but still get recent context
        DateTime startTime = DateTime.UtcNow.AddMinutes(-10);
        lastWSLModuleLogTimestamp = startTime;
        lastWSLDatabaseLogTimestamp = startTime;        // Reset process protection flags
        isReadingWSLModuleLogs = false;
        isReadingWSLDatabaseLogs = false;
        databaseLogStartFresh = false; // Will be set to true if clearDatabaseLogAtStart is used
        databaseLogFreshStartTime = DateTime.MinValue;
        moduleLogStartFresh = false; // Will be set to true if clearModuleLogAtStart is used
        moduleLogFreshStartTime = DateTime.MinValue;
    }

    public void StartWSLLogging()
    {   
        // Clear logs if needed
        if (clearModuleLogAtStart)
        {
            ClearWSLModuleLog();
            // When clearing module log at start, set timestamp to current time to only show new logs
            lastWSLModuleLogTimestamp = DateTime.UtcNow;
            moduleLogStartFresh = true; // Flag to prevent historical log fallback
            // Set fresh start time a few seconds back to ensure we capture startup messages
            moduleLogFreshStartTime = DateTime.UtcNow.AddSeconds(-5); // Allow 5 seconds back to capture startup
            
            // Save fresh mode state to SessionState
            SessionState.SetBool(SessionKeyModuleLogStartFresh, moduleLogStartFresh);
            SessionState.SetString(SessionKeyModuleLogFreshStartTime, moduleLogFreshStartTime.ToString("O"));
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Enabled module fresh mode - will reject all logs before {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss} (5 seconds back from start)");
        }
        else
        {
            // If not clearing, preserve existing module fresh mode state from SessionState
            // Only disable fresh mode if it wasn't previously enabled
            if (!moduleLogStartFresh)
            {
                // If fresh mode wasn't active, initialize to 10 minutes ago to get recent context
                lastWSLModuleLogTimestamp = DateTime.UtcNow.AddMinutes(-10);
                if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Module fresh mode not active - will show recent logs");
            }
            else
            {
                // Fresh mode was active from previous session, keep it active
                // Set timestamp to fresh start time to ensure we only read logs from that point
                lastWSLModuleLogTimestamp = moduleLogFreshStartTime;
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Preserving module fresh mode from previous session - will reject logs before {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
            }
        }
        
        if (clearDatabaseLogAtStart)
        {   
            ClearWSLDatabaseLog();
            // When clearing database log at start, set timestamp to current time to only show new logs
            lastWSLDatabaseLogTimestamp = DateTime.UtcNow;
            databaseLogStartFresh = true; // Flag to prevent historical log fallback
            // Set fresh start time a few seconds back to ensure we capture startup messages
            databaseLogFreshStartTime = DateTime.UtcNow.AddSeconds(-5); // Allow 5 seconds back to capture startup
            
            // Save fresh mode state to SessionState
            SessionState.SetBool(SessionKeyDatabaseLogStartFresh, databaseLogStartFresh);
            SessionState.SetString(SessionKeyDatabaseLogFreshStartTime, databaseLogFreshStartTime.ToString("O"));
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Enabled fresh mode - will reject all logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss} (5 seconds back from start)");
        }
        else
        {
        // If not clearing, preserve existing fresh mode state from SessionState
        // Only disable fresh mode if it wasn't previously enabled
        if (!databaseLogStartFresh)
        {
            // If fresh mode wasn't active, initialize to 10 minutes ago to get recent context
            lastWSLDatabaseLogTimestamp = DateTime.UtcNow.AddMinutes(-10);
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Fresh mode not active - will show recent logs");
        }
        else
        {
            // Fresh mode was active from previous session, keep it active
            // Set timestamp to fresh start time to ensure we only read logs from that point
            lastWSLDatabaseLogTimestamp = databaseLogFreshStartTime;
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Preserving fresh mode from previous session - will reject logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }
    }
        
        // Reset process protection flags
        isReadingWSLModuleLogs = false;
        isReadingWSLDatabaseLogs = false;
        
        if (debugMode) logCallback("Started WSL periodic log reading", 1);
        
        // Trigger initial log check to start the periodic reading
        EditorApplication.delayCall += () => {
            CheckWSLLogProcesses(EditorApplication.timeSinceStartup);
        };
    }
    
    // Clear the module log for WSL
    public void ClearWSLModuleLog()
    {
        if (debugMode) logCallback("Clearing WSL module log...", 0);
        
        // Kill any orphaned journalctl processes first
        try
        {
            if (!string.IsNullOrEmpty(userName))
            {
                Process cleanupProcess = new Process();
                cleanupProcess.StartInfo.FileName = "wsl";
                cleanupProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -c \"pkill -f 'journalctl.*{SpacetimeServiceName}' || true\"";
                cleanupProcess.StartInfo.UseShellExecute = false;
                cleanupProcess.StartInfo.CreateNoWindow = true;
                cleanupProcess.StartInfo.RedirectStandardOutput = true;
                cleanupProcess.StartInfo.RedirectStandardError = true;
                
                cleanupProcess.Start();
                cleanupProcess.WaitForExit(3000);
                cleanupProcess.Dispose();
                
                if (debugMode) logCallback("Cleaned up any orphaned WSL module log processes", 1);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Warning: Could not cleanup orphaned processes: {ex.Message}", 0);
        }
        
        silentServerCombinedLog = "";
        cachedModuleLogContent = "";
        SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
        SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);        // Reset timestamp to start fresh and reset protection flag
        lastWSLModuleLogTimestamp = DateTime.UtcNow;
        isReadingWSLModuleLogs = false;
        moduleLogStartFresh = true; // Flag to prevent historical log fallback
        // Set fresh start time a few seconds back to ensure we capture any immediate messages
        moduleLogFreshStartTime = DateTime.UtcNow.AddSeconds(-5); // Allow 5 seconds back to capture immediate logs
        
        // Save fresh mode state to SessionState
        SessionState.SetBool(SessionKeyModuleLogStartFresh, moduleLogStartFresh);
        SessionState.SetString(SessionKeyModuleLogFreshStartTime, moduleLogFreshStartTime.ToString("O"));
        
        if (debugMode) 
        {
            logCallback($"WSL module log cleared successfully - fresh mode enabled, will reject logs before {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss} (5 seconds back from clear)", 1);
            UnityEngine.Debug.Log($"[ServerLogProcess] Manual module clear - enabled fresh mode, will reject all logs before {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }
    }

    // Clear the database log for WSL
    public void ClearWSLDatabaseLog()
    {
        if (debugMode) logCallback("Clearing WSL database log...", 0);
        
        // Kill any orphaned journalctl processes first
        try
        {
            if (!string.IsNullOrEmpty(userName))
            {
                Process cleanupProcess = new Process();
                cleanupProcess.StartInfo.FileName = "wsl";
                cleanupProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -c \"pkill -f 'journalctl.*{SpacetimeDatabaseLogServiceName}' || true\"";
                cleanupProcess.StartInfo.UseShellExecute = false;
                cleanupProcess.StartInfo.CreateNoWindow = true;
                cleanupProcess.StartInfo.RedirectStandardOutput = true;
                cleanupProcess.StartInfo.RedirectStandardError = true;
                
                cleanupProcess.Start();
                cleanupProcess.WaitForExit(3000);
                cleanupProcess.Dispose();
                
                if (debugMode) logCallback("Cleaned up any orphaned WSL database log processes", 1);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Warning: Could not cleanup orphaned processes: {ex.Message}", 0);
        }
        
        databaseLogContent = "";
        cachedDatabaseLogContent = "";
        SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
        SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);        // Reset timestamp to start fresh and reset protection flag
        lastWSLDatabaseLogTimestamp = DateTime.UtcNow;
        isReadingWSLDatabaseLogs = false;
        databaseLogStartFresh = true; // Flag to prevent historical log fallback
        // Set fresh start time a few seconds back to ensure we capture any immediate messages
        databaseLogFreshStartTime = DateTime.UtcNow.AddSeconds(-5); // Allow 5 seconds back to capture immediate logs
        
        // Save fresh mode state to SessionState
        SessionState.SetBool(SessionKeyDatabaseLogStartFresh, databaseLogStartFresh);
        SessionState.SetString(SessionKeyDatabaseLogFreshStartTime, databaseLogFreshStartTime.ToString("O"));
        
        if (debugMode) 
        {
            logCallback($"WSL database log cleared successfully - fresh mode enabled, will reject logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss} (5 seconds back from clear)", 1);
            UnityEngine.Debug.Log($"[ServerLogProcess] Manual clear - enabled fresh mode, will reject all logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }
    }

    // Read module logs from journalctl periodically for WSL
    private async Task ReadWSLModuleLogsAsync()
    {
        if (string.IsNullOrEmpty(userName))
        {
            return;
        }
        
        // Prevent multiple concurrent processes
        if (isReadingWSLModuleLogs)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] WSL module log read already in progress, skipping");
            return;
        }
        
        isReadingWSLModuleLogs = true;
        
        Process readProcess = null;        try
        {
            // Ensure we have a valid timestamp - if not, use recent time to avoid massive log dumps
            if (lastWSLModuleLogTimestamp == DateTime.MinValue || lastWSLModuleLogTimestamp.Year < 2020)
            {
                lastWSLModuleLogTimestamp = DateTime.UtcNow.AddMinutes(-10);
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Invalid WSL module timestamp detected, reset to: {lastWSLModuleLogTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
            
            // Format timestamp for journalctl --since parameter
            string sinceTimestamp = lastWSLModuleLogTimestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
              // Use timeout to prevent orphaned processes and ensure cleanup
            // Try without sudo first - most users can read journalctl logs without sudo if properly configured
            string journalCommand = $"timeout 10s journalctl -u {SpacetimeServiceName} --since \\\"{sinceTimestamp}\\\" --no-pager -o short-iso-precise";
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Reading WSL module logs since: {sinceTimestamp}");
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL command: wsl -d Debian -u {userName} --exec bash -c \"{journalCommand}\"");
            
            readProcess = new Process();
            readProcess.StartInfo.FileName = "wsl";
            readProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -c \"{journalCommand}\"";
            readProcess.StartInfo.UseShellExecute = false;
            readProcess.StartInfo.CreateNoWindow = true;
            readProcess.StartInfo.RedirectStandardOutput = true;
            readProcess.StartInfo.RedirectStandardError = true;
            
            readProcess.Start();
            
            string output = await readProcess.StandardOutput.ReadToEndAsync();
            string error = await readProcess.StandardError.ReadToEndAsync();
            
            // Wait for process to complete with shorter timeout since we have timeout in the command
            if (!readProcess.WaitForExit(12000)) // 12 seconds to allow for the 10s timeout + cleanup
            {
                if (debugMode) UnityEngine.Debug.LogWarning("[ServerLogProcess] WSL module log process timed out, killing process");
                try
                {
                    if (!readProcess.HasExited)
                    {
                        readProcess.Kill();
                        readProcess.WaitForExit(1000);
                    }
                }
                catch (Exception killEx)
                {
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error killing WSL module log process: {killEx.Message}");
                }
            }
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL module logs - Output length: {output?.Length ?? 0}, Error: {error}");
            
            if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Split('\n');
                bool hasNewLogs = false;
                int lineCount = 0;
                DateTime latestTimestamp = lastWSLModuleLogTimestamp;
                
                foreach (string line in lines)
                {
                    if (!string.IsNullOrEmpty(line.Trim()) && !line.Trim().Equals("-- No entries --"))
                    {
                        // Extract timestamp before formatting to ensure we can track progression
                        DateTime logTimestamp = ExtractTimestampFromJournalLine(line.Trim());
                        
                        // If module fresh start mode is enabled, filter out logs before the fresh start time
                        if (moduleLogStartFresh && logTimestamp != DateTime.MinValue && logTimestamp < moduleLogFreshStartTime)
                        {
                            if (debugMode && lineCount < 3) // Only log first few rejections to avoid spam
                            {
                                UnityEngine.Debug.Log($"[ServerLogProcess] Rejecting module log from {logTimestamp:yyyy-MM-dd HH:mm:ss} (before fresh start time {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss})");
                            }
                            
                            // Still track the timestamp progression even if we skip the log content
                            if (logTimestamp > latestTimestamp)
                            {
                                latestTimestamp = logTimestamp;
                            }
                            continue; // Skip processing this log line
                        }
                        
                        string formattedLine = FormatServerLogLine(line.Trim());
                        
                        // Only process if line wasn't filtered out (not already formatted)
                        if (formattedLine != null)
                        {
                            // Check if this log was already processed by checking if it's in the current content
                            if (!silentServerCombinedLog.Contains(formattedLine))
                            {
                                silentServerCombinedLog += formattedLine + "\n";
                                cachedModuleLogContent += formattedLine + "\n";
                                hasNewLogs = true;
                                lineCount++;
                                
                                if (debugMode && lineCount <= 3) // Show first few lines for debugging
                                {
                                    UnityEngine.Debug.Log($"[ServerLogProcess] Added WSL module log line {lineCount}: {formattedLine.Substring(0, Math.Min(100, formattedLine.Length))}...");
                                }
                            }
                            else if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerLogProcess] Skipping duplicate module log line: {formattedLine.Substring(0, Math.Min(80, formattedLine.Length))}...");
                            }
                        }
                        
                        // Track the latest timestamp even if the line was a duplicate
                        if (logTimestamp != DateTime.MinValue && logTimestamp > latestTimestamp)
                        {
                            latestTimestamp = logTimestamp;
                        }
                    }
                }
                
                if (hasNewLogs)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Read {lineCount} new WSL module log lines");
                    
                    // Always advance the timestamp to prevent infinite loops
                    DateTime timestampToUse;
                    
                    if (latestTimestamp > lastWSLModuleLogTimestamp)
                    {
                        timestampToUse = latestTimestamp.AddSeconds(1);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using parsed timestamp: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    else
                    {
                        timestampToUse = lastWSLModuleLogTimestamp.AddSeconds(1);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No valid timestamps found, advancing by 1 second: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    
                    // Update the timestamp
                    lastWSLModuleLogTimestamp = timestampToUse;
                    
                    // Manage log size
                    const int maxLogLength = 75000;
                    const int trimToLength = 50000;
                    if (silentServerCombinedLog.Length > maxLogLength)
                    {
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory silent log from {silentServerCombinedLog.Length} chars.");
                        silentServerCombinedLog = "[... Log Truncated ...]\n" + silentServerCombinedLog.Substring(silentServerCombinedLog.Length - trimToLength);
                        cachedModuleLogContent = "[... Log Truncated ...]\n" + cachedModuleLogContent.Substring(cachedModuleLogContent.Length - trimToLength);
                    }
                    
                    // Update SessionState immediately for WSL logs
                    SessionState.SetString(SessionKeyCombinedLog, silentServerCombinedLog);
                    SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
                    
                    // Notify of log update
                    EditorApplication.delayCall += () => onModuleLogUpdated?.Invoke();
                }
                else 
                {
                    // Even if no new logs, advance timestamp slightly to prevent infinite queries
                    lastWSLModuleLogTimestamp = lastWSLModuleLogTimestamp.AddSeconds(0.5);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No new WSL module log lines found, advancing timestamp slightly to: {lastWSLModuleLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
                }
            }
            else if (debugMode)
            {
                UnityEngine.Debug.Log("[ServerLogProcess] WSL module logs - No output received");
            }              if (!string.IsNullOrEmpty(error))
            {
                if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] WSL module log read error: {error}");
                
                // If service doesn't exist, provide helpful message
                if (error.Contains("Unit " + SpacetimeServiceName + " could not be found"))
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] SpacetimeDB service not found - ensure SpacetimeDB is running as a systemd service");
                }
                // If permissions issue, provide helpful guidance
                else if (error.Contains("permission denied") || error.Contains("Permission denied") || error.Contains("access denied"))
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Permission denied accessing journalctl. User '{userName}' may need to be added to 'systemd-journal' group. Run: sudo usermod -a -G systemd-journal {userName}");
                }
                // If sudo password required, provide guidance
                else if (error.Contains("password is required") || error.Contains("sudo:"))
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Sudo password required for journalctl. Consider adding user '{userName}' to 'systemd-journal' group to avoid needing sudo: sudo usermod -a -G systemd-journal {userName}");
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error reading WSL module logs: {ex.Message}");
        }        finally
        {
            // Reset the protection flag
            isReadingWSLModuleLogs = false;
            
            // Ensure process is always disposed
            try
            {
                if (readProcess != null)
                {
                    if (!readProcess.HasExited)
                    {
                        if (debugMode) UnityEngine.Debug.LogWarning("[ServerLogProcess] Force killing WSL module log process in finally block");
                        readProcess.Kill();
                        readProcess.WaitForExit(1000);
                    }
                    readProcess.Dispose();
                }
            }
            catch (Exception disposeEx)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error disposing WSL module log process: {disposeEx.Message}");
            }
        }
    }

    private async Task ReadWSLDatabaseLogsAsync()
    {
        if (string.IsNullOrEmpty(userName))
        {
            return;
        }
        
        if (string.IsNullOrEmpty(moduleName))
        {
            return; // Can't read database logs without module name
        }
        
        // Prevent multiple concurrent processes
        if (isReadingWSLDatabaseLogs)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] WSL database log read already in progress, skipping");
            return;
        }
          isReadingWSLDatabaseLogs = true;
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Starting WSL database log read - Fresh mode: {databaseLogStartFresh}, Fresh start time: {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        
        Process readProcess = null;try
        {            // Ensure we have a valid timestamp - if not, use appropriate fallback based on whether we want fresh logs
            if (lastWSLDatabaseLogTimestamp == DateTime.MinValue || lastWSLDatabaseLogTimestamp.Year < 2020)
            {
                if (databaseLogStartFresh)
                {
                    // If we want fresh logs only, start from current time
                    lastWSLDatabaseLogTimestamp = DateTime.UtcNow;
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Invalid WSL database timestamp detected, reset to current time for fresh logs: {lastWSLDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    // Otherwise use 10 minutes ago for recent context
                    lastWSLDatabaseLogTimestamp = DateTime.UtcNow.AddMinutes(-10);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Invalid WSL database timestamp detected, reset to 10 minutes ago: {lastWSLDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss}");
                }
            }            // Format timestamp for journalctl --since parameter
            string sinceTimestamp;
            if (databaseLogStartFresh && databaseLogFreshStartTime != DateTime.MinValue)
            {
                // For fresh logs, use the exact fresh start time and rely on application filtering
                sinceTimestamp = databaseLogFreshStartTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using fresh start timestamp for journalctl: {sinceTimestamp} (fresh start time: {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss})");
            }
            else
            {
                sinceTimestamp = lastWSLDatabaseLogTimestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using last timestamp for journalctl: {sinceTimestamp}");
            }
              // Use timeout to prevent orphaned processes and ensure cleanup
            // Try without sudo first - most users can read journalctl logs without sudo if properly configured
            string journalCommand = $"timeout 10s journalctl -u {SpacetimeDatabaseLogServiceName} --since \\\"{sinceTimestamp}\\\" --no-pager -o short-iso-precise";
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Reading WSL database logs since: {sinceTimestamp}");
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL Database command: wsl -d Debian -u {userName} --exec bash -c \"{journalCommand}\"");
            
            readProcess = new Process();
            readProcess.StartInfo.FileName = "wsl";
            readProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -c \"{journalCommand}\"";
            readProcess.StartInfo.UseShellExecute = false;
            readProcess.StartInfo.CreateNoWindow = true;
            readProcess.StartInfo.RedirectStandardOutput = true;
            readProcess.StartInfo.RedirectStandardError = true;
            
            readProcess.Start();
            
            string output = await readProcess.StandardOutput.ReadToEndAsync();
            string error = await readProcess.StandardError.ReadToEndAsync();
            
            // Ensure process completes and is cleaned up properly with shorter timeout
            if (!readProcess.WaitForExit(12000)) // 12 seconds to allow for the 10s timeout + cleanup
            {
                if (debugMode) UnityEngine.Debug.LogWarning("[ServerLogProcess] WSL database log process timed out, killing process");
                try
                {
                    if (!readProcess.HasExited)
                    {
                        readProcess.Kill();
                        readProcess.WaitForExit(1000); // Give it a second to clean up after kill
                    }
                }
                catch (Exception killEx)
                {
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error killing WSL database log process: {killEx.Message}");
                }
            }

            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL database logs - Output length: {output?.Length ?? 0}, Error: {error}");
            
            if (!string.IsNullOrEmpty(output))
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL database raw output: {output.Substring(0, Math.Min(500, output.Length))}...");
                
                var lines = output.Split('\n');
                bool hasNewLogs = false;
                int lineCount = 0;
                int rejectedCount = 0;
                DateTime latestTimestamp = lastWSLDatabaseLogTimestamp;
                
                foreach (string line in lines)
                {   
                    if (!string.IsNullOrEmpty(line.Trim()) && !line.Trim().Equals("-- No entries --"))
                    {
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Processing database log line: {line.Trim().Substring(0, Math.Min(150, line.Trim().Length))}...");
                        
                        // Extract timestamp before formatting to ensure we can track progression
                        DateTime logTimestamp = ExtractTimestampFromJournalLine(line.Trim());
                          string formattedLine = FormatDatabaseLogLine(line.Trim());
                        
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] After formatting - formattedLine is null: {formattedLine == null}, timestamp: {logTimestamp:yyyy-MM-dd HH:mm:ss}");
                        
                        if (formattedLine != null) // Only process if line wasn't filtered out
                        {                            // Additional check for fresh logs: if we're in fresh mode, reject any logs older than fresh start time
                            if (databaseLogStartFresh && databaseLogFreshStartTime != DateTime.MinValue)
                            {
                                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Fresh mode active - comparing log timestamp {logTimestamp:yyyy-MM-dd HH:mm:ss} vs fresh start {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
                                
                                if (logTimestamp != DateTime.MinValue && logTimestamp < databaseLogFreshStartTime)
                                {
                                    rejectedCount++;
                                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Rejecting old database log (timestamp {logTimestamp:yyyy-MM-dd HH:mm:ss} < fresh start {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}): {formattedLine.Substring(0, Math.Min(80, formattedLine.Length))}...");
                                    continue; // Skip this old log entry
                                }
                                else if (logTimestamp == DateTime.MinValue)
                                {
                                    // If we can't extract timestamp but we're in fresh mode, reject it to be safe
                                    rejectedCount++;
                                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Rejecting database log with unparseable timestamp in fresh mode: {formattedLine.Substring(0, Math.Min(80, formattedLine.Length))}...");
                                    continue;
                                }
                                else if (debugMode)
                                {
                                    UnityEngine.Debug.Log($"[ServerLogProcess] Accepting database log (timestamp {logTimestamp:yyyy-MM-dd HH:mm:ss} >= fresh start {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}): {formattedLine.Substring(0, Math.Min(80, formattedLine.Length))}...");
                                }
                            }
                            else if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerLogProcess] Fresh mode not active (startFresh: {databaseLogStartFresh}, freshStartTime: {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}) - accepting log: {formattedLine.Substring(0, Math.Min(80, formattedLine.Length))}...");
                            }
                              // Check if this log was already processed by checking if it's in the current content
                            if (!databaseLogContent.Contains(formattedLine))
                            {
                                // Final simple filter: If fresh mode is active, reject any log older than fresh start time
                                bool shouldAdd = true;
                                if (databaseLogStartFresh && databaseLogFreshStartTime != DateTime.MinValue)
                                {
                                    DateTime logTime = ExtractTimestampFromJournalLine(formattedLine);
                                    if (logTime != DateTime.MinValue && logTime < databaseLogFreshStartTime)
                                    {
                                        shouldAdd = false;
                                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] FINAL FILTER: Rejecting old log {logTime:yyyy-MM-dd HH:mm:ss} < {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}: {formattedLine.Substring(0, Math.Min(80, formattedLine.Length))}...");
                                    }
                                }
                                
                                if (shouldAdd)
                                {
                                    databaseLogContent += formattedLine + "\n";
                                    cachedDatabaseLogContent += formattedLine + "\n";
                                    hasNewLogs = true;
                                    lineCount++;
                                    
                                    if (debugMode && lineCount <= 3) // Show first few lines for debugging
                                    {
                                        UnityEngine.Debug.Log($"[ServerLogProcess] Added WSL database log line {lineCount}: {formattedLine.Substring(0, Math.Min(100, formattedLine.Length))}...");
                                    }
                                }
                            }
                            else if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerLogProcess] Skipping duplicate database log line: {formattedLine.Substring(0, Math.Min(80, formattedLine.Length))}...");
                            }
                            
                            // Track the latest timestamp even if the line was a duplicate
                            if (logTimestamp != DateTime.MinValue && logTimestamp > latestTimestamp)
                            {
                                latestTimestamp = logTimestamp;
                            }
                        }
                    }
                }

                if (hasNewLogs)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Read {lineCount} new WSL database log lines, rejected {rejectedCount} old lines (fresh mode: {databaseLogStartFresh})");
                    
                    // Manage log size similar to module logs
                    const int maxLogLength = 75000;
                    const int trimToLength = 50000;
                    if (databaseLogContent.Length > maxLogLength)
                    {
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating database log from {databaseLogContent.Length} chars.");
                        databaseLogContent = "[... Log Truncated ...]\n" + databaseLogContent.Substring(databaseLogContent.Length - trimToLength);
                        cachedDatabaseLogContent = "[... Log Truncated ...]\n" + cachedDatabaseLogContent.Substring(cachedDatabaseLogContent.Length - trimToLength);
                    }
                    
                    // Update SessionState immediately
                    SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
                    SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
                    
                    // Always advance the timestamp to prevent infinite loops
                    DateTime timestampToUse;
                    
                    if (latestTimestamp > lastWSLDatabaseLogTimestamp)
                    {
                        // Use the actual latest log timestamp if we successfully parsed one
                        timestampToUse = latestTimestamp.AddSeconds(1);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using parsed database timestamp: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff} (was: {lastWSLDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss.fff})");
                    }
                    else
                    {
                        // Fallback: advance by at least 1 second from the last query time to prevent infinite loops
                        timestampToUse = lastWSLDatabaseLogTimestamp.AddSeconds(1);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No valid database timestamps found, advancing by 1 second: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff} (was: {lastWSLDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss.fff})");
                    }                    // Update the timestamp
                    lastWSLDatabaseLogTimestamp = timestampToUse;
                    
                    // Keep fresh mode active - don't disable it unless explicitly cleared again
                    // Fresh mode should persist until the next server start with clearDatabaseLogAtStart=true
                    
                    // Notify of log update
                    EditorApplication.delayCall += () => onDatabaseLogUpdated?.Invoke();
                }                else 
                {
                    // Even if no new logs, advance timestamp slightly to prevent infinite queries
                    lastWSLDatabaseLogTimestamp = lastWSLDatabaseLogTimestamp.AddSeconds(0.5);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No new WSL database log lines found, rejected {rejectedCount} old lines (fresh mode: {databaseLogStartFresh}), advancing timestamp slightly to: {lastWSLDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
                    
                    // Keep fresh mode active - don't disable it just because we filtered out old logs
                    // Fresh mode should persist until the next server start with clearDatabaseLogAtStart=true
                }
            }
            else if (debugMode)
            {
                UnityEngine.Debug.Log("[ServerLogProcess] WSL database logs - No output received");
            }
            
            if (!string.IsNullOrEmpty(error))
            {
                // If service doesn't exist yet, that's expected until user creates it
                if (error.Contains("Unit " + SpacetimeDatabaseLogServiceName + " could not be found"))
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Database log service not found yet - this is expected until the service is created");
                }
                // If permissions issue, provide helpful guidance
                else if (error.Contains("permission denied") || error.Contains("Permission denied") || error.Contains("access denied"))
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Permission denied accessing journalctl for database logs. User '{userName}' may need to be added to 'systemd-journal' group. Run: sudo usermod -a -G systemd-journal {userName}");
                }
                // If sudo password required, provide guidance
                else if (error.Contains("password is required") || error.Contains("sudo:"))
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Sudo password required for journalctl database logs. Consider adding user '{userName}' to 'systemd-journal' group to avoid needing sudo: sudo usermod -a -G systemd-journal {userName}");
                }
                else if (debugMode)
                {
                    UnityEngine.Debug.LogWarning($"[ServerLogProcess] WSL database log read error: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error reading WSL database logs: {ex.Message}");
        }

        finally
        {
            // Reset the protection flag
            isReadingWSLDatabaseLogs = false;
            
            // Ensure process is always disposed
            try
            {
                if (readProcess != null)
                {
                    if (!readProcess.HasExited)
                    {
                        if (debugMode) UnityEngine.Debug.LogWarning("[ServerLogProcess] Force killing WSL database log process in finally block");
                        readProcess.Kill();
                        readProcess.WaitForExit(1000);
                    }
                    readProcess.Dispose();
                }
            }
            catch (Exception disposeEx)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error disposing WSL database log process: {disposeEx.Message}");
            }
        }
    }
    
    public void CheckWSLLogProcesses(double currentTime)
    {
        if (currentTime - lastWSLLogReadTime > wslLogReadInterval)
        {
            lastWSLLogReadTime = currentTime;
            hasScheduledNextWSLCheck = false; // Reset the scheduling flag
            
            if (serverRunning && !isCustomServer)
            {
                EditorApplication.delayCall += async () =>
                {
                    try
                    {
                        await ReadWSLModuleLogsAsync();
                        await ReadWSLDatabaseLogsAsync();
                    }
                    catch (Exception ex)
                    {
                        if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error in CheckWSLLogProcesses: {ex.Message}");
                    }
                };
            }
            
            // Schedule the next check based on wslLogReadInterval
            ScheduleNextWSLLogCheck();
        }
    }
    
    private void ScheduleNextWSLLogCheck()
    {
        if (!hasScheduledNextWSLCheck && serverRunning && !isCustomServer)
        {
            hasScheduledNextWSLCheck = true;
            
            // Schedule the next check after wslLogReadInterval seconds
            EditorApplication.delayCall += () =>
            {
                System.Threading.Tasks.Task.Delay((int)(wslLogReadInterval * 1000)).ContinueWith(_ =>
                {
                    if (serverRunning && !isCustomServer)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            CheckWSLLogProcesses(EditorApplication.timeSinceStartup);
                        };
                    }
                });
            };
        }
    }
    
    public void StopWSLLogging()
    {
        if (debugMode) logCallback("Stopping WSL periodic log reading", 0);
        
        // Kill any orphaned journalctl processes
        try
        {
            if (!string.IsNullOrEmpty(userName))
            {
                Process cleanupProcess = new Process();
                cleanupProcess.StartInfo.FileName = "wsl";
                cleanupProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -c \"pkill -f 'journalctl.*spacetimedb' || true\"";
                cleanupProcess.StartInfo.UseShellExecute = false;
                cleanupProcess.StartInfo.CreateNoWindow = true;
                cleanupProcess.StartInfo.RedirectStandardOutput = true;
                cleanupProcess.StartInfo.RedirectStandardError = true;
                
                cleanupProcess.Start();
                cleanupProcess.WaitForExit(3000);
                cleanupProcess.Dispose();
                
                if (debugMode) logCallback("Cleaned up orphaned WSL journalctl processes", 1);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Warning: Could not cleanup orphaned processes: {ex.Message}", 0);
        }
          // Reset timestamps to 10 minutes ago to avoid massive log dumps on restart
        lastWSLModuleLogTimestamp = DateTime.UtcNow.AddMinutes(-10);
        lastWSLDatabaseLogTimestamp = DateTime.UtcNow.AddMinutes(-10);
        lastWSLLogReadTime = 0;
        hasScheduledNextWSLCheck = false; // Reset scheduling flag to stop the chain
        
        // Reset process protection flags
        isReadingWSLModuleLogs = false;
        isReadingWSLDatabaseLogs = false;
        
        SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
        
        if (debugMode) logCallback("WSL log reading stopped", 0);
    }

    public void SwitchModuleWSL(string newModuleName, bool clearDatabaseLogOnSwitch = true)
    {
        if (string.Equals(this.moduleName, newModuleName, StringComparison.OrdinalIgnoreCase))
        {
            if (debugMode) logCallback($"WSL: Module '{newModuleName}' is already active. No switch needed.", 0);
            return;
        }

        if (debugMode) logCallback($"Switching WSL database logs from module '{this.moduleName}' to '{newModuleName}'", 0);

        // Update the module name
        string oldModuleName = this.moduleName;
        this.moduleName = newModuleName;

        // Clear database logs if requested
        if (clearDatabaseLogOnSwitch)
        {
            ClearWSLDatabaseLog(); // This clears in-memory and SessionState for WSL logs

            // Add a separator message to indicate the switch
            string timestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
            string switchMessage = $"{timestamp} [MODULE SWITCHED - WSL] Logs for module '{oldModuleName}' stopped. Now showing logs for module: {newModuleName}\\n";
            
            databaseLogContent += switchMessage;
            cachedDatabaseLogContent += switchMessage;

            // Update SessionState immediately
            SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
            SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
        }

        // Brief pause to allow the old process to fully terminate and release resources
        System.Threading.Thread.Sleep(250); // 250ms delay

        // If the server is running, trigger an immediate refresh of WSL logs to pick up the new module's logs.
        if (serverRunning && !isCustomServer)
        {
            EditorApplication.delayCall += async () => 
            {
                await ReadWSLDatabaseLogsAsync();
            };
        }

        // Notify of log update
        onDatabaseLogUpdated?.Invoke();
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
        cachedDatabaseLogContent = SessionState.GetString(SessionKeyCachedDatabaseLog, ""); // Load cached logs        // Load fresh mode state from session state
        databaseLogStartFresh = SessionState.GetBool(SessionKeyDatabaseLogStartFresh, false);
        string freshStartTimeString = SessionState.GetString(SessionKeyDatabaseLogFreshStartTime, "");
        if (DateTime.TryParse(freshStartTimeString, out DateTime parsedFreshStartTime))
        {
            databaseLogFreshStartTime = parsedFreshStartTime;
        }
        else
        {
            databaseLogFreshStartTime = DateTime.MinValue;
        }
        
        // Load module fresh mode state from session state
        moduleLogStartFresh = SessionState.GetBool(SessionKeyModuleLogStartFresh, false);
        string moduleFreshStartTimeString = SessionState.GetString(SessionKeyModuleLogFreshStartTime, "");
        if (DateTime.TryParse(moduleFreshStartTimeString, out DateTime parsedModuleFreshStartTime))
        {
            moduleLogFreshStartTime = parsedModuleFreshStartTime;
        }
        else
        {
            moduleLogFreshStartTime = DateTime.MinValue;
        }
        
        if (debugMode && databaseLogStartFresh)
        {
            UnityEngine.Debug.Log($"[ServerLogProcess] Restored database fresh mode from SessionState - will reject logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }
        
        if (debugMode && moduleLogStartFresh)
        {
            UnityEngine.Debug.Log($"[ServerLogProcess] Restored module fresh mode from SessionState - will reject logs before {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }

        processingCts = new CancellationTokenSource();
        StartLogLimiter();
    }
    
    public void Configure(string moduleName, string serverDirectory, bool clearModuleLogAtStart, bool clearDatabaseLogAtStart, string userName)
    {
        bool moduleChanged = !string.Equals(this.moduleName, moduleName, StringComparison.OrdinalIgnoreCase);
        string oldModuleName = this.moduleName; // Store old module name for logging

        this.moduleName = moduleName;
        this.serverDirectory = serverDirectory;
        this.clearModuleLogAtStart = clearModuleLogAtStart;
        this.clearDatabaseLogAtStart = clearDatabaseLogAtStart;
        this.userName = userName;
        // Clear old logs from SessionState if requested
        if (clearDatabaseLogAtStart)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] clearDatabaseLogAtStart is true, clearing old database logs from SessionState and enabling fresh mode");
            databaseLogContent = "";
            cachedDatabaseLogContent = "";
            SessionState.SetString(SessionKeyDatabaseLog, "");
            SessionState.SetString(SessionKeyCachedDatabaseLog, "");
            
            // Enable fresh mode when explicitly requested
            databaseLogStartFresh = true;
            databaseLogFreshStartTime = DateTime.UtcNow;
            SessionState.SetBool(SessionKeyDatabaseLogStartFresh, databaseLogStartFresh);
            SessionState.SetString(SessionKeyDatabaseLogFreshStartTime, databaseLogFreshStartTime.ToString("O"));
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Fresh mode enabled in Configure - will reject logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }
        else if (debugMode && databaseLogStartFresh)
        {
            // Keep existing fresh mode if it was previously enabled
            UnityEngine.Debug.Log($"[ServerLogProcess] Preserving existing fresh mode from SessionState - will reject logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }
        
        if (clearModuleLogAtStart)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] clearModuleLogAtStart is true, clearing old module logs from SessionState");
            silentServerCombinedLog = "";
            cachedModuleLogContent = "";
            SessionState.SetString(SessionKeyCombinedLog, "");
            SessionState.SetString(SessionKeyCachedModuleLog, "");
        }
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Configured with username: {this.userName}, moduleName: {this.moduleName}, serverDirectory: {this.serverDirectory}");

        // If module changed and server is running, switch to the new module
        if (moduleChanged && serverRunning && !string.IsNullOrEmpty(this.moduleName))
        {
            if (debugMode) logCallback($"Module changed from '{oldModuleName}' to '{this.moduleName}' while server running, switching database logs...", 0);
            
            if (isCustomServer)
            {
                // For SSH, we need a specific method to handle the log source change
                SwitchModuleSSH(this.moduleName, true); // Clear logs when switching
            }
            else
            {
                // For local/WSL, use the general SwitchModule method
                SwitchModule(this.moduleName, true); // Clear logs when switching
            }
        }
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

    public void SwitchModule(string newModuleName, bool clearDatabaseLogOnSwitch = true)
    {
        if (string.Equals(this.moduleName, newModuleName, StringComparison.OrdinalIgnoreCase))
        {
            if (debugMode) logCallback($"Module '{newModuleName}' is already active. No switch needed.", 0);
            return;
        }

        if (debugMode) logCallback($"Switching database logs from module '{this.moduleName}' to '{newModuleName}'", 0);

        string oldModuleName = this.moduleName;
        this.moduleName = newModuleName;

        // Use appropriate method based on server type
        if (isCustomServer)
        {
            SwitchModuleSSH(newModuleName, clearDatabaseLogOnSwitch);
        }
        else
        {
            SwitchModuleWSL(newModuleName, clearDatabaseLogOnSwitch);
        }
    }
    
    // Force refresh in-memory logs from SessionState - used when ServerOutputWindow gets focus
    public void ForceRefreshLogsFromSessionState()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Force refreshing logs from SessionState");
        
        string sessionModuleLog = SessionState.GetString(SessionKeyCombinedLog, "");
        string sessionDatabaseLog = SessionState.GetString(SessionKeyDatabaseLog, "");
        string sessionCachedModuleLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
        string sessionCachedDatabaseLog = SessionState.GetString(SessionKeyCachedDatabaseLog, "");
        
        if (!string.IsNullOrEmpty(sessionModuleLog) && sessionModuleLog.Length > silentServerCombinedLog.Length)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Updating module log from session state: {sessionModuleLog.Length} chars");
            silentServerCombinedLog = sessionModuleLog;
        }
        
        if (!string.IsNullOrEmpty(sessionDatabaseLog) && sessionDatabaseLog.Length > databaseLogContent.Length)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Updating database log from session state: {sessionDatabaseLog.Length} chars");
            databaseLogContent = sessionDatabaseLog;
        }
        
        if (!string.IsNullOrEmpty(sessionCachedModuleLog) && sessionCachedModuleLog.Length > cachedModuleLogContent.Length)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Updating cached module log from session state: {sessionCachedModuleLog.Length} chars");
            cachedModuleLogContent = sessionCachedModuleLog;
        }
        
        if (!string.IsNullOrEmpty(sessionCachedDatabaseLog) && sessionCachedDatabaseLog.Length > cachedDatabaseLogContent.Length)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Updating cached database log from session state: {sessionCachedDatabaseLog.Length} chars");
            cachedDatabaseLogContent = sessionCachedDatabaseLog;
        }
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
    
    // Force WSL log refresh - triggers new journalctl commands immediately for WSL
    public void ForceWSLLogRefresh()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Force triggering WSL log refresh");
        
        if (serverRunning && !isCustomServer)
        {
            EditorApplication.delayCall += async () =>
            {
                try
                {
                    await ReadWSLModuleLogsAsync();
                    await ReadWSLDatabaseLogsAsync();
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error in ForceWSLLogRefresh: {ex.Message}");
                }
            };
        }
    }
    
    #region Log Methods
    
    public void StartLogging()
    {
        if (isCustomServer)
        {
            StartSSHLogging();
        }
        else
        {
            StartWSLLogging();
        }
    }
    
    public void StopLogging()
    {
        if (isCustomServer)
        {
            StopSSHLogging();
        }
        else
        {
            StopWSLLogging();
        }
    }
    
    public void CheckLogProcesses(double currentTime)
    {
        if (isCustomServer)
        {
            CheckSSHLogProcesses(currentTime);
        }
        else
        {
            CheckWSLLogProcesses(currentTime);
        }
    }
    
    public void ClearModuleLogFile()
    {
        if (isCustomServer)
        {
            ClearSSHModuleLogFile();
        }
        else
        {
            ClearWSLModuleLog();
        }
    }
    
    public void ClearDatabaseLog()
    {
        if (isCustomServer)
        {
            ClearSSHDatabaseLog();
        }
        else
        {
            ClearWSLDatabaseLog();
        }
    }
    #endregion
    
    #region Log Limiter

    private void StartLogLimiter()
    {
        processingTask = Task.Run(async () =>
        {
            while (!processingCts.Token.IsCancellationRequested)
            {
                try
                {
                    ProcessDatabaseLogQueue();
                    await Task.Delay(PROCESS_INTERVAL_MS, processingCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error in log limiter: {ex.Message}");
                }
            }
        }, processingCts.Token);
    }

    private void ProcessDatabaseLogQueue()
    {
        if (isProcessing) return;
        
        lock (logLock)
        {
            isProcessing = true;
        }

        try
        {
            StringBuilder batchBuffer = new StringBuilder();
            int processedCount = 0;
            const int maxBatchSize = 50;

            while (databaseLogQueue.TryDequeue(out string logLine) && processedCount < maxBatchSize)
            {
                batchBuffer.AppendLine(logLine);
                processedCount++;
            }

            if (processedCount > 0)
            {
                string batchContent = batchBuffer.ToString();
                  // Update logs on main thread
                EditorApplication.delayCall += () =>
                {
                    databaseLogContent += batchContent;
                    cachedDatabaseLogContent += batchContent;

                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Added {processedCount} database log lines. Total length: {databaseLogContent.Length} chars");

                    // Manage log size
                    if (databaseLogContent.Length > BUFFER_SIZE)
                    {
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating database log from {databaseLogContent.Length} chars");
                        databaseLogContent = "[... Log Truncated ...]\n" + databaseLogContent.Substring(databaseLogContent.Length - TARGET_SIZE);
                        cachedDatabaseLogContent = "[... Log Truncated ...]\n" + cachedDatabaseLogContent.Substring(cachedDatabaseLogContent.Length - TARGET_SIZE);
                    }

                    // Always update SessionState immediately when we have new database log content
                    SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
                    SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
                    
                    // Update timestamp for periodic updates
                    DateTime now = DateTime.Now;

                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Updated SessionState with database log content. Length: {databaseLogContent.Length}");

                    onDatabaseLogUpdated?.Invoke();
                };
            }
        }
        finally
        {
            lock (logLock)
            {
                isProcessing = false;
            }
        }
    }
    #endregion    

    // Helper method to format server log lines with consistent timestamps    
    private string FormatServerLogLine(string logLine, bool isError = false)
    {
        if (string.IsNullOrEmpty(logLine))
            return logLine;
            
        // Filter out journalctl boot messages and other unwanted system messages
        if (logLine.Contains("-- Boot ") || 
            logLine.StartsWith("-- Boot ") ||
            logLine.Contains("systemd[1]:") ||
            logLine.Trim().Equals("-- No entries --"))
        {
            return null; // Filter out unwanted system messages
        }
            
        // Check if the line is already formatted with our timestamp prefix
        if (System.Text.RegularExpressions.Regex.IsMatch(logLine, @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]"))
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Module log line already formatted, skipping: {logLine.Substring(0, Math.Min(100, logLine.Length))}");
            return null; // Return null to indicate this line should be skipped (already processed)
        }

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

    // Helper method to extract and format timestamps from log lines
    private string FormatDatabaseLogLine(string logLine, bool isError = false)
    {
        if (string.IsNullOrEmpty(logLine))
            return logLine;
              // Check if the line is already formatted with our timestamp prefix
        if (System.Text.RegularExpressions.Regex.IsMatch(logLine, @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]"))
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Log line already formatted, skipping: {logLine.Substring(0, Math.Min(100, logLine.Length))}");
            return null; // Return null to indicate this line should be skipped (already processed)
        }

        // Filter out specific error messages when not in debug mode
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

        // Filter out systemd service messages and repeated connection messages
        if (logLine.Contains("systemd[1]:") ||
            logLine.Contains("client error (Connect)") ||
            logLine.Contains("os error 111") ||
            logLine.Contains("Error: error sending request") ||
            logLine.Contains("spacetimedb-logs.service: Deactivated successfully") ||
            logLine.Contains("spacetimedb-logs.service: Main process exited") ||
            logLine.Contains("spacetimedb-logs.service: Failed with result") ||
            logLine.Contains("Stopped spacetimedb-logs.service") ||
            logLine.Contains("Started spacetimedb-logs.service") ||
            logLine.Contains("-- Boot ") ||  // Filter out journalctl boot messages
            logLine.Contains("Caused by:") ||
            logLine.StartsWith("-- Boot "))   // Filter out journalctl boot messages at start of line
        {
            return null; // Filter out systemd service messages and reconnection spam
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

    private DateTime ExtractTimestampFromJournalLine(string line)
    {
        try
        {
            // Primary pattern: journalctl -o short-iso-precise format with hostname
            // "2025-06-21T20:00:52.866169+02:00 hostname servicename[pid]: message"
            var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+[+-]\d{2}:\d{2})\s+\S+");
            if (match.Success)
            {
                if (DateTimeOffset.TryParse(match.Groups[1].Value, out DateTimeOffset parsed))
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Extracted timestamp (pattern 1): {parsed.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff} from line: {line.Substring(0, Math.Min(80, line.Length))}");
                    return parsed.UtcDateTime;
                }
            }
            
            // Secondary pattern: shorter precision
            // "2025-06-21T20:00:52+02:00 hostname servicename[pid]: message"
            match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2})\s+\S+");
            if (match.Success)
            {
                if (DateTimeOffset.TryParse(match.Groups[1].Value, out DateTimeOffset parsed))
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Extracted timestamp (pattern 2): {parsed.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff} from line: {line.Substring(0, Math.Min(80, line.Length))}");
                    return parsed.UtcDateTime;
                }
            }
            
            // Third pattern: any ISO 8601 timestamp at the start of the line
            match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}))");
            if (match.Success)
            {
                if (DateTimeOffset.TryParse(match.Groups[1].Value, out DateTimeOffset parsed))
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Extracted timestamp (pattern 3): {parsed.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff} from line: {line.Substring(0, Math.Min(80, line.Length))}");
                    return parsed.UtcDateTime;
                }
            }
              // Fourth pattern: formatted timestamps like "[2025-06-21 20:21:08]" (already processed logs)
            match = System.Text.RegularExpressions.Regex.Match(line, @"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]");
            if (match.Success)
            {
                if (DateTime.TryParse(match.Groups[1].Value, out DateTime parsed))
                {
                    // Assume already processed logs are in UTC (since they come from journalctl which uses system time)
                    DateTime utcTime = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Extracted timestamp (pattern 4): {utcTime:yyyy-MM-dd HH:mm:ss.fff} from line: {line.Substring(0, Math.Min(80, line.Length))}");
                    return utcTime;
                }
            }
            
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] No timestamp pattern matched for line: {line.Substring(0, Math.Min(100, line.Length))}...");
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Failed to parse timestamp from line: {line.Substring(0, Math.Min(50, line.Length))}... Error: {ex.Message}");
        }
        
        // If parsing fails, return DateTime.MinValue to indicate failure
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
        {   
            Process process = new Process();
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
    
    // Helper method to run journalctl commands with fallback to sudo if needed
    private async Task<(string output, string error, bool success)> RunJournalctlCommandAsync(string serviceName, string sinceTimestamp)
    {
        if (string.IsNullOrEmpty(userName))
        {
            return ("", "Username not configured", false);
        }
        
        // First try without sudo
        string journalCommand = $"timeout 10s journalctl -u {serviceName} --since \\\"{sinceTimestamp}\\\" --no-pager -o short-iso-precise";
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Trying journalctl without sudo: {journalCommand}");
        
        Process readProcess = null;
        try
        {
            readProcess = new Process();
            readProcess.StartInfo.FileName = "wsl";
            readProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -c \"{journalCommand}\"";
            readProcess.StartInfo.UseShellExecute = false;
            readProcess.StartInfo.CreateNoWindow = true;
            readProcess.StartInfo.RedirectStandardOutput = true;
            readProcess.StartInfo.RedirectStandardError = true;
            
            readProcess.Start();
            
            string output = await readProcess.StandardOutput.ReadToEndAsync();
            string error = await readProcess.StandardError.ReadToEndAsync();
            
            if (!readProcess.WaitForExit(12000))
            {
                if (!readProcess.HasExited)
                {
                    readProcess.Kill();
                    readProcess.WaitForExit(1000);
                }
                return ("", "Process timed out", false);
            }
            
            // If we get permission denied or similar, it might work with sudo
            bool needsSudo = !string.IsNullOrEmpty(error) && 
                           (error.Contains("permission denied") || 
                            error.Contains("Permission denied") || 
                            error.Contains("access denied") ||
                            error.Contains("Operation not permitted"));
            
            if (needsSudo && debugMode)
            {
                UnityEngine.Debug.Log($"[ServerLogProcess] Permission denied without sudo, will try with sudo next time. Consider adding user to systemd-journal group: sudo usermod -a -G systemd-journal {userName}");
            }
            
            readProcess.Dispose();
            return (output, error, !needsSudo);
        }
        catch (Exception ex)
        {
            if (readProcess != null && !readProcess.HasExited)
            {
                try { readProcess.Kill(); readProcess.WaitForExit(1000); } catch { }
            }
            readProcess?.Dispose();
            
            if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Exception in journalctl command: {ex.Message}");
            return ("", ex.Message, false);
        }
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games