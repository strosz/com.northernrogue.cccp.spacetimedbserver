using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text.RegularExpressions;
using NorthernRogue.CCCP.Editor.Settings;

// Handles and displays the logs from both the SpacetimeDB module and the database ///

namespace NorthernRogue.CCCP.Editor {

public class ServerOutputWindow : EditorWindow
{
    // Add unique instance tracking to prevent duplicates
    private static ServerOutputWindow currentInstance;
    
    public static bool debugMode = false; // Controlled by ServerWindow    

    // Logs
    private string moduleLogFull = ""; // Module logs
    private string databaseLogFull = ""; // Database logs
    private Vector2 scrollPosition;
    private int selectedTab = 0;
    private string[] tabs = { "Module All", "Module Errors", "Database All", "Database Errors" };
    private string[] databaseOnlyTabs = { "Database All", "Database Errors" };
    private bool autoScroll = true; // Start with auto-scroll enabled
    private string currentServerMode = ""; // Track current server mode for tab visibility
    private bool isWindowEnabled = false; // Guard flag
    private int lastKnownLogHash; // For detecting changes
    private int lastKnownDbLogHash; // For detecting database log changes
    private float lastUpdateTime; // For throttling updates
    public static string logHashModule; // For echo to console
    public static string logHashDatabase; // For echo to console

    // Echo Logs to Console
    public static bool echoToConsole = false; // Whether to echo module logs to Unity Console
    private static HashSet<string> loggedToConsoleModule = new HashSet<string>(); // Track logs already sent to console
    private static HashSet<string> loggedToConsoleDatabase = new HashSet<string>(); // Track database logs already sent to console
    private bool showLocalTime = false; // Toggle for showing timestamps in local time zone
    private float logUpdateFrequency = 1f; // User-configurable log update frequency (1-10)    // Session state keys
    private const string SessionKeyModuleLog = "ServerWindow_ModuleLog";
    private const string SessionKeyCachedModuleLog = "ServerWindow_CachedModuleLog";
    private const string SessionKeyDatabaseLog = "ServerWindow_DatabaseLog";
    
    // ServerOutputWindow-specific session state keys for its display variables
    private const string SessionKeyOutputWindowModuleLogFull = "ServerOutputWindow_ModuleLogFull";
    private const string SessionKeyOutputWindowDatabaseLogFull = "ServerOutputWindow_DatabaseLogFull";
    
    // Compilation detection and state preservation keys
    private const string SessionKeyCompilationBackupModuleLog = "ServerOutputWindow_CompilationBackup_ModuleLog";
    private const string SessionKeyCompilationBackupDatabaseLog = "ServerOutputWindow_CompilationBackup_DatabaseLog";
    private const string SessionKeyCompilationDetected = "ServerOutputWindow_CompilationDetected";
    
    // Protection flags to prevent ReloadLogs from overwriting compilation recovery
    private bool compilationRecoveryActive = false;
    private double compilationRecoveryTime = 0;
    
    // Performance optimizations
    private bool needsRepaint = false; // Flag to track if content changed and needs repainting
    private Dictionary<int, string> formattedLogCache = new Dictionary<int, string>(); // Cache formatted logs
    private string currentFormattedLog = ""; // Currently displayed formatted log
    private int currentLogHash = 0; // Hash of currently displayed log
    private Rect lastScrollViewRect; // For virtualization calculations
    private Vector2 contentSize = Vector2.zero; // Content size for virtualization
    private List<string> visibleLines = new List<string>(); // Only lines currently visible
    private double lastRepaintTime = 0; // For limiting repaints
    private const double MIN_REPAINT_INTERVAL = 1.0; // Minimum time between repaints in seconds
    private const int MAX_TEXT_LENGTH = 100000; // Maximum text length to show    // RefreshOpenWindow rate limiting
    private static double lastRefreshTime = 0;
    private static double refreshInterval = 1.0; // How often it can echo to console, won't affect rate of logs in window
    
    // SSH log refresh optimization
    private static double sshRefreshInterval = 1.0;
    
    // OnFocus rate limiting to prevent rapid SSH refresh triggers
    private static double lastOnFocusTime = 0;
    private const double ON_FOCUS_INTERVAL = 2.0; // Prevent rapid OnFocus refreshes
    
    // TriggerSessionStateRefreshIfWindowExists rate limiting
    private static double lastTriggerSessionStateTime = 0;
    private const double TRIGGER_SESSION_STATE_INTERVAL = 1.0; // Prevent rapid session state refreshes
    
    // Style-related fields
    private GUIStyle logStyle;
    private GUIStyle containerStyle;
    private GUIStyle toolbarButtonStyle;
    private Font consolasFont;
    private bool stylesInitialized = false;
    private Color cmdBackgroundColor = new Color(0.1f, 0.1f, 0.1f);
    private Color cmdTextColor = new Color(0.8f, 0.8f, 0.8f);
    private Texture2D backgroundTexture;

    // Server log size tracking for custom server mode
    private float serverLogSizeMB = 0f;
    private float spacetimeDbModuleLogSizeMB = 0f;
    private float spacetimeDbDatabaseLogSizeMB = 0f;
    private bool isLoadingLogSize = false;
    private double lastLogSizeUpdateTime = 0;
    private const double LOG_SIZE_UPDATE_INTERVAL = 10.0; // Update log size every 10 seconds
    private ServerCustomProcess serverCustomProcess;
    private ServerWSLProcess wslProcess;
    private ServerManager serverManager;
    
    // Track data changes
    private bool scrollToBottom = false;
    private string displayedText = string.Empty;

    // Track instances
    private static List<ServerOutputWindow> openWindows = new List<ServerOutputWindow>();
    
    // Track compilation state to force refresh after compilation
    private static bool wasCompiling = false;

