using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using System;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NorthernRogue.CCCP.Editor.Settings;

// Processes the logs of the server for all server modes ///

namespace NorthernRogue.CCCP.Editor {

public class ServerLogProcess
{
    public static bool debugMode = false;
    
    // Log contents
    private string moduleLogContent = "";
    private string cachedModuleLogContent = "";
    private string databaseLogContent = "";
    private string cachedDatabaseLogContent = "";
    
    // Session state keys
    private const string SessionKeyModuleLog = "ServerWindow_ModuleLog";
    private const string SessionKeyCachedModuleLog = "ServerWindow_CachedModuleLog";
    private const string SessionKeyDatabaseLog = "ServerWindow_DatabaseLog";
    private const string SessionKeyCachedDatabaseLog = "ServerWindow_CachedDatabaseLog";
    private const string SessionKeyDatabaseLogRunning = "ServerWindow_DatabaseLogRunning";
    private const string SessionKeyDatabaseLogStartFresh = "ServerWindow_DatabaseLogStartFresh";
    private const string SessionKeyDatabaseLogFreshStartTime = "ServerWindow_DatabaseLogFreshStartTime";
    private const string SessionKeyModuleLogStartFresh = "ServerWindow_ModuleLogStartFresh";
    private const string SessionKeyModuleLogFreshStartTime = "ServerWindow_ModuleLogFreshStartTime";
    private const string SessionKeySSHModuleLogTimestamp = "ServerWindow_SSHModuleLogTimestamp";
    private const string SessionKeySSHDatabaseLogTimestamp = "ServerWindow_SSHDatabaseLogTimestamp";
    
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
    
    // Reference to the WSL processor for executing commands
    private ServerWSLProcess wslProcess;
    
    // Reference to the Docker processor for executing commands
    private ServerDockerProcess dockerProcessor;
    
    // Reference to the Custom processor for executing SSH commands
    private ServerCustomProcess customProcessor;

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

    // WSL Logging Variables

    // WSL MODULE LOGS: Read from journalctl for spacetimedb.service using --since timestamp
    // WSL DATABASE LOGS: Read from journalctl for spacetimedb-logs.service
    private DateTime lastWSLModuleLogTimestamp = DateTime.MinValue;
    private DateTime lastWSLDatabaseLogTimestamp = DateTime.MinValue;
    private double lastWSLLogReadTime = 0;
    private double wslLogReadInterval = 1.0;
    // Add process protection flags to prevent multiple concurrent processes
    private bool isReadingWSLModuleLogs = false;
    private bool isReadingWSLDatabaseLogs = false;
    private bool databaseLogStartFresh = false; // Track when we want to start fresh (no historical logs)
    private DateTime databaseLogFreshStartTime = DateTime.MinValue; // Track when fresh mode was initiated
    private bool moduleLogStartFresh = false; // Track when module logs should start fresh
    private DateTime moduleLogFreshStartTime = DateTime.MinValue; // Track when module fresh mode was initiated

    // Prevention of multiple simultaneous WSL log processing tasks
    private bool isWSLLogProcessingScheduled = false;

    // Docker Logging Variables
    
    // DOCKER MODULE LOGS: Read from docker logs command
    // DOCKER DATABASE LOGS: Read from spacetime logs --server <local|maincloud> <module> inside container
    private double lastDockerLogReadTime = 0;
    private double dockerLogReadInterval = 1.0;
    private bool isReadingDockerModuleLogs = false;
    private bool isReadingDockerDatabaseLogs = false;
    private bool isDockerServer = false;
    
    // Timestamp-based filtering for Docker logs (much more robust than hashing)
    private DateTime dockerModuleLogCutoffTimestamp = DateTime.MinValue; // Only show logs after this time
    private DateTime dockerDatabaseLogCutoffTimestamp = DateTime.MinValue; // Only show logs after this time
    
    // Prevention of multiple simultaneous Docker log processing tasks
    private bool isDockerLogProcessingScheduled = false;
    
    // Track current server mode for proper command construction
    private NorthernRogue.CCCP.Editor.ServerManager.ServerMode currentServerMode = NorthernRogue.CCCP.Editor.ServerManager.ServerMode.WSLServer;

    // SSH Logging Variables

    // SSH MODULE LOGS: Read from journalctl for spacetimedb.service using --since timestamp
    // SSH DATABASE LOGS: Read from journalctl for spacetimedb-logs.service
    
    // SSH details
    private string sshUser = "";
    private string sshHost = "";
    private string sshKeyPath = "";
    private bool isCustomServer = false;
    private string remoteSpacetimePath = "spacetime"; // Default path
    private DateTime lastModuleLogTimestamp = DateTime.MinValue;
    private DateTime lastDatabaseLogTimestamp = DateTime.MinValue;
    private double lastLogReadTime = 0;
    private double sshLogReadInterval = 1.0;

    // Deduplication tracking for SSH logs
    private HashSet<string> recentModuleLogHashes = new HashSet<string>();
    private HashSet<string> recentDatabaseLogHashes = new HashSet<string>();
    private const int MAX_RECENT_LOG_HASHES = 1000;

    // Prevention of multiple simultaneous SSH log processing tasks
    private bool isSSHLogProcessingScheduled = false;

    // Service names for journalctl
    private const string SpacetimeServiceName = "spacetimedb.service";
    private const string SpacetimeDatabaseLogServiceName = "spacetimedb-logs.service";

    // Path for remote server logs (kept for fallback/reference)
    public const string CustomServerCombinedLogPath = "/var/log/spacetimedb/spacetimedb.log";

    // Config file paths for dynamic module switching (using persistent user home locations)
    private string GetWSLModuleConfigFile() => $"/home/{userName}/.local/current_spacetime_module";
    private string GetSSHModuleConfigFile() => $"/home/{sshUser}/.local/current_spacetime_module";
    
    // Read current module from WSL config file
    private async Task<string> GetCurrentWSLModuleAsync()
    {
        if (wslProcess == null || string.IsNullOrEmpty(userName))
            return null;
            
        try
        {
            string configFile = GetWSLModuleConfigFile();
            string readCommand = $"cat {configFile} 2>/dev/null || echo ''";
            
            // Use a synchronous approach since we don't have async WSL commands
            var tempFile = System.IO.Path.GetTempFileName();
            string wslCommand = $"wsl -d Debian -u {userName} bash -c \"{readCommand}\" > \"{tempFile}\"";
            
            var process = new System.Diagnostics.Process()
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {wslCommand}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            await Task.Run(() => process.WaitForExit());
            
            if (System.IO.File.Exists(tempFile))
            {
                string content = await System.IO.File.ReadAllTextAsync(tempFile);
                System.IO.File.Delete(tempFile);
                return content.Trim();
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Error reading WSL config file: {ex.Message}", -1);
        }
        
        return null;
    }
    
    // Read current module from SSH config file
    private async Task<string> GetCurrentSSHModuleAsync()
    {
        if (customProcessor == null || string.IsNullOrEmpty(sshUser))
            return null;
            
        try
        {
            string configFile = GetSSHModuleConfigFile();
            string readCommand = $"cat {configFile} 2>/dev/null || echo ''";
            
            var result = await customProcessor.RunCustomCommandAsync(readCommand);
            if (result.success)
            {
                return result.output.Trim();
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Error reading SSH config file: {ex.Message}", -1);
        }
        
        return null;
    }

    public void SyncSettings()
    {
        try
        {
            // Server Mode
            currentServerMode = CCCPSettingsAdapter.GetServerMode();
            bool shouldBeCustomServer = currentServerMode == NorthernRogue.CCCP.Editor.ServerManager.ServerMode.CustomServer;
            
            // For MaincloudServer mode, check the localCLIProvider to determine if Docker should be used for logging
            bool shouldBeDockerServer = currentServerMode == NorthernRogue.CCCP.Editor.ServerManager.ServerMode.DockerServer ||
                                       (currentServerMode == NorthernRogue.CCCP.Editor.ServerManager.ServerMode.MaincloudServer && 
                                        CCCPSettingsAdapter.GetLocalCLIProvider() == "Docker");

            if (isCustomServer != shouldBeCustomServer)
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Server mode sync: isCustomServer was {isCustomServer}, should be {shouldBeCustomServer} (ServerMode: {currentServerMode})");
                isCustomServer = shouldBeCustomServer;
            }
            
            if (isDockerServer != shouldBeDockerServer)
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Server mode sync: isDockerServer was {isDockerServer}, should be {shouldBeDockerServer} (ServerMode: {currentServerMode}, LocalCLIProvider: {CCCPSettingsAdapter.GetLocalCLIProvider()})");
                isDockerServer = shouldBeDockerServer;
            }

            // SSH User
            string sshUser = CCCPSettingsAdapter.GetSSHUserName();
            if (this.sshUser != sshUser)
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] SSH user sync: sshUser was {this.sshUser}, now {sshUser}");
                this.sshUser = sshUser;
            }

