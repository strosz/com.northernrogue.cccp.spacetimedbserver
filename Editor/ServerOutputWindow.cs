using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text.RegularExpressions;

// Handles and displays the logs from both the SpacetimeDB module and the database ///

namespace NorthernRogue.CCCP.Editor {

public class ServerOutputWindow : EditorWindow
{
    public static bool debugMode = false; // Controlled by ServerWindow    // Add EditorPrefs keys
    private const string PrefsKeyPrefix = "CCCP_";
    private const string PrefsKeyAutoScroll = PrefsKeyPrefix + "AutoScroll";
    private const string PrefsKeyEchoToConsole = PrefsKeyPrefix + "EchoToConsole";
    private const string PrefsKeyShowLocalTime = PrefsKeyPrefix + "ShowLocalTime";

    // Logs
    private string outputLogFull = ""; // Module logs
    private string databaseLogFull = ""; // Database logs
    private Vector2 scrollPosition;
    private int selectedTab = 0;
    private string[] tabs = { "Module All", "Module Errors", "Database All", "Database Errors" };
    private bool autoScroll = true; // Start with auto-scroll enabled
    private bool isWindowEnabled = false; // Guard flag
    private int lastKnownLogHash; // For detecting changes
    private int lastKnownDbLogHash; // For detecting database log changes
    private float lastUpdateTime; // For throttling updates
    private const float UPDATE_INTERVAL = 1f; // Log update interval
    public static string logHashModule; // For echo to console
    public static string logHashDatabase; // For echo to console

    // Echo Logs to Console
    public static bool echoToConsoleModule = false; // Whether to echo module logs to Unity Console
    private static HashSet<string> loggedToConsoleModule = new HashSet<string>(); // Track logs already sent to console
    private bool showLocalTime = false; // Toggle for showing timestamps in local time zone    // Session state keys
    private const string SessionKeyCombinedLog = "ServerWindow_SilentCombinedLog";
    private const string SessionKeyCachedModuleLog = "ServerWindow_CachedModuleLog";
    private const string SessionKeyDatabaseLog = "ServerWindow_DatabaseLog";
    
    // Performance optimizations
    private bool needsRepaint = false; // Flag to track if content changed and needs repainting
    private Dictionary<int, string> formattedLogCache = new Dictionary<int, string>(); // Cache formatted logs
    private string currentFormattedLog = ""; // Currently displayed formatted log
    private int currentLogHash = 0; // Hash of currently displayed log
    private Rect lastScrollViewRect; // For virtualization calculations
    private Vector2 contentSize = Vector2.zero; // Content size for virtualization
    private List<string> visibleLines = new List<string>(); // Only lines currently visible
    private double lastRepaintTime = 0; // For limiting repaints
    private const double MIN_REPAINT_INTERVAL = 0.1; // Minimum time between repaints in seconds
    private const int MAX_TEXT_LENGTH = 100000; // Maximum text length to show

    // RefreshOpenWindow rate limiting
    private static double lastRefreshTime = 0;
    private const double REFRESH_INTERVAL = 1.0; // How often it can echo to console, won't affect rate of logs in window
    
    // SSH log refresh optimization
    private const double SSH_REFRESH_INTERVAL = 1.0;
    
    // Style-related fields
    private GUIStyle logStyle;
    private GUIStyle containerStyle;
    private GUIStyle toolbarButtonStyle;
    private Font consolasFont;
    private bool stylesInitialized = false;
    private Color cmdBackgroundColor = new Color(0.1f, 0.1f, 0.1f);
    private Color cmdTextColor = new Color(0.8f, 0.8f, 0.8f);
    private Texture2D backgroundTexture;
    
    // Track data changes
    private bool scrollToBottom = false;
    private string displayedText = string.Empty;

    // Track instances
    private static List<ServerOutputWindow> openWindows = new List<ServerOutputWindow>();
    
    // Track compilation state to force refresh after compilation
    private static bool wasCompiling = false;