    /// <summary>
    /// Updates tab visibility based on server mode - called by ServerWindow when mode changes
    /// </summary>
    public static void UpdateTabVisibilityForServerMode(string serverMode)
    {
        if (currentInstance != null)
        {
            currentInstance.UpdateTabsForServerMode(serverMode);
        }
        
        // Update all open windows
        for (int i = openWindows.Count - 1; i >= 0; i--)
        {
            if (openWindows[i] != null)
            {
                openWindows[i].UpdateTabsForServerMode(serverMode);
            }
            else
            {
                openWindows.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Updates the tabs array based on the current server mode
    /// </summary>
    private void UpdateTabsForServerMode(string serverMode)
    {
        if (currentServerMode == serverMode) return; // No change needed
        
        string previousServerMode = currentServerMode; // Store previous mode before updating
        currentServerMode = serverMode;
        int previousSelectedTab = selectedTab;
        
        if (serverMode.Equals("MaincloudServer", StringComparison.OrdinalIgnoreCase))
        {
            tabs = databaseOnlyTabs;
            // Map previous tab selection to database tabs
            if (previousSelectedTab >= 2) // Was on Database All or Database Errors
            {
                selectedTab = previousSelectedTab - 2; // Map to 0 or 1
            }
            else // Was on Module tabs
            {
                selectedTab = 0; // Default to Database All
            }
        }
        else
        {
            tabs = new string[] { "Module All", "Module Errors", "Database All", "Database Errors" };
            // Map previous database-only tab selection back to full tabs
            if (previousServerMode.Equals("MaincloudServer", StringComparison.OrdinalIgnoreCase) && previousSelectedTab < 2)
            {
                selectedTab = previousSelectedTab + 2; // Map back to Database tabs (2 or 3)
            }
            // Otherwise keep the same tab index if it's valid
            else if (selectedTab >= tabs.Length)
            {
                selectedTab = 0; // Reset to first tab if invalid
            }
        }
        
        // Ensure selectedTab is valid for the current tabs array
        if (selectedTab >= tabs.Length || selectedTab < 0)
        {
            selectedTab = 0;
        }
        
        // Trigger scroll to bottom if auto-scroll is enabled
        if (autoScroll)
        {
            scrollToBottom = true;
        }
        
        needsRepaint = true;
        Repaint();
        
        if (debugMode)
        {
            UnityEngine.Debug.Log($"ServerOutputWindow: Updated tabs for server mode '{serverMode}'. Tab count: {tabs.Length}, Selected tab: {selectedTab}");
        }
    }

    [MenuItem("Window/SpacetimeDB Server Manager/View Logs")]
    public static void ShowWindow()
    {
        // Check if an instance already exists and close it to prevent duplicates
        if (currentInstance != null)
        {
            currentInstance.Close();
            currentInstance = null;
        }
        
        // Trigger SessionState refresh before opening window
        TriggerSessionStateRefreshIfWindowExists();
        
        ServerOutputWindow window = GetWindow<ServerOutputWindow>("Server Logs");
        window.minSize = new Vector2(400, 300);        
        window.Focus(); 
        window.ReloadLogs();
        currentInstance = window;
    }    

    public static void ShowWindow(int tab)
    {
        // Check if an instance already exists and close it to prevent duplicates
        if (currentInstance != null)
        {
            currentInstance.Close();
            currentInstance = null;
        }
        
        // Trigger SessionState refresh before opening window
        TriggerSessionStateRefreshIfWindowExists();
        
        ServerOutputWindow window = GetWindow<ServerOutputWindow>("Server Logs");
        window.minSize = new Vector2(400, 300);
        window.selectedTab = Mathf.Clamp(tab, 0, 3); // Ensure tab index is valid
        window.Focus();
        window.ReloadLogs();
        currentInstance = window;
        
        // If auto-scroll is enabled, scroll to the bottom when opening with a specific tab
        if (window.autoScroll)
        {
            window.scrollToBottom = true;
            // Use delayCall to ensure UI is updated properly
            EditorApplication.delayCall += window.Repaint;
        }
    }

    #region OnEnable
    private void OnEnable()
    {
        // Set this as the current instance
        currentInstance = this;
        
        // Add this window to the list of open windows
        if (!openWindows.Contains(this))
        {
            openWindows.Add(this);
        }
        
        CacheServerManager();
        
        // Subscribe to update
        EditorApplication.update += CheckForLogUpdates;
        
        // Subscribe to assembly reload events for compilation detection and log state preservation
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        
        // Check if we're recovering from a compilation
        RestoreLogsAfterCompilation();
        
        // Load settings from Settings
        autoScroll = CCCPSettingsAdapter.GetAutoscroll();
        echoToConsole = CCCPSettingsAdapter.GetEchoToConsole();
        showLocalTime = CCCPSettingsAdapter.GetShowLocalTime();
        logUpdateFrequency = CCCPSettingsAdapter.GetLogUpdateFrequency();

        // Apply log update frequency to intervals
        UpdateRefreshIntervals(logUpdateFrequency);
        
        // Debug timezone info on startup if debug mode is on
        if (debugMode)
        {
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
            Debug.Log($"[ServerOutputWindow] OnEnable - Local timezone offset: {offset.Hours} hours, {offset.Minutes} minutes");
        }

        // Load the log data
        // First, try to restore our own log variables from SessionState
        string restoredModuleLog = SessionState.GetString(SessionKeyOutputWindowModuleLogFull, "");
        string restoredDatabaseLog = SessionState.GetString(SessionKeyOutputWindowDatabaseLogFull, "");
        
        if (!string.IsNullOrEmpty(restoredModuleLog) && restoredModuleLog != "(No Module Log Found.)")
        {
            moduleLogFull = restoredModuleLog;
            if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] Restored moduleLogFull from SessionState: {moduleLogFull.Length} chars");
        }
        
        if (!string.IsNullOrEmpty(restoredDatabaseLog) && restoredDatabaseLog != "(No Database Log Found.)")
        {
            databaseLogFull = restoredDatabaseLog;
            if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] Restored databaseLogFull from SessionState: {databaseLogFull.Length} chars");
        }
        
        // Then reload from ServerLogProcess SessionState
        ReloadLogs();
        
        // Add post-compilation recovery with delay to ensure ServerLogProcess is ready
        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] OnEnable - Scheduling post-compilation recovery");
        EditorApplication.delayCall += () =>
        {
            // Give ServerLogProcess time to initialize, then reload again
            EditorApplication.delayCall += () =>
            {
                ReloadLogs();
                if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Post-compilation delayed reload completed");
                
                // If logs are still empty after SessionState reload, trigger server log continuation
                if ((string.IsNullOrEmpty(moduleLogFull) || moduleLogFull == "(No Module Log Found.)") && 
                    (string.IsNullOrEmpty(databaseLogFull) || databaseLogFull == "(No Database Log Found.)"))
                {
                    if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Logs still empty after delayed reload - triggering server refresh");
                    TriggerSessionStateRefreshIfWindowExists();
                }
            };
        };
        
        isWindowEnabled = true;
        
        // Listen for play mode state changes
        EditorApplication.playModeStateChanged += PlayModeStateChanged;// Clear caches
        formattedLogCache.Clear();
        needsRepaint = true;
        
        // Note: InitializeStyles() is called lazily in OnGUI() to avoid EditorStyles availability issues

        // If a non-default tab was selected when opening the window, ensure we properly handle scrolling
        if (selectedTab > 0)
        {
            // If auto-scroll is enabled, scroll to the bottom when a specific tab is selected
            if (autoScroll)
            {
                scrollToBottom = true;
            }
            EditorApplication.delayCall += Repaint;
        }
        
        // Initialize server processes based on mode
        string modeName = CCCPSettingsAdapter.GetServerMode().ToString();
        
        // Set initial tab visibility based on current server mode
        // Force update even if currentServerMode seems the same (it's empty on first run)
        string tempCurrentMode = currentServerMode;
        currentServerMode = ""; // Reset to force update
        UpdateTabsForServerMode(modeName);
        
        if (modeName.Equals("CustomServer", StringComparison.OrdinalIgnoreCase))
        {
            InitializeServerCustomProcess();
            // Update log size on window open
            UpdateLogSizeForCustomServer();
        }
        else if (modeName.Equals("WslServer", StringComparison.OrdinalIgnoreCase))
        {
            InitializewslProcess();
            // Update log size on window open
            UpdateLogSizeForWSLServer();
        }
    }