            // SSH Host
            string sshHost = ServerUtilityProvider.ExtractHostname(CCCPSettingsAdapter.GetCustomServerUrl());
            if (this.sshHost != sshHost)
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] SSH host sync: sshHost was {this.sshHost}, now {sshHost}");
                this.sshHost = sshHost;
            }

            // SSH Key Path
            string sshKeyPath = CCCPSettingsAdapter.GetSSHPrivateKeyPath();
            if (this.sshKeyPath != sshKeyPath)
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] SSH key path sync: sshKeyPath was {this.sshKeyPath}, now {sshKeyPath}");
                this.sshKeyPath = sshKeyPath;
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerLogProcess] Error syncing server mode flags: {ex.Message}");
            }
        }
    }

    #region SSH Logging
    
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
        
        // Clear deduplication cache for fresh start
        recentModuleLogHashes.Clear();
        recentDatabaseLogHashes.Clear();
        
        // Initialize timestamps to 1 hour ago to avoid getting massive historical logs
        // Only set if not already restored from SessionState (domain reload)
        if (lastModuleLogTimestamp == DateTime.MinValue)
        {
            lastModuleLogTimestamp = DateTime.UtcNow.AddHours(-1);
            SessionState.SetString(SessionKeySSHModuleLogTimestamp, lastModuleLogTimestamp.ToString("O"));
        }
        
        if (lastDatabaseLogTimestamp == DateTime.MinValue)
        {
            lastDatabaseLogTimestamp = DateTime.UtcNow.AddHours(-1);
            SessionState.SetString(SessionKeySSHDatabaseLogTimestamp, lastDatabaseLogTimestamp.ToString("O"));
        }
        
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
            }        
        }
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
            clearProcess.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            clearProcess.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            
            clearProcess.Start();
            clearProcess.WaitForExit(5000); // Wait up to 5 seconds
            
            // Always clear the in-memory log and cached version when explicitly called from Clear Logs button
            // This ensures the clearing works regardless of restored content
            moduleLogContent = "";
            cachedModuleLogContent = "";
            SessionState.SetString(SessionKeyModuleLog, moduleLogContent);
            SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
            
            // Clear deduplication cache for fresh start
            recentModuleLogHashes.Clear();
            
            if (debugMode) logCallback("Cleared SSH module logs and SessionState", 1);
            
            // Reset timestamp to start fresh
            lastModuleLogTimestamp = DateTime.UtcNow;
            
            // Enable fresh start mode to prevent historical log fallback (similar to WSL implementation)
            moduleLogStartFresh = true;
            // Set fresh start time a few seconds back to ensure we capture any immediate messages
            moduleLogFreshStartTime = DateTime.UtcNow.AddSeconds(-5); // Allow 5 seconds back to capture immediate logs
            
            // Save fresh mode state to SessionState
            SessionState.SetBool(SessionKeyModuleLogStartFresh, moduleLogStartFresh);
            SessionState.SetString(SessionKeyModuleLogFreshStartTime, moduleLogFreshStartTime.ToString("O"));
            
            // Save updated timestamp to session state
            SessionState.SetString(SessionKeySSHModuleLogTimestamp, lastModuleLogTimestamp.ToString("O"));
            
            if (debugMode) 
            {
                logCallback($"SSH module log cleared successfully - fresh mode enabled, will reject logs before {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss} (5 seconds back from clear)", 1);
                UnityEngine.Debug.Log($"[ServerLogProcess] Manual SSH module clear - enabled fresh mode, will reject all logs before {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
            }
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
        
        // Always clear the in-memory log and cached version when explicitly called from Clear Logs button
        // This ensures the clearing works regardless of restored content
        databaseLogContent = "";
        cachedDatabaseLogContent = "";
        SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
        SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
        
        // Clear deduplication cache
        recentDatabaseLogHashes.Clear();
        
        if (debugMode) logCallback("Cleared SSH database logs and SessionState", 1);
        
        // Reset timestamp to start fresh
        lastDatabaseLogTimestamp = DateTime.UtcNow;
        
        // Enable fresh start mode to prevent historical log fallback (similar to WSL and module implementation)
        databaseLogStartFresh = true;
        // Set fresh start time a few seconds back to ensure we capture any immediate messages
        databaseLogFreshStartTime = DateTime.UtcNow.AddSeconds(-5); // Allow 5 seconds back to capture immediate logs
        
        // Save fresh mode state to SessionState
        SessionState.SetBool(SessionKeyDatabaseLogStartFresh, databaseLogStartFresh);
        SessionState.SetString(SessionKeyDatabaseLogFreshStartTime, databaseLogFreshStartTime.ToString("O"));
        
        // Save updated timestamp to session state
        SessionState.SetString(SessionKeySSHDatabaseLogTimestamp, lastDatabaseLogTimestamp.ToString("O"));
        
        if (debugMode) 
        {
            logCallback($"SSH database log cleared successfully - fresh mode enabled, will reject logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss} (5 seconds back from clear)", 1);
            UnityEngine.Debug.Log($"[ServerLogProcess] Manual SSH database clear - enabled fresh mode, will reject all logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }
    }
    
    // Read module logs from journalctl periodically
    private async Task ReadSSHModuleLogsAsync()
    {
        if (string.IsNullOrEmpty(sshUser) || string.IsNullOrEmpty(sshHost) || string.IsNullOrEmpty(sshKeyPath) || !isCustomServer)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] SSH module log read skipped - SSHUser:" + sshUser + ", SSHHost:" + sshHost + ", SSHKeyPath:" + sshKeyPath + ", IsCustomServer:" + isCustomServer);
            return;
        }
        
        try
        {
            // Validate and fix timestamp if it's invalid
            if (lastModuleLogTimestamp == DateTime.MinValue || lastModuleLogTimestamp.Year < 2020)
            {
                if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] SSH module timestamp was invalid ({lastModuleLogTimestamp:yyyy-MM-dd HH:mm:ss}), resetting to 1 hour ago");
                lastModuleLogTimestamp = DateTime.UtcNow.AddHours(-1);
                SessionState.SetString(SessionKeySSHModuleLogTimestamp, lastModuleLogTimestamp.ToString("O"));
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] SSH module timestamp reset to: {lastModuleLogTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
            
            // Format timestamp for journalctl --since parameter (correct format)
            // Add additional safety check to ensure we never use invalid timestamps
            DateTime safeTimestamp = lastModuleLogTimestamp;
            if (safeTimestamp == DateTime.MinValue || safeTimestamp.Year < 2020)
            {
                safeTimestamp = DateTime.UtcNow.AddHours(-1);
                if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Emergency timestamp fix in SSH module logs - using: {safeTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
            string sinceTimestamp = safeTimestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            // Use journalctl to read module logs since last timestamp
            string journalCommand = $"sudo journalctl -u {SpacetimeServiceName} --since \\\"{sinceTimestamp}\\\" --no-pager -o short-iso-precise";
            //string journalCommand = $"sudo journalctl -u {SpacetimeServiceName} --no-pager -o short-iso-precise";
            
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Reading SSH module logs since: {sinceTimestamp}");
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Full SSH command: ssh -i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{journalCommand}\"");
            
            Process readProcess = new Process();
            readProcess.StartInfo.FileName = "ssh";
            readProcess.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{journalCommand}\"";
            readProcess.StartInfo.UseShellExecute = false;
            readProcess.StartInfo.CreateNoWindow = true;
            readProcess.StartInfo.RedirectStandardOutput = true;
            readProcess.StartInfo.RedirectStandardError = true;
            readProcess.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            readProcess.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            
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
                int duplicateCount = 0;
                DateTime latestTimestamp = lastModuleLogTimestamp;
                
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
                                UnityEngine.Debug.Log($"[ServerLogProcess] Rejecting SSH module log from {logTimestamp:yyyy-MM-dd HH:mm:ss} (before fresh start time {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss})");
                            }
                            
                            // Still track the timestamp progression even if we skip the log content
                            if (logTimestamp > latestTimestamp)
                            {
                                latestTimestamp = logTimestamp;
                            }
                            continue; // Skip processing this log line
                        }
                        
                        // Create a hash for deduplication (using raw line to avoid format changes affecting dedup)
                        string lineHash = GenerateLogLineHash(line.Trim());
                        
                        // Check for duplicates
                        if (recentModuleLogHashes.Contains(lineHash))
                        {
                            duplicateCount++;
                            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Skipping duplicate SSH module log line: {line.Trim().Substring(0, Math.Min(80, line.Trim().Length))}");
                            continue;
                        }
                        
                        // Add to recent logs for deduplication
                        recentModuleLogHashes.Add(lineHash);
                        
                        // Trim the hash set if it gets too large
                        if (recentModuleLogHashes.Count > MAX_RECENT_LOG_HASHES)
                        {
                            // Remove oldest entries (this is a simple approach, could be improved with LRU)
                            var hashesToKeep = recentModuleLogHashes.Skip(recentModuleLogHashes.Count / 2).ToArray();
                            recentModuleLogHashes.Clear();
                            foreach (var hash in hashesToKeep)
                            {
                                recentModuleLogHashes.Add(hash);
                            }
                        }
                        
                        string formattedLine = FormatServerLogLine(line.Trim());
                        moduleLogContent += formattedLine + "\n";
                        cachedModuleLogContent += formattedLine + "\n";
                        hasNewLogs = true;
                        lineCount++;
                        
                        // Track the latest timestamp even if the line was processed
                        if (logTimestamp != DateTime.MinValue && logTimestamp > latestTimestamp)
                        {
                            latestTimestamp = logTimestamp;
                        }
                    }
                }
                
                if (debugMode && duplicateCount > 0)
                {
                    UnityEngine.Debug.Log($"[ServerLogProcess] Filtered out {duplicateCount} duplicate SSH module log lines");
                }
                
                if (hasNewLogs)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Read {lineCount} new SSH module log lines (filtered {duplicateCount} duplicates)");
                    
                    // Always advance the timestamp to prevent infinite loops
                    DateTime timestampToUse;
                    
                    if (latestTimestamp > lastModuleLogTimestamp)
                    {
                        // Use the actual latest log timestamp plus a small buffer
                        timestampToUse = latestTimestamp.AddSeconds(1);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using parsed timestamp: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    else
                    {
                        // Fallback: advance by at least 2 seconds to ensure we skip past any problematic entries
                        timestampToUse = lastModuleLogTimestamp.AddSeconds(2);
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No valid timestamps found, advancing by 2 seconds: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    
                    // Update the timestamp
                    lastModuleLogTimestamp = timestampToUse;
                    
                    // Save timestamp to session state for persistence across domain reloads
                    SessionState.SetString(SessionKeySSHModuleLogTimestamp, lastModuleLogTimestamp.ToString("O"));
                    
                    // Manage log size
                    const int maxLogLength = 75000;
                    const int trimToLength = 50000;
                    if (moduleLogContent.Length > maxLogLength)
                    {
                        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory SSH log from {moduleLogContent.Length} chars.");
                        moduleLogContent = "[... Log Truncated ...]\n" + moduleLogContent.Substring(moduleLogContent.Length - trimToLength);
                        cachedModuleLogContent = "[... Log Truncated ...]\n" + cachedModuleLogContent.Substring(cachedModuleLogContent.Length - trimToLength);
                        
                        // Clear hash set when truncating to avoid false positives on old logs
                        recentModuleLogHashes.Clear();
                        recentDatabaseLogHashes.Clear();
                    }
                    
                    // Update SessionState immediately for SSH logs to ensure they appear
                    SessionState.SetString(SessionKeyModuleLog, moduleLogContent);
                    SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
                    
                    // Notify of log update
                    EditorApplication.delayCall += () => onModuleLogUpdated?.Invoke();
                }
                else 
                {
                    // Even if no new logs, advance timestamp to prevent infinite queries
                    // Use a larger increment if we had duplicates to help break repetition cycles
                    double advanceSeconds = duplicateCount > 0 ? 3.0 : 1.0;
                    lastModuleLogTimestamp = lastModuleLogTimestamp.AddSeconds(advanceSeconds);
                    
                    // Save timestamp to session state for persistence across domain reloads
                    SessionState.SetString(SessionKeySSHModuleLogTimestamp, lastModuleLogTimestamp.ToString("O"));
                    
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No new SSH module log lines found, advancing timestamp by {advanceSeconds}s to: {lastModuleLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
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
            // Validate and fix timestamp if it's invalid
            if (lastDatabaseLogTimestamp == DateTime.MinValue || lastDatabaseLogTimestamp.Year < 2020)
            {
                if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] SSH database timestamp was invalid ({lastDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss}), resetting to 1 hour ago");
                lastDatabaseLogTimestamp = DateTime.UtcNow.AddHours(-1);
                SessionState.SetString(SessionKeySSHDatabaseLogTimestamp, lastDatabaseLogTimestamp.ToString("O"));
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] SSH database timestamp reset to: {lastDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
            
            // Format timestamp for journalctl --since parameter (correct format)
            // Add additional safety check to ensure we never use invalid timestamps
            DateTime safeTimestamp = lastDatabaseLogTimestamp;
            if (safeTimestamp == DateTime.MinValue || safeTimestamp.Year < 2020)
            {
                safeTimestamp = DateTime.UtcNow.AddHours(-1);
                if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] Emergency timestamp fix in SSH database logs - using: {safeTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
            string sinceTimestamp = safeTimestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
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
            readProcess.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            readProcess.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            
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
                int duplicateCount = 0;
                DateTime latestTimestamp = lastDatabaseLogTimestamp;                  
                
                foreach (string line in lines)
                {
                    if (!string.IsNullOrEmpty(line.Trim()) && !line.Trim().Equals("-- No entries --"))
                    {
                        // Create a hash for deduplication (using raw line to avoid format changes affecting dedup)
                        string lineHash = GenerateLogLineHash(line.Trim());
                        
                        // Check for duplicates
                        if (recentDatabaseLogHashes.Contains(lineHash))
                        {
                            duplicateCount++;
                            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Skipping duplicate SSH database log line: {line.Trim().Substring(0, Math.Min(80, line.Trim().Length))}");
                            continue;
                        }
                        
                        string formattedLine = FormatDatabaseLogLine(line.Trim());
                        if (formattedLine != null) // Only process if line wasn't filtered out
                        {
                            // Add to recent logs for deduplication after successful formatting
                            recentDatabaseLogHashes.Add(lineHash);
                            
                            // Trim the hash set if it gets too large
                            if (recentDatabaseLogHashes.Count > MAX_RECENT_LOG_HASHES)
                            {
                                // Remove oldest entries (this is a simple approach, could be improved with LRU)
                                var hashesToKeep = recentDatabaseLogHashes.Skip(recentDatabaseLogHashes.Count / 2).ToArray();
                                recentDatabaseLogHashes.Clear();
                                foreach (var hash in hashesToKeep)
                                {
                                    recentDatabaseLogHashes.Add(hash);
                                }
                            }
                            
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
                
                if (debugMode && duplicateCount > 0)
                {
                    UnityEngine.Debug.Log($"[ServerLogProcess] Filtered out {duplicateCount} duplicate SSH database log lines");
                }
                  if (hasNewLogs)
                {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Read {lineCount} new SSH database log lines (filtered {duplicateCount} duplicates)");
                    
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
                    
                    // Save timestamp to session state for persistence across domain reloads
                    SessionState.SetString(SessionKeySSHDatabaseLogTimestamp, lastDatabaseLogTimestamp.ToString("O"));
                    
                    // Notify of log update
                    EditorApplication.delayCall += () => onDatabaseLogUpdated?.Invoke();
                }
                else 
                {
                    // Even if no new logs, advance timestamp slightly to prevent infinite queries
                    // Use a larger increment if we had duplicates to help break repetition cycles
                    double advanceSeconds = duplicateCount > 0 ? 3.0 : 0.5;
                    lastDatabaseLogTimestamp = lastDatabaseLogTimestamp.AddSeconds(advanceSeconds);
                    
                    // Save timestamp to session state for persistence across domain reloads
                    SessionState.SetString(SessionKeySSHDatabaseLogTimestamp, lastDatabaseLogTimestamp.ToString("O"));
                    
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No new SSH database log lines found, advancing timestamp by {advanceSeconds}s to: {lastDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
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
        // Add protection against rapid successive calls (e.g., when Unity regains focus)
        if (currentTime - lastLogReadTime < 0.5) // Minimum 500ms between calls
        {
            return;
        }

        // Skip if there's already a scheduled SSH log processing task
        if (isSSHLogProcessingScheduled)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] SSH log processing already scheduled, skipping");
            return;
        }
        
        if (currentTime - lastLogReadTime > sshLogReadInterval)
        {
            lastLogReadTime = currentTime;
            
            if (serverRunning && isCustomServer)
            {
                // Set flag to prevent multiple simultaneous tasks
                isSSHLogProcessingScheduled = true;
                
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
                    finally
                    {
                        // Clear the flag when processing is complete
                        isSSHLogProcessingScheduled = false;
                    }
                };
            }
        }
    }
    
    public void StopSSHLogging()
    {
        if (debugMode) logCallback("Stopping SSH periodic log reading", 0);
        
        // Reset timestamps to 1 hour ago to avoid massive log dumps on restart
        lastModuleLogTimestamp = DateTime.UtcNow.AddHours(-1);
        lastDatabaseLogTimestamp = DateTime.UtcNow.AddHours(-1);
        
        // Save updated timestamps to session state
        SessionState.SetString(SessionKeySSHModuleLogTimestamp, lastModuleLogTimestamp.ToString("O"));
        SessionState.SetString(SessionKeySSHDatabaseLogTimestamp, lastDatabaseLogTimestamp.ToString("O"));
        lastLogReadTime = 0;
        
        // Clear deduplication cache
        recentModuleLogHashes.Clear();
        recentDatabaseLogHashes.Clear();
        
        // Clear processing flag
        isSSHLogProcessingScheduled = false;
        
        SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
        
        if (debugMode) logCallback("SSH log reading stopped", 0);
    }

    /// <summary>
    /// Restart SSH log reading processes (useful when they get stuck after recompilation)
    /// </summary>
    public void RestartSSHLogging()
    {
        if (debugMode) logCallback("[ServerLogProcess] Restarting SSH log reading processes", 0);
        
        // Reset read flags to allow new reads
        isReadingWSLModuleLogs = false;
        isReadingWSLDatabaseLogs = false;
        isSSHLogProcessingScheduled = false;
        
        // Reset read timer to force immediate next read
        lastLogReadTime = 0;
        
        if (debugMode) logCallback("[ServerLogProcess] SSH log processes restarted", 0);
    }

    public async void SwitchModuleSSH(string newModuleName, bool clearDatabaseLogOnSwitch = true)
    {
        if (debugMode) logCallback($"[SSH] Starting module switch to: {newModuleName}", 0);
        
        // Read current module from config file to verify actual state
        string currentModuleFromFile = await GetCurrentSSHModuleAsync();
        
        if (debugMode) 
        {
            logCallback($"[SSH] Module in memory: '{this.moduleName}'", 0);
            logCallback($"[SSH] Module from config file: '{currentModuleFromFile}'", 0);
        }
        
        // Use config file as source of truth for current module
        string actualCurrentModule = !string.IsNullOrEmpty(currentModuleFromFile) ? currentModuleFromFile : this.moduleName;
        
        if (string.Equals(actualCurrentModule, newModuleName, StringComparison.OrdinalIgnoreCase))
        {
            if (debugMode) logCallback($"SSH: Module '{newModuleName}' is already active. No switch needed.", 0);
            return;
        }

        if (debugMode) logCallback($"Switching SSH database logs from module '{actualCurrentModule}' to '{newModuleName}'", 0);

        // Update the module name
        string oldModuleName = actualCurrentModule;
        this.moduleName = newModuleName;

        // Update the database log service configuration
        UpdateSSHDatabaseLogService(newModuleName);

        // Clear database logs if requested
        if (clearDatabaseLogOnSwitch)
        {
            ClearSSHDatabaseLog(); // This clears in-memory and SessionState for SSH logs

            // Add a separator message to indicate the switch
            string timestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
            string switchMessage = $"{timestamp} [MODULE SWITCHED - SSH] Logs for module '{oldModuleName}' stopped. Now showing logs for module: {newModuleName}\n";
            
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

    // Update the module config file and restart the database logs service for SSH
    public async void UpdateSSHDatabaseLogService(string newModuleName)
    {
        if (customProcessor == null)
        {
            if (debugMode) logCallback?.Invoke("[ServerLogProcess] Custom processor not available for SSH service update", 0);
            return;
        }

        if (string.IsNullOrEmpty(newModuleName))
        {
            if (debugMode) logCallback?.Invoke("[ServerLogProcess] Module name is empty, cannot update service", -1);
            return;
        }

        try
        {
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Updating SSH database log service to module: {newModuleName}", 0);

            string configFile = GetSSHModuleConfigFile();
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Writing module '{newModuleName}' to config file: {configFile}", 0);

            // Write module name to config file
            string updateConfigCommand = $"echo '{newModuleName}' > {configFile}";
            var configResult = await customProcessor.RunCustomCommandAsync(updateConfigCommand);
            
            if (!configResult.success)
            {
                logCallback?.Invoke($"Failed to update module config file: {configResult.output}", -1);
                return;
            }
            
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Config file updated successfully", 0);
            
            // Verify the file was written correctly
            string verifyCommand = $"cat {configFile}";
            var verifyResult = await customProcessor.RunCustomCommandAsync(verifyCommand);
            
            if (debugMode) 
            {
                if (verifyResult.success)
                {
                    logCallback?.Invoke($"[ServerLogProcess] Config file content: '{verifyResult.output.Trim()}'", 0);
                }
                else
                {
                    logCallback?.Invoke($"[ServerLogProcess] Failed to verify config file: {verifyResult.output}", -1);
                }
            }

            // Restart the service to pick up new module
            string restartCommand = "sudo systemctl restart spacetimedb-logs.service";
            var restartResult = await customProcessor.RunCustomCommandAsync(restartCommand);
            
            if (restartResult.success)
            {
                if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Successfully updated database log service to module: {newModuleName}", 1);
            }
            else
            {
                logCallback?.Invoke($"Failed to restart database log service: {restartResult.output}", -1);
            }
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Error updating SSH database log service: {ex.Message}", -1);
        }
    }


    #endregion
    
    #region WSL Journalctl Logging
    
    public void ConfigureWSL(bool isLocalServer)
    {
        // Ensure we're not in custom server or Docker mode for WSL
        this.isCustomServer = false;
        this.isDockerServer = false;
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Configured WSL journalctl: IsLocalServer={isLocalServer}, IsCustomServer={isCustomServer}, IsDockerServer={isDockerServer}");
        
        // Initialize timestamps to 10 minutes ago to avoid getting massive historical logs but still get recent context
        DateTime startTime = DateTime.UtcNow.AddMinutes(-10);
        lastWSLModuleLogTimestamp = startTime;
        lastWSLDatabaseLogTimestamp = startTime;
        
        // Reset process protection flags
        isReadingWSLModuleLogs = false;
        isReadingWSLDatabaseLogs = false;
        databaseLogStartFresh = false; // Will be set to true if clearDatabaseLogAtStart is used
        databaseLogFreshStartTime = DateTime.MinValue;
        moduleLogStartFresh = false; // Will be set to true if clearModuleLogAtStart is used
        moduleLogFreshStartTime = DateTime.MinValue;
    }
    
    public void ConfigureDocker(bool isLocalServer)
    {
        // Ensure we're in Docker mode
        this.isCustomServer = false;
        this.isDockerServer = true;
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Configured Docker logging: IsLocalServer={isLocalServer}, IsDockerServer={isDockerServer}");
        
        // Reset Docker-specific flags but DON'T reset first-read flags or hash sets
        // This allows logs to persist across mode switches and recompilation
        isReadingDockerModuleLogs = false;
        isReadingDockerDatabaseLogs = false;
        lastDockerLogReadTime = 0;
        // Note: NOT clearing hash sets or first-read flags to preserve log history
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
        
        // Only clear if no restored content exists
        bool hasRestoredContent = !string.IsNullOrEmpty(moduleLogContent) && moduleLogContent != "(No Module Log Found.)";
        if (!hasRestoredContent)
        {
            moduleLogContent = "";
            cachedModuleLogContent = "";
            SessionState.SetString(SessionKeyModuleLog, moduleLogContent);
            SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
            
            if (debugMode) logCallback("Cleared WSL module logs and SessionState", 1);
        }
        else
        {
            if (debugMode) logCallback("Preserving restored module logs from compilation - not clearing SessionState", 1);
        }
        // Reset timestamp to start fresh and reset protection flag
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
        
        // Only clear if no restored content exists
        bool hasRestoredContent = !string.IsNullOrEmpty(databaseLogContent) && databaseLogContent != "(No Database Log Found.)";
        if (!hasRestoredContent)
        {
            databaseLogContent = "";
            cachedDatabaseLogContent = "";
            SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
            SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
            
            if (debugMode) logCallback("Cleared WSL database logs and SessionState", 1);
        }
        else
        {
            if (debugMode) logCallback("Preserving restored database logs from compilation - not clearing SessionState", 1);
        }        // Reset timestamp to start fresh and reset protection flag
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
        
        Process readProcess = null;        
        try
        {
            // Ensure we have a valid timestamp - if not, use recent time to avoid massive log dumps
            if (lastWSLModuleLogTimestamp == DateTime.MinValue || lastWSLModuleLogTimestamp.Year < 2020)
            {
                lastWSLModuleLogTimestamp = DateTime.UtcNow.AddMinutes(-10);
                //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Invalid WSL module timestamp detected, reset to: {lastWSLModuleLogTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
            
            // Format timestamp for journalctl --since parameter
            string sinceTimestamp = lastWSLModuleLogTimestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
              // Use timeout to prevent orphaned processes and ensure cleanup
            // Try without sudo first - most users can read journalctl logs without sudo if properly configured
            string journalCommand = $"timeout 10s journalctl -u {SpacetimeServiceName} --since \\\"{sinceTimestamp}\\\" --no-pager -o short-iso-precise";
            
            //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Reading WSL module logs since: {sinceTimestamp}");
            //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL command: wsl -d Debian -u {userName} --exec bash -c \"{journalCommand}\"");
            
            readProcess = new Process();
            readProcess.StartInfo.FileName = "wsl";
            readProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -c \"{journalCommand}\"";
            readProcess.StartInfo.UseShellExecute = false;
            readProcess.StartInfo.CreateNoWindow = true;
            readProcess.StartInfo.RedirectStandardOutput = true;
            readProcess.StartInfo.RedirectStandardError = true;
            readProcess.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            readProcess.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            
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
            
            //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL module logs - Output length: {output?.Length ?? 0}, Error: {error}");
            
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
                                //UnityEngine.Debug.Log($"[ServerLogProcess] Rejecting module log from {logTimestamp:yyyy-MM-dd HH:mm:ss} (before fresh start time {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss})");
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
                            if (!moduleLogContent.Contains(formattedLine))
                            {
                                moduleLogContent += formattedLine + "\n";
                                cachedModuleLogContent += formattedLine + "\n";
                                hasNewLogs = true;
                                lineCount++;
                                
                                if (debugMode && lineCount <= 3) // Show first few lines for debugging
                                {
                                    //UnityEngine.Debug.Log($"[ServerLogProcess] Added WSL module log line {lineCount}: {formattedLine.Substring(0, Math.Min(100, formattedLine.Length))}...");
                                }
                            }
                            else if (debugMode)
                            {
                                //UnityEngine.Debug.Log($"[ServerLogProcess] Skipping duplicate module log line: {formattedLine.Substring(0, Math.Min(80, formattedLine.Length))}...");
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
                    //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Read {lineCount} new WSL module log lines");
                    
                    // Always advance the timestamp to prevent infinite loops
                    DateTime timestampToUse;
                    
                    if (latestTimestamp > lastWSLModuleLogTimestamp)
                    {
                        timestampToUse = latestTimestamp.AddSeconds(1);
                        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using parsed timestamp: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    else
                    {
                        timestampToUse = lastWSLModuleLogTimestamp.AddSeconds(1);
                        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No valid timestamps found, advancing by 1 second: {timestampToUse:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    
                    // Update the timestamp
                    lastWSLModuleLogTimestamp = timestampToUse;
                    
                    // Manage log size
                    const int maxLogLength = 75000;
                    const int trimToLength = 50000;
                    if (moduleLogContent.Length > maxLogLength)
                    {
                        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Truncating in-memory silent log from {moduleLogContent.Length} chars.");
                        moduleLogContent = "[... Log Truncated ...]\n" + moduleLogContent.Substring(moduleLogContent.Length - trimToLength);
                        cachedModuleLogContent = "[... Log Truncated ...]\n" + cachedModuleLogContent.Substring(cachedModuleLogContent.Length - trimToLength);
                    }
                    
                    // Update SessionState immediately for WSL logs
                    SessionState.SetString(SessionKeyModuleLog, moduleLogContent);
                    SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
                    
                    // Notify of log update
                    EditorApplication.delayCall += () => onModuleLogUpdated?.Invoke();
                }
                else 
                {
                    // Even if no new logs, advance timestamp slightly to prevent infinite queries
                    lastWSLModuleLogTimestamp = lastWSLModuleLogTimestamp.AddSeconds(0.5);
                    //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No new WSL module log lines found, advancing timestamp slightly to: {lastWSLModuleLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
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
        
        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Starting WSL database log read - Fresh mode: {databaseLogStartFresh}, Fresh start time: {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        
        Process readProcess = null;try
        {   
            // Ensure we have a valid timestamp - if not, use appropriate fallback based on whether we want fresh logs
            if (lastWSLDatabaseLogTimestamp == DateTime.MinValue || lastWSLDatabaseLogTimestamp.Year < 2020)
            {
                if (databaseLogStartFresh)
                {
                    // If we want fresh logs only, start from current time
                    lastWSLDatabaseLogTimestamp = DateTime.UtcNow;
                    //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Invalid WSL database timestamp detected, reset to current time for fresh logs: {lastWSLDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    // Otherwise use 10 minutes ago for recent context
                    lastWSLDatabaseLogTimestamp = DateTime.UtcNow.AddMinutes(-10);
                    //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Invalid WSL database timestamp detected, reset to 10 minutes ago: {lastWSLDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss}");
                }
            }
            // Format timestamp for journalctl --since parameter
            string sinceTimestamp;
            if (databaseLogStartFresh && databaseLogFreshStartTime != DateTime.MinValue)
            {
                // For fresh logs, use the exact fresh start time and rely on application filtering
                sinceTimestamp = databaseLogFreshStartTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using fresh start timestamp for journalctl: {sinceTimestamp} (fresh start time: {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss})");
            }
            else
            {
                sinceTimestamp = lastWSLDatabaseLogTimestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Using last timestamp for journalctl: {sinceTimestamp}");
            }
              // Use timeout to prevent orphaned processes and ensure cleanup
            // Try without sudo first - most users can read journalctl logs without sudo if properly configured
            string journalCommand = $"timeout 10s journalctl -u {SpacetimeDatabaseLogServiceName} --since \\\"{sinceTimestamp}\\\" --no-pager -o short-iso-precise";
            
            //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Reading WSL database logs since: {sinceTimestamp}");
            //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL Database command: wsl -d Debian -u {userName} --exec bash -c \"{journalCommand}\"");
            
            readProcess = new Process();
            readProcess.StartInfo.FileName = "wsl";
            readProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -c \"{journalCommand}\"";
            readProcess.StartInfo.UseShellExecute = false;
            readProcess.StartInfo.CreateNoWindow = true;
            readProcess.StartInfo.RedirectStandardOutput = true;
            readProcess.StartInfo.RedirectStandardError = true;
            readProcess.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            readProcess.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            
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

            //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL database logs - Output length: {output?.Length ?? 0}, Error: {error}");
            
            if (!string.IsNullOrEmpty(output))
            {
                //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] WSL database raw output: {output.Substring(0, Math.Min(500, output.Length))}...");
                
                var lines = output.Split('\n');
                bool hasNewLogs = false;
                int lineCount = 0;
                int rejectedCount = 0;
                DateTime latestTimestamp = lastWSLDatabaseLogTimestamp;
                
                foreach (string line in lines)
                {   
                    if (!string.IsNullOrEmpty(line.Trim()) && !line.Trim().Equals("-- No entries --"))
                    {
                        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Processing database log line: {line.Trim().Substring(0, Math.Min(150, line.Trim().Length))}...");
                        
                        // Extract timestamp before formatting to ensure we can track progression
                        DateTime logTimestamp = ExtractTimestampFromJournalLine(line.Trim());
                          string formattedLine = FormatDatabaseLogLine(line.Trim());
                        
                        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] After formatting - formattedLine is null: {formattedLine == null}, timestamp: {logTimestamp:yyyy-MM-dd HH:mm:ss}");
                        
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
                } else 
                {
                    // Even if no new logs, advance timestamp slightly to prevent infinite queries
                    lastWSLDatabaseLogTimestamp = lastWSLDatabaseLogTimestamp.AddSeconds(0.5);
                    //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] No new WSL database log lines found, rejected {rejectedCount} old lines (fresh mode: {databaseLogStartFresh}), advancing timestamp slightly to: {lastWSLDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
                    
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
        // Add protection against rapid successive calls (e.g., when Unity regains focus)
        if (currentTime - lastWSLLogReadTime < 0.5) // Minimum 500ms between calls
        {
            return;
        }
        
        // Skip if there's already a scheduled WSL log processing task
        if (isWSLLogProcessingScheduled)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] WSL log processing already scheduled, skipping");
            return;
        }
        
        if (currentTime - lastWSLLogReadTime > wslLogReadInterval)
        {
            lastWSLLogReadTime = currentTime;
            
            if (serverRunning && !isCustomServer)
            {
                // Set flag to prevent multiple simultaneous tasks
                isWSLLogProcessingScheduled = true;
                
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
                    finally
                    {
                        // Clear the flag when processing is complete
                        isWSLLogProcessingScheduled = false;
                    }
                };
            }
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
        
        // Reset process protection flags
        isReadingWSLModuleLogs = false;
        isReadingWSLDatabaseLogs = false;
        
        // Clear processing flag
        isWSLLogProcessingScheduled = false;
        
        SessionState.SetBool(SessionKeyDatabaseLogRunning, false);
        
        if (debugMode) logCallback("WSL log reading stopped", 0);
    }

    /// <summary>
    /// Restart WSL log reading processes (useful when they get stuck after recompilation)
    /// </summary>
    public void RestartWSLLogging()
    {
        if (debugMode) logCallback("[ServerLogProcess] Restarting WSL log reading processes", 0);
        
        // Reset read flags to allow new reads
        isReadingWSLModuleLogs = false;
        isReadingWSLDatabaseLogs = false;
        isWSLLogProcessingScheduled = false;
        
        // Reset read timer to force immediate next read
        lastWSLLogReadTime = 0;
        
        if (debugMode) logCallback("[ServerLogProcess] WSL log processes restarted", 0);
    }

    public async void SwitchModuleWSL(string newModuleName, bool clearDatabaseLogOnSwitch = true)
    {
        if (debugMode) logCallback($"[WSL] Starting module switch to: {newModuleName}", 0);
        
        // Read current module from config file to verify actual state
        string currentModuleFromFile = await GetCurrentWSLModuleAsync();
        
        if (debugMode) 
        {
            logCallback($"[WSL] Module in memory: '{this.moduleName}'", 0);
            logCallback($"[WSL] Module from config file: '{currentModuleFromFile}'", 0);
        }
        
        // Use config file as source of truth for current module
        string actualCurrentModule = !string.IsNullOrEmpty(currentModuleFromFile) ? currentModuleFromFile : this.moduleName;
        
        if (string.Equals(actualCurrentModule, newModuleName, StringComparison.OrdinalIgnoreCase))
        {
            if (debugMode) logCallback($"WSL: Module '{newModuleName}' is already active. No switch needed.", 0);
            return;
        }

        if (debugMode) logCallback($"Switching WSL database logs from module '{actualCurrentModule}' to '{newModuleName}'", 0);

        // Update the module name
        string oldModuleName = actualCurrentModule;
        this.moduleName = newModuleName;

        // Update the database log service configuration
        UpdateWSLDatabaseLogService(newModuleName);

        // Clear database logs if requested
        if (clearDatabaseLogOnSwitch)
        {
            ClearWSLDatabaseLog(); // This clears in-memory and SessionState for WSL logs

            // Add a separator message to indicate the switch
            string timestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]");
            string switchMessage = $"{timestamp} [MODULE SWITCHED - WSL] Logs for module '{oldModuleName}' stopped. Now showing logs for module: {newModuleName}\n";
            
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

    // Update the module config file and restart the database logs service for WSL
    public async void UpdateWSLDatabaseLogService(string newModuleName)
    {
        if (string.IsNullOrEmpty(newModuleName))
        {
            if (debugMode) logCallback?.Invoke("[ServerLogProcess] Module name is empty, cannot update WSL service", -1);
            return;
        }

        if (wslProcess == null)
        {
            if (debugMode) logCallback?.Invoke("[ServerLogProcess] CMD processor not available for WSL service update", 0);
            return;
        }

        try
        {
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Updating WSL database log service to module: {newModuleName}", 0);
            
            string configFile = GetWSLModuleConfigFile();
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Writing module '{newModuleName}' to config file: {configFile}", 0);

            // Ensure the directory exists and has proper permissions
            string setupCommand = $"mkdir -p $(dirname {configFile}) && chmod 755 $(dirname {configFile})";
            int setupResult = wslProcess.RunWslCommandSilent(setupCommand);
            
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Directory setup exit code: {setupResult}", 0);

            // Write module name to config file
            string updateConfigCommand = $"echo '{newModuleName}' > {configFile}";
            int configResult = wslProcess.RunWslCommandSilent(updateConfigCommand);
            
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Config update command exit code: {configResult}", 0);
            
            // Verify the file was written correctly by reading it back
            await Task.Delay(100); // Brief delay to ensure file system sync
            string actualContent = await GetCurrentWSLModuleAsync();
            
            if (debugMode) 
            {
                logCallback?.Invoke($"[ServerLogProcess] Config file content after write: '{actualContent}'", 0);
                logCallback?.Invoke($"[ServerLogProcess] Expected content: '{newModuleName}'", 0);
            }
            
            if (!string.Equals(actualContent, newModuleName, StringComparison.OrdinalIgnoreCase))
            {
                logCallback?.Invoke($"[ServerLogProcess] WARNING: Config file content '{actualContent}' does not match expected '{newModuleName}'", -1);
                
                // Try alternative method with explicit permissions
                string altCommand = $"echo '{newModuleName}' | tee {configFile} > /dev/null && chmod 644 {configFile}";
                int altResult = wslProcess.RunWslCommandSilent(altCommand);
                
                if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Alternative write command exit code: {altResult}", 0);
                
                // Verify again
                await Task.Delay(100);
                actualContent = await GetCurrentWSLModuleAsync();
                if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Config file content after retry: '{actualContent}'", 0);
            }

            // Restart the service to pick up new module
            string restartCommand = "sudo systemctl restart spacetimedb-logs.service";
            int restartResult = wslProcess.RunWslCommandSilent(restartCommand);
            
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] Service restart command exit code: {restartResult}", 0);
            
            if (debugMode) logCallback?.Invoke($"[ServerLogProcess] WSL database log service update completed for module: {newModuleName}", 1);
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Error updating WSL database log service: {ex.Message}", -1);
        }
    }
    #endregion
    
    #region Docker Logging
    
    public void StartDockerLogging()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] Starting Docker log reading");
        
        if (clearModuleLogAtStart)
        {
            ClearDockerLogs();
        }
        
        // Set timestamps to show all logs from now on (timestamp-based filtering)
        dockerModuleLogCutoffTimestamp = DateTime.MinValue; // Show all logs
        dockerDatabaseLogCutoffTimestamp = DateTime.MinValue; // Show all logs
        lastDockerLogReadTime = 0;
    }
    
    /// <summary>
    /// Restart Docker log reading processes (useful when they get stuck after recompilation)
    /// </summary>
    public void RestartDockerLogging()
    {
        if (debugMode) logCallback("[ServerLogProcess] Restarting Docker log reading processes", 0);
        
        // Reset read flags to allow new reads
        isReadingDockerModuleLogs = false;
        isReadingDockerDatabaseLogs = false;
        isDockerLogProcessingScheduled = false;
        
        // Reset read timer to force immediate next read
        lastDockerLogReadTime = 0;
        
        // Keep cutoff timestamps so we don't re-read old logs
        // Just restart the reading process
        if (debugMode) logCallback("[ServerLogProcess] Docker log processes restarted", 0);
    }
    
    public void ClearDockerLogs()
    {
        if (debugMode) logCallback("[ServerLogProcess] Clearing Docker logs...", 0);
        
        // Set cutoff timestamps to now, so only new logs from this point forward are shown
        dockerModuleLogCutoffTimestamp = DateTime.UtcNow;
        dockerDatabaseLogCutoffTimestamp = DateTime.UtcNow;
        
        // Clear in-memory logs
        lock (logLock)
        {
            moduleLogContent = "";
            cachedModuleLogContent = "";
            databaseLogContent = "";
            cachedDatabaseLogContent = "";
            
            SessionState.SetString(SessionKeyModuleLog, "");
            SessionState.SetString(SessionKeyCachedModuleLog, "");
            SessionState.SetString(SessionKeyDatabaseLog, "");
            SessionState.SetString(SessionKeyCachedDatabaseLog, "");
        }
        
        onModuleLogUpdated?.Invoke();
        onDatabaseLogUpdated?.Invoke();
        
        if (debugMode) logCallback("[ServerLogProcess] Docker logs cleared", 1);
    }
    
    private async Task ReadDockerModuleLogsAsync()
    {
        try
        {
            logCallback("[ServerLogProcess] ReadDockerModuleLogsAsync STARTED - checking processor", 0);
            UnityEngine.Debug.Log("[ServerLogProcess] ReadDockerModuleLogsAsync STARTED - checking processor");
            
            if (dockerProcessor == null)
            {
                logCallback("[ServerLogProcess] ERROR: Docker processor is NULL!", -1);
                UnityEngine.Debug.LogError("[ServerLogProcess] ERROR: Docker processor is NULL!");
                return;
            }
            
            logCallback("[ServerLogProcess] Getting Docker logs (non-blocking)...", 0);
            UnityEngine.Debug.Log("[ServerLogProcess] Getting Docker logs (non-blocking)...");
            
            // Use a non-blocking approach with timeout
            string newLogs = null;
            try
            {
                var logsTask = dockerProcessor.GetDockerLogs(1000);
                // Wait up to 10 seconds
                if (await Task.WhenAny(logsTask, Task.Delay(10000)) == logsTask)
                {
                    newLogs = await logsTask;
                }
                else
                {
                    logCallback("[ServerLogProcess] WARNING: GetDockerLogs timed out", 0);
                    UnityEngine.Debug.LogWarning("[ServerLogProcess] WARNING: GetDockerLogs timed out");
                }
            }
            catch (Exception ex)
            {
                logCallback($"[ServerLogProcess] Error calling GetDockerLogs: {ex.Message}", -1);
                UnityEngine.Debug.LogError($"[ServerLogProcess] Error calling GetDockerLogs: {ex.Message}");
            }
            
            logCallback($"[ServerLogProcess] GetDockerLogs returned {newLogs?.Length ?? 0} characters", 0);
            
            if (string.IsNullOrEmpty(newLogs))
            {
                logCallback("[ServerLogProcess] WARNING: No Docker module logs retrieved", 0);
                return;
            }
            
            // Split into individual log lines
            string[] logLines = newLogs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            logCallback($"[ServerLogProcess] Split Docker module logs into {logLines.Length} lines", 0);
            
            // Filter logs by timestamp and format timestamps
            List<string> newFilteredLines = new List<string>();
            
            foreach (string line in logLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                // Try to extract timestamp from line
                DateTime logTimestamp = ExtractDockerTimestamp(line);
                
                // Debug first few lines
                if (newFilteredLines.Count < 3)
                {
                    logCallback($"[ServerLogProcess] Module log sample: {line.Substring(0, Math.Min(80, line.Length))}", 0);
                    logCallback($"[ServerLogProcess] Module timestamp: {logTimestamp:yyyy-MM-dd HH:mm:ss}, cutoff: {dockerModuleLogCutoffTimestamp:yyyy-MM-dd HH:mm:ss}", 0);
                }
                
                // Include log based on timestamp logic
                // Lines with valid timestamps are filtered by time
                // Lines without timestamps (MinValue) are always included (banners, warnings, etc.)
                bool shouldInclude = false;
                
                if (logTimestamp == DateTime.MinValue)
                {
                    // No timestamp found - this is a non-timestamped line (banner, warning, etc.)
                    // Always include these on first read, skip on subsequent reads to avoid duplication
                    shouldInclude = (dockerModuleLogCutoffTimestamp == DateTime.MinValue);
                }
                else if (dockerModuleLogCutoffTimestamp == DateTime.MinValue)
                {
                    // First read: include all logs with valid timestamps
                    shouldInclude = true;
                }
                else if (logTimestamp > dockerModuleLogCutoffTimestamp)
                {
                    // Subsequent reads: only include logs after the cutoff
                    shouldInclude = true;
                }
                
                if (shouldInclude)
                {
                    string formattedLine = FormatDockerLogTimestamp(line);
                    newFilteredLines.Add(formattedLine);
                }
            }
            
            logCallback($"[ServerLogProcess] After filtering: {newFilteredLines.Count} logs pass filter", 0);
            
            // Only update if we have new logs
            if (newFilteredLines.Count > 0)
            {
                string combinedNewLogs = string.Join(Environment.NewLine, newFilteredLines);
                
                lock (logLock)
                {
                    moduleLogContent = combinedNewLogs;
                    cachedModuleLogContent = combinedNewLogs;
                    
                    // Apply size limiting
                    if (moduleLogContent.Length > BUFFER_SIZE)
                    {
                        moduleLogContent = "[... Log Truncated ...]\n" + moduleLogContent.Substring(moduleLogContent.Length - TARGET_SIZE);
                    }
                    if (cachedModuleLogContent.Length > BUFFER_SIZE)
                    {
                        cachedModuleLogContent = "[... Log Truncated ...]\n" + cachedModuleLogContent.Substring(cachedModuleLogContent.Length - TARGET_SIZE);
                    }
                    
                    SessionState.SetString(SessionKeyModuleLog, moduleLogContent);
                    SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
                }
                
                onModuleLogUpdated?.Invoke();
                logCallback($"[ServerLogProcess] SUCCESS: Read {newFilteredLines.Count} Docker module log lines", 1);
            }
            else
            {
                logCallback("[ServerLogProcess] WARNING: No logs passed filter", 0);
            }
        }
        catch (OperationCanceledException)
        {
            logCallback("[ServerLogProcess] Module logs read was cancelled", 0);
        }
        catch (Exception ex)
        {
            logCallback($"[ServerLogProcess] EXCEPTION in ReadDockerModuleLogsAsync: {ex.Message}", -1);
            UnityEngine.Debug.LogError($"[ServerLogProcess] EXCEPTION in ReadDockerModuleLogsAsync: {ex}");
        }
        finally
        {
            isReadingDockerModuleLogs = false;
            logCallback("[ServerLogProcess] ReadDockerModuleLogsAsync COMPLETED", 0);
        }
    }
    
    private async Task ReadDockerDatabaseLogsAsync()
    {
        if (dockerProcessor == null)
        {
            if (debugMode) logCallback("[ServerLogProcess] Docker processor not initialized", -1);
            return;
        }
        
        if (string.IsNullOrEmpty(moduleName))
        {
            logCallback("[ServerLogProcess] ERROR: No module name set for Docker database logs!", 0);
            return;
        }
        
        logCallback("[ServerLogProcess] ReadDockerDatabaseLogsAsync STARTED", 0);
        
        // Check if already reading
        if (isReadingDockerDatabaseLogs)
        {
            logCallback("[ServerLogProcess] WARNING: Docker database log read already in progress, skipping", 0);
            return;
        }
        
        isReadingDockerDatabaseLogs = true;
        
        try
        {
            // Determine which server to target based on current server mode
            string serverParam = "";
            if (currentServerMode == NorthernRogue.CCCP.Editor.ServerManager.ServerMode.MaincloudServer)
            {
                // For Maincloud mode, explicitly target maincloud server
                serverParam = "--server maincloud";
                logCallback($"[ServerLogProcess] Using Docker with Maincloud server for database logs (module: {moduleName})", 0);
            }
            else
            {
                // For DockerServer mode, target local server (default)
                serverParam = "--server local";
                logCallback($"[ServerLogProcess] Using Docker with local server for database logs (module: {moduleName})", 0);
            }
            
            // Run "spacetime logs <module>" inside the Docker container with appropriate server target
            string command = $"spacetime logs {serverParam} {moduleName} --num-lines 100";
            logCallback($"[ServerLogProcess] Running Docker command: {command}", 0);
            
            var commandTask = dockerProcessor.RunServerCommandAsync(command, null, false);
            var timeoutTask = Task.Delay(15000); // 15 second timeout
            var completedTask = await Task.WhenAny(commandTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                logCallback("[ServerLogProcess] ERROR: Docker command timed out after 15 seconds!", -1);
                return;
            }
            
            var result = await commandTask;
            
            logCallback($"[ServerLogProcess] Docker command result - Success: {result.success}, Output length: {result.output?.Length ?? 0}", 0);
            
            if (!result.success || string.IsNullOrEmpty(result.output))
            {
                if (!string.IsNullOrEmpty(result.error))
                {
                    logCallback($"[ServerLogProcess] ERROR getting Docker database logs: {result.error}", -1);
                }
                if (string.IsNullOrEmpty(result.output))
                {
                    logCallback("[ServerLogProcess] WARNING: Docker database log output was empty", 0);
                }
                return;
            }
            
            logCallback($"[ServerLogProcess] Raw output preview: {result.output.Substring(0, Math.Min(200, result.output.Length))}...", 0);
            
            // Split into individual log lines
            string[] logLines = result.output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            logCallback($"[ServerLogProcess] Split Docker database logs into {logLines.Length} lines", 0);
            
            // Filter logs by timestamp and format timestamps
            List<string> newFilteredLines = new List<string>();
            
            foreach (string line in logLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                // Try to extract timestamp from line
                DateTime logTimestamp = ExtractDockerTimestamp(line);
                
                // Debug first few lines with actual content
                if (newFilteredLines.Count < 3)
                {
                    logCallback($"[ServerLogProcess] Database log sample: {line.Substring(0, Math.Min(80, line.Length))}", 0);
                    logCallback($"[ServerLogProcess] Database log parsed timestamp={logTimestamp:yyyy-MM-dd HH:mm:ss}, cutoff={dockerDatabaseLogCutoffTimestamp:yyyy-MM-dd HH:mm:ss}", 0);
                }
                
                // Include log if it's after the cutoff timestamp
                // Special case: if cutoff is MinValue (first read), include all logs with valid timestamps
                if (dockerDatabaseLogCutoffTimestamp == DateTime.MinValue)
                {
                    // First read: include all logs with valid (non-MinValue) timestamps
                    if (logTimestamp > DateTime.MinValue)
                    {
                        string formattedLine = FormatDockerLogTimestamp(line);
                        newFilteredLines.Add(formattedLine);
                    }
                }
                else if (logTimestamp > dockerDatabaseLogCutoffTimestamp)
                {
                    // Subsequent reads: only include logs after the cutoff
                    string formattedLine = FormatDockerLogTimestamp(line);
                    newFilteredLines.Add(formattedLine);
                }
            }
            
            logCallback($"[ServerLogProcess] After filtering: {newFilteredLines.Count} database logs pass filter", 0);
            
            // Only update if we have new logs
            if (newFilteredLines.Count > 0)
            {
                string combinedNewLogs = string.Join(Environment.NewLine, newFilteredLines);
                
                // Docker database logs from spacetime logs command
                // REPLACE entire log with the current output (not queue/append) to mirror actual log state
                lock (logLock)
                {
                    databaseLogContent = combinedNewLogs;
                    cachedDatabaseLogContent = combinedNewLogs;
                    
                    // Apply size limiting
                    if (databaseLogContent.Length > BUFFER_SIZE)
                    {
                        databaseLogContent = "[... Log Truncated ...]\n" + databaseLogContent.Substring(databaseLogContent.Length - TARGET_SIZE);
                    }
                    if (cachedDatabaseLogContent.Length > BUFFER_SIZE)
                    {
                        cachedDatabaseLogContent = "[... Log Truncated ...]\n" + cachedDatabaseLogContent.Substring(cachedDatabaseLogContent.Length - TARGET_SIZE);
                    }
                    
                    // Update session state
                    SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
                    SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
                }
                
                // Trigger callback
                onDatabaseLogUpdated?.Invoke();
                logCallback($"[ServerLogProcess] SUCCESS: Read {newFilteredLines.Count} Docker database log lines", 1);
            }
            else
            {
                logCallback("[ServerLogProcess] WARNING: No database logs passed filter (all filtered out)", 0);
            }
        }
        catch (Exception ex)
        {
            logCallback($"[ServerLogProcess] EXCEPTION reading Docker database logs: {ex.Message}\n{ex.StackTrace}", -1);
        }
        finally
        {
            isReadingDockerDatabaseLogs = false;
            logCallback("[ServerLogProcess] ReadDockerDatabaseLogsAsync COMPLETED", 0);
        }
    }
    
    // Helper method to generate hash for a log line
    private string GenerateLogHash(string logLine)
    {
        // Use a simple hash of the log content
        // For Docker logs, we want to include the entire line including Docker's timestamp prefix
        return logLine.GetHashCode().ToString();
    }
    
    // Helper method to extract timestamp from Docker log line
    private DateTime ExtractDockerTimestamp(string logLine)
    {
        try
        {
            // Docker logs come in TWO formats:
            // 1. Module logs (docker container stdout): "2025-10-13T19:35:07.086695Z  INFO ..."
            // 2. Database logs (spacetime logs cmd): "2025-10-13 21:30:57.706 | ..."
            // Some lines may have ANSI codes or no timestamps at all
            
            if (string.IsNullOrEmpty(logLine))
                return DateTime.MinValue;
            
            // Remove ANSI escape codes (colors, formatting, etc.)
            // They start with \x1b[ or \e[ and end with a letter
            string cleanLine = System.Text.RegularExpressions.Regex.Replace(logLine, @"\x1b\[[0-9;]*[A-Za-z]", "");
            cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, @"\e\[[0-9;]*[A-Za-z]", "");
            
            if (cleanLine.Length < 19)
                return DateTime.MinValue;
            
            // Try Format 1: ISO 8601 with Z (module logs)
            // Look for pattern: YYYY-MM-DDTHH:MM:SS.ffffffZ
            int zIndex = cleanLine.IndexOf('Z');
            if (zIndex >= 19 && zIndex < 40) // Reasonable range for timestamp
            {
                string timestampStr = cleanLine.Substring(0, zIndex + 1);
                // Check if it looks like a timestamp (contains T and hyphen)
                if (timestampStr.Contains('T') && timestampStr.Contains('-'))
                {
                    if (DateTime.TryParse(timestampStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime utcTime))
                    {
                        return utcTime;
                    }
                }
            }
            
            // Try Format 2: with pipe separator (database logs)
            int pipeIndex = cleanLine.IndexOf('|');
            if (pipeIndex >= 19) // Has pipe separator
            {
                string timestampStr = cleanLine.Substring(0, pipeIndex).Trim();
                
                // Try to parse: "2025-10-13 21:30:57.706"
                if (DateTime.TryParse(timestampStr, null, System.Globalization.DateTimeStyles.None, out DateTime parsedTime))
                {
                    return parsedTime;
                }
                
                // Try without milliseconds: "2025-10-13 21:30:57"
                if (timestampStr.Contains("."))
                {
                    string withoutMs = timestampStr.Substring(0, timestampStr.LastIndexOf('.'));
                    if (DateTime.TryParse(withoutMs, null, System.Globalization.DateTimeStyles.None, out DateTime parsedTimeNoMs))
                    {
                        return parsedTimeNoMs;
                    }
                }
            }
            
            return DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
    
    // Helper method to format Docker log timestamps from UTC to local time
    private string FormatDockerLogTimestamp(string logLine)
    {
        try
        {
            // Docker logs come in TWO formats:
            // 1. Module logs (docker container stdout): "2025-10-13T19:35:07.086695Z  INFO ..."
            // 2. Database logs (spacetime logs cmd): "2025-10-13 21:30:57.706 | ..."
            // Some lines may have ANSI codes that we need to strip
            
            if (string.IsNullOrEmpty(logLine))
                return logLine;
            
            // Remove ANSI escape codes first
            string cleanLine = System.Text.RegularExpressions.Regex.Replace(logLine, @"\x1b\[[0-9;]*[A-Za-z]", "");
            cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, @"\e\[[0-9;]*[A-Za-z]", "");
            
            if (cleanLine.Length < 19)
                return logLine; // Return original if too short
            
            // Try Format 1: ISO 8601 with Z (module logs)
            int zIndex = cleanLine.IndexOf('Z');
            if (zIndex >= 19 && zIndex < 40)
            {
                string timestampStr = cleanLine.Substring(0, zIndex + 1);
                string remainder = cleanLine.Substring(zIndex + 1);
                
                // Check if it looks like a timestamp
                if (timestampStr.Contains('T') && timestampStr.Contains('-'))
                {
                    if (DateTime.TryParse(timestampStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime utcTime))
                    {
                        // Convert to local time
                        DateTime localTime = utcTime.ToLocalTime();
                        // Format as "[YYYY-MM-DD HH:mm:ss]" to match SSH logs
                        string formattedTime = "[" + localTime.ToString("yyyy-MM-dd HH:mm:ss") + "]";
                        return formattedTime + remainder;
                    }
                }
            }
            
            // Try Format 2: with pipe separator (database logs)
            int pipeIndex = cleanLine.IndexOf('|');
            if (pipeIndex >= 19)
            {
                string timestampStr = cleanLine.Substring(0, pipeIndex).Trim();
                string remainder = cleanLine.Substring(pipeIndex + 1); // Skip the pipe
                
                DateTime parsedTime = DateTime.MinValue;
                
                if (DateTime.TryParse(timestampStr, null, System.Globalization.DateTimeStyles.None, out DateTime result))
                {
                    parsedTime = result;
                }
                else if (timestampStr.Contains("."))
                {
                    string withoutMs = timestampStr.Substring(0, timestampStr.LastIndexOf('.'));
                    if (DateTime.TryParse(withoutMs, null, System.Globalization.DateTimeStyles.None, out DateTime resultNoMs))
                    {
                        parsedTime = resultNoMs;
                    }
                }
                
                if (parsedTime > DateTime.MinValue)
                {
                    // Format as "[YYYY-MM-DD HH:mm:ss]" to match SSH logs
                    string formattedTime = "[" + parsedTime.ToString("yyyy-MM-dd HH:mm:ss") + "]";
                    return formattedTime + remainder;
                }
            }
            
            // Return cleaned line (ANSI codes removed)
            return cleanLine;
        }
        catch
        {
            return logLine;
        }
    }
    
    public void CheckDockerLogProcesses(double currentTime)
    {
        // Rate limit log reading
        if (currentTime - lastDockerLogReadTime < 0.5)
        {
            return;
        }
        
        // Skip if there's already a scheduled Docker log processing task
        /*if (isDockerLogProcessingScheduled)
        {
            if (debugMode) logCallback("[ServerLogProcess] Docker log processing already scheduled, skipping", 0);
            return;
        }*/
        
        if (currentTime - lastDockerLogReadTime > dockerLogReadInterval)
        {
            lastDockerLogReadTime = currentTime;
            
            if (serverRunning && isDockerServer)
            {
                // Set flag to prevent multiple simultaneous tasks
                isDockerLogProcessingScheduled = true;
                
                // Call async method directly - it will execute on current context
                _ = ProcessDockerLogsAsync();
            }
        }
    }
    
    private async Task ProcessDockerLogsAsync()
    {
        try
        {
            if (debugMode) logCallback("[ServerLogProcess] Docker log processing started", 0);
            await ReadDockerModuleLogsAsync();
            await ReadDockerDatabaseLogsAsync();
            if (debugMode) logCallback("[ServerLogProcess] Docker log processing completed", 0);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerLogProcess] Error in Docker log reading: {ex.Message}", -1);
        }
        finally
        {
            // Clear the flag when processing is complete
            isDockerLogProcessingScheduled = false;
        }
    }
    
    public void StopDockerLogging()
    {
        if (debugMode) logCallback("[ServerLogProcess] Stopping Docker log reading", 0);
        
        // Reset flags
        isReadingDockerModuleLogs = false;
        isReadingDockerDatabaseLogs = false;
        isDockerLogProcessingScheduled = false;
        
        if (debugMode) logCallback("[ServerLogProcess] Docker log reading stopped", 1);
    }
    
    #endregion
    
    // Public method to update log read intervals
    public void UpdateLogReadIntervals(double interval)
    {
        // Clamp interval between 1 and 10 seconds
        interval = Math.Max(1.0, Math.Min(10.0, interval));
        
        wslLogReadInterval = interval;
        dockerLogReadInterval = interval;
        sshLogReadInterval = interval;
        
        if (debugMode)
        {
            logCallback?.Invoke($"Log read intervals updated to {interval:F1}s", 1);
        }
    }
    
    // Public method to get current WSL log read interval
    public double GetWSLLogReadInterval()
    {
        return wslLogReadInterval;
    }
    
    // Public method to get current SSH log read interval
    public double GetSSHLogReadInterval()
    {
        return sshLogReadInterval;
    }
    
    public ServerLogProcess(
        Action<string, int> logCallback, 
        Action onModuleLogUpdated,
        Action onDatabaseLogUpdated,
        ServerWSLProcess wslProcess = null,
        ServerDockerProcess dockerProcessor = null,
        ServerCustomProcess customProcessor = null,
        bool debugMode = false)
    {
        this.logCallback = logCallback;
        this.onModuleLogUpdated = onModuleLogUpdated;
        this.onDatabaseLogUpdated = onDatabaseLogUpdated;
        this.wslProcess = wslProcess;
        this.dockerProcessor = dockerProcessor;
        this.customProcessor = customProcessor;
        ServerLogProcess.debugMode = debugMode;
        
        // Load username from Settings
        this.userName = CCCPSettingsAdapter.GetUserName();
        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Initialized with username from Settings: {this.userName}");

        // Load log update frequency from Settings and apply it
        float savedLogUpdateFrequency = CCCPSettingsAdapter.GetLogUpdateFrequency();
        UpdateLogReadIntervals(savedLogUpdateFrequency);
        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Applied saved log update frequency: {savedLogUpdateFrequency}s");
          // Load log content from session state
        moduleLogContent = SessionState.GetString(SessionKeyModuleLog, "");
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
            //UnityEngine.Debug.Log($"[ServerLogProcess] Restored database fresh mode from SessionState - will reject logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }
        
        if (debugMode && moduleLogStartFresh)
        {
            //UnityEngine.Debug.Log($"[ServerLogProcess] Restored module fresh mode from SessionState - will reject logs before {moduleLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }

        // Load SSH timestamps from session state to persist across domain reloads
        string sshModuleTimestampString = SessionState.GetString(SessionKeySSHModuleLogTimestamp, "");
        if (DateTime.TryParse(sshModuleTimestampString, out DateTime parsedSSHModuleTimestamp))
        {
            lastModuleLogTimestamp = parsedSSHModuleTimestamp;
        }
        else
        {
            lastModuleLogTimestamp = DateTime.MinValue;
        }
        
        string sshDatabaseTimestampString = SessionState.GetString(SessionKeySSHDatabaseLogTimestamp, "");
        if (DateTime.TryParse(sshDatabaseTimestampString, out DateTime parsedSSHDatabaseTimestamp))
        {
            lastDatabaseLogTimestamp = parsedSSHDatabaseTimestamp;
        }
        else
        {
            lastDatabaseLogTimestamp = DateTime.MinValue;
        }
        
        if (debugMode && lastModuleLogTimestamp != DateTime.MinValue)
        {
            UnityEngine.Debug.Log($"[ServerLogProcess] Restored SSH module timestamp from SessionState: {lastModuleLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
        }
        else if (debugMode)
        {
            UnityEngine.Debug.Log($"[ServerLogProcess] SSH module timestamp is MinValue - SessionState string: '{sshModuleTimestampString}'");
        }
        
        if (debugMode && lastDatabaseLogTimestamp != DateTime.MinValue)
        {
            UnityEngine.Debug.Log($"[ServerLogProcess] Restored SSH database timestamp from SessionState: {lastDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
        }
        else if (debugMode)
        {
            UnityEngine.Debug.Log($"[ServerLogProcess] SSH database timestamp is MinValue - SessionState string: '{sshDatabaseTimestampString}'");
        }

        // Restore log content from SessionState to prevent compilation clearing
        // This ensures ServerLogProcess maintains log continuity across domain reloads
        string restoredModuleLog = SessionState.GetString(SessionKeyModuleLog, "");
        string restoredCachedModuleLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
        string restoredDatabaseLog = SessionState.GetString(SessionKeyDatabaseLog, "");
        
        if (!string.IsNullOrEmpty(restoredModuleLog) && restoredModuleLog != "(No Module Log Found.)")
        {
            moduleLogContent = restoredModuleLog;
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Restored moduleLogContent from SessionState: {moduleLogContent.Length} chars");
        }
        
        if (!string.IsNullOrEmpty(restoredCachedModuleLog) && restoredCachedModuleLog != "(No Module Log Found.)")
        {
            cachedModuleLogContent = restoredCachedModuleLog;
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Restored cachedModuleLogContent from SessionState: {cachedModuleLogContent.Length} chars");
        }
        
        if (!string.IsNullOrEmpty(restoredDatabaseLog) && restoredDatabaseLog != "(No Database Log Found.)")
        {
            databaseLogContent = restoredDatabaseLog;
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Restored databaseLogContent from SessionState: {databaseLogContent.Length} chars");
        }

        processingCts = new CancellationTokenSource();
        StartLogLimiter();
    }
    
    public void Configure(string moduleName, string serverDirectory, bool clearModuleLogAtStart, bool clearDatabaseLogAtStart, string userName)
    {
        // Sync server mode flags at configuration time
        SyncSettings();
        
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
            // Don't clear logs if we just restored them from a compilation - preserve log continuity
            bool hasRestoredContent = !string.IsNullOrEmpty(databaseLogContent) && databaseLogContent != "(No Database Log Found.)";
            if (hasRestoredContent)
            {
                if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] clearDatabaseLogAtStart is true, but preserving restored database logs from compilation");
            }
            else
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
        }
        else if (debugMode && databaseLogStartFresh)
        {
            // Keep existing fresh mode if it was previously enabled
            UnityEngine.Debug.Log($"[ServerLogProcess] Preserving existing fresh mode from SessionState - will reject logs before {databaseLogFreshStartTime:yyyy-MM-dd HH:mm:ss}");
        }
        
        if (clearModuleLogAtStart)
        {
            // Don't clear logs if we just restored them from a compilation - preserve log continuity
            bool hasRestoredContent = !string.IsNullOrEmpty(moduleLogContent) && moduleLogContent != "(No Module Log Found.)";
            if (hasRestoredContent)
            {
                if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] clearModuleLogAtStart is true, but preserving restored module logs from compilation");
            }
            else
            {
                if (debugMode) UnityEngine.Debug.Log("[ServerLogProcess] clearModuleLogAtStart is true, clearing old module logs from SessionState");
                moduleLogContent = "";
                cachedModuleLogContent = "";
                SessionState.SetString(SessionKeyModuleLog, "");
                SessionState.SetString(SessionKeyCachedModuleLog, "");
            }
        }
        
        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Configured with username: {this.userName}, moduleName: {this.moduleName}, serverDirectory: {this.serverDirectory}");

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
        //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Server running state set to: {isRunning}");
    }

    public void SwitchModule(string newModuleName, bool clearDatabaseLogOnSwitch = true)
    {
        // Sync server mode flags before switching modules
        SyncSettings();
        
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
        
        string sessionModuleLog = SessionState.GetString(SessionKeyModuleLog, "");
        string sessionDatabaseLog = SessionState.GetString(SessionKeyDatabaseLog, "");
        string sessionCachedModuleLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
        string sessionCachedDatabaseLog = SessionState.GetString(SessionKeyCachedDatabaseLog, "");
        
        if (!string.IsNullOrEmpty(sessionModuleLog) && sessionModuleLog.Length > moduleLogContent.Length)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Updating module log from session state: {sessionModuleLog.Length} chars");
            moduleLogContent = sessionModuleLog;
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
        // Sync server mode flags before force refresh
        SyncSettings();
        
        if (debugMode) logCallback("Force refreshing SSH logs via journalctl...", 1);
        
        if (isCustomServer && serverRunning)
        {
            try
            {
                // Reset timestamps to start fresh (for manual refresh)
                lastModuleLogTimestamp = DateTime.UtcNow;
                lastDatabaseLogTimestamp = DateTime.UtcNow;
                
                // Save updated timestamps to session state
                SessionState.SetString(SessionKeySSHModuleLogTimestamp, lastModuleLogTimestamp.ToString("O"));
                SessionState.SetString(SessionKeySSHDatabaseLogTimestamp, lastDatabaseLogTimestamp.ToString("O"));

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
            if (debugMode) logCallback("SSH log refresh skipped - isCustomServer:" + isCustomServer + ", serverRunning:" + serverRunning, 0);
        }
    }
    
    // Force SSH log continuation after compilation - preserves timestamps to maintain log continuity
    public async void ForceSSHLogContinuation()
    {
        // Sync server mode flags before force refresh
        SyncSettings();
        
        if (debugMode) logCallback("Force continuing SSH logs after compilation...", 1);
        
        if (isCustomServer && serverRunning)
        {
            try
            {
                // Check if timestamps are valid and not too far behind (more than 30 minutes)
                DateTime now = DateTime.UtcNow;
                bool moduleTimestampValid = lastModuleLogTimestamp != DateTime.MinValue && lastModuleLogTimestamp.Year >= 2020;
                bool databaseTimestampValid = lastDatabaseLogTimestamp != DateTime.MinValue && lastDatabaseLogTimestamp.Year >= 2020;
                
                // If timestamps are very old (more than 30 minutes behind), adjust them to prevent massive log dumps
                if (moduleTimestampValid && (now - lastModuleLogTimestamp).TotalMinutes > 30)
                {
                    var oldTimestamp = lastModuleLogTimestamp;
                    lastModuleLogTimestamp = now.AddMinutes(-30); // Go back 30 minutes to get some recent history
                    SessionState.SetString(SessionKeySSHModuleLogTimestamp, lastModuleLogTimestamp.ToString("O"));
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Adjusted old module timestamp from {oldTimestamp:yyyy-MM-dd HH:mm:ss} to {lastModuleLogTimestamp:yyyy-MM-dd HH:mm:ss} to prevent massive log dump");
                }
                
                if (databaseTimestampValid && (now - lastDatabaseLogTimestamp).TotalMinutes > 30)
                {
                    var oldTimestamp = lastDatabaseLogTimestamp;
                    lastDatabaseLogTimestamp = now.AddMinutes(-30); // Go back 30 minutes to get some recent history
                    SessionState.SetString(SessionKeySSHDatabaseLogTimestamp, lastDatabaseLogTimestamp.ToString("O"));
                    if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Adjusted old database timestamp from {oldTimestamp:yyyy-MM-dd HH:mm:ss} to {lastDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss} to prevent massive log dump");
                }
                
                // DON'T reset timestamps - continue from where we left off
                if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Continuing SSH logs from timestamps - Module: {lastModuleLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}, Database: {lastDatabaseLogTimestamp:yyyy-MM-dd HH:mm:ss.fff}");

                // Force immediate SSH log read with existing timestamps
                await ReadSSHModuleLogsAsync();
                await ReadSSHDatabaseLogsAsync();
                
                if (debugMode) logCallback("SSH log continuation completed", 1);
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerLogProcess] Error in ForceSSHLogContinuation: {ex.Message}");
                logCallback($"SSH log continuation failed: {ex.Message}", -1);
            }
        }
        else
        {
            if (debugMode) logCallback("SSH log continuation skipped - isCustomServer:" + isCustomServer + ", serverRunning:" + serverRunning, 0);
        }
    }
    
    // Force WSL log refresh - triggers new journalctl commands immediately for WSL
    public void ForceWSLLogRefresh()
    {
        // Sync server mode flags before force refresh
        SyncSettings();
        
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
        // Sync server mode flags before starting logging
        SyncSettings();
        
        if (isCustomServer)
        {
            StartSSHLogging();
        }
        else if (isDockerServer)
        {
            StartDockerLogging();
        }
        else
        {
            StartWSLLogging();
        }
    }
    
    public void StopLogging()
    {
        // Sync server mode flags before stopping logging
        SyncSettings();
        
        if (isCustomServer)
        {
            StopSSHLogging();
        }
        else if (isDockerServer)
        {
            StopDockerLogging();
        }
        else
        {
            StopWSLLogging();
        }
    }
    
    public void CheckLogProcesses(double currentTime)
    {
        // Sync server mode flags to ensure isCustomServer matches CCCPSettingsAdapter.GetServerMode()
        SyncSettings();
        
        if (isCustomServer)
        {
            CheckSSHLogProcesses(currentTime);
        }
        else if (isDockerServer)
        {
            CheckDockerLogProcesses(currentTime);
        }
        else
        {
            CheckWSLLogProcesses(currentTime);
        }
    }
    
    public void ClearModuleLogFile()
    {
        // Sync server mode flags before clearing logs
        SyncSettings();
        
        // Clear all module logs regardless of mode to ensure clean slate when switching
        moduleLogContent = "";
        cachedModuleLogContent = "";
        SessionState.SetString(SessionKeyModuleLog, moduleLogContent);
        SessionState.SetString(SessionKeyCachedModuleLog, cachedModuleLogContent);
        
        // Clear deduplication cache for fresh start
        recentModuleLogHashes.Clear();
        
        if (isCustomServer)
        {
            ClearSSHModuleLogFile();
        }
        else if (isDockerServer)
        {
            ClearDockerLogs();
        }
        else
        {
            ClearWSLModuleLog();
        }
        
        if (debugMode) logCallback($"Module logs cleared for mode: {(isCustomServer ? "CustomServer" : isDockerServer ? "DockerServer" : "WSL")}", 1);
    }
    
    public void ClearDatabaseLog()
    {
        // Clear all database logs regardless of mode to ensure clean slate when switching
        databaseLogContent = "";
        cachedDatabaseLogContent = "";
        SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
        SessionState.SetString(SessionKeyCachedDatabaseLog, cachedDatabaseLogContent);
        
        // Clear deduplication cache for fresh start
        recentDatabaseLogHashes.Clear();
        
        if (isCustomServer)
        {
            ClearSSHDatabaseLog();
        }
        else if (isDockerServer)
        {
            ClearDockerLogs();
        }
        else
        {
            ClearWSLDatabaseLog();
        }
        
        if (debugMode) logCallback($"Database logs cleared for mode: {(isCustomServer ? "CustomServer" : isDockerServer ? "DockerServer" : "WSL")}", 1);
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
            logLine.Contains("-- Boot ") ||
            logLine.Contains("Caused by:") ||
            logLine.Contains("error sending request for url") ||
            logLine.Contains("tcp connect error") ||
            logLine.Contains("client error (Connect)") ||
            logLine.Contains("Connection refused"))
        {
            return null; // Filter out systemd service messages and reconnection spam
        }
        
        // Check if this is a journalctl format line (SSH logs)
        // Format 1: "May 29 20:16:31 LoreMagic spacetime[51367]: 2025-05-29T20:16:31.212054Z  INFO: src/lib.rs:140: Player 4 reconnected."
        // Format 2: "2025-05-29T20:32:45.845810+00:00 LoreMagic spacetime[74350]: 2025-05-29T20:16:31.212054Z  INFO: src/lib.rs:140: Player 4 reconnected."
        // Format 3: "2025-09-29T23:32:17.533355+02:00 M spacetime-database-logs-switching.sh[3239]:   INFO: src/lib.rs:33: Hello, World!"
        var journalMatch = System.Text.RegularExpressions.Regex.Match(logLine, @"^(\w{3}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+\w+\s+spacetime\[\d+\]:\s*(.*)$");
        var journalIsoMatch = System.Text.RegularExpressions.Regex.Match(logLine, @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+\+\d{2}:\d{2})\s+\w+\s+spacetime\[\d+\]:\s*(.*)$");
        var switchingScriptMatch = System.Text.RegularExpressions.Regex.Match(logLine, @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+\+\d{2}:\d{2})\s+\w+\s+spacetime-database-logs-switching\.sh\[\d+\]:\s*(.*)$");
        
        if (journalMatch.Success || journalIsoMatch.Success || switchingScriptMatch.Success)
        {
            // Extract the actual log content after the journalctl prefix
            string actualLogContent = "";
            if (journalMatch.Success)
                actualLogContent = journalMatch.Groups[2].Value.Trim();
            else if (journalIsoMatch.Success)
                actualLogContent = journalIsoMatch.Groups[2].Value.Trim();
            else if (switchingScriptMatch.Success)
                actualLogContent = switchingScriptMatch.Groups[2].Value.Trim();
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
                    //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Extracted timestamp (pattern 1): {parsed.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff} from line: {line.Substring(0, Math.Min(80, line.Length))}");
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
                    //if (debugMode) UnityEngine.Debug.Log($"[ServerLogProcess] Extracted timestamp (pattern 2): {parsed.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff} from line: {line.Substring(0, Math.Min(80, line.Length))}");
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
            
            //if (debugMode) UnityEngine.Debug.LogWarning($"[ServerLogProcess] No timestamp pattern matched for line: {line.Substring(0, Math.Min(100, line.Length))}...");
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
            process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            
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
                process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
                
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
        {   
            Process process = new Process();
            process.StartInfo.FileName = "ssh";
            // Command to test connection and run a simple spacetime command with login shell
            string testCommand = $"echo 'Testing SSH connection' && bash -l -c '{remoteSpacetimePath} --version'";
            
            process.StartInfo.Arguments = $"-i \"{sshKeyPath}\" {sshUser}@{sshHost} \"{testCommand}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            
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
            process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            
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
    
    // Generate a simple hash for log line deduplication
    private string GenerateLogLineHash(string logLine)
    {
        try
        {
            // Use a simple approach: combine timestamp and message content
            // This helps detect truly duplicate log entries while allowing legitimate repeated messages
            
            // Extract timestamp part (if exists) and message content
            var timestampMatch = System.Text.RegularExpressions.Regex.Match(logLine, @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)\s+(.*)$");
            
            if (timestampMatch.Success)
            {
                string timestamp = timestampMatch.Groups[1].Value;
                string content = timestampMatch.Groups[2].Value;
                
                // Create hash from timestamp + content for exact duplicate detection
                return $"{timestamp}|{content.GetHashCode()}";
            }
            else
            {
                // Fallback: use the entire line
                return logLine.GetHashCode().ToString();
            }
        }
        catch (Exception)
        {
            // Safe fallback
            return logLine.GetHashCode().ToString();
        }
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games