    [MenuItem("SpacetimeDB/Server Logs (Silent)")]
    public static void ShowWindow()
    {
        // Trigger SessionState refresh before opening window
        TriggerSessionStateRefreshIfWindowExists();
        
        ServerOutputWindow window = GetWindow<ServerOutputWindow>("Server Logs (Silent)");
        window.minSize = new Vector2(400, 300);        
        window.Focus(); 
        window.ReloadLogs();
    }    

    public static void ShowWindow(int tab)
    {
        // Trigger SessionState refresh before opening window
        TriggerSessionStateRefreshIfWindowExists();
        
        ServerOutputWindow window = GetWindow<ServerOutputWindow>("Server Logs (Silent)");
        window.minSize = new Vector2(400, 300);
        window.selectedTab = Mathf.Clamp(tab, 0, 3); // Ensure tab index is valid
        window.Focus();
        window.ReloadLogs();
        
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
        // Add this window to the list of open windows
        if (!openWindows.Contains(this))
        {
            openWindows.Add(this);
        }
        
        // Subscribe to update
        EditorApplication.update += CheckForLogUpdates;
          // Load settings from EditorPrefs
        autoScroll = EditorPrefs.GetBool(PrefsKeyAutoScroll, true);
        echoToConsoleModule = EditorPrefs.GetBool(PrefsKeyEchoToConsole, true);
        showLocalTime = EditorPrefs.GetBool(PrefsKeyShowLocalTime, false);
        
        // Debug timezone info on startup if debug mode is on
        if (debugMode)
        {
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
            Debug.Log($"[ServerOutputWindow] OnEnable - Local timezone offset: {offset.Hours} hours, {offset.Minutes} minutes");        }
          // Load the log data
        ReloadLogs();
        
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
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= PlayModeStateChanged;
        EditorApplication.update -= CheckForLogUpdates;
        isWindowEnabled = false;
        openWindows.Remove(this);
        
        // Clean up textures
        if (backgroundTexture != null)
        {
            DestroyImmediate(backgroundTexture);
            backgroundTexture = null;
        }
          // Clear caches to free memory
        formattedLogCache.Clear();        visibleLines.Clear();
    }
    
    // Reload logs when the window gets focus
    private void OnFocus()
    {
        // Simply reload logs when window gets focus - avoid aggressive SSH restart attempts
        // Try to trigger the same mechanism that ServerReducerWindow uses
        // Load settings from EditorPrefs like ServerReducerWindow does        LoadServerSettings();
        
        // Trigger SessionState refresh if ServerWindow exists
        TriggerSessionStateRefreshIfWindowExists();
        
        ReloadLogs();
        ForceRefreshLogs();
    }
    
    // Load server settings from EditorPrefs to potentially trigger SSH log refresh
    private void LoadServerSettings()
    {
        // This mimics what ServerReducerWindow.LoadSettings() does
        // Maybe accessing these EditorPrefs triggers some internal mechanism
        string serverURL = EditorPrefs.GetString(PrefsKeyPrefix + "ServerURL", "");
        string moduleName = EditorPrefs.GetString(PrefsKeyPrefix + "ModuleName", "");
        string authToken = EditorPrefs.GetString(PrefsKeyPrefix + "AuthToken", "");
        string serverMode = EditorPrefs.GetString(PrefsKeyPrefix + "ServerMode", "");    }
    