    private void CacheServerManager()
    {
        try
        {
            ServerWindow[] serverWindows = Resources.FindObjectsOfTypeAll<ServerWindow>();
            if (serverWindows != null && serverWindows.Length > 0)
            {
                ServerWindow serverWindow = serverWindows[0];
                serverManager = serverWindow.GetServerManager();
                if (debugMode && serverManager != null)
                {
                    UnityEngine.Debug.Log($"[ServerOutputWindow] Cached ServerManager for {serverManager.CurrentServerMode} mode");
                }
            }
            else
            {
                serverManager = null;
            }
        }
        catch (Exception ex)
        {
            serverManager = null;
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerOutputWindow] Failed to cache ServerManager: {ex.Message}");
            }
        }
    }

    // Simple logging callback for ServerCustomProcess
    private void LogMessage(string message, int style)
    {
        if (debugMode)
        {
            UnityEngine.Debug.Log($"[ServerOutputWindow] {message}");
        }
    }

    private void InitializeServerCustomProcess()
    {
        try
        {
            serverCustomProcess = new ServerCustomProcess(LogMessage, debugMode);
            if (debugMode) 
                UnityEngine.Debug.Log("[ServerOutputWindow] ServerCustomProcess instance initialized directly");
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Failed to initialize ServerCustomProcess: {ex.Message}");
        }
    }

    private void InitializewslProcess()
    {
        try
        {
            // Get wslProcess from ServerManager if available
            if (serverManager != null)
            {
                wslProcess = serverManager.GetWSLProcessor();
                if (debugMode && wslProcess != null) 
                    UnityEngine.Debug.Log("[ServerOutputWindow] wslProcess instance obtained from ServerManager");
            }
            else
            {
                if (debugMode) UnityEngine.Debug.LogWarning("[ServerOutputWindow] ServerManager not available, cannot get wslProcess");
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Failed to initialize wslProcess: {ex.Message}");
        }
    }

    private void OnDisable()
    {
        // Clear the current instance if this is it
        if (currentInstance == this)
        {
            currentInstance = null;
        }
        
        EditorApplication.playModeStateChanged -= PlayModeStateChanged;
        EditorApplication.update -= CheckForLogUpdates;
        isWindowEnabled = false;
        openWindows.Remove(this);
        
        // Save current log state to SessionState before window is disabled
        if (!string.IsNullOrEmpty(moduleLogFull) && moduleLogFull != "(No Module Log Found.)")
        {
            SessionState.SetString(SessionKeyOutputWindowModuleLogFull, moduleLogFull);
        }
        
        if (!string.IsNullOrEmpty(databaseLogFull) && databaseLogFull != "(No Database Log Found.)")
        {
            SessionState.SetString(SessionKeyOutputWindowDatabaseLogFull, databaseLogFull);
        }
        
        // Also save as compilation backup in case this is a compilation-triggered disable
        SaveLogsForCompilationRecovery();
        
        if (debugMode && (!string.IsNullOrEmpty(moduleLogFull) || !string.IsNullOrEmpty(databaseLogFull)))
        {
            UnityEngine.Debug.Log($"[ServerOutputWindow] OnDisable - Saved log state to SessionState: module {moduleLogFull.Length} chars, database {databaseLogFull.Length} chars");
        }
        
        // Use delayCall to update serverwindow states after the window is fully destroyed
        EditorApplication.delayCall += () => {
            try 
            {
                ServerWindow serverWindow = GetWindow<ServerWindow>();
                serverWindow.UpdateWindowStates();
            }
            catch (System.Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Failed to update window states: {ex.Message}");
            }
        };

        // Clean up textures
        if (backgroundTexture != null)
        {
            DestroyImmediate(backgroundTexture);
            backgroundTexture = null;
        }
        // Clear caches to free memory
        formattedLogCache.Clear();        
        visibleLines.Clear();
        
        // Unsubscribe from assembly reload events to prevent memory leaks
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
    }
    
    /// <summary>
    /// Called before assembly reload to save log state for post-compilation recovery
    /// </summary>
    private void OnBeforeAssemblyReload()
    {
        if (debugMode)
            UnityEngine.Debug.Log("[ServerOutputWindow] Assembly reload detected - saving log state for recovery...");
        
        // Save logs for all open windows
        SaveAllWindowLogsForCompilation();
        
        SaveLogsForCompilationRecovery();
        
        // Unregister the event to prevent memory leaks
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
    }
    
    /// <summary>
    /// Save logs for all open ServerOutputWindow instances
    /// </summary>
    private static void SaveAllWindowLogsForCompilation()
    {
        foreach (var window in openWindows)
        {
            if (window != null)
            {
                window.SaveLogsForCompilationRecovery();
            }
        }
    }
    
    /// <summary>
    /// Saves current log state to compilation-specific SessionState keys
    /// </summary>
    private void SaveLogsForCompilationRecovery()
    {
        // Mark that we detected a compilation
        SessionState.SetBool(SessionKeyCompilationDetected, true);
        
        // Save current log content to compilation backup keys
        if (!string.IsNullOrEmpty(moduleLogFull) && moduleLogFull != "(No Module Log Found.)")
        {
            SessionState.SetString(SessionKeyCompilationBackupModuleLog, moduleLogFull);
            // Also save to EditorPrefs as additional backup
            EditorPrefs.SetString("ServerOutputWindow_EditorPrefs_ModuleLog", moduleLogFull);
            if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] Saved moduleLogFull for compilation recovery: {moduleLogFull.Length} chars");
        }
        
        if (!string.IsNullOrEmpty(databaseLogFull) && databaseLogFull != "(No Database Log Found.)")
        {
            SessionState.SetString(SessionKeyCompilationBackupDatabaseLog, databaseLogFull);
            // Also save to EditorPrefs as additional backup
            EditorPrefs.SetString("ServerOutputWindow_EditorPrefs_DatabaseLog", databaseLogFull);
            if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] Saved databaseLogFull for compilation recovery: {databaseLogFull.Length} chars");
        }
        
        // Also save to regular SessionState keys as backup
        SessionState.SetString(SessionKeyOutputWindowModuleLogFull, moduleLogFull);
        SessionState.SetString(SessionKeyOutputWindowDatabaseLogFull, databaseLogFull);
        
        // Save compilation timestamp for recovery validation
        EditorPrefs.SetString("ServerOutputWindow_EditorPrefs_CompilationTime", DateTime.Now.ToBinary().ToString());
        
        if (debugMode)
        {
            UnityEngine.Debug.Log($"[ServerOutputWindow] Compilation backup complete - Module: {moduleLogFull.Length} chars, Database: {databaseLogFull.Length} chars");
        }
    }
    
    /// <summary>
    /// Restores logs after compilation if compilation was detected
    /// </summary>
    private void RestoreLogsAfterCompilation()
    {
        bool compilationWasDetected = SessionState.GetBool(SessionKeyCompilationDetected, false);
        
        // Also check EditorPrefs for recent compilation (within last 30 seconds)
        string compilationTimeStr = EditorPrefs.GetString("ServerOutputWindow_EditorPrefs_CompilationTime", "");
        bool recentCompilation = false;
        if (!string.IsNullOrEmpty(compilationTimeStr) && long.TryParse(compilationTimeStr, out long timeBinary))
        {
            DateTime compilationTime = DateTime.FromBinary(timeBinary);
            recentCompilation = (DateTime.Now - compilationTime).TotalSeconds < 30;
        }
        
        if (compilationWasDetected || recentCompilation)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] Compilation recovery detected - SessionState: {compilationWasDetected}, EditorPrefs: {recentCompilation}");
            
            // Try SessionState first
            string backupModuleLog = SessionState.GetString(SessionKeyCompilationBackupModuleLog, "");
            string backupDatabaseLog = SessionState.GetString(SessionKeyCompilationBackupDatabaseLog, "");
            
            // Fallback to EditorPrefs if SessionState is empty
            if (string.IsNullOrEmpty(backupModuleLog) || backupModuleLog == "(No Module Log Found.)")
            {
                backupModuleLog = EditorPrefs.GetString("ServerOutputWindow_EditorPrefs_ModuleLog", "");
                if (debugMode && !string.IsNullOrEmpty(backupModuleLog)) UnityEngine.Debug.Log("[ServerOutputWindow] Using EditorPrefs fallback for module log");
            }
            
            if (string.IsNullOrEmpty(backupDatabaseLog) || backupDatabaseLog == "(No Database Log Found.)")
            {
                backupDatabaseLog = EditorPrefs.GetString("ServerOutputWindow_EditorPrefs_DatabaseLog", "");
                if (debugMode && !string.IsNullOrEmpty(backupDatabaseLog)) UnityEngine.Debug.Log("[ServerOutputWindow] Using EditorPrefs fallback for database log");
            }
            
            if (!string.IsNullOrEmpty(backupModuleLog) && backupModuleLog != "(No Module Log Found.)")
            {
                moduleLogFull = backupModuleLog;
                if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] Restored moduleLogFull from compilation backup: {moduleLogFull.Length} chars");
            }
            
            if (!string.IsNullOrEmpty(backupDatabaseLog) && backupDatabaseLog != "(No Database Log Found.)")
            {
                databaseLogFull = backupDatabaseLog;
                if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] Restored databaseLogFull from compilation backup: {databaseLogFull.Length} chars");
            }
            
            // Set protection flag to prevent ReloadLogs from overwriting recovered logs
            compilationRecoveryActive = true;
            compilationRecoveryTime = EditorApplication.timeSinceStartup;
            
            // Clear the compilation detection flag and EditorPrefs
            SessionState.SetBool(SessionKeyCompilationDetected, false);
            EditorPrefs.DeleteKey("ServerOutputWindow_EditorPrefs_CompilationTime");
            EditorPrefs.DeleteKey("ServerOutputWindow_EditorPrefs_ModuleLog");
            EditorPrefs.DeleteKey("ServerOutputWindow_EditorPrefs_DatabaseLog");
            
            // Clear the backup keys to prevent stale data
            SessionState.SetString(SessionKeyCompilationBackupModuleLog, "");
            SessionState.SetString(SessionKeyCompilationBackupDatabaseLog, "");
            
            if (debugMode)
            {
                UnityEngine.Debug.Log($"[ServerOutputWindow] Compilation recovery complete - Module: {moduleLogFull.Length} chars, Database: {databaseLogFull.Length} chars. Protection active for 5 seconds.");
            }
        }
        else if (debugMode)
        {
            UnityEngine.Debug.Log("[ServerOutputWindow] No compilation detected - skipping compilation recovery");
        }
    }
    
    // Reload logs when the window gets focus
    private void OnFocus()
    {
        double currentTime = EditorApplication.timeSinceStartup;
        
        // Rate limit OnFocus triggers to prevent rapid SSH refreshes
        if (currentTime - lastOnFocusTime < ON_FOCUS_INTERVAL)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] OnFocus rate limited - only {currentTime - lastOnFocusTime:F1}s since last focus");
            return;
        }
        
        lastOnFocusTime = currentTime;
        
        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] OnFocus triggered - refreshing logs");
        
        TriggerSessionStateRefreshIfWindowExists();
        
        ReloadLogs();
        ForceRefreshLogs();
    }
    
    // Static method to trigger SessionState refresh if ServerWindow already exists
    public static void TriggerSessionStateRefreshIfWindowExists()
    {
        double currentTime = EditorApplication.timeSinceStartup;
        
        // Rate limit to prevent rapid session state refreshes
        if (currentTime - lastTriggerSessionStateTime < TRIGGER_SESSION_STATE_INTERVAL)
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] TriggerSessionStateRefreshIfWindowExists rate limited - only {currentTime - lastTriggerSessionStateTime:F1}s since last trigger");
            return;
        }
        
        lastTriggerSessionStateTime = currentTime;
        
        try
        {
            // Check if ServerWindow exists without creating it
            var existingWindows = Resources.FindObjectsOfTypeAll<ServerWindow>();
            if (existingWindows != null && existingWindows.Length > 0)
            {
                var serverWindow = existingWindows[0];
                if (serverWindow != null)
                {
                    serverWindow.ForceRefreshLogsFromSessionState();
                    if (debugMode)
                    {
                        UnityEngine.Debug.Log("[ServerOutputWindow] SessionState refresh triggered on existing ServerWindow");
                    }
                    
                    // Also trigger log continuation based on server mode to get fresh logs
                    if (serverWindow.GetCurrentServerMode() == ServerWindow.ServerMode.CustomServer)
                    {
                        EditorApplication.delayCall += () => 
                        {
                            serverWindow.ForceSSHLogContinuation();
                            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] SSH log continuation triggered for post-compilation recovery");
                        };
                    }
                    else if (serverWindow.GetCurrentServerMode() == ServerWindow.ServerMode.WSLServer)
                    {
                        EditorApplication.delayCall += () => 
                        {
                            serverWindow.ForceWSLLogRefresh();
                            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] WSL log refresh triggered for post-compilation recovery");
                        };
                    }
                }
            }
            else if (debugMode)
            {
                UnityEngine.Debug.Log("[ServerOutputWindow] No existing ServerWindow found - skipping SessionState refresh");
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Failed to trigger SessionState refresh: {ex.Message}");
            }
        }
    }
    
    // Called by ServerWindow when new log data arrives
    public static void RefreshOpenWindow()
    {
        double currentTime = EditorApplication.timeSinceStartup;
        
        // Load server mode
        string modeName = CCCPSettingsAdapter.GetServerMode().ToString();
        // Check if this might be an SSH log update (faster refresh needed)
        bool isSSHLogUpdate = modeName.Equals("CustomServer", StringComparison.OrdinalIgnoreCase);
        double currentRefreshInterval = isSSHLogUpdate ? sshRefreshInterval : refreshInterval;
        
        if (currentTime - lastRefreshTime < currentRefreshInterval)
        {
            return;
        }
        lastRefreshTime = currentTime;

        // This helps prevent UI calls during potentially unstable Editor state transitions.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (debugMode) UnityEngine.Debug.LogWarning("[ServerOutputWindow] RefreshOpenWindow skipped due to play mode change.");
            return;
        }

        // To debug how often this is called, uncomment the line below
        //if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] RefreshOpenWindow() called. Updating logs in background to be able to echo to console.");
        
        // Mark windows for update without immediate repaint (or with immediate repaint for SSH)
        var windowsToRefresh = openWindows.ToList(); 
        foreach (var window in windowsToRefresh)
        {
            if (window != null)
            {
                try
                {
                    window.needsRepaint = true;
                    // Force immediate repaint for SSH log updates to improve responsiveness
                    if (isSSHLogUpdate)
                    {
                        window.Repaint();
                    }
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Exception during RefreshOpenWindow for window '{window.titleContent.text}': {ex.Message}");
                }
            }
        }
        openWindows.RemoveAll(item => item == null); // Clean up null entries just in case
    }
    
    // High-priority refresh for database logs (bypasses rate limiting)
    public static void RefreshDatabaseLogs()
    {
        ForceRefreshLogs();
    }
    
    // Force refresh that bypasses rate limiting for critical updates
    public static void ForceRefreshLogs()
    {
        // This helps prevent UI calls during potentially unstable Editor state transitions.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (debugMode) UnityEngine.Debug.LogWarning("[ServerOutputWindow] ForceRefreshLogs skipped due to play mode change.");
            return;
        }

        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] ForceRefreshLogs() called - immediate update for database logs.");
        
        // Mark windows for immediate update
        var windowsToRefresh = openWindows.ToList(); 
        foreach (var window in windowsToRefresh)
        {
            if (window != null)
            {
                try
                {
                    window.needsRepaint = true;
                    window.Repaint(); // Force immediate repaint
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Exception during ForceRefreshLogs for window '{window.titleContent.text}': {ex.Message}");
                }
            }
        }
        openWindows.RemoveAll(item => item == null); // Clean up null entries
    }    
    
    // Refresh specifically for post-compilation state recovery
    private void ForceRefreshAfterCompilation()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] ForceRefreshAfterCompilation: Starting aggressive refresh sequence.");
        
        // Try to force SessionState refresh from the ServerWindow
        TriggerSessionStateRefreshIfWindowExists();
        
        // Multiple attempts with delays to ensure SessionState is properly refreshed
        for (int attempt = 0; attempt < 3; attempt++)
        {
            EditorApplication.delayCall += () => {
                // Try to trigger ServerWindow SessionState refresh again
                try
                {
                    var existingWindows = Resources.FindObjectsOfTypeAll<ServerWindow>();
                    if (existingWindows != null && existingWindows.Length > 0)
                    {
                        var serverWindow = existingWindows[0];
                        if (serverWindow != null)
                        {
                            serverWindow.ForceRefreshLogsFromSessionState();
                            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] ForceRefreshAfterCompilation: ServerWindow SessionState refresh triggered");
                            
                            // Trigger SSH log continuation to maintain historical logs after compilation
                            if (serverWindow.GetCurrentServerMode() == ServerWindow.ServerMode.CustomServer)
                            {
                                serverWindow.ForceSSHLogContinuation();
                                if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] ForceRefreshAfterCompilation: SSH log continuation triggered");
                            }
                            else if (serverWindow.GetCurrentServerMode() == ServerWindow.ServerMode.WSLServer)
                            {
                                serverWindow.ForceWSLLogRefresh();
                                if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] ForceRefreshAfterCompilation: WSL log refresh triggered");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] ForceRefreshAfterCompilation: ServerWindow refresh failed: {ex.Message}");
                }
                
                // Check if we have log content after refresh attempt
                string currentLog = SessionState.GetString(SessionKeyModuleLog, "");
                string cachedLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
                
                // Always reload and repaint after each attempt
                ReloadLogs();
                needsRepaint = true;
                if (autoScroll) scrollToBottom = true;
                Repaint();
            };
        }
    }

    // Debug method to manually trigger SSH log refresh
    public static void ForceSSHLogRefresh()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Manually forcing SSH log refresh...");        // Get all open ServerOutputWindows and trigger refresh
        foreach (var window in openWindows)
        {
            if (window != null)
            {
                window.ReloadLogs();
                window.Repaint();
            }
        }
    }

    // Debug method to manually test the post-compilation refresh
    public static void DebugForcePostCompilationRefresh()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Manually triggering post-compilation refresh...");        
        // Get all open ServerOutputWindows and trigger post-compilation refresh
        foreach (var window in openWindows)
        {
            if (window != null)
            {
                window.ForceRefreshAfterCompilation();
            }
        }
    }
    #endregion

    #region Styles    
    // Initialize styles for CMD-like appearance
    private void InitializeStyles()
    {
        if (stylesInitialized) return;
        
        // Check if EditorStyles are available - if not, defer initialization
        try
        {
            // Test access to EditorStyles - this will throw if not ready
            var testStyle = EditorStyles.textArea;
            var testToolbarStyle = EditorStyles.toolbarButton;
        }
        catch
        {
            // EditorStyles not ready yet, defer initialization
            return;
        }
        
        // Try to find Consolas font
        consolasFont = Font.CreateDynamicFontFromOSFont("Consolas", 12);
        if (consolasFont == null)
        {
            // Fallback options if Consolas is not available
            string[] preferredFonts = new string[] { "Courier New", "Courier", "Lucida Console", "Monaco", "Monospace" };
            foreach (string fontName in preferredFonts)
            {
                consolasFont = Font.CreateDynamicFontFromOSFont(fontName, 12);
                if (consolasFont != null) break;
            }
            
            // Ultimate fallback to default monospace font
            if (consolasFont == null)
            {
                consolasFont = EditorStyles.standardFont;
            }
        }
        
        // Create background texture
        if (backgroundTexture != null)
        {
            DestroyImmediate(backgroundTexture);
        }
        backgroundTexture = new Texture2D(1, 1);
        backgroundTexture.SetPixel(0, 0, cmdBackgroundColor);
        backgroundTexture.Apply();
        
        // Initialize container style with dark background
        containerStyle = new GUIStyle();
        containerStyle.normal.background = backgroundTexture;
        containerStyle.margin = new RectOffset(0, 0, 0, 0);
        containerStyle.padding = new RectOffset(5, 5, 5, 5);
        
        // Initialize text area style for CMD-like appearance
        logStyle = new GUIStyle(EditorStyles.textArea);
        logStyle.richText = true;
        logStyle.font = consolasFont;
        logStyle.normal.textColor = cmdTextColor;
        logStyle.normal.background = backgroundTexture;
        logStyle.focused.background = backgroundTexture;
        logStyle.active.background = backgroundTexture;
        logStyle.hover.background = backgroundTexture;
        logStyle.focused.textColor = cmdTextColor;
        logStyle.hover.textColor = cmdTextColor;
        logStyle.active.textColor = cmdTextColor;
        logStyle.wordWrap = false; // Allow horizontal scrolling
        
        // Toolbar button style
        toolbarButtonStyle = new GUIStyle(EditorStyles.toolbarButton);
        
        stylesInitialized = true;
    }

    // Reload logs from SessionState - Load combined log
    private void ReloadLogs()
    {
        // Only block during compilation
        if (EditorApplication.isCompiling)
        {
            if (debugMode) UnityEngine.Debug.LogWarning("[ServerOutputWindow] ReloadLogs skipped due to compilation.");
            return;
        }
        
        // Check if compilation recovery is active and should be protected
        double currentTime = EditorApplication.timeSinceStartup;
        if (compilationRecoveryActive)
        {
            // Protect recovered logs for 5 seconds to allow ServerLogProcess to restore and provide content
            if (currentTime - compilationRecoveryTime < 5.0)
            {
                if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] ReloadLogs skipped - compilation recovery protection active ({5.0 - (currentTime - compilationRecoveryTime):F1}s remaining)");
                return;
            }
            else
            {
                // Protection period expired
                compilationRecoveryActive = false;
                if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Compilation recovery protection expired - resuming normal ReloadLogs");
            }
        }        
        string newOutputLog = SessionState.GetString(SessionKeyModuleLog, "");
        string cachedModuleLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
        
        // Debug: Log SessionState content length
        if (debugMode)
        {
            UnityEngine.Debug.Log($"[ServerOutputWindow] ReloadLogs - SessionState module log: {newOutputLog.Length} chars, cached: {cachedModuleLog.Length} chars, current moduleLogFull: {moduleLogFull.Length} chars");
        }
        
        // Use the longer log content (current vs cached) to ensure we get the most complete data
        // This helps when server state gets confused after compilation
        if (cachedModuleLog.Length > newOutputLog.Length)
        {
            newOutputLog = cachedModuleLog;
            if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] Using cached module log ({cachedModuleLog.Length} chars) over current ({newOutputLog.Length} chars)");
        }
        
        // If both are empty, show default message
        if (string.IsNullOrEmpty(newOutputLog))
        {
            newOutputLog = "(Waiting for new Module logs...)";
        }

        string newDatabaseLog = SessionState.GetString(SessionKeyDatabaseLog, "(No Database Log Found.)");
        
        // Debug: Log database content length  
        if (debugMode)
        {
            UnityEngine.Debug.Log($"[ServerOutputWindow] ReloadLogs - SessionState database log: {newDatabaseLog.Length} chars, current databaseLogFull: {databaseLogFull.Length} chars");
        }

        // Only update and invalidate cache if log content changed
        if (newOutputLog != moduleLogFull || newDatabaseLog != databaseLogFull)
        {
            // Check if this is substantial new content
            bool hasSubstantialContent = (!string.IsNullOrEmpty(newOutputLog) && newOutputLog != "(No Module Log Found.)" && newOutputLog.Length > 100) ||
                                       (!string.IsNullOrEmpty(newDatabaseLog) && newDatabaseLog != "(No Database Log Found.)" && newDatabaseLog.Length > 100);
            
            // If compilation recovery is active, disable it when we get substantial new content
            if (compilationRecoveryActive && hasSubstantialContent)
            {
                compilationRecoveryActive = false;
                if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Disabling compilation recovery protection - ServerLogProcess providing new content");
            }
            
            // Normal log update - ServerLogProcess now handles continuity internally
            moduleLogFull = newOutputLog;
            databaseLogFull = newDatabaseLog;
            
            if (debugMode)
            {
                UnityEngine.Debug.Log($"[ServerOutputWindow] Log content updated - moduleLogFull: {moduleLogFull.Length} chars, databaseLogFull: {databaseLogFull.Length} chars");
            }
            
            // Save the updated log variables to SessionState for persistence across compilation
            SessionState.SetString(SessionKeyOutputWindowModuleLogFull, moduleLogFull);
            SessionState.SetString(SessionKeyOutputWindowDatabaseLogFull, databaseLogFull);
            
            // Also update compilation backup to ensure it's always current
            SessionState.SetString(SessionKeyCompilationBackupModuleLog, moduleLogFull);
            SessionState.SetString(SessionKeyCompilationBackupDatabaseLog, databaseLogFull);
            
            if (debugMode)
            {
                UnityEngine.Debug.Log($"[ServerOutputWindow] Saved log variables to SessionState - module: {moduleLogFull.Length} chars, database: {databaseLogFull.Length} chars");
            }
            
            // Invalidate the cache for the current tab
            if (formattedLogCache.ContainsKey(selectedTab))
            {
                formattedLogCache.Remove(selectedTab);
            }
            
            needsRepaint = true;
            
            // Reset manual scroll flag when logs reload if auto-scroll is on
            if(autoScroll) {
                scrollToBottom = true;
            }
        }
    }
    #endregion

    #region OnGUI
    private void OnGUI()
    {
        isWindowEnabled = true;
        
        // Ensure styles are initialized
        if (!stylesInitialized)
        {
            InitializeStyles();
        }
        
        // Check if Editor is compiling
        if (EditorApplication.isCompiling)
        {
            GUILayout.Label("Editor is compiling...");
            return;
        }

        // Toolbar section - using default Unity styling
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh", toolbarButtonStyle, GUILayout.Width(60)))
        {
            ForceRefreshAfterCompilation();

            // Handle server-specific refresh based on mode
            try
            {
                var serverWindow = GetWindow<ServerWindow>();
                if (serverWindow != null)
                {
                    if (serverWindow.GetCurrentServerMode() == ServerWindow.ServerMode.CustomServer)
                    {
                        serverWindow.ForceSSHLogRefresh();
                        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Triggered SSH log refresh for custom server");
                        
                        // Update log size when refresh button is clicked for custom server
                        UpdateLogSizeForCustomServer();
                    }
                    else if (serverWindow.GetCurrentServerMode() == ServerWindow.ServerMode.WSLServer)
                    {
                        serverWindow.ForceWSLLogRefresh();
                        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Triggered WSL log refresh for WSL server");
                        
                        // Update log size when refresh button is clicked for WSL server
                        UpdateLogSizeForWSLServer();
                    }
                }
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Failed to trigger server-specific log refresh: {ex.Message}");
            }
            
            ReloadLogs();
            formattedLogCache.Clear(); // Clear all cached formatted logs
            needsRepaint = true;
        }
        
        if (GUILayout.Button("Clear Logs", toolbarButtonStyle, GUILayout.Width(80)))
        {
            try
            {
                GetWindow<ServerWindow>().ClearModuleLogFile();
                GetWindow<ServerWindow>().ClearDatabaseLog();
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Failed to clear logs via ServerWindow: {ex.Message}");
            }
            
            moduleLogFull = "";
            databaseLogFull = "";
            SessionState.SetString(SessionKeyModuleLog, "");
            SessionState.SetString(SessionKeyDatabaseLog, "");
            loggedToConsoleModule.Clear();
            loggedToConsoleDatabase.Clear();
            formattedLogCache.Clear();
            scrollPosition = Vector2.zero;
            // Reset auto-scroll when clearing logs
            autoScroll = true;
            CCCPSettingsAdapter.SetAutoscroll(true);
            needsRepaint = true;
        }

        // Add Save Current Log button
        if (GUILayout.Button("Save Current Log", toolbarButtonStyle, GUILayout.Width(110)))
        {
            SaveCurrentLogToFile();
        }
        
        // Auto Scroll toggle
        string autoScrollTooltip = "Automatically scroll to the bottom when new logs arrive.";
        bool newAutoScroll = GUILayout.Toggle(autoScroll, new GUIContent("Auto Scroll", autoScrollTooltip), GUILayout.Width(90));
        if (newAutoScroll != autoScroll)
        {
            autoScroll = newAutoScroll;
            CCCPSettingsAdapter.SetAutoscroll(autoScroll);

            if (autoScroll)
            {
                scrollToBottom = true;
                needsRepaint = true;
            }
        }

        // Echo to Console toggle
        string echoToConsoleTooltip = "Echo module and database errors to Unity Console for easier debugging.";
        bool newEchoToConsole = GUILayout.Toggle(echoToConsole, new GUIContent("Echo to Console", echoToConsoleTooltip), GUILayout.Width(120));
        if (newEchoToConsole != echoToConsole)
        {
            echoToConsole = newEchoToConsole;
            CCCPSettingsAdapter.SetEchoToConsole(echoToConsole);

            if (echoToConsole)
            {
                loggedToConsoleModule.Clear();
                loggedToConsoleDatabase.Clear();
            }
        }

        // Local Time toggle
        string localTimeTooltip = "Show timestamps in local time zone instead of UTC.";
        bool newShowLocalTime = GUILayout.Toggle(showLocalTime, new GUIContent("Local Time", localTimeTooltip), GUILayout.Width(90));
        if (newShowLocalTime != showLocalTime)
        {
            showLocalTime = newShowLocalTime;
            CCCPSettingsAdapter.SetShowLocalTime(showLocalTime);

            // Clear the format cache to refresh timestamps
            formattedLogCache.Clear();
            currentLogHash = 0; // Reset hash to force reformatting
            needsRepaint = true;
            
            // Add debug info to verify timezone
            if (debugMode)
            {
                TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
                Debug.Log($"[ServerOutputWindow] Local timezone offset: {offset.Hours} hours, {offset.Minutes} minutes");
            }        
        }
        
        // Log Update Frequency control
        string logUpdateFrequencyTooltip = "Adjust the frequency of log updates requested from the server (every 10-1 seconds).";
        GUILayout.Label(new GUIContent($"Update Frequency:", logUpdateFrequencyTooltip), GUILayout.Width(110));
        float newLogUpdateFrequency = EditorGUILayout.Slider(logUpdateFrequency, 10f, 1f, GUILayout.Width(110));
        if (Math.Abs(newLogUpdateFrequency - logUpdateFrequency) > 0.01f)
        {
            logUpdateFrequency = newLogUpdateFrequency;
            CCCPSettingsAdapter.SetLogUpdateFrequency(logUpdateFrequency);

            // Update the refresh intervals for RefreshOpenWindow rate limiting
            UpdateRefreshIntervals(logUpdateFrequency);
            
            if (debugMode)
            {
                UnityEngine.Debug.Log($"[ServerOutputWindow] Log update frequency changed to {logUpdateFrequency:F1}s");
            }
        }
        
        // Server Log Size label (for Custom Server and WSL Server modes)
        string modeName = CCCPSettingsAdapter.GetServerMode().ToString();
        if (modeName.Equals("CustomServer", StringComparison.OrdinalIgnoreCase) || modeName.Equals("WslServer", StringComparison.OrdinalIgnoreCase))
        {
            GUILayout.FlexibleSpace(); // Push the log size label to the right
            string logSizeText = isLoadingLogSize ? "Loading..." : $"Journal Size: {serverLogSizeMB:F2} MB";
            string serverTypeText = modeName.Equals("CustomServer", StringComparison.OrdinalIgnoreCase) ? "Custom Server" : "WSL";
            GUIContent logSizeContent = new GUIContent(
                logSizeText,
                $"The size of the complete {serverTypeText.ToLower()} server journalctl folder that keeps all the server logs including OS processes and SpacetimeDB. It's normal that it is a few hundred MB, but displayed here to easier keep check of it.\n"+
                "SpacetimeDB Module Log Size: " + spacetimeDbModuleLogSizeMB + " MB\n" +
                "SpacetimeDB Database Log Size: " + spacetimeDbDatabaseLogSizeMB + " MB"
            );
            
            // Use EditorGUILayout.LabelField for better tooltip support
            EditorGUILayout.LabelField(logSizeContent, EditorStyles.miniLabel, GUILayout.Width(170));
        }
        
        EditorGUILayout.EndHorizontal();
        
        // Tabs
        int newSelectedTab = GUILayout.Toolbar(selectedTab, tabs);
        if (newSelectedTab != selectedTab)
        {
            selectedTab = newSelectedTab;
            needsRepaint = true;
            
            // Reset scroll positions when changing tabs
            if (autoScroll) scrollToBottom = true;
        }

        // Get the formatted text for display
        displayedText = GetFormattedLogForDisplay();
        
        // Calculate the content height for proper scrolling
        lastScrollViewRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, 
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                
        // Handle AutoScroll before displaying
        if (scrollToBottom && autoScroll)
        {
            // Apply scrolling at end of frame to ensure it works properly
            EditorApplication.delayCall += () => {
                scrollPosition.y = float.MaxValue;
                scrollToBottom = false;
                Repaint();
            };
        }
        
        // Create a scope for the background color
        GUI.Box(lastScrollViewRect, GUIContent.none, containerStyle);
        
        // Calculate exact content height
        float contentHeight = GetContentHeight(displayedText);
        
        // Begin scroll view with both horizontal and vertical scrolling
        scrollPosition = GUI.BeginScrollView(
            lastScrollViewRect, 
            scrollPosition, 
            new Rect(0, 0, GetContentWidth(displayedText), contentHeight),
            true, true);

        // Display the text using TextArea with consistent style
        // We use GUI.TextArea because it allows proper selection and copy/paste
        int controlID = GUIUtility.GetControlID(FocusType.Passive);
        
        // Use exact content rect for the TextArea to avoid extra space
        GUI.TextArea(new Rect(0, 0, GetContentWidth(displayedText), contentHeight), 
            displayedText, logStyle);
        
        // Store the control ID for keyboard focus handling
        if (Event.current.type == EventType.MouseDown && 
            new Rect(0, 0, GetContentWidth(displayedText), GetContentHeight(displayedText))
              .Contains(Event.current.mousePosition))
        {
            GUIUtility.keyboardControl = controlID;
        }
               
        GUI.EndScrollView();
        
        // Only request repaint if needed and not too frequent
        if (needsRepaint && (EditorApplication.timeSinceStartup - lastRepaintTime) >= MIN_REPAINT_INTERVAL)
        {
            lastRepaintTime = EditorApplication.timeSinceStartup;
            needsRepaint = false;
            Repaint();
        }
    }

    // Update refresh intervals for RefreshOpenWindow rate limiting
    private void UpdateRefreshIntervals(float frequency)
    {
        // Clamp frequency between 1 and 10
        frequency = Mathf.Clamp(frequency, 1f, 10f);
        
        // Update the intervals used by RefreshOpenWindow for rate limiting
        refreshInterval = frequency;
        sshRefreshInterval = frequency;
    }

    // Calculate content width based on longest line
    private float GetContentWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return position.width;
        
        // Ensure style is initialized
        if (logStyle == null) InitializeStyles();
        
        // Estimate based on character count in the longest line
        string[] lines = text.Split('\n');
        int maxLength = 0;
        foreach (string line in lines)
        {
            maxLength = Mathf.Max(maxLength, line.Length);
        }
        
        // Estimate width based on character count (average char width for monospace)
        float charWidth = logStyle.CalcSize(new GUIContent("M")).x;
        float estimatedWidth = charWidth * maxLength;
        
        // Ensure minimum width matches window width
        return Mathf.Max(estimatedWidth, position.width - 20);
    }
    
    // Calculate content height for scroll view
    private float GetContentHeight(string text)
    {
        if (string.IsNullOrEmpty(text)) return 10f;
        
        // Use line count to determine height
        int lineCount = text.Split('\n').Length;
        
        // Get line height from style (fallback to standard line height) // Set this manually for now to get the text to go to the bottom of the window
        float singleLineHeight = 14.023f;/*logStyle != null ? 
            logStyle.CalcSize(new GUIContent("Test")).y : EditorGUIUtility.singleLineHeight;*/
        
        // Calculate precise height without extra padding
        return lineCount * singleLineHeight;
        //return 1300f;
    }

    
    // Public method to select the Database All tab
    public void SelectDatabaseTab()
    {
        selectedTab = 2; // Index for "Database All" tab
        needsRepaint = true;
        Repaint();
    }

    // Public method to select the Database Errors tab
    public void SelectDatabaseErrorsTab()
    {
        // Find the Database Errors tab in the current tabs array
        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == "Database Errors")
            {
                selectedTab = i;
                needsRepaint = true;
                Repaint();
                return;
            }
        }
        
        // Fallback: if Database Errors tab not found, select the last tab (likely Database All)
        selectedTab = tabs.Length - 1;
        needsRepaint = true;
        Repaint();
    }
    #endregion

    #region Logs
    // Helper method to select and limit log content based on tab
    private string GetLogForCurrentTab()
    {
        string logToShow = "";
        
        // Validate selectedTab is within bounds
        if (selectedTab < 0 || selectedTab >= tabs.Length)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Invalid selectedTab {selectedTab}, tabs.Length={tabs.Length}. Resetting to 0.");
            }
            selectedTab = 0;
        }
        
        // Determine actual tab type based on current tabs configuration
        string currentTabName = tabs[selectedTab];
        
        if (debugMode)
        {
            UnityEngine.Debug.Log($"[ServerOutputWindow] GetLogForCurrentTab: selectedTab={selectedTab}, currentTabName='{currentTabName}', serverMode='{currentServerMode}'");
        }
        
        switch (currentTabName)
        {
            case "Module All":
                if (string.IsNullOrEmpty(moduleLogFull)) {
                    return "(Waiting for new Module logs...)";
                }
                logToShow = moduleLogFull;
                break;
            
            case "Module Errors":
                if (string.IsNullOrEmpty(moduleLogFull)) {
                    return "(Waiting for new Module logs...)";
                }
                
                try {
                    var errorLines = moduleLogFull.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => 
                            line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || 
                            line.Contains("WARN", StringComparison.OrdinalIgnoreCase) || 
                            line.Contains("panic", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("[TAIL ERROR]") || 
                            line.StartsWith("E "));
                    
                    logToShow = string.Join("\n", errorLines);
                    
                    if (string.IsNullOrWhiteSpace(logToShow)) {
                        return "(No errors detected in Module log)";
                    }
                } catch (Exception ex) {
                    return $"Error filtering logs: {ex.Message}";
                }
                break;
            
            case "Database All":
                if (string.IsNullOrEmpty(databaseLogFull)) {
                    return "(Waiting for new Database logs...)";
                }
                logToShow = databaseLogFull;
                break;
                
            case "Database Errors":
                if (string.IsNullOrEmpty(databaseLogFull)) {
                    return "(Waiting for new Database logs...)";
                }
                
                try {
                    var errorLines = databaseLogFull.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => 
                            line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || 
                            line.Contains("WARN", StringComparison.OrdinalIgnoreCase) || 
                            line.Contains("panic", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("[TAIL ERROR]") || 
                            line.StartsWith("E "));
                    
                    logToShow = string.Join("\n", errorLines);
                    
                    if (string.IsNullOrWhiteSpace(logToShow)) {
                        return "(No errors detected in Database log)";
                    }
                } catch (Exception ex) {
                    return $"Error filtering database logs: {ex.Message}";
                }
                break;
                
            default:
                if (debugMode)
                {
                    UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Unknown tab selected: '{currentTabName}' (index {selectedTab})");
                }
                return $"(Unknown tab selected: '{currentTabName}')";
        }
        
        // Limit log size for performance: more aggressive for database logs
        bool isDatabaseTab = currentTabName.Contains("Database");
        int maxLogLength = isDatabaseTab ? 40000 : MAX_TEXT_LENGTH;
        
        if (logToShow.Length > maxLogLength) {
            logToShow = "[... Log Truncated for Performance ...]\n" + 
                       logToShow.Substring(logToShow.Length - maxLogLength);
        }
        
        return logToShow;
    }    
    
    // Efficiently format log content with caching
    private string GetFormattedLogForDisplay()
    {
        // Get raw log text for current tab
        string rawLog = GetLogForCurrentTab();
        int logHash = rawLog.GetHashCode();
        
        // Generate a compound hash that includes the showLocalTime setting
        int combinedHash = logHash ^ (showLocalTime ? 1 : 0);
        
        // If we already formatted this exact log content with the same settings, use cached version
        if (formattedLogCache.TryGetValue(selectedTab, out string cachedLog) && currentLogHash == combinedHash)
        {
            return cachedLog;
        }
        
        // Otherwise, format the log and cache it
        string formattedLog = FormatLogContent(rawLog);
        // Update cache and current state
        formattedLogCache[selectedTab] = formattedLog;
        currentLogHash = logHash ^ (showLocalTime ? 1 : 0); // Store combined hash
        currentFormattedLog = formattedLog; // Keep reference to current formatted log
        
        return formattedLog;
    }

    // Format log content efficiently
    private string FormatLogContent(string logContent)
    {
        if (string.IsNullOrEmpty(logContent))
            return logContent;

        // Strip ANSI Escape Codes with improved pattern
        string strippedLog = Regex.Replace(logContent, @"\x1B\[[0-9;]*[mK]", "");
        
        // Remove only problematic control characters, preserve Unicode art characters
        // Remove null, bell, backspace, form feed, and delete characters
        strippedLog = Regex.Replace(strippedLog, @"[\x00\x07\x08\x0C\x7F]", "");
        
        // Replace Unicode characters that don't render properly in Unity Editor with ASCII alternatives
        // Map common Braille and box-drawing characters to ASCII equivalents for better display
        strippedLog = strippedLog
            .Replace("⢀", "'")
            .Replace("⣼", "#")
            .Replace("⠟", "*")
            .Replace("⣠", "#")
            .Replace("⣶", "#")
            .Replace("⡿", "#")
            .Replace("⠿", "0")
            .Replace("⠛", "*")
            .Replace("⣠", "#")
            .Replace("⡾", "#")
            .Replace("⠋", "*")
            .Replace("⠤", "-")
            .Replace("⠞", "*")
            .Replace("⠉", "'")
            .Replace("⢀", "'")
            .Replace("⡼", "#")
            .Replace("⠋", "*")
            .Replace("⢀", ",")
            .Replace("⠔", ".")
            .Replace("⢠", ".")
            .Replace("⣴", "d")
            .Replace("⣦", "h")
            .Replace("⣤", "o")
            .Replace("⠒", "-")
            .Replace("⠶", "o")
            .Replace("⣀", ".")
            .Replace("⣾", "d")
            .Replace("⣷", "b");        
            
        // Replace only the Unicode replacement character with spaces
        strippedLog = strippedLog.Replace('\uFFFD', ' ');
                
        // Add CMD-style color formatting for error/warning messages
        strippedLog = strippedLog.Replace("ERROR", "<color=#FF6666>ERROR</color>");
        strippedLog = strippedLog.Replace("error:", "<color=#FF6666>error:</color>");
        strippedLog = strippedLog.Replace("Error:", "<color=#FF6666>Error:</color>");
        strippedLog = strippedLog.Replace("WARN", "<color=#FFCC66>WARN</color>");
        strippedLog = strippedLog.Replace("WARNING", "<color=#FFCC66>WARNING</color>");
        strippedLog = strippedLog.Replace("warning:", "<color=#FFCC66>warning:</color>");
        strippedLog = strippedLog.Replace("INFO", "<color=#66CCFF>INFO</color>");
        strippedLog = strippedLog.Replace("DEBUG", "<color=#66CCFF>DEBUG</color>");

        string modeName = CCCPSettingsAdapter.GetServerMode().ToString();

        // Also convert any existing formatted timestamps if showing local time
        if (showLocalTime) {
            strippedLog = Regex.Replace(strippedLog, 
                @"\[(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2})\]", 
                match => {
                    try {
                        // Parse the timestamp
                        int year = int.Parse(match.Groups[1].Value);
                        int month = int.Parse(match.Groups[2].Value);
                        int day = int.Parse(match.Groups[3].Value);
                        int hour = int.Parse(match.Groups[4].Value);
                        int minute = int.Parse(match.Groups[5].Value);
                        int second = int.Parse(match.Groups[6].Value);
                        
                        // Create a DateTimeOffset assuming UTC
                        var utcTime = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
                        
                        if (modeName.Equals("WslServer", StringComparison.OrdinalIgnoreCase) && (selectedTab == 0 || selectedTab == 1)) {
                            // For WSL server mode module log, we already get local time. Quick fix until later.
                            utcTime = utcTime.AddHours(-2);
                        }

                        // Convert to local time
                        var localTime = utcTime.ToLocalTime();
                        
                        // Return the formatted timestamp
                        return $"[{localTime.ToString("yyyy-MM-dd HH:mm:ss")}]";
                    }
                    catch {
                        // If any parsing fails, return the original timestamp
                        return match.Value;
                    }
                });
        }
        
        // Format timestamps with special color
        strippedLog = Regex.Replace(strippedLog, 
            @"(\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\])", 
            "<color=#AAAAAA>$1</color>");
        
        return strippedLog;
    }    
    
    private void CheckForLogUpdates()
    {
        // Track compilation state changes and force refresh after compilation completes
        if (wasCompiling && !EditorApplication.isCompiling)
        {
            // Compilation just finished, force immediate refresh
            wasCompiling = false;
            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Compilation finished, forcing log refresh.");
            
            // Force clear cache and reload to refresh logs
            formattedLogCache.Clear();
            lastKnownLogHash = 0;
            lastKnownDbLogHash = 0;            // Use delayCall to ensure compilation is fully finished
            EditorApplication.delayCall += () => {
                // More aggressive refresh after compilation
                ForceRefreshAfterCompilation();
            };
            
            return;
        }
        else if (!wasCompiling && EditorApplication.isCompiling)
        {
            wasCompiling = true;
            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Compilation started.");
        }
        
        // Only block during compilation
        if (!isWindowEnabled || EditorApplication.isCompiling)
            return;

        // Throttle updates to avoid excessive checks, but check more frequently during play mode
        float checkInterval = EditorApplication.isPlaying ? 0.2f : logUpdateFrequency;
        if (Time.realtimeSinceStartup - lastUpdateTime < checkInterval)
            return;

        lastUpdateTime = Time.realtimeSinceStartup;
        
        // Trigger log processing directly based on logUpdateFrequency instead of waiting for ServerManager's CheckAllStatus
        TriggerLogProcessing();        
        // Check main logs (consider both current and cached module logs)
        string currentLog = SessionState.GetString(SessionKeyModuleLog, "");
        string cachedModuleLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
        
        // Use the longer log content to ensure we detect changes properly
        string effectiveModuleLog = cachedModuleLog.Length > currentLog.Length ? cachedModuleLog : currentLog;
        int currentHash = effectiveModuleLog.GetHashCode();

        // Check database logs 
        string currentDbLog = SessionState.GetString(SessionKeyDatabaseLog, "");
        int currentDbHash = currentDbLog.GetHashCode();

        // Only reload and repaint if the content actually changed
        if (currentHash != lastKnownLogHash || currentDbHash != lastKnownDbLogHash)
        {
            ReloadLogs();
            needsRepaint = true;
            
            // Only repaint if the updated log is currently displayed
            if ((selectedTab == 0 || selectedTab == 1) && currentHash != lastKnownLogHash)
            {
                if (autoScroll) scrollToBottom = true;
                Repaint();
            }
            else if (selectedTab == 2 && currentDbHash != lastKnownDbLogHash)
            {
                if (autoScroll) scrollToBottom = true;
                Repaint();
            }
            else if (selectedTab == 3 && currentDbHash != lastKnownDbLogHash)
            {
                if (autoScroll) scrollToBottom = true;
                Repaint();
            }
            
            lastKnownLogHash = currentHash;
            lastKnownDbLogHash = currentDbHash;
        }
    }

    // Trigger log processing directly based on logUpdateFrequency instead of waiting for ServerManager
    private void TriggerLogProcessing()
    {
        try
        {
            if (serverManager != null)
            {
                // Use the new TriggerLogProcessing method in ServerManager
                serverManager.TriggerLogProcessing();
                
                if (debugMode)
                {
                    UnityEngine.Debug.Log($"[ServerOutputWindow] Triggered log processing for {serverManager.CurrentServerMode} mode");
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerOutputWindow] Failed to trigger log processing: {ex.Message}");
            }
        }
    }

    private void PlayModeStateChanged(PlayModeStateChange state)
    {
        // Force an immediate update when entering play mode
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            lastUpdateTime = 0f; // Reset the update timer
            needsRepaint = true;
            CheckForLogUpdates();
        }
    }
    #endregion

    #region Echo Logs
    private static void EchoLogsToConsole(string log, string logType)
    {
        if (string.IsNullOrEmpty(log)) return;
        
        // Select the appropriate tracking collection and hash variable based on log type
        HashSet<string> loggedToConsole;
        if (logType == "Module")
        {
            loggedToConsole = loggedToConsoleModule;
        }
        else if (logType == "Database")
        {
            loggedToConsole = loggedToConsoleDatabase;
        }
        else
        {
            return; // Unknown log type
        }
        
        // Split the log into lines
        string[] lines = log.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (string line in lines)
        {
            // Only process lines we haven't logged before to avoid duplicates
            string trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine)) continue;
            
            // Create a hash of the line to track what we've logged
            // Strip timestamps or other variable data to avoid re-logging the same error
            string logHash = GetLogHash(trimmedLine);
            
            if (!string.IsNullOrEmpty(logHash) && !loggedToConsole.Contains(logHash))
            {
                // Check for errors or warnings (adjust patterns as needed)
                if (trimmedLine.Contains("ERROR") || trimmedLine.Contains("error:") || 
                    trimmedLine.ToLower().Contains("exception"))
                {
                    UnityEngine.Debug.LogError($"[SpacetimeDB {logType}] {trimmedLine}");
                    loggedToConsole.Add(logHash);
                }
                else if (trimmedLine.Contains("WARNING") || trimmedLine.Contains("warning:"))
                {
                    if (trimmedLine.Contains("some trace filter directives would enable traces that are disabled statically"))
                    {
                        // Skip this known warning which informs about debug levels
                        continue;
                    }

                    UnityEngine.Debug.LogWarning($"[SpacetimeDB {logType}] {trimmedLine}");
                    loggedToConsole.Add(logHash);
                }
                
                // Limit tracked messages to prevent memory issues
                if (loggedToConsole.Count > 1000)
                {
                    loggedToConsole.Clear(); // Just reset if we hit the limit
                }
            }
        }
    }
    
    // Helper to get a hash of the log line for tracking duplication
    private static string GetLogHash(string line)
    {
        // Skip timestamp portion (usually at the beginning in brackets)
        int contentStart = line.IndexOf(']');
        if (contentStart > 0 && contentStart < line.Length - 1)
        {
            // Get just the error message part without timestamps
            return line.Substring(contentStart + 1).Trim();
        }
        return line.Trim();
    }

    public static void EchoLogsToConsole()
    {
        //Debug.Log("[ServerOutputWindow] Echoing logs to console...");
        try
        {
            string moduleLog = SessionState.GetString(SessionKeyModuleLog, "");
            string databaseLog = SessionState.GetString(SessionKeyDatabaseLog, "");
            
            if (!string.IsNullOrEmpty(moduleLog))
            {
                EchoLogsToConsole(moduleLog, "Module");
            }
            
            if (!string.IsNullOrEmpty(databaseLog))
            {
                EchoLogsToConsole(databaseLog, "Database");
            }
            
            if (debugMode && (!string.IsNullOrEmpty(moduleLog) || !string.IsNullOrEmpty(databaseLog)))
            {
                string modeName = CCCPSettingsAdapter.GetServerMode().ToString();
                //UnityEngine.Debug.Log($"[ServerOutputWindow] Standalone echo completed for {modeName} mode");
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerOutputWindow] Error in EchoLogsToConsoleStandalone: {ex.Message}");
            }
        }
    }
    #endregion

    #region Save Logs
    // New method to save the current log to a file
    private void SaveCurrentLogToFile()
    {
        try
        {
            // Get the log content based on the selected tab
            string logContent = "";
            string logType = "";
            
            // Get the formatted log content that's being displayed in the window
            string displayedContent = GetFormattedLogForDisplay();
            
            // Strip any rich text formatting
            displayedContent = Regex.Replace(displayedContent, @"<color=[^>]+>", "");
            displayedContent = Regex.Replace(displayedContent, @"</color>", "");
            
            if (selectedTab == 2 || selectedTab == 3) // Database logs
            {
                logType = "DatabaseLogs";
            }
            else // Module logs
            {
                logType = "ModuleLogs";
            }
            
            // Use the displayed content rather than raw logs
            // For raw logs, use the following line instead:
            // logContent = moduleLogFull;
            // logContent = databaseLogFull;
            logContent = displayedContent;
            
            if (string.IsNullOrEmpty(logContent))
            {
                EditorUtility.DisplayDialog("Save Log", "There is no log content to save.", "OK");
                return;
            }
            
            // Get the default save directory from ServerWindow if available
            string defaultDirectory = "";
            ServerWindow serverWindow = GetWindow<ServerWindow>();
            if (serverWindow != null)
            {
                System.Reflection.FieldInfo backupDirField = serverWindow.GetType().GetField("backupDirectory", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (backupDirField != null)
                {
                    defaultDirectory = (string)backupDirField.GetValue(serverWindow);
                }
            }
            
            // Fallback to Application.dataPath if no backup directory is configured
            if (string.IsNullOrEmpty(defaultDirectory))
            {
                defaultDirectory = Application.dataPath;
            }
            
            // Create a filename with date and time
            string dateTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string defaultFilename = $"{logType}_{dateTime}.txt";
            
            // Show save file dialog
            string savedPath = EditorUtility.SaveFilePanel(
                "Save Log File",
                defaultDirectory,
                defaultFilename,
                "txt");
            
            // If user cancels, return
            if (string.IsNullOrEmpty(savedPath))
                return;
                
            // Write the log content to the file
            System.IO.File.WriteAllText(savedPath, logContent);
            
            // Notify the user
            Debug.Log($"Log saved to: {savedPath}");
            EditorUtility.DisplayDialog("Save Log", "Log saved successfully.", "OK");
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Error", $"Failed to save log: {ex.Message}", "OK");
            Debug.LogError($"Error saving log: {ex}");
        }
    }
    #endregion

    #region Custom Server Log Size    
    // Update log size for custom server mode
    private async void UpdateLogSizeForCustomServer()
    {
        // Check if we're in custom server mode
        string modeName = CCCPSettingsAdapter.GetServerMode().ToString();
        if (!modeName.Equals("CustomServer", StringComparison.OrdinalIgnoreCase))
        {
            return; // Not custom server mode, skip
        }
        
        double currentTime = EditorApplication.timeSinceStartup;
        if (isLoadingLogSize || (currentTime - lastLogSizeUpdateTime) < LOG_SIZE_UPDATE_INTERVAL)
        {
            return; // Already loading or updated recently
        }
        
        isLoadingLogSize = true;
        lastLogSizeUpdateTime = currentTime;
        
        try
        {
            // Use the directly initialized ServerCustomProcess instance
            if (serverCustomProcess != null)
            {
                float logSize = await serverCustomProcess.GetJournalSize();
                (float spacetimedbModuleLogsSizeMB, float spacetimedbDatabaseLogsSizeMB) = await serverCustomProcess.GetSpacetimeLogSizes();
                if (logSize >= 0)
                {
                    serverLogSizeMB = logSize;
                    if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] Log size updated: {serverLogSizeMB:F2} MB");
                    Repaint(); // Update the UI
                }
                if (spacetimedbModuleLogsSizeMB >= 0)
                {
                    spacetimeDbModuleLogSizeMB = spacetimedbModuleLogsSizeMB;
                    if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] SpacetimeDB module logs size updated: {spacetimedbModuleLogsSizeMB:F2} MB");
                }
                if (spacetimedbDatabaseLogsSizeMB >= 0)
                {
                    spacetimeDbDatabaseLogSizeMB = spacetimedbDatabaseLogsSizeMB;
                    if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] SpacetimeDB database logs size updated: {spacetimedbDatabaseLogsSizeMB:F2} MB");
                }
            }
            else
            {
                if (debugMode) UnityEngine.Debug.LogWarning("[ServerOutputWindow] ServerCustomProcess is null, cannot get log size");
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Failed to update log size: {ex.Message}");
        }
        finally
        {
            isLoadingLogSize = false;
        }
    }
    #endregion

    #region WSL Server Log Size    
    // Update log size for WSL server mode
    private async void UpdateLogSizeForWSLServer()
    {
        // Check if we're in WSL server mode
        string modeName = CCCPSettingsAdapter.GetServerMode().ToString();
        if (!modeName.Equals("WslServer", StringComparison.OrdinalIgnoreCase))
        {
            return; // Not WSL server mode, skip
        }
        
        double currentTime = EditorApplication.timeSinceStartup;
        if (isLoadingLogSize || (currentTime - lastLogSizeUpdateTime) < LOG_SIZE_UPDATE_INTERVAL)
        {
            return; // Already loading or updated recently
        }
        
        isLoadingLogSize = true;
        lastLogSizeUpdateTime = currentTime;
        
        try
        {
            // Use the wslProcess instance from ServerManager
            if (wslProcess != null)
            {
                if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] Calling WSL log size methods...");
                
                float logSize = await wslProcess.GetWSLJournalSize();
                (float spacetimedbModuleLogsSizeMB, float spacetimedbDatabaseLogsSizeMB) = await wslProcess.GetWSLSpacetimeLogSizes();
                
                if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] WSL log size results: Journal={logSize:F2}MB, Module={spacetimedbModuleLogsSizeMB:F2}MB, Database={spacetimedbDatabaseLogsSizeMB:F2}MB");
                
                if (logSize >= 0)
                {
                    serverLogSizeMB = logSize;
                    if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] WSL log size updated: {serverLogSizeMB:F2} MB");
                    Repaint(); // Update the UI
                }
                if (spacetimedbModuleLogsSizeMB >= 0)
                {
                    spacetimeDbModuleLogSizeMB = spacetimedbModuleLogsSizeMB;
                    if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] WSL SpacetimeDB module logs size updated: {spacetimedbModuleLogsSizeMB:F2} MB");
                }
                if (spacetimedbDatabaseLogsSizeMB >= 0)
                {
                    spacetimeDbDatabaseLogSizeMB = spacetimedbDatabaseLogsSizeMB;
                    if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] WSL SpacetimeDB database logs size updated: {spacetimedbDatabaseLogsSizeMB:F2} MB");
                }
            }
            else
            {
                if (debugMode) UnityEngine.Debug.LogWarning("[ServerOutputWindow] wslProcess is null, cannot get WSL log size");
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] Failed to update WSL log size: {ex.Message}");
        }
        finally
        {
            isLoadingLogSize = false;
        }
    }
    #endregion
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games