    // Static method to trigger SessionState refresh if ServerWindow already exists
    public static void TriggerSessionStateRefreshIfWindowExists()
    {
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
    
    // Simple method to trigger refresh by adding minimal content to SessionState
    private void TriggerSessionStateUpdate()
    {
        try
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] TriggerSessionStateUpdate: Adding minimal trigger to SessionState");
            
            // Get current SessionState content
            string currentLog = SessionState.GetString(SessionKeyCombinedLog, "");
            string cachedLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
            
            // If both are empty, add a minimal trigger
            if (string.IsNullOrEmpty(currentLog) && string.IsNullOrEmpty(cachedLog))
            {
                // Add a minimal marker that will be replaced by real content
                string triggerContent = "[LOG REFRESH TRIGGER]\n";
                SessionState.SetString(SessionKeyCombinedLog, triggerContent);
                
                if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] TriggerSessionStateUpdate: Added trigger content to SessionState");
                
                // Force immediate refresh
                EditorApplication.delayCall += () => {
                    // Try to get the real content again
                    TriggerSessionStateRefreshIfWindowExists();
                    
                    EditorApplication.delayCall += () => {
                        ReloadLogs();
                        Repaint();
                        
                        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] TriggerSessionStateUpdate: Refresh cycle completed");
                    };
                };
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] TriggerSessionStateUpdate failed: {ex.Message}");
        }
    }
    
    // Called by ServerWindow when new log data arrives
    public static void RefreshOpenWindow()
    {
        double currentTime = EditorApplication.timeSinceStartup;
        
        // Load server mode
        string modeName = EditorPrefs.GetString(PrefsKeyPrefix + "ServerMode", "WslServer");
        // Check if this might be an SSH log update (faster refresh needed)
        bool isSSHLogUpdate = modeName.Equals("CustomServer", StringComparison.OrdinalIgnoreCase);
        double refreshInterval = isSSHLogUpdate ? SSH_REFRESH_INTERVAL : REFRESH_INTERVAL;
        
        if (currentTime - lastRefreshTime < refreshInterval)
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
          // If echoToConsoleModule is enabled, check for errors/warnings to send to Unity Console
        if (echoToConsoleModule)
        {
            string currentLog = SessionState.GetString(SessionKeyCombinedLog, "");
            string cachedLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
            
            // Use the longer log to ensure we don't miss any content
            string logToEcho = cachedLog.Length > currentLog.Length ? cachedLog : currentLog;
            
            EchoLogsToConsole(logToEcho, true);
        }
        
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
        
        // Step 1: Try to force SessionState refresh from the ServerWindow
        TriggerSessionStateRefreshIfWindowExists();
        
        // Step 2: Simulate new log arrival to trigger the same mechanism that works during normal operation
        SimulateLogArrival();
        
        // Step 3: Try the simple SessionState trigger method
        TriggerSessionStateUpdate();
        
        // Step 4: Multiple attempts with delays to ensure SessionState is properly refreshed
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
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] ForceRefreshAfterCompilation: ServerWindow refresh failed: {ex.Message}");
                }
                
                // Check if we have log content after refresh attempt
                string currentLog = SessionState.GetString(SessionKeyCombinedLog, "");
                string cachedLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
                
                if (string.IsNullOrEmpty(currentLog) && string.IsNullOrEmpty(cachedLog))
                {
                    if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] ForceRefreshAfterCompilation: Logs still empty, triggering fallback refresh method");
                    TriggerFallbackLogRefresh();
                }
                
                // Always reload and repaint after each attempt
                LoadServerSettings();
                ReloadLogs();
                needsRepaint = true;
                if (autoScroll) scrollToBottom = true;
                Repaint();
            };
        }
    }
    
    // Fallback method to trigger log display refresh by simulating log activity
    private void TriggerFallbackLogRefresh()
    {
        try
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] TriggerFallbackLogRefresh: Implementing fallback trigger method");
            
            // Get current SessionState logs
            string currentLog = SessionState.GetString(SessionKeyCombinedLog, "");
            string cachedLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
            string databaseLog = SessionState.GetString(SessionKeyDatabaseLog, "");
            
            // If logs are still empty, try to force a minimal update to trigger the display system
            if (string.IsNullOrEmpty(currentLog) && string.IsNullOrEmpty(cachedLog))
            {
                // Add a temporary trigger line and immediately remove it
                string triggerLine = "\n"; // Just a newline to trigger the system
                
                // Temporarily add the trigger to SessionState
                SessionState.SetString(SessionKeyCombinedLog, triggerLine);
                
                // Use delayCall to remove it immediately after
                EditorApplication.delayCall += () => {
                    SessionState.SetString(SessionKeyCombinedLog, "");
                    
                    // Force one more refresh cycle
                    EditorApplication.delayCall += () => {
                        TriggerSessionStateRefreshIfWindowExists();
                        ReloadLogs();
                        Repaint();
                        
                        if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] TriggerFallbackLogRefresh: Fallback trigger sequence completed");
                    };
                };
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] TriggerFallbackLogRefresh failed: {ex.Message}");
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
                window.LoadServerSettings();
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
        string newOutputLog = SessionState.GetString(SessionKeyCombinedLog, "");
        string cachedModuleLog = SessionState.GetString(SessionKeyCachedModuleLog, "");
        
        // Use the longer log content (current vs cached) to ensure we get the most complete data
        // This helps when server state gets confused after compilation
        if (cachedModuleLog.Length > newOutputLog.Length)
        {
            newOutputLog = cachedModuleLog;
        }
        
        // If both are empty, show default message
        if (string.IsNullOrEmpty(newOutputLog))
        {
            newOutputLog = "(No Module Log Found. Start your server to view logs.)";
        }
        
        string newDatabaseLog = SessionState.GetString(SessionKeyDatabaseLog, "(No Database Log Found. Start your server to view logs.)");
        
        // Only update and invalidate cache if log content changed
        if (newOutputLog != outputLogFull || newDatabaseLog != databaseLogFull)
        {
            outputLogFull = newOutputLog;
            databaseLogFull = newDatabaseLog;
            
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
            
            outputLogFull = "";
            databaseLogFull = "";
            SessionState.SetString(SessionKeyCombinedLog, "");
            SessionState.SetString(SessionKeyDatabaseLog, "");
            ServerOutputWindow.loggedToConsoleModule.Clear();
            formattedLogCache.Clear();
            scrollPosition = Vector2.zero;
            autoScroll = true;
            EditorPrefs.SetBool(PrefsKeyAutoScroll, autoScroll);
            needsRepaint = true;
        }

        // Add Save Current Log button
        if (GUILayout.Button("Save Current Log", toolbarButtonStyle, GUILayout.Width(110)))
        {
            SaveCurrentLogToFile();
        }
        
        // Auto Scroll toggle
        bool newAutoScroll = GUILayout.Toggle(autoScroll, "Auto Scroll", GUILayout.Width(100));
        if (newAutoScroll != autoScroll)
        {
            autoScroll = newAutoScroll;
            EditorPrefs.SetBool(PrefsKeyAutoScroll, autoScroll);
            
            if (autoScroll)
            {
                scrollToBottom = true;
                needsRepaint = true;
            }
        }
          // Echo to Console toggle
        bool newEchoToConsole = GUILayout.Toggle(echoToConsoleModule, "Echo to Console", GUILayout.Width(120));
        if (newEchoToConsole != echoToConsoleModule)
        {
            echoToConsoleModule = newEchoToConsole;
            EditorPrefs.SetBool(PrefsKeyEchoToConsole, echoToConsoleModule);
            
            if (echoToConsoleModule)
            {
                loggedToConsoleModule.Clear();
            }
        }
          // Local Time toggle
        bool newShowLocalTime = GUILayout.Toggle(showLocalTime, "Show Local Time", GUILayout.Width(120));
        if (newShowLocalTime != showLocalTime)
        {
            showLocalTime = newShowLocalTime;
            EditorPrefs.SetBool(PrefsKeyShowLocalTime, showLocalTime);
            
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
        selectedTab = 3; // Index for "Database Errors" tab
        needsRepaint = true;
        Repaint();
    }
    #endregion

    #region Logs
    // Helper method to select and limit log content based on tab
    private string GetLogForCurrentTab()
    {
        string logToShow = "";
        
        switch (selectedTab)
        {
            case 0: // Main All
                if (string.IsNullOrEmpty(outputLogFull)) {
                    return "(Module log is empty)";
                }
                logToShow = outputLogFull;
                break;
            
            case 1: // Main Errors Only
                if (string.IsNullOrEmpty(outputLogFull)) {
                    return "(Module log is empty)";
                }
                
                try {
                    var errorLines = outputLogFull.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
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
                        return "(No errors/warnings detected in log)";
                    }
                } catch (Exception ex) {
                    return $"Error filtering logs: {ex.Message}";
                }
                break;
            
            case 2: // Database All
                if (string.IsNullOrEmpty(databaseLogFull)) {
                    return "(Database log is empty)";
                }
                logToShow = databaseLogFull;
                break;
                
            case 3: // Database Errors Only
                if (string.IsNullOrEmpty(databaseLogFull)) {
                    return "(Database log is empty)";
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
                        return "(No errors/warnings detected in database log)";
                    }
                } catch (Exception ex) {
                    return $"Error filtering database logs: {ex.Message}";
                }
                break;
        }
        
        // Limit log size for performance: more aggressive for database logs
        int maxLogLength = selectedTab == 2 ? 40000 : MAX_TEXT_LENGTH;
        
        if (logToShow.Length > maxLogLength) {
            logToShow = "[... Log Truncated for Performance ...]\n" + 
                       logToShow.Substring(logToShow.Length - maxLogLength);
        }
        
        return logToShow;
    }    // Efficiently format log content with caching
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
              // Strip ANSI Escape Codes
        string strippedLog = Regex.Replace(logContent, @"\[[0-?]*[ -/]*[@-~]", "");
          // Format timestamps (ISO -> [YYYY-MM-DD HH:MM:SS]) with optional local time
        strippedLog = Regex.Replace(strippedLog, 
            @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)(\s*)([A-Z]+:)?", 
            match => {
                if (DateTimeOffset.TryParse(match.Groups[1].Value, out DateTimeOffset dt)) {
                    if (showLocalTime) {
                        // Convert to local time using DateTimeOffset for proper timezone handling
                        DateTimeOffset localTime = dt.ToLocalTime();
                        return $"[{localTime.ToString("yyyy-MM-dd HH:mm:ss")}]{match.Groups[2].Value}{match.Groups[3].Value}";
                    } else {
                        // Keep UTC time (original behavior)
                        return $"[{dt.ToString("yyyy-MM-dd HH:mm:ss")}]{match.Groups[2].Value}{match.Groups[3].Value}";
                    }
                }
                return match.Value;
            });
          // Clean up double timestamps
        strippedLog = Regex.Replace(strippedLog, 
            @"(\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]) \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z", 
            "$1");
            
        // Clean up duplicate timestamps in the format "[date time] [date time]"
        strippedLog = Regex.Replace(strippedLog,
            @"(\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]) \[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]",
            "$1");

        // Handle journalctl output format - remove middle timestamp and service info
        strippedLog = Regex.Replace(strippedLog,
            @"(\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]) [A-Za-z]{3} \d{1,2} \d{2}:\d{2}:\d{2}\.\d+ [A-Za-z]+ spacetime\[\d+\]: ",
            "$1 ");
        
        // Add CMD-style color formatting for error/warning messages
        strippedLog = strippedLog.Replace("ERROR", "<color=#FF6666>ERROR</color>");
        strippedLog = strippedLog.Replace("error:", "<color=#FF6666>error:</color>");
        strippedLog = strippedLog.Replace("WARN", "<color=#FFCC66>WARN</color>");
        strippedLog = strippedLog.Replace("warning:", "<color=#FFCC66>warning:</color>");
        strippedLog = strippedLog.Replace("INFO", "<color=#66CCFF>INFO</color>");
        strippedLog = strippedLog.Replace("DEBUG", "<color=#66CCFF>DEBUG</color>");
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
        float checkInterval = EditorApplication.isPlaying ? 0.2f : UPDATE_INTERVAL;
        if (Time.realtimeSinceStartup - lastUpdateTime < checkInterval)
            return;

        lastUpdateTime = Time.realtimeSinceStartup;        // Check main logs (consider both current and cached module logs)
        string currentLog = SessionState.GetString(SessionKeyCombinedLog, "");
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
    private static void EchoLogsToConsole(string log, bool isModule) // Is database if false
    {
        if (string.IsNullOrEmpty(log)) return;
        
        // Split the log into lines
        string[] lines = log.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (string line in lines)
        {
            // Only process lines we haven't logged before to avoid duplicates
            string trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine)) continue;
            
            // Create a hash of the line to track what we've logged
            // Strip timestamps or other variable data to avoid re-logging the same error
            logHashModule = GetLogHash(trimmedLine);
            
            if (!string.IsNullOrEmpty(logHashModule) && !loggedToConsoleModule.Contains(logHashModule))
            {
                // Check for errors or warnings (adjust patterns as needed)
                if (trimmedLine.Contains("ERROR") || trimmedLine.Contains("error:") || 
                    trimmedLine.ToLower().Contains("exception"))
                {
                    UnityEngine.Debug.LogError($"[SpacetimeDB Module] {trimmedLine}");
                    loggedToConsoleModule.Add(logHashModule);
                }
                else if (trimmedLine.Contains("WARNING") || trimmedLine.Contains("warning:"))
                {
                    UnityEngine.Debug.LogWarning($"[SpacetimeDB Module] {trimmedLine}");
                    loggedToConsoleModule.Add(logHashModule);
                }
                
                // Limit tracked messages to prevent memory issues
                if (loggedToConsoleModule.Count > 1000)
                {
                    loggedToConsoleModule.Clear(); // Just reset if we hit the limit
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
            // logContent = outputLogFull;
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
    
    // Alternative method to force refresh by directly triggering log process refresh
    private void SimulateLogArrival()
    {
        try
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] SimulateLogArrival: Simulating new log arrival to trigger display");
            
            // First, try to force the ServerLogProcess to refresh its SessionState data
            ForceLogProcessRefresh();
            
            // Then call the same method that gets called when new logs arrive
            RefreshOpenWindow();
            
            // Also force the database log refresh
            RefreshDatabaseLogs();
            
            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] SimulateLogArrival: Simulation completed");
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] SimulateLogArrival failed: {ex.Message}");
        }
    }
    
    // Force the ServerLogProcess to refresh its SessionState data
    private void ForceLogProcessRefresh()
    {
        try
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] ForceLogProcessRefresh: Attempting to force log process to refresh SessionState");
            
            // Get the ServerWindow and access its log process
            var existingWindows = Resources.FindObjectsOfTypeAll<ServerWindow>();
            if (existingWindows != null && existingWindows.Length > 0)
            {
                var serverWindow = existingWindows[0];
                if (serverWindow != null)
                {
                    // First try the existing method
                    serverWindow.ForceRefreshLogsFromSessionState();
                    
                    // Try to access the log process directly through reflection if needed
                    var logProcessField = serverWindow.GetType().GetField("logProcess", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (logProcessField != null)
                    {
                        var logProcess = logProcessField.GetValue(serverWindow);
                        if (logProcess != null)
                        {
                            // Try to call ForceRefreshLogsFromSessionState on the log process
                            var refreshMethod = logProcess.GetType().GetMethod("ForceRefreshLogsFromSessionState");
                            if (refreshMethod != null)
                            {
                                refreshMethod.Invoke(logProcess, null);
                                if (debugMode) UnityEngine.Debug.Log("[ServerOutputWindow] ForceLogProcessRefresh: Called ForceRefreshLogsFromSessionState on log process");
                            }
                            
                            // Also try to trigger a log update by accessing the log content directly
                            var moduleLogMethod = logProcess.GetType().GetMethod("GetModuleLogContent");
                            var databaseLogMethod = logProcess.GetType().GetMethod("GetDatabaseLogContent");
                            
                            if (moduleLogMethod != null && databaseLogMethod != null)
                            {
                                string moduleLogContent = (string)moduleLogMethod.Invoke(logProcess, null);
                                string databaseLogContent = (string)databaseLogMethod.Invoke(logProcess, null);
                                
                                // Force update SessionState with the actual log content
                                if (!string.IsNullOrEmpty(moduleLogContent))
                                {
                                    SessionState.SetString(SessionKeyCombinedLog, moduleLogContent);
                                    SessionState.SetString(SessionKeyCachedModuleLog, moduleLogContent);
                                    if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] ForceLogProcessRefresh: Updated SessionState with {moduleLogContent.Length} chars of module log");
                                }
                                
                                if (!string.IsNullOrEmpty(databaseLogContent))
                                {
                                    SessionState.SetString(SessionKeyDatabaseLog, databaseLogContent);
                                    if (debugMode) UnityEngine.Debug.Log($"[ServerOutputWindow] ForceLogProcessRefresh: Updated SessionState with {databaseLogContent.Length} chars of database log");
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogWarning($"[ServerOutputWindow] ForceLogProcessRefresh failed: {ex.Message}");
        }
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
