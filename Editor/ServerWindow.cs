using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

// The main Comos Cove Control Panel that controls the server and launches all features ///

namespace NorthernRogue.CCCP.Editor {

public class ServerWindow : EditorWindow
{
    // Server Manager
    private ServerManager serverManager;
    
    // Process Handlers
    private ServerCMDProcess cmdProcessor;
    private ServerCustomProcess serverCustomProcess;
    private ServerDetectionProcess detectionProcess;
    
    // Server mode
    private ServerMode serverMode = ServerMode.WslServer;

    // Pre-requisites WSL
    private bool hasWSL = false;
    private bool hasDebian = false;
    private bool hasDebianTrixie = false;
    private bool hasCurl = false;
    private bool hasSpacetimeDBServer = false;
    private bool hasSpacetimeDBPath = false;
    private bool hasSpacetimeDBService = false;
    private bool hasSpacetimeDBLogsService = false;
    private bool hasRust = false;
    private bool hasBinaryen = false;
    private bool wslPrerequisitesChecked = false;
    private bool initializedFirstModule = false;
    private string userName = "";
    private string backupDirectory = "";
    private string serverDirectory = "";
    private string unityLang = "rust";
    private string clientDirectory = "";
    private string serverLang = "rust";
    private string moduleName = "";
    private string serverUrl = "";
    private int serverPort = 3000;
    private string authToken = "";

    // Pre-requisites Custom Server
    private string sshUserName = "";
    private string customServerUrl = "";
    private int customServerPort = 0;
    private string customServerAuthToken = "";
    private string sshPrivateKeyPath = ""; // Added SSH private key path variable
    private bool isConnected;

    // Pre-requisites Maincloud Server
    //private string maincloudUrl = "maincloud.spacetimedb.com";
    private string maincloudAuthToken = "";
   
    // Server status
    private double lastCheckTime = 0;
    private const double checkInterval = 5.0; // Master interval for status checks

    // Server Settings
    public bool debugMode = false;
    private bool hideWarnings = false;
    private bool detectServerChanges = false;
    private bool serverChangesDetected = false;
    private bool autoPublishMode = false;
    private bool publishAndGenerateMode = false;
    private bool silentMode = false;
    private bool autoCloseWsl = false;
    private bool clearModuleLogAtStart = false;
    private bool clearDatabaseLogAtStart = false;

    // Update SpacetimeDB
    private string spacetimeDBCurrentVersion = "";
    private string spacetimeDBCurrentVersionCustom = "";
    private string spacetimeDBLatestVersion = "";

    // UI
    private Vector2 scrollPosition;
    private string commandOutputLog = "";
    private bool autoscroll = true;
    private bool colorLogo = true;
    private bool publishing = false;
    private bool isUpdatingCCCP = false;
    private double cccpUpdateStartTime = 0;
    
    // Scroll tracking for autoscroll behavior
    private Vector2 lastScrollPosition;
    private bool wasAutoScrolling = false;
    private bool needsScrollToBottom = false; // Flag to control when to apply autoscroll
    private Texture2D logoTexture;
    private GUIStyle connectedStyle;
    private GUIStyle buttonStyle;
    private bool stylesInitialized = false;    // UI optimization
    private const double changeCheckInterval = 3.0; // More responsive interval when window is in focus
    private bool windowFocused = false;
    
    // Window toggle states
    private bool viewLogsWindowOpen = false;
    private bool browseDbWindowOpen = false;
    private bool runReducerWindowOpen = false;
    private Color windowToggleColor = new Color(0.6f, 1.6f, 0.6f);
    
    // Session state key for domain reload
    private const string SessionKeyWasRunningSilently = "ServerWindow_WasRunningSilently";
    private const string PrefsKeyPrefix = "CCCP_";

    // Track WSL status
    private bool isWslRunning = false;

    // Cancellation token source for status checks
    private System.Threading.CancellationTokenSource statusCheckCTS;

    public static string Documentation = "https://docs.google.com/document/d/1HpGrdNicubKD8ut9UN4AzIOwdlTh1eO4ampZuEk5fM0/edit?usp=sharing";

    [MenuItem("SpacetimeDB/Server Management Panel", priority = -9000)]
    public static void ShowWindow()
    {
        ServerWindow window = GetWindow<ServerWindow>("SpacetimeDB");
        window.minSize = new Vector2(270f, 600f);
    }    
    
    public enum ServerMode
    {
        WslServer,
        CustomServer,
        MaincloudServer,
    }

    [System.Serializable]
    public struct ModuleInfo
    {
        public string name;
        public string path;
        
        public ModuleInfo(string name, string path)
        {
            this.name = name;
            this.path = path;
        }
    }

    // Saved modules list
    private List<ModuleInfo> savedModules = new List<ModuleInfo>();
    private int selectedModuleIndex = -1;
    private string newModuleNameInput = ""; // Input field for new module name

    [System.Serializable]
    private class SerializableList<T>
    {
        public List<T> items;
        
        public SerializableList(List<T> items)
        {
            this.items = items;
        }
    }
    #region OnGUI

    private void OnGUI()
    {
        if (!stylesInitialized) InitializeStyles();

        EditorGUILayout.BeginVertical();
               
        // Load and display the logo image
        if (colorLogo)
        logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.northernrogue.cccp.spacetimedbserver/Editor/cosmos_logo_azure.png");
        else
        logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.northernrogue.cccp.spacetimedbserver/Editor/cosmos_logo.png");

        if (logoTexture != null)
        {
            float maxHeight = 70f;
            float aspectRatio = (float)logoTexture.width / logoTexture.height;
            float width = maxHeight * aspectRatio;
            float height = maxHeight;
            
            // Centered Logo
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(logoTexture, GUILayout.Width(width), GUILayout.Height(height));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(-10);

            EditorGUILayout.Space(-5);
            
            GUILayout.BeginHorizontal();
            // Subtitle
            GUIStyle subTitleStyle = new GUIStyle(EditorStyles.label);
            subTitleStyle.fontSize = 10;
            subTitleStyle.normal.textColor = new Color(0.43f, 0.43f, 0.43f);
            subTitleStyle.hover.textColor = new Color(0.43f, 0.43f, 0.43f);
            subTitleStyle.alignment = TextAnchor.MiddleCenter;

            //GUILayout.Label("Begin by checking the pre-requisites", subTitleStyle);
            if (serverMode == ServerMode.WslServer)
                EditorGUILayout.LabelField("WSL Server Mode", subTitleStyle);
            else if (serverMode == ServerMode.CustomServer)
                EditorGUILayout.LabelField("Custom Server Mode", subTitleStyle);
            else if (serverMode == ServerMode.MaincloudServer)
                EditorGUILayout.LabelField("Maincloud Server Mode", subTitleStyle);

            GUILayout.EndHorizontal();

            EditorGUILayout.Space(-15);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // Logo version and color control
            GUIStyle titleControlStyle = new GUIStyle(EditorStyles.miniLabel);
            titleControlStyle.fontSize = 10;
            titleControlStyle.normal.textColor = new Color(0.43f, 0.43f, 0.43f);
            string tooltipVersion = "Click to change logo color";
            if (GUILayout.Button(new GUIContent("version " + ServerUpdateProcess.GetCurrentPackageVersion(), tooltipVersion), titleControlStyle))
            {
                colorLogo = !colorLogo;
                EditorPrefs.SetBool(PrefsKeyPrefix + "ColorLogo", colorLogo);
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            // Fallback if image not found
            GUIStyle titleStyle = new GUIStyle(EditorStyles.whiteLargeLabel);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontSize = 15;
            GUILayout.Label("SpacetimeDB Server Management", titleStyle);
            GUILayout.Label("Control your SpacetimeDB server and run commands.\n If starting fresh check the pre-requisites first.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space();
        }
        
        DrawPrerequisitesSection();
        EditorGUILayout.Space(5);
        
        DrawSettingsSection();
        EditorGUILayout.Space(5);

        DrawServerSection();
        EditorGUILayout.Space(5);
        
        DrawCommandsSection();
        EditorGUILayout.Space(5);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Command Output:", EditorStyles.boldLabel);

        GUILayout.FlexibleSpace();

        // Autoscroll button
        EditorGUILayout.BeginVertical();
        EditorGUILayout.Space(2);
        GUIStyle autoscrollStyle = new GUIStyle(EditorStyles.miniLabel);
        autoscrollStyle.fontSize = 12;
        autoscrollStyle.normal.textColor = autoscroll ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.34f, 0.34f, 0.34f);
        autoscrollStyle.hover.textColor = autoscrollStyle.normal.textColor; // Explicitly define hover textColor        
        if (GUILayout.Button(new GUIContent("autoscroll"), autoscrollStyle, GUILayout.Width(75)))
        {
            autoscroll = !autoscroll;
            EditorPrefs.SetBool(PrefsKeyPrefix + "Autoscroll", autoscroll);
            
            // If autoscroll was just enabled, scroll to bottom immediately
            if (autoscroll)
            {
                needsScrollToBottom = true;
            }
            
            Repaint();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(-10);

        // Clear button
        EditorGUILayout.BeginVertical();
        EditorGUILayout.Space(2);
        GUIStyle clearStyle = new GUIStyle(EditorStyles.miniLabel);
        clearStyle.fontSize = 12;
        clearStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
        clearStyle.hover.textColor = clearStyle.normal.textColor; // Explicitly define hover textColor
        if (GUILayout.Button(new GUIContent("clear"), clearStyle, GUILayout.Width(50)))
        {
            commandOutputLog = "";
            Repaint();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        
        // Output log with rich text support
        GUIStyle richTextStyle = new GUIStyle(EditorStyles.textArea);
        richTextStyle.richText = true;
        // Store previous scroll position to detect user scrolling
        Vector2 previousScrollPosition = scrollPosition;        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        EditorGUILayout.TextArea(commandOutputLog.TrimEnd('\n'), richTextStyle, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        
        // Handle autoscroll behavior based on user interaction
        HandleAutoscrollBehavior(previousScrollPosition);

        // Github Update Button
        if (ServerUpdateProcess.IsGithubUpdateAvailable())
        {
            // Check if we need to reset the updating state after 10 seconds
            if (isUpdatingCCCP && EditorApplication.timeSinceStartup - cccpUpdateStartTime > 10.0)
            {
                isUpdatingCCCP = false;
            }
            
            string buttonText = isUpdatingCCCP ? "Updating CCCP Package..." : "New CCCP Update Available";
            
            // Create a custom style for the button based on the state
            GUIStyle updateButtonStyle = new GUIStyle(GUI.skin.button);
            if (isUpdatingCCCP)
            {
                updateButtonStyle.normal.textColor = Color.green;
                updateButtonStyle.hover.textColor = Color.green;
                updateButtonStyle.active.textColor = Color.green;
                updateButtonStyle.focused.textColor = Color.green;
            }
            else
            {
                updateButtonStyle.normal.textColor = Color.white;
                updateButtonStyle.hover.textColor = Color.white;
                updateButtonStyle.active.textColor = Color.white;
                updateButtonStyle.focused.textColor = Color.white;
            }
            
            if (GUILayout.Button(buttonText, updateButtonStyle))
            {
                isUpdatingCCCP = true;
                cccpUpdateStartTime = EditorApplication.timeSinceStartup;
                ServerUpdateProcess.UpdateGithubPackage();
            }
        }
        EditorGUILayout.EndVertical();
    }
    
    private void InitializeStyles()
    {
        // For all connection status labels
        connectedStyle = new GUIStyle(EditorStyles.label);
        connectedStyle.fontSize = 11;
        connectedStyle.normal.textColor = new Color(0.3f, 0.8f, 0.3f);
        connectedStyle.fontStyle = FontStyle.Bold;
        
        // Create custom button style with white text
        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = Color.white;
        buttonStyle.active.textColor = Color.white;
        buttonStyle.focused.textColor = Color.white;
        buttonStyle.fontSize = 12;
        buttonStyle.fontStyle = FontStyle.Normal;
        buttonStyle.alignment = TextAnchor.MiddleCenter;
        
        stylesInitialized = true;
    }
    #endregion

    #region OnEnable

    private void OnEnable()
    {
        // Initialize ServerManager with logging callback
        serverManager = new ServerManager(LogMessage, Repaint);

        // Load server mode from EditorPrefs
        LoadServerModeFromPrefs();            
        // Sync local fields from ServerManager's values (for UI display only)
        SyncFieldsFromServerManager();
        // Load saved modules list
        LoadModulesList();

        // Load UI preferences
        autoscroll = EditorPrefs.GetBool(PrefsKeyPrefix + "Autoscroll", true);
        colorLogo = EditorPrefs.GetBool(PrefsKeyPrefix + "ColorLogo", true);

        // Register for focus events
        EditorApplication.focusChanged += OnFocusChanged;

        // Start checking server status
        EditorApplication.update += EditorUpdateHandler;

        // Check if we were previously running silently and restore state if needed
        if (!serverManager.IsServerStarted || !serverManager.SilentMode)
        {
            // Clear the flag if not running silently on enable
            SessionState.SetBool(SessionKeyWasRunningSilently, false);
        }

        // Ensure the flag is correctly set based on current state when enabled
        SessionState.SetBool(SessionKeyWasRunningSilently, serverManager.IsServerStarted && serverManager.SilentMode);

        EditorApplication.playModeStateChanged += HandlePlayModeStateChange;

        // Check if we need to restart the database log process
        bool databaseLogWasRunning = SessionState.GetBool("ServerWindow_DatabaseLogRunning", false);
        if (serverManager.IsServerStarted && serverManager.SilentMode && databaseLogWasRunning)
        {
            if (serverManager.DebugMode) LogMessage("Restarting database logs after editor reload...", 0);
            AttemptDatabaseLogRestartAfterReload();
        }

        // Add this section near the end of OnEnable
        // Perform an immediate WSL status check if in WSL mode
        if (serverManager.CurrentServerMode == ServerManager.ServerMode.WslServer)
        {
            EditorApplication.delayCall += async () =>
            {
                try
                {
                    await serverManager.CheckWslStatus();
                    isWslRunning = serverManager.IsWslRunning;
                    Repaint();
                }
                catch (Exception ex)
                {
                    if (serverManager.DebugMode) UnityEngine.Debug.LogError($"Error in WSL status check: {ex.Message}");
                }
            };
        }
    }
    
    private void SyncFieldsFromServerManager()
    {
        // Copy values from ServerManager to local fields for UI display
        hasWSL = serverManager.HasWSL;
        hasDebian = serverManager.HasDebian;
        hasDebianTrixie = serverManager.HasDebianTrixie;
        hasCurl = serverManager.HasCurl;
        hasSpacetimeDBServer = serverManager.HasSpacetimeDBServer;
        hasSpacetimeDBPath = serverManager.HasSpacetimeDBPath;
        hasSpacetimeDBService = serverManager.HasSpacetimeDBService;
        hasSpacetimeDBLogsService = serverManager.HasSpacetimeDBLogsService;
        hasRust = serverManager.HasRust;
        hasBinaryen = serverManager.HasBinaryen;
        
        initializedFirstModule = serverManager.InitializedFirstModule;
        
        wslPrerequisitesChecked = serverManager.WslPrerequisitesChecked;
        userName = serverManager.UserName;
        serverUrl = serverManager.ServerUrl;
        serverPort = serverManager.ServerPort;
        authToken = serverManager.AuthToken;

        backupDirectory = serverManager.BackupDirectory;
        serverDirectory = serverManager.ServerDirectory;
        serverLang = serverManager.ServerLang;
        clientDirectory = serverManager.ClientDirectory;
        unityLang = serverManager.UnityLang;
        moduleName = serverManager.ModuleName;
        selectedModuleIndex = serverManager.SelectedModuleIndex;

        sshUserName = serverManager.SSHUserName;
        sshPrivateKeyPath = serverManager.SSHPrivateKeyPath;
        customServerUrl = serverManager.CustomServerUrl;
        customServerPort = serverManager.CustomServerPort;
        customServerAuthToken = serverManager.CustomServerAuthToken;

        hideWarnings = serverManager.HideWarnings;
        detectServerChanges = serverManager.DetectServerChanges;
        autoPublishMode = serverManager.AutoPublishMode;
        publishAndGenerateMode = serverManager.PublishAndGenerateMode;
        silentMode = serverManager.SilentMode;
        debugMode = serverManager.DebugMode;
        clearModuleLogAtStart = serverManager.ClearModuleLogAtStart;
        clearDatabaseLogAtStart = serverManager.ClearDatabaseLogAtStart;
        autoCloseWsl = serverManager.AutoCloseWsl;
        
        serverMode = (ServerMode)serverManager.CurrentServerMode;
        serverChangesDetected = serverManager.ServerChangesDetected;

        spacetimeDBCurrentVersion = serverManager.spacetimeDBCurrentVersion;
        spacetimeDBCurrentVersionCustom = serverManager.spacetimeDBCurrentVersionCustom;
        spacetimeDBLatestVersion = serverManager.spacetimeDBLatestVersion;

        isWslRunning = serverManager.IsWslRunning;
        publishing = serverManager.Publishing;

        maincloudAuthToken = serverManager.MaincloudAuthToken;
    }

    private async void EditorUpdateHandler()
    {
        if (serverManager == null) return;
        
        // Skip processing if Unity Editor doesn't have focus to prevent accumulating delayed calls
        if (!windowFocused)
        {
            return;
        }
        
        double currentTime = EditorApplication.timeSinceStartup;
        
        // Throttle how often we check things to not overload the main thread
        if (currentTime - lastCheckTime > checkInterval)
        {
            lastCheckTime = currentTime;
                        
            // Cancel any previous check that might still be running
            if (statusCheckCTS != null)
            {
                statusCheckCTS.Cancel();
                statusCheckCTS.Dispose();
            }
            
            statusCheckCTS = new System.Threading.CancellationTokenSource();
            
            await CheckStatusAsync(statusCheckCTS.Token);
        }
        
        // For Custom Server mode, ensure UI is refreshed periodically to update connection status
        if (serverManager.CurrentServerMode == ServerManager.ServerMode.CustomServer && windowFocused)
        {
            if (currentTime - lastCheckTime > changeCheckInterval)
            {
                Repaint();
            }
        }
    }
    
    private async Task CheckStatusAsync(System.Threading.CancellationToken token)
    {
        try
        {
            await serverManager.CheckAllStatus();
            //UnityEngine.Debug.Log($"[ServerWindow CheckStatusAsync] Status check completed at {DateTime.Now}"); // Keep for debugging
            // Only update UI if the operation wasn't cancelled
            if (!token.IsCancellationRequested)
            {
                // Update local state for UI display
                serverChangesDetected = serverManager.ServerChangesDetected;
                isWslRunning = serverManager.IsWslRunning;
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && debugMode)
            {
                UnityEngine.Debug.LogError($"Error in status check: {ex.Message}");
            }
        }
    }
    
    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChange; // Remove handler

        // Save state before disabling (might be domain reload)
        // Ensure SessionState reflects the state *just before* disable/reload
        SessionState.SetBool(SessionKeyWasRunningSilently, serverManager.IsServerStarted && serverManager.SilentMode);

        // Cleanup event handlers
        EditorApplication.update -= EditorUpdateHandler;
        EditorApplication.focusChanged -= OnFocusChanged;
        
        // Cleanup cancellation token source
        if (statusCheckCTS != null)
        {
            statusCheckCTS.Cancel();
            statusCheckCTS.Dispose();
            statusCheckCTS = null;
        }
    }
    
    private void OnFocusChanged(bool focused)
    {
        windowFocused = focused;
        
        // Update ServerManager with focus state
        if (serverManager != null)
        {
            serverManager.SetEditorFocus(focused);
        }
        
        // When regaining focus, reset the timing to prevent accumulated backlog
        if (focused)
        {
            lastCheckTime = EditorApplication.timeSinceStartup;
            if (debugMode)
            {
                UnityEngine.Debug.Log("[ServerWindow] Editor focus regained - resetting timing to prevent log processing backlog");
            }
        }
        
        SyncFieldsFromServerManager();
    }
    #endregion
    
    #region Pre-RequisitesUI

    private void DrawPrerequisitesSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox); // Start of Pre-Requisites section

        bool showPrerequisites = EditorGUILayout.Foldout(EditorPrefs.GetBool(PrefsKeyPrefix + "ShowPrerequisites", false), "Pre-Requisites", true);
        EditorPrefs.SetBool(PrefsKeyPrefix + "ShowPrerequisites", showPrerequisites);

        if (showPrerequisites)
        {
            EditorGUILayout.Space(0);

            EditorGUILayout.LabelField("Server Mode", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            // Draw a visible 3px high dark line across the window
            Rect lineRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1.7f), GUILayout.ExpandWidth(true));
            Color lineColor = EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.6f, 0.6f, 0.6f, 1f);
            EditorGUI.DrawRect(lineRect, lineColor);

            // Style Active serverMode
            GUIStyle activeToolbarButton = new GUIStyle(EditorStyles.toolbarButton);
            activeToolbarButton.normal.textColor = Color.green;

            // Style Inactive serverMode
            GUIStyle inactiveToolbarButton = new GUIStyle(EditorStyles.toolbarButton);
            inactiveToolbarButton.normal.textColor = Color.gray;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            string wslModeTooltip = "Run a local server with SpacetimeDB on Debian WSL";
            if (GUILayout.Button(new GUIContent("WSL Local", wslModeTooltip), serverMode == ServerMode.WslServer ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                if (serverManager.serverStarted && serverMode == ServerMode.MaincloudServer)
                {
                    bool modeChange = EditorUtility.DisplayDialog("Confirm Mode Change", "Do you want to stop your Maincloud log process and change the server mode to WSL Local server?","OK","Cancel");
                    if (modeChange)
                    {
                        serverManager.StopMaincloudLog();
                        if (debugMode) LogMessage("Stopped Maincloud log process before mode switch", 0);

                        serverMode = ServerMode.WslServer;
                        UpdateServerModeState();
                    }
                } else // If server is not started or in Custom mode, just switch to WSL
                {
                    serverMode = ServerMode.WslServer;
                    UpdateServerModeState();
                }
            }
            string customModeTooltip = "Connect to your custom remote server and run spacetime commands";
            if (GUILayout.Button(new GUIContent("Custom Remote", customModeTooltip), serverMode == ServerMode.CustomServer ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                if (serverManager.serverStarted && serverMode == ServerMode.WslServer)
                {
                    ClearModuleLogFile();
                    ClearDatabaseLog();
                    serverMode = ServerMode.CustomServer;
                    UpdateServerModeState();
                }
                else if (serverManager.serverStarted && serverMode == ServerMode.MaincloudServer)
                {
                    ClearModuleLogFile();
                    ClearDatabaseLog();
                    serverMode = ServerMode.CustomServer;
                    UpdateServerModeState();
                }
                else // If server is not started just switch to Custom
                {
                    serverMode = ServerMode.CustomServer;
                    UpdateServerModeState();
                }
            }
            string maincloudModeTooltip = "Connect to the official SpacetimeDB cloud server and run spacetime commands";
            if (GUILayout.Button(new GUIContent("Maincloud", maincloudModeTooltip), serverMode == ServerMode.MaincloudServer ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                if (serverManager.serverStarted && serverMode == ServerMode.WslServer)
                {
                    bool modeChange = EditorUtility.DisplayDialog("Confirm Mode Change", "Do you want to stop your WSL Local server and change the server mode to Maincloud server?","OK","Cancel");
                    if (modeChange)
                    {
                        serverManager.StopServer();
                        serverMode = ServerMode.MaincloudServer;
                        UpdateServerModeState();
                    }
                } 
                if (serverManager.serverStarted && serverMode == ServerMode.CustomServer)
                {
                    ClearModuleLogFile();
                    ClearDatabaseLog();
                    serverMode = ServerMode.MaincloudServer;
                    UpdateServerModeState();
                }
                else // If server is not started just switch to Maincloud
                {
                    serverMode = ServerMode.MaincloudServer;
                    UpdateServerModeState();
                }
            }
            EditorGUILayout.EndHorizontal();
            
            // Create a custom window style without padding and top aligned
            GUIStyle customWindowStyle = new GUIStyle(GUI.skin.window);
            customWindowStyle.padding = new RectOffset(5, 5, 5, 5); // Add bottom padding
            customWindowStyle.contentOffset = Vector2.zero;
            customWindowStyle.alignment = TextAnchor.UpperLeft;
            customWindowStyle.stretchHeight = false; // Prevent automatic stretching

            GUILayout.BeginVertical(customWindowStyle); // Window Texture of Pre-Requisites
            #endregion

            #region Shared Settings
            // Shared settings
            GUILayout.Label("Shared Settings", EditorStyles.centeredGreyMiniLabel);

            // Unity Autogenerated files Language dropdown
            EditorGUILayout.BeginHorizontal();
            string unityLangTooltip = 
            "C-Sharp: The default programming language for auto-generated Unity client code. \n\n"+
            "Rust: Programming language for auto-generated Unity client code. \n\n"+
            "Typescript: Programming language for auto-generated Unity client code. \n\n"+
            "Recommended: C-Sharp which is natively supported by Unity.";
            EditorGUILayout.LabelField(new GUIContent("Client Language:", unityLangTooltip), GUILayout.Width(110));
            string[] unityLangOptions = new string[] { "Rust", "C-Sharp", "Typescript"};
            string[] unityLangValues = new string[] { "rust", "csharp", "typescript" };
            int unityLangSelectedIndex = Array.IndexOf(unityLangValues, unityLang);
            if (unityLangSelectedIndex < 0) unityLangSelectedIndex = 1; // Default to Rust if not found
            int newunityLangSelectedIndex = EditorGUILayout.Popup(unityLangSelectedIndex, unityLangOptions, GUILayout.Width(150));
            if (newunityLangSelectedIndex != unityLangSelectedIndex)
            {
                unityLang = unityLangValues[newunityLangSelectedIndex];
                serverManager.SetUnityLang(unityLang);
                LogMessage($"Module language set to: {unityLangOptions[newunityLangSelectedIndex]}", 0);
            }
            GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(unityLang)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Unity Autogenerated files Directory setting
            EditorGUILayout.BeginHorizontal();
            string clientDirectoryTooltip = 
            "Directory where SpacetimeDB Unity client scripts will be automatically generated.\n\n"+
            "Note: This should be placed in the Assets folder of your Unity project.";
            EditorGUILayout.LabelField(new GUIContent("Client Path:", clientDirectoryTooltip), GUILayout.Width(110));
            string clientDirButtonTooltip = "Current set path: " + (string.IsNullOrEmpty(clientDirectory) ? "Not Set" : clientDirectory);
            if (GUILayout.Button(new GUIContent("Set Client Path", clientDirButtonTooltip), GUILayout.Width(150), GUILayout.Height(20)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Client Path", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    clientDirectory = path;
                    serverManager.SetClientDirectory(clientDirectory);
                    LogMessage($"Client path set to: {clientDirectory}", 1);
                }
            }
            GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(clientDirectory)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Module Language dropdown
            EditorGUILayout.BeginHorizontal();
            string serverLangTooltip = 
            "Rust: The default programming language for SpacetimeDB server modules. \n\n"+
            "C-Sharp: The C# programming language for SpacetimeDB server modules. \n\n"+
            "Recommended: Rust which is 2x faster than C#.";
            EditorGUILayout.LabelField(new GUIContent("Module Language:", serverLangTooltip), GUILayout.Width(110));
            string[] serverLangOptions = new string[] { "Rust", "C-Sharp"};
            string[] serverLangValues = new string[] { "rust", "csharp" };
            int serverLangSelectedIndex = Array.IndexOf(serverLangValues, serverLang);
            if (serverLangSelectedIndex < 0) serverLangSelectedIndex = 0; // Default to Rust if not found
            int newServerLangSelectedIndex = EditorGUILayout.Popup(serverLangSelectedIndex, serverLangOptions, GUILayout.Width(150));
            if (newServerLangSelectedIndex != serverLangSelectedIndex)
            {
                serverLang = serverLangValues[newServerLangSelectedIndex];
                serverManager.SetServerLang(serverLang);
                LogMessage($"Server language set to: {serverLangOptions[newServerLangSelectedIndex]}", 0);
            }
            GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(serverLang)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();            
            
            // Add New Module Entry
            EditorGUILayout.BeginHorizontal();
            string moduleSettingsTooltip = 
            "Set a new module name and path for your SpacetimeDB module.\n\n"+
            "Name: The name of your existing SpacetimeDB module you used when you created the module,\n"+
            "OR the name you want your SpacetimeDB module to have when initializing a new one.\n\n"+
            "Path: Directory of where Cargo.toml is located or to be created at.\n"+
            "Note: Create a new empty folder if the module has not been created yet.";            EditorGUILayout.LabelField(new GUIContent("Module New Entry:", moduleSettingsTooltip), GUILayout.Width(110));
            newModuleNameInput = EditorGUILayout.TextField(newModuleNameInput, GUILayout.Width(100));
            string serverDirButtonTooltip = "Current set path: " + (string.IsNullOrEmpty(serverDirectory) ? "Not Set" : serverDirectory);
            if (GUILayout.Button(new GUIContent("Add", serverDirButtonTooltip), GUILayout.Width(47), GUILayout.Height(20)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Module Path", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    serverDirectory = path;
                    serverManager.SetServerDirectory(serverDirectory);

                    // Update the detection process with the new directory
                    if (detectionProcess != null)
                    {
                        detectionProcess.Configure(serverDirectory, detectServerChanges);
                    }
                      // Save module if both name and path are set
                    if (!string.IsNullOrEmpty(newModuleNameInput) && !string.IsNullOrEmpty(serverDirectory))
                    {
                        string moduleNameToAdd = newModuleNameInput; // Store the name before clearing
                        int moduleIndex = AddModuleToSavedList(moduleNameToAdd, serverDirectory);
                        serverManager.SetModuleName(moduleNameToAdd);
                        
                        // Automatically select the newly added module
                        if (moduleIndex >= 0)
                        {
                            SelectSavedModule(moduleIndex);
                        }
                        
                        // Clear the input field after successful addition
                        newModuleNameInput = "";
                        
                        LogMessage($"Module {moduleNameToAdd} successfully added! Path: {serverDirectory}", 1);
                    }
                }
            }
            GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(serverDirectory) && !string.IsNullOrEmpty(moduleName)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Select Module
            EditorGUILayout.BeginHorizontal();
            string savedModulesTooltip = 
            "Modules Selection: Select your saved SpacetimeDB module for editing, publishing and change detection.";
            EditorGUILayout.LabelField(new GUIContent("Module Selection:", savedModulesTooltip), GUILayout.Width(110));
            if (savedModules.Count > 0)
            {
                // Create display options for the dropdown
                string[] moduleOptions = new string[savedModules.Count + 1];
                moduleOptions[0] = "Select a module...";
                
                for (int i = 0; i < savedModules.Count; i++)
                {
                    var module = savedModules[i];
                    string lastPathPart = Path.GetFileName(module.path);
                    if (string.IsNullOrEmpty(lastPathPart))
                        lastPathPart = Path.GetFileName(Path.GetDirectoryName(module.path));

                    moduleOptions[i + 1] = module.name + "   ∕ " + lastPathPart;
                }
                
                // Adjust selectedModuleIndex for dropdown (add 1 because of "Select..." option)
                int dropdownIndex = selectedModuleIndex >= 0 ? selectedModuleIndex + 1 : 0;
                int newDropdownIndex = EditorGUILayout.Popup(dropdownIndex, moduleOptions, GUILayout.Width(150));
                
                // Handle selection change
                if (newDropdownIndex != dropdownIndex)
                {
                    if (newDropdownIndex == 0)
                    {
                        selectedModuleIndex = -1; // No selection
                    }
                    else
                    {
                        SelectSavedModule(newDropdownIndex - 1); // Subtract 1 for "Select..." option
                    }
                }
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Popup(0, new string[] { "No saved modules" }, GUILayout.Width(150));
                EditorGUI.EndDisabledGroup();
            }
            GUILayout.Label(GetStatusIcon(selectedModuleIndex != -1), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Init a new module / Delete Selected Module
            EditorGUILayout.BeginHorizontal();
            bool deleteMode = Event.current.control && Event.current.alt;
            bool hasSelectedModule = selectedModuleIndex >= 0 && selectedModuleIndex < savedModules.Count;
            string buttonText = deleteMode && hasSelectedModule ? "Delete Selected Module" : "Init New Module";
            string baseTooltip = deleteMode && hasSelectedModule ? 
                "Delete Selected Module: Removes the currently selected saved module from the list." :
                "Init a new module: Initializes a new SpacetimeDB module with the selected name and path.";
            
            string fullTooltip = baseTooltip + "\n\nTip: Hold Ctrl + Alt while clicking to delete the selected saved module instead (The path and files remain on the disk).";
            
            EditorGUILayout.LabelField(new GUIContent("Module Init or Del:", fullTooltip), GUILayout.Width(110));
            
            // Create button style for delete mode
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            if (deleteMode && hasSelectedModule)
            {
                // Orange color for delete warning
                Color warningColor;
                ColorUtility.TryParseHtmlString("#FFA500", out warningColor); // Orange
                buttonStyle.normal.textColor = warningColor;
                buttonStyle.hover.textColor = warningColor;
                Repaint();
            }
            
            EditorGUI.BeginDisabledGroup(deleteMode && !hasSelectedModule);
            if (GUILayout.Button(new GUIContent(buttonText, fullTooltip), buttonStyle, GUILayout.Width(150), GUILayout.Height(20)))
            {
                if (deleteMode && hasSelectedModule)
                {
                    // Delete selected module with confirmation
                    var moduleToDelete = savedModules[selectedModuleIndex];
                    if (EditorUtility.DisplayDialog(
                            "Confirm Module Deletion",
                            $"Are you sure you want to delete the saved module '{moduleToDelete.name}'?\n\nThis will only remove it from the saved list, not delete any files.",
                            "Yes, Delete",
                            "Cancel"))
                    {
                        savedModules.RemoveAt(selectedModuleIndex);
                        selectedModuleIndex = -1; // Clear selection
                        SaveModulesList();
                        LogMessage($"Removed saved module: {moduleToDelete.name}", 1);
                    }
                }
                else
                {
                    // Normal init new module functionality
                    InitNewModule();
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            #endregion

            #region WSL Mode
            if (serverMode == ServerMode.WslServer)
            {
                // WSL Settings
                GUILayout.Label("WSL Server Settings", EditorStyles.centeredGreyMiniLabel);

                // Backup Directory setting
                EditorGUILayout.BeginHorizontal();
                string backupDirectoryTooltip = 
                "Directory where SpacetimeDB server backups will be saved.\n\n"+
                "Note: Create a new empty folder if the server backups have not been created yet.\n"+
                "Backups for the server use little space, so you can commit this folder to your repository.";
                EditorGUILayout.LabelField(new GUIContent("Backup Directory:", backupDirectoryTooltip), GUILayout.Width(110));
                string backupDirButtonTooltip = "Current set path: " + (string.IsNullOrEmpty(backupDirectory) ? "Not Set" : backupDirectory);
                if (GUILayout.Button(new GUIContent("Set Backup Directory", backupDirButtonTooltip), GUILayout.Width(150), GUILayout.Height(20)))
                {
                    string path = EditorUtility.OpenFolderPanel("Select Backup Directory", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        backupDirectory = path;
                        serverManager.SetBackupDirectory(backupDirectory);
                        LogMessage($"Backup directory set to: {backupDirectory}", 1);
                    }
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(backupDirectory)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                // Debian Username setting
                EditorGUILayout.BeginHorizontal();
                string userNameTooltip = 
                "The Debian username to use for Debian commands.\n\n"+
                "Note: Needed for most server commands and utilities.";
                EditorGUILayout.LabelField(new GUIContent("Debian Username:", userNameTooltip), GUILayout.Width(110));
                string newUserName = EditorGUILayout.DelayedTextField(userName, GUILayout.Width(150));
                if (newUserName != userName)
                {
                    userName = newUserName;
                    serverManager.SetUserName(userName);
                    if (debugMode) LogMessage($"Debian username set to: {userName}", 0);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(userName)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                   
                // URL setting
                EditorGUILayout.BeginHorizontal();
                string urlTooltip = 
                "Required for the Server Database Window. The full URL of your SpacetimeDB server including port number.\n" +
                "Default: http://0.0.0.0:3000/\n" +
                "Note: The port number is required.";
                EditorGUILayout.LabelField(new GUIContent("URL:", urlTooltip), GUILayout.Width(110));
                string newUrl = EditorGUILayout.DelayedTextField(serverUrl, GUILayout.Width(150));
                if (newUrl != serverUrl)
                {
                    serverUrl = newUrl;
                    serverManager.SetServerUrl(serverUrl);
                    // Extract port from URL
                    int extractedPort = ExtractPortFromUrl(serverUrl);
                    if (extractedPort > 0) // If a valid port is found
                    {
                        if (extractedPort != serverPort) // If the port is different from the current customServerPort
                        {
                            serverPort = extractedPort;
                            serverManager.SetServerPort(serverPort);

                            if (debugMode) LogMessage($"Port extracted from URL: {serverPort}", 0);
                        }
                        // If the port is the same, we don't need to do anything,
                        // as the URL itself has changed and been set.
                    }
                    else
                    {
                        LogMessage("No valid port found in URL. Please include port in format 'http://127.0.0.1:3000/'", -2);
                    }
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(serverUrl)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();                
                
                // Auth Token setting
                EditorGUILayout.BeginHorizontal();
                string tokenTooltip = GetAuthTokenTooltip(PrefsKeyPrefix + "AuthToken",
                "Required to modify the database and run reducers. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.");
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(authToken, GUILayout.Width(150));
                if (newAuthToken != authToken)
                {
                    authToken = newAuthToken;
                    serverManager.SetAuthToken(authToken);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(authToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(3);

                if (GUILayout.Button("Launch WSL Server Installer"))
                        ServerInstallerWindow.ShowWindow();
                if (GUILayout.Button("Check Pre-Requisites", GUILayout.Height(20)))
                    CheckPrerequisites();

                // WSL Status display - add after the Check Pre-requisites button
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("WSL:", GUILayout.Width(110));
                Color originalWslColor = connectedStyle.normal.textColor;
                connectedStyle.normal.textColor = isWslRunning ? originalWslColor : Color.gray;
                string wslStatusText = isWslRunning ? "RUNNING" : "STOPPED";
                EditorGUILayout.LabelField(wslStatusText, connectedStyle);
                // Restore the original color after using it
                connectedStyle.normal.textColor = originalWslColor;
                EditorGUILayout.EndHorizontal();
            }
            #endregion

            #region Custom Mode
            if (serverMode == ServerMode.CustomServer)
            {
                // Remote Settings
                GUILayout.Label("Custom Server Settings", EditorStyles.centeredGreyMiniLabel);
                
                // SSH Keygen button
                EditorGUILayout.BeginHorizontal();
                string keygenTooltip = "Generates a new SSH key pair using Ed25519 algorithm.";
                EditorGUILayout.LabelField(new GUIContent("SSH Keygen:", keygenTooltip), GUILayout.Width(110));                if (GUILayout.Button("Generate SSH Key Pair", GUILayout.Width(150)))
                {
                    if (cmdProcessor == null)
                    {
                        cmdProcessor = new ServerCMDProcess(LogMessage, debugMode);
                    }
                    // Generate SSH key pair with default path and empty passphrase non-interactively
                    string sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
                    string defaultKeyPath = Path.Combine(sshDir, "id_ed25519");
                    // Create .ssh directory first, then generate the key pair
                    cmdProcessor.RunPowerShellCommand($"New-Item -ItemType Directory -Path '{sshDir}' -Force | Out-Null; ssh-keygen -t ed25519 -f '{defaultKeyPath}' -N '' -q", LogMessage);
                }
                EditorGUILayout.EndHorizontal();
                
                // SSH Private Key Path (button only)
                EditorGUILayout.BeginHorizontal();
                string keyPathTooltip = "The full path to your SSH private key file (e.g., C:\\Users\\YourUser\\.ssh\\id_ed25519).";
                if (!string.IsNullOrEmpty(sshPrivateKeyPath))
                {
                    keyPathTooltip += $"\n\nCurrent path: {sshPrivateKeyPath}";
                }                
                EditorGUILayout.LabelField(new GUIContent("Private Key Path:", keyPathTooltip), GUILayout.Width(110));
                if (GUILayout.Button(new GUIContent("Set Private Key Path", keyPathTooltip), GUILayout.Width(150)))
                {                    
                    // Use the same default .ssh directory as SSH key generation
                    string defaultSshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
                    string path = EditorUtility.OpenFilePanel("Select SSH Private Key", defaultSshDir, "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        sshPrivateKeyPath = path;
                        serverManager.SetSSHPrivateKeyPath(sshPrivateKeyPath);
                        if (serverCustomProcess != null) // Update ServerCustomProcess if it exists
                        {
                            serverCustomProcess.SetPrivateKeyPath(sshPrivateKeyPath);
                        }
                        if (debugMode) LogMessage($"SSH Private Key Path set to: {sshPrivateKeyPath}", 0);
                    }
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(sshPrivateKeyPath) && System.IO.File.Exists(sshPrivateKeyPath)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                // SSH Username
                EditorGUILayout.BeginHorizontal();
                string userNameTooltip = 
                "The SSH username to use to login to your distro.";
                EditorGUILayout.LabelField(new GUIContent("SSH Username:", userNameTooltip), GUILayout.Width(110));
                string newUserName = EditorGUILayout.DelayedTextField(sshUserName, GUILayout.Width(150));
                if (newUserName != sshUserName)
                {
                    sshUserName = newUserName;
                    serverManager.SetSSHUserName(sshUserName);
                                       
                    if (debugMode) LogMessage($"SSH username set to: {sshUserName}", 0);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(sshUserName)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                // URL Custom Server setting
                EditorGUILayout.BeginHorizontal();
                string urlTooltip = 
                "The full URL of your remote SpacetimeDB server including SpacetimeDB port number.\n" +
                "Note: The port number is required. Example: http://0.0.0.0:3000/\n" +
                "Make sure port 22 (SSH, used automatically) and port 3000 (SpacetimeDB) are open.\n" +
                "Press Enter after editing to apply changes.";
                EditorGUILayout.LabelField(new GUIContent("URL:", urlTooltip), GUILayout.Width(110));
                string newUrl = EditorGUILayout.DelayedTextField(customServerUrl, GUILayout.Width(150));
                if (newUrl != customServerUrl)
                {
                    customServerUrl = newUrl;
                    serverManager.SetCustomServerUrl(customServerUrl);
                    
                    // Extract port from URL
                    int extractedPort = ExtractPortFromUrl(customServerUrl);
                    if (extractedPort > 0) // If a valid port is found
                    {
                        if (extractedPort != customServerPort) // If the port is different from the current customServerPort
                        {
                            customServerPort = extractedPort;
                            serverManager.SetCustomServerPort(customServerPort);
                            
                            if (debugMode) LogMessage($"Port extracted from URL: {customServerPort}", 0);
                        }
                        // If the port is the same, we don't need to do anything,
                        // as the URL itself has changed and been set.
                    }
                    else
                    {
                        LogMessage("No valid port found in URL. Please include port in format \'http://127.0.0.1:3000/\'", -2);
                    }
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(customServerUrl)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();                

                // Custom Serer Auth Token
                EditorGUILayout.BeginHorizontal();
                string tokenTooltip = GetAuthTokenTooltip(PrefsKeyPrefix + "CustomServerAuthToken",
                "Required to modify the database and run reducers. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.");
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(customServerAuthToken, GUILayout.Width(150));
                if (newAuthToken != customServerAuthToken)
                {
                    customServerAuthToken = newAuthToken;
                    serverManager.SetCustomServerAuthToken(customServerAuthToken);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(customServerAuthToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(3);

                if (GUILayout.Button("Launch Custom Server Installer"))
                    ServerInstallerWindow.ShowCustomWindow();
                if (GUILayout.Button("Check Pre-Requisites and Connect", GUILayout.Height(20)))
                    CheckPrerequisitesCustom();

                // Connection status display
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connection:", GUILayout.Width(110));
                
                // Update connection status asynchronously to avoid blocking UI
                if (serverMode == ServerMode.CustomServer)
                {
                    serverManager.SSHConnectionStatusAsync();
                    isConnected = serverManager.IsSSHConnectionActive;
                }
                else
                {
                    isConnected = false;
                }
                
                Color originalColor = connectedStyle.normal.textColor;
                connectedStyle.normal.textColor = isConnected ? originalColor : Color.gray;
                string connectionStatusText = isConnected ? "CONNECTED SSH" : "DISCONNECTED";
                EditorGUILayout.LabelField(connectionStatusText, connectedStyle);
                // Restore the original color after using it
                connectedStyle.normal.textColor = originalColor;
                EditorGUILayout.EndHorizontal();
            }
            #endregion
            
            #region Maincloud Mode
            if (serverMode == ServerMode.MaincloudServer)
            {
                // Remote Settings
                GUILayout.Label("Maincloud Settings", EditorStyles.centeredGreyMiniLabel);

                // Maincloud Login
                EditorGUILayout.BeginHorizontal();
                string loginTooltip = "Login to the official SpacetimeDB Maincloud using your Github account";
                EditorGUILayout.LabelField(new GUIContent("Login:",loginTooltip), GUILayout.Width(110));
                if (GUILayout.Button("Login to Maincloud", GUILayout.Width(150)))
                {
                    if (cmdProcessor == null)
                    {
                        cmdProcessor = new ServerCMDProcess(LogMessage, debugMode);
                    }
                    LoginMaincloud();
                }
                EditorGUILayout.EndHorizontal();                

                // Auth Token setting
                EditorGUILayout.BeginHorizontal();
                string tokenTooltip = GetAuthTokenTooltip(PrefsKeyPrefix + "MaincloudAuthToken",
                "Required to modify the database and run reducers. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.");
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(maincloudAuthToken, GUILayout.Width(150));
                if (newAuthToken != maincloudAuthToken)
                {
                    maincloudAuthToken = newAuthToken;
                    serverManager.SetMaincloudAuthToken(maincloudAuthToken);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(maincloudAuthToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(3);

                if (GUILayout.Button("Launch Official Webpanel"))
                        Application.OpenURL("https://spacetimedb.com/login");
                if (GUILayout.Button("Check Pre-Requisites and Connect", GUILayout.Height(20)))
                    CheckPrerequisitesMaincloud();

                // Connection status display
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connection:", GUILayout.Width(110));
                bool isMaincloudConnected = serverManager.IsMaincloudConnected;
                Color originalColor = connectedStyle.normal.textColor;
                connectedStyle.normal.textColor = isMaincloudConnected ? originalColor : Color.gray;
                string maincloudStatusText = isMaincloudConnected ? "CONNECTED" : "DISCONNECTED";
                EditorGUILayout.LabelField(maincloudStatusText, connectedStyle);
                // Restore the original color after using it
                connectedStyle.normal.textColor = originalColor;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical(); // GUI Background
        }
        EditorGUILayout.EndVertical(); // End of Entire Pre-Requisites section
    }
    #endregion

    #region SettingsUI

    private void DrawSettingsSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        bool showSettingsWindow = EditorGUILayout.Foldout(EditorPrefs.GetBool(PrefsKeyPrefix + "ShowSettingsWindow", false), "Settings", true);
        EditorPrefs.SetBool(PrefsKeyPrefix + "ShowSettingsWindow", showSettingsWindow);

        if (showSettingsWindow)
        {   
            Color recommendedColor = Color.green;
            Color warningColor;
            ColorUtility.TryParseHtmlString("#FFA500", out warningColor); // Orange
            Color hiddenColor;
            ColorUtility.TryParseHtmlString("#808080", out hiddenColor); // Grey
            Color debugColor;
            ColorUtility.TryParseHtmlString("#30C099", out debugColor); // Cyan
        
            if (serverMode == ServerMode.WslServer) {
                // Server Mode toggle
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                string serverModeTooltip = 
                "Show CMD: Displays the standard CMD process window of the server. \n\n"+
                "Silent Mode: The server runs silently in the background without any window.";
                EditorGUILayout.LabelField(new GUIContent("CMD Visiblity:", serverModeTooltip), GUILayout.Width(120));
                GUIStyle silentToggleStyle = new GUIStyle(GUI.skin.button);
                if (serverManager.SilentMode)
                {
                    silentToggleStyle.normal.textColor = hiddenColor;
                    silentToggleStyle.hover.textColor = hiddenColor;
                }
                if (GUILayout.Button(serverManager.SilentMode ? "Silent Mode" : "Show CMD", silentToggleStyle))
                {
                    bool newSilentMode = !serverManager.SilentMode;
                    serverManager.SetSilentMode(newSilentMode);
                    silentMode = newSilentMode; // Keep local field in sync
                }
                EditorGUILayout.EndHorizontal();
            }
            
            // Server Change Detection toggle
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            string changeDetectionTooltip = 
            "Detecting Changes: Will detect changes to the server module and notify you. (Auto enabled in Auto Publish Mode) \n\n"+
            "Not Detecting Changes: Will not detect changes to the server module and will not notify you.";
            EditorGUILayout.LabelField(new GUIContent("Server Changes:", changeDetectionTooltip), GUILayout.Width(120));
            GUIStyle changeToggleStyle = new GUIStyle(GUI.skin.button);
            if (serverManager.DetectServerChanges)
            {
                // Use green for active detection
                changeToggleStyle.normal.textColor = recommendedColor;
                changeToggleStyle.hover.textColor = recommendedColor;
            }
            if (GUILayout.Button(serverManager.DetectServerChanges ? "Detecting Changes" : "Not Detecting Changes", changeToggleStyle))
            {
                bool newDetectChanges = !serverManager.DetectServerChanges;
                serverManager.SetDetectServerChanges(newDetectChanges);
                detectServerChanges = newDetectChanges; // Keep local field in sync
                
                if (newDetectChanges)
                {
                    serverChangesDetected = false; // Reset local flag
                    serverManager.ServerChangesDetected = false; // Reset server manager flag
                }
                else
                {
                    serverChangesDetected = false;
                    serverManager.ServerChangesDetected = false;
                }
            }
            EditorGUILayout.EndHorizontal();
            
            // Auto Publish Mode toggle
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            string autoPublishTooltip = 
            "Automatic Publishing: The server will automatically publish the module when changes are detected. \n\n"+
            "Manual Publish: The server will not automatically publish the module and will require manual publishing.";
            EditorGUILayout.LabelField(new GUIContent("Auto Publish Mode:", autoPublishTooltip), GUILayout.Width(120));
            GUIStyle autoPublishStyle = new GUIStyle(GUI.skin.button);
            if (serverManager.AutoPublishMode)
            {
                // Use green for active auto-publish
                autoPublishStyle.normal.textColor = recommendedColor;
                autoPublishStyle.hover.textColor = recommendedColor;
            }
            if (GUILayout.Button(serverManager.AutoPublishMode ? "Automatic Publishing" : "Manual Publish", autoPublishStyle))
            {
                bool newAutoPublish = !serverManager.AutoPublishMode;
                serverManager.SetAutoPublishMode(newAutoPublish);
                autoPublishMode = newAutoPublish; // Keep local field in sync
                
                if (newAutoPublish && !serverManager.DetectServerChanges)
                {
                    serverManager.SetDetectServerChanges(true);
                    detectServerChanges = true; // Keep local field in sync
                    serverChangesDetected = false;
                    serverManager.ServerChangesDetected = false;
                }
            }
            EditorGUILayout.EndHorizontal();
            
            // Publish and Generate Mode toggle
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            string publishGenerateTooltip = 
            "Publish will Generate: Publish button publishes the module and generates the Client Code. \n\n"+
            "Separate Generate: Separate generate button to generate the Client Code.\n\n"+
            "Recommended: Publish will Generate.";
            EditorGUILayout.LabelField(new GUIContent("Publish / Generate:", publishGenerateTooltip), GUILayout.Width(120));
            GUIStyle publishGenerateStyle = new GUIStyle(GUI.skin.button);
            if (serverManager.PublishAndGenerateMode)
            {
                // Use green for active auto-generate
                publishGenerateStyle.normal.textColor = recommendedColor;
                publishGenerateStyle.hover.textColor = recommendedColor;
            }
            if (GUILayout.Button(serverManager.PublishAndGenerateMode ? "Publish will Generate" : "Separate Generate", publishGenerateStyle))
            {
                bool newPublishGenerate = !serverManager.PublishAndGenerateMode;
                serverManager.SetPublishAndGenerateMode(newPublishGenerate);
                publishAndGenerateMode = newPublishGenerate; // Keep local field in sync
            }
            EditorGUILayout.EndHorizontal();

            if (serverMode == ServerMode.WslServer)
            {
                // WSL Auto Close toggle
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                string wslCloseTooltip = 
                "Close WSL at Unity Quit: The server will close the WSL process when Unity is closed. \n"+
                "Saves resources when server is not in use. WSL may otherwise leave several processes running.\n\n"+
                "Keep Running: The server will keep the WSL process running after Unity is closed.\n\n"+
                "Recommended: Close WSL at Unity Quit";
                EditorGUILayout.LabelField(new GUIContent("WSL Auto Close:", wslCloseTooltip), GUILayout.Width(120));
                GUIStyle wslCloseStyle = new GUIStyle(GUI.skin.button);
                if (serverManager.AutoCloseWsl)
                {
                    wslCloseStyle.normal.textColor = warningColor;
                    wslCloseStyle.hover.textColor = warningColor;
                }
                if (GUILayout.Button(serverManager.AutoCloseWsl ? "Close WSL at Unity Quit" : "Keep Running", wslCloseStyle))
                {
                    bool newAutoClose = !serverManager.AutoCloseWsl;
                    serverManager.SetAutoCloseWsl(newAutoClose);
                    autoCloseWsl = newAutoClose; // Keep local field in sync
                }
                EditorGUILayout.EndHorizontal();
            }

            // Clear Module and Database Log at Start toggle buttons
            if ((serverManager.SilentMode && serverMode == ServerMode.WslServer) || serverMode == ServerMode.CustomServer && serverMode != ServerMode.MaincloudServer)
            {
                // Module clear log at start toggle button in Silent Mode
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                string moduleLogTooltip = 
                "Clear Module Log at Server Start: The server will clear the module log at start. \n\n"+
                "Keep Module Log: The server will keep the module log between server restarts.";
                EditorGUILayout.LabelField(new GUIContent("Module Log:", moduleLogTooltip), GUILayout.Width(120));
                GUIStyle moduleLogToggleStyle = new GUIStyle(GUI.skin.button);
                if (serverManager.ClearModuleLogAtStart)
                {
                    moduleLogToggleStyle.normal.textColor = warningColor;
                    moduleLogToggleStyle.hover.textColor = warningColor;
                }
                if (GUILayout.Button(serverManager.ClearModuleLogAtStart ? "Clear at Server Start" : "Keeping Module Log", moduleLogToggleStyle))
                {
                    bool newClearModule = !serverManager.ClearModuleLogAtStart;
                    serverManager.SetClearModuleLogAtStart(newClearModule);
                    clearModuleLogAtStart = newClearModule; // Keep local field in sync
                }
                EditorGUILayout.EndHorizontal();
                
                // Database clear log at start toggle button in Silent Mode (only kept in memory for performance)
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                string databaseLogTooltip = 
                "Clear Database Log at Server Start: The server will clear the database log at start. \n\n"+
                "Keep Database Log: The server will keep the database log between server restarts.\n\n"+
                "Note: The database log is only kept in memory for performance, so it will be lost when the server is stopped if WSL Auto Close is enabled.";
                EditorGUILayout.LabelField(new GUIContent("Database Log:", databaseLogTooltip), GUILayout.Width(120));
                GUIStyle databaseLogToggleStyle = new GUIStyle(GUI.skin.button);
                if (serverManager.ClearDatabaseLogAtStart)
                {
                    databaseLogToggleStyle.normal.textColor = warningColor;
                    databaseLogToggleStyle.hover.textColor = warningColor;
                }
                if (GUILayout.Button(serverManager.ClearDatabaseLogAtStart ? "Clear at Server Start" : "Keeping Database Log", databaseLogToggleStyle))
                {
                    bool newClearDatabase = !serverManager.ClearDatabaseLogAtStart;
                    serverManager.SetClearDatabaseLogAtStart(newClearDatabase);
                    clearDatabaseLogAtStart = newClearDatabase; // Keep local field in sync
                }
                EditorGUILayout.EndHorizontal();
            }

            // Debug Mode toggle
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            string debugTooltip = 
            "Debug Mode: Will show all server management debug messages. \n\n"+
            "Debug Disabled: Will not show most debug messages. Important errors are still shown.";
            EditorGUILayout.LabelField(new GUIContent("Debug:", debugTooltip), GUILayout.Width(120));
            GUIStyle debugToggleStyle = new GUIStyle(GUI.skin.button);
            if (serverManager.DebugMode)
            {
                debugToggleStyle.normal.textColor = debugColor;
                debugToggleStyle.hover.textColor = debugColor;
            }
            if (GUILayout.Button(serverManager.DebugMode ? "Debug Mode" : "Debug Disabled", debugToggleStyle))
            {
                bool newDebugMode = !serverManager.DebugMode;
                serverManager.SetDebugMode(newDebugMode);
                debugMode = newDebugMode;
                
                // Update other components that need to know about debug mode
                ServerOutputWindow.debugMode = newDebugMode;
                ServerWindowInitializer.debugMode = newDebugMode;
                ServerUpdateProcess.debugMode = newDebugMode;
                ServerLogProcess.debugMode = newDebugMode;
                ServerCMDProcess.debugMode = newDebugMode;
                ServerCustomProcess.debugMode = newDebugMode;
                ServerDataWindow.debugMode = newDebugMode;
                ServerReducerWindow.debugMode = newDebugMode;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }
    #endregion

    #region ServerUI

    private void DrawServerSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        if (serverMode != ServerMode.MaincloudServer)
        {
            bool serverRunning = serverManager.IsServerStarted || serverManager.IsStartingUp;
            if (!serverManager.WslPrerequisitesChecked || !serverManager.HasWSL || !serverManager.HasDebian)
            {
                if (GUILayout.Button("Check Prerequisites to Start SpacetimeDB", GUILayout.Height(30)))
                {
                    CheckPrerequisites();
                }
            }
            else // If Prerequisites are checked then show normal server controls
            {
                if (serverMode == ServerMode.WslServer)
                {
                    if (!serverRunning)
                    {
                        if (GUILayout.Button("Start SpacetimeDB WSL", GUILayout.Height(30)))
                        {
                            serverManager.StartServer();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Stop SpacetimeDB WSL", GUILayout.Height(30)))
                        {
                            serverManager.StopServer();
                        }
                    }
                } else if (serverMode == ServerMode.CustomServer)
                {
                    if (!serverRunning)
                    {
                        if (GUILayout.Button("Start SpacetimeDB Remote", GUILayout.Height(30)))
                        {
                            serverManager.StartServer();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Stop SpacetimeDB Remote", GUILayout.Height(30)))
                        {
                            serverManager.StopServer();
                        }
                    }
                }
            }
        }

        // Activation of Server Windows
        bool wslServerActive = serverManager.IsServerStarted && serverMode == ServerMode.WslServer;
        bool wslServerActiveSilent = serverManager.SilentMode && serverMode == ServerMode.WslServer;
        bool customServerActive = serverManager.IsServerStarted && serverMode == ServerMode.CustomServer;
        bool customServerActiveSilent = serverMode == ServerMode.CustomServer;
        bool maincloudActive = serverManager.IsMaincloudConnected && serverMode == ServerMode.MaincloudServer;

        // Begin horizontal layout for the three buttons
        EditorGUILayout.BeginHorizontal();
               
        // View Logs
        EditorGUI.BeginDisabledGroup(!wslServerActiveSilent && !customServerActiveSilent && !maincloudActive);
        var logIcon = EditorGUIUtility.IconContent("d_Profiler.UIDetails").image;
        GUIContent logContent = new GUIContent("View Logs", "View detailed server logs");
        EditorGUILayout.BeginVertical(GUILayout.Height(40));
        // Icon centered at the top
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(logIcon, GUILayout.Width(20), GUILayout.Height(20));
        GUILayout.FlexibleSpace();       
        GUILayout.EndHorizontal();
        
        // Set button color based on window state
        Color originalColor = GUI.backgroundColor;
        if (viewLogsWindowOpen)
            GUI.backgroundColor = windowToggleColor; // Light green tint when active

        if (GUILayout.Button(logContent, buttonStyle, GUILayout.ExpandHeight(true)))
        {
            if (viewLogsWindowOpen)
            {
                CloseWindow<ServerOutputWindow>();
            }
            else
            {
                serverManager.ViewServerLogs();
            }
            UpdateWindowStates();
        }
        
        // Restore original color
        GUI.backgroundColor = originalColor;
        EditorGUILayout.EndVertical();
        EditorGUI.EndDisabledGroup();
               
        // Browse Database
        EditorGUI.BeginDisabledGroup(!wslServerActive && !customServerActive && !maincloudActive);
        var dbIcon = EditorGUIUtility.IconContent("d_VerticalLayoutGroup Icon").image;
        GUIContent dbContent = new GUIContent("Browse DB", "Browse and query the SpacetimeDB database");
        EditorGUILayout.BeginVertical(GUILayout.Height(40));
        // Icon centered at the top
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(dbIcon, GUILayout.Width(20), GUILayout.Height(20));
        GUILayout.FlexibleSpace();        
        GUILayout.EndHorizontal();
        
        // Set button color based on window state
        Color originalColor2 = GUI.backgroundColor;
        if (browseDbWindowOpen)
            GUI.backgroundColor = windowToggleColor; // Light green tint when active
        
        if (GUILayout.Button(dbContent, buttonStyle, GUILayout.ExpandHeight(true)))
        {
            if (browseDbWindowOpen)
            {
                CloseWindow<ServerDataWindow>();
            }
            else
            {
                ServerDataWindow.ShowWindow();
            }
            UpdateWindowStates();
        }
        
        // Restore original color
        GUI.backgroundColor = originalColor2;
        EditorGUILayout.EndVertical();
        EditorGUI.EndDisabledGroup();
                
        // Run Reducer
        EditorGUI.BeginDisabledGroup(!wslServerActive && !customServerActive && !maincloudActive);
        var playIcon = EditorGUIUtility.IconContent("d_PlayButton").image;
        GUIContent reducerContent = new GUIContent("Run Reducer", "Run database reducers");
        EditorGUILayout.BeginVertical(GUILayout.Height(40));
        // Icon centered at the top
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(playIcon, GUILayout.Width(20), GUILayout.Height(20));
        GUILayout.FlexibleSpace();        
        GUILayout.EndHorizontal();
        
        // Set button color based on window state
        Color originalColor3 = GUI.backgroundColor;
        if (runReducerWindowOpen)
            GUI.backgroundColor = windowToggleColor; // Light green tint when active

        if (GUILayout.Button(reducerContent, buttonStyle, GUILayout.ExpandHeight(true)))
        {
            if (runReducerWindowOpen)
            {
                CloseWindow<ServerReducerWindow>();
            }
            else
            {
                ServerReducerWindow.ShowWindow();
            }
            UpdateWindowStates();
        }
        
        // Restore original color
        GUI.backgroundColor = originalColor3;
        EditorGUILayout.EndVertical();
        EditorGUI.EndDisabledGroup();
               
        EditorGUILayout.EndHorizontal();
        // End horizontal layout for the three buttons
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("SpacetimeDB:", GUILayout.Width(110));
        
        // Use green for running/starting, gray for stopped
        string statusText;
        Color originalStatusColor = connectedStyle.normal.textColor;
        bool isActive = (serverMode == ServerMode.MaincloudServer && serverManager.IsMaincloudConnected) || 
                        serverManager.IsStartingUp || 
                        serverManager.IsServerStarted;
        
        if (serverMode == ServerMode.MaincloudServer && serverManager.IsMaincloudConnected)
        {
            connectedStyle.normal.textColor = isActive ? originalStatusColor : Color.gray;
            statusText = "MAINCLOUD";
        }
        else if (serverManager.IsStartingUp)
        {
            connectedStyle.normal.textColor = isActive ? originalStatusColor : Color.gray;
            statusText = "STARTING...";
        }
        else if (serverManager.IsServerStarted)
        {
            connectedStyle.normal.textColor = isActive ? originalStatusColor : Color.gray;
            statusText = "RUNNING";
        }
        else
        {
            connectedStyle.normal.textColor = Color.gray;
            statusText = "STOPPED";
        }

        EditorGUILayout.LabelField(statusText, connectedStyle);

        EditorGUILayout.EndHorizontal();
        
        GUILayout.Space(-20);

        EditorGUILayout.BeginHorizontal();

        GUILayout.FlexibleSpace();
        
        // Restore the original color after using it
        connectedStyle.normal.textColor = originalStatusColor;

        GUIStyle versionStyle = new GUIStyle(EditorStyles.miniLabel);
        versionStyle.fontSize = 10;
        versionStyle.normal.textColor = new Color(0.43f, 0.43f, 0.43f);

        EditorGUILayout.LabelField("v", versionStyle, GUILayout.Width(10));
        if (serverMode == ServerMode.WslServer)
            EditorGUILayout.LabelField(spacetimeDBCurrentVersion, versionStyle, GUILayout.Width(25));
        else if (serverMode == ServerMode.CustomServer)
            EditorGUILayout.LabelField(spacetimeDBCurrentVersionCustom, versionStyle, GUILayout.Width(25));
        else if (serverMode == ServerMode.MaincloudServer)
            EditorGUILayout.LabelField(spacetimeDBLatestVersion, versionStyle, GUILayout.Width(25));

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }
    #endregion

    #region CommandUI

    private void DrawCommandsSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);       

        bool showUtilityCommands = EditorGUILayout.Foldout(EditorPrefs.GetBool(PrefsKeyPrefix + "ShowUtilityCommands", false), "Commands", true);
        EditorPrefs.SetBool(PrefsKeyPrefix + "ShowUtilityCommands", showUtilityCommands);
        
        if (showUtilityCommands)
        {
            EditorGUILayout.Space(-10);

            if (serverMode == ServerMode.WslServer)
                EditorGUILayout.LabelField("WSL SpacetimeDB Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));
            else if (serverMode == ServerMode.CustomServer)
                EditorGUILayout.LabelField("Remote SpacetimeDB Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));
            else if (serverMode == ServerMode.MaincloudServer)
                EditorGUILayout.LabelField("Maincloud SpacetimeDB Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            if (GUILayout.Button("Login", GUILayout.Height(20)))
            {
                if (serverMode == ServerMode.WslServer && CLIAvailableLocal()) serverManager.RunServerCommand("spacetime login", "Logging in to SpacetimeDB");
                #pragma warning disable CS4014 // Because this call is not awaited we disable the warning, it works anyhow
                else if (serverMode == ServerMode.CustomServer && CLIAvailableRemote()) serverCustomProcess.RunVisibleSSHCommand($"/home/{sshUserName}/.local/bin/spacetime login");
                #pragma warning restore CS4014
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL) or remote (SSH) and it is available.", -1);
            }

            if (GUILayout.Button("Show Login Info", GUILayout.Height(20)))
            {
                if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                serverManager.RunServerCommand("spacetime login show --token", "Showing SpacetimeDB login info and token");
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL) or remote (SSH) and it is available.", -1);
            }

            if (GUILayout.Button("Show Server Config", GUILayout.Height(20)))
            {
                if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                serverManager.RunServerCommand("spacetime server list", "Showing SpacetimeDB server config");
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL) or remote (SSH) and it is available.", -1);
            }

            EditorGUI.BeginDisabledGroup(serverMode != ServerMode.MaincloudServer && !serverManager.IsServerStarted);
                if (GUILayout.Button("Show Active Modules", GUILayout.Height(20)))
                {
                    if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                    serverManager.RunServerCommand("spacetime list", "Showing active modules");
                    else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL) or remote (SSH) and it is available.", -1);
                }
            EditorGUI.EndDisabledGroup();

            if (serverMode != ServerMode.MaincloudServer)
            {
                if (GUILayout.Button("Ping Server", GUILayout.Height(20)))
                {
                    if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                    serverManager.PingServer(true);
                    else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL) or remote (SSH) and it is available.", -1);
                }
            }

            // Service Status button (only in Custom Server mode)
            if (serverMode == ServerMode.CustomServer)
            {
                if (GUILayout.Button("Service Status", buttonStyle))
                {
                    if (CLIAvailableRemote())
                    CheckServiceStatus();
                    else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a remote (SSH) and it is available.", -1);
                }
            }
            
            if (GUILayout.Button("Show Version", GUILayout.Height(20)))
            {
                if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                serverManager.RunServerCommand("spacetime --version", "Showing SpacetimeDB version");
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL) or remote (SSH) and it is available.", -1);
            }

            if (serverMode == ServerMode.WslServer)
            {
                if (spacetimeDBCurrentVersion != spacetimeDBLatestVersion)
                {
                    string updateTooltip = "Version " + spacetimeDBLatestVersion + " of SpacetimeDB is available.\nUpdate when the WSL server is not running.";
                    EditorGUI.BeginDisabledGroup(serverManager.IsServerStarted);
                    if (GUILayout.Button(new GUIContent("Update SpacetimeDB", updateTooltip), GUILayout.Height(20)))
                    {
                        ServerInstallerWindow.ShowWindow();
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }

            if (serverMode == ServerMode.CustomServer)
            {
                if (spacetimeDBCurrentVersionCustom != spacetimeDBLatestVersion)
                {
                    string updateTooltip = "Version " + spacetimeDBLatestVersion + " of SpacetimeDB is available.\nUpdate when the server is not running.";
                    // Use cached connection status instead of blocking synchronous check
                    serverManager.SSHConnectionStatusAsync();
                    EditorGUI.BeginDisabledGroup(serverManager.IsSSHConnectionActive);
                    if (GUILayout.Button(new GUIContent("Update SpacetimeDB", updateTooltip), GUILayout.Height(20)))
                    {
                        ServerInstallerWindow.ShowCustomWindow();
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }

            EditorGUILayout.LabelField("WSL Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            if (GUILayout.Button("Open Debian Window", GUILayout.Height(20)))
            {
                serverManager.OpenDebianWindow();
            }

            if (serverMode == ServerMode.WslServer)
            {
                string backupTooltip = "Creates a tar archive of the DATA folder in your SpacetimeDB server, which contains the database, logs and settings of your module.";
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(serverManager.BackupDirectory));
                if (GUILayout.Button(new GUIContent("Backup Server Data", backupTooltip), GUILayout.Height(20)))
                {
                    serverManager.BackupServerData();
                }
                EditorGUI.EndDisabledGroup();
            }

            if (debugMode && serverMode == ServerMode.WslServer)
            {
                if (GUILayout.Button("Test Server Running", GUILayout.Height(20)))
                {
                    if (cmdProcessor == null)
                    {
                        cmdProcessor = new ServerCMDProcess(LogMessage, debugMode);
                    }
                    // Run the async check and handle result via continuation
                    TestServerRunningAsync();
                }
            }
        }
        
        // Display server changes notification if detected
        if (serverManager.ServerChangesDetected)
        {
            GUIStyle updateStyle = new GUIStyle(EditorStyles.boldLabel);
            updateStyle.normal.textColor = Color.green;
            
            // Make it more visibly clickable by adding hover effects and underline
            updateStyle.hover.textColor = new Color(0.0f, 0.8f, 0.0f); // Darker green on hover
            updateStyle.fontStyle = FontStyle.Bold;
            
            // Add clickable visual indicator
            string displayText = !serverManager.AutoPublishMode ? "New Server Update Ready to Publish!" : "New Server Update! (Auto Publish Mode) (Click to dismiss)";
            string tooltip = "Server changes have been detected and are ready to be published. \nClick to dismiss this notification until new changes are detected.";
            
            // Create a button-like appearance
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            // Use a button that looks like a label for better click response
            if (GUILayout.Button(new GUIContent(displayText, tooltip), updateStyle))
            {
                serverManager.ResetServerDetection();
                Repaint();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        
        if (serverManager.PublishAndGenerateMode) {
            if (!serverManager.AutoPublishMode) {
                EditorGUILayout.LabelField("Will Publish then Generate client code automatically.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(20));
            } else {
                EditorGUILayout.LabelField("Will Publish then Generate client code automatically on detected changes.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(20));
            }
        } else {
            EditorGUILayout.LabelField("First Publish then Generate client code.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(20));
        }

        // Add Publish Module button
        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(serverManager.ModuleName));
        
        string editModuleTooltip = "Edit the lib.rs script of the selected module.";
        if (GUILayout.Button(new GUIContent("Edit Module", editModuleTooltip), GUILayout.Height(20)))
        {
            // Open the module script in the default editor
            string modulePath = Path.Combine(serverDirectory, "src", "lib.rs");
            if (File.Exists(modulePath))
            {
                Process.Start(modulePath);
            }
            else
            {
                LogMessage($"Module script not found at: {modulePath}", -2);
            }
        }

        // Check if control key is held
        bool resetDatabase = Event.current.control && Event.current.alt;
        
        // Create button style based on control key state
        GUIStyle publishButtonStyle = new GUIStyle(GUI.skin.button);
        if (resetDatabase)
        {
            // Orange color for database reset warning
            Color warningColor;
            ColorUtility.TryParseHtmlString("#FFA500", out warningColor); // Orange
            publishButtonStyle.normal.textColor = warningColor;
            publishButtonStyle.hover.textColor = warningColor;
            Repaint();
        }
        if (serverManager.ServerChangesDetected)
        {
            publishButtonStyle.normal.textColor = Color.green;
            publishButtonStyle.hover.textColor = Color.green;
        }

        string buttonText;
        if (serverMode == ServerMode.MaincloudServer)
        buttonText = resetDatabase ? "Publish Module and Reset Database" : "Publish Module to Maincloud";
        else
        buttonText = resetDatabase ? "Publish Module and Reset Database" : "Publish Module";

        if (publishing) 
        {
            buttonText = "Publishing...";
            publishButtonStyle.normal.textColor = Color.green; // Disable button while publishing
            publishButtonStyle.hover.textColor = Color.green;
        }

        string publishTooltip = "Publish the selected module to the server.\n\n" +
                                "Ctrl + Alt + Click to also reset the database.";

        if (GUILayout.Button(new GUIContent(buttonText, publishTooltip), publishButtonStyle, GUILayout.Height(30)))
        {
            // Use reset database if control+alt key is held
            if (resetDatabase)
            {
                // Display confirmation dialog when resetting database
                if (EditorUtility.DisplayDialog(
                        "Confirm Database Reset",
                        "Are you sure you wish to delete the entire database and publish the module?",
                        "Yes, Reset Database",
                        "Cancel"))
                {
                    serverManager.Publish(true);
                }
            }
            else
            {
                serverManager.Publish(false);
                publishing = true; // Set flag to indicate publishing is in progress
            }
        }
        
        // Add Generate Unity Files button
        if (!serverManager.PublishAndGenerateMode)
        {
            if (GUILayout.Button("Generate Client Code", GUILayout.Height(30)))
            {
                string outDir = serverManager.GetRelativeClientPath();
                serverManager.RunServerCommand($"spacetime generate --out-dir {outDir} --lang {serverManager.UnityLang} -y", "Generating Client Code");
                LogMessage($"Generated Client Code to: {outDir}", 1);
            }
        }
        
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.EndVertical();
    }
    #endregion

    #region Check Pre-reqs

    public void CheckPrerequisites()
    {
        serverManager.CheckPrerequisites((wsl, debian, trixie, curl, spacetime, spacetimePath, rust, spacetimeService, spacetimeLogsService, binaryen) => {
            EditorApplication.delayCall += () => {
                // Update local state for UI
                hasWSL = wsl;
                hasDebian = debian;
                hasDebianTrixie = trixie;
                hasCurl = curl;
                hasSpacetimeDBServer = spacetime;
                hasSpacetimeDBPath = spacetimePath;
                hasSpacetimeDBService = spacetimeService;
                hasSpacetimeDBLogsService = spacetimeLogsService;
                hasRust = rust;
                hasBinaryen = binaryen;
                wslPrerequisitesChecked = true;
                
                // Load userName value 
                userName = serverManager.UserName;
                
                Repaint();
                
                bool essentialSoftware = 
                    wsl && debian && trixie && curl && 
                    spacetime && spacetimePath && rust;

                bool essentialUserSettings = 
                    !string.IsNullOrEmpty(userName) && 
                    !string.IsNullOrEmpty(serverDirectory) && 
                    !string.IsNullOrEmpty(moduleName) && 
                    !string.IsNullOrEmpty(serverLang);
                
                if (!essentialSoftware)
                {
                    LogMessage("Please check that you have everything necessary installed. Launching Server Installer Window.",-2);
                    ServerInstallerWindow.ShowWindow();
                }
                else if (essentialSoftware && essentialUserSettings && !initializedFirstModule)
                {
                    bool result = EditorUtility.DisplayDialog(
                        "Initialize First Module",
                        "All requirements met to initialize a server module. Do you wish to do this now?\n" +
                        "When you publish your successfully initialized module it will automatically create the database for your module and all the necessary files so you can start developing!",
                        "OK",
                        "Cancel"
                    );

                    // Set the flag so the Initialize First Module dialog doesn't show again
                    initializedFirstModule = true;
                    serverManager.SetInitializedFirstModule(true);
                    
                    if (result)
                    {
                        InitNewModule();
                    }
                }
            };
        });
    }

    public async void CheckPrerequisitesCustom()
    {
        if (string.IsNullOrEmpty(sshUserName))
        {
            LogMessage("Please set your SSH username first", -1);
            return;
        }
        
        if (string.IsNullOrEmpty(sshPrivateKeyPath))
        {
            LogMessage("Please set your SSH Private Key Path first", -1);
            return;
        }
        if (!System.IO.File.Exists(sshPrivateKeyPath))
        {
            LogMessage($"SSH Private Key file not found at: {sshPrivateKeyPath}. Please ensure the path is correct.", -1);
            return;
        }

        // Initialize serverCustomProcess if it's null
        if (serverCustomProcess == null)
        {
            serverCustomProcess = new ServerCustomProcess(LogMessage, debugMode);
            serverCustomProcess.LoadSettings();
        }

        serverCustomProcess.SetPrivateKeyPath(sshPrivateKeyPath);
        
        if (string.IsNullOrEmpty(customServerUrl))
        {
            LogMessage("Please set your custom server URL first", -1);
            return;
        }
        
        bool isReachable = await serverCustomProcess.CheckServerReachable();
        
        if (!isReachable)
        {
            LogMessage("Server is not reachable. Please check if the server is online and your network connection.", -1);
            return;
        }
        
        bool connectionSuccessful = await serverCustomProcess.StartSession();
        
        if (connectionSuccessful)
        {           
            await serverCustomProcess.CheckSpacetimeDBInstalled();
        }
        else
        {
            LogMessage("Failed to establish SSH connection.", -1);
        }
    }

    private async void CheckPrerequisitesMaincloud()
    {
        // Check if module name is set
        if (string.IsNullOrEmpty(moduleName))
        {
            LogMessage("Error: Module Name is not set. Please set it in the Pre-requisites section.", -1);
            return;
        }
        
        // Check maincloud connectivity
        LogMessage("Checking Maincloud connectivity...", 0);
        
        // Run the connectivity check
        await serverManager.CheckMaincloudConnectivity();
        
        // The result is automatically updated in the UI via the RepaintCallback
        if (serverManager.IsMaincloudConnected)
        {
            LogMessage("Connected to Maincloud successfully!", 1);
            
            // After connection check, also verify auth token if available
            if (string.IsNullOrEmpty(serverManager.MaincloudAuthToken))
            {
                LogMessage("No auth token set. Please click Show Login Info in Commands and paste the token to your pre-requisites field.", -2);
            }
            
            // Start the server to enable log viewing regardless of auth token status
            //serverManager.StartServer();
        }
        else
        {
            LogMessage("Failed to connect to Maincloud. Please check your internet connection and make sure your module name is correct.", -1);
        }
    }
    #endregion

    #region Server Methods

    public void ClearModuleLogFile() // Clears the module tmp log file
    {
        serverManager.ClearModuleLogFile();
    }
    
    public void ClearDatabaseLog() // Clears the database log content (only kept in memory for performance)
    {
        serverManager.ClearDatabaseLog();
    }

    public void AttemptDatabaseLogRestartAfterReload()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerWindow] Checking database log process");
        serverManager.AttemptDatabaseLogRestartAfterReload();
    }

    public void ForceRefreshLogsFromSessionState()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerWindow] Force refreshing logs from SessionState");
        if (serverManager != null)
        {
            serverManager.ForceRefreshLogsFromSessionState();
        }
    }

    public ServerMode GetCurrentServerMode()
    {
        return serverMode;
    }

    public void ForceSSHLogRefresh()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerWindow] Force triggering SSH log refresh");
        if (serverManager != null && serverManager.CurrentServerMode == ServerManager.ServerMode.CustomServer)
        {
            serverManager.ForceSSHLogRefresh();
        }
    }

    public void ForceWSLLogRefresh()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerWindow] Force triggering WSL log refresh");
        if (serverManager != null && serverManager.CurrentServerMode == ServerManager.ServerMode.WslServer)
        {
            serverManager.ForceWSLLogRefresh();
        }
    }

    public async void LoginMaincloud()
    {
        serverManager.RunServerCommand("spacetime logout", "Logging out to clear possible local login...");
        await Task.Delay(1000); // Wait for logout to complete
        serverManager.RunServerCommand("spacetime login", "Launching Maincloud login...");
    }

    private async void CheckServiceStatus()
    {
        if (serverMode != ServerMode.CustomServer)
        {
            LogMessage("Service status check is only available in Custom Server mode", -1);
            return;
        }

        var result = await serverCustomProcess.CheckServiceStatus();
        if (result.success)
        {
            LogMessage("Service Status:", 0);
            LogMessage(result.output, 0);
        }
        else
        {
            LogMessage($"Check if SpacetimeDB is installed as a service. Failed to get service status: {result.error}", -1);
        }
    }
    #endregion

    #region LogMessage
    
    public void LogMessage(string message, int style)
    {
        // Skip extra warning messages
        if (message.Contains("WARNING"))
        {
            return;
        }
        
        // Check if message contains "Error:" or "error[E####]:" patterns and color them red
        string processedMessage = message;
        System.Text.RegularExpressions.Regex errorCodeRegex = new System.Text.RegularExpressions.Regex(@"error\[[^\]]+\]:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        // Handle "Error:" pattern first - check if we're already in error state or if this message contains "Error:"
        int errorIndex = message.IndexOf("Error:", StringComparison.OrdinalIgnoreCase);
        bool hasErrorCode = errorCodeRegex.IsMatch(message);
        
        if (errorIndex != -1)
        {
            // If this message contains "Error:", start error state
            if (errorIndex != -1)
            {
                if (message.Contains("\n"))
                {
                    // Multi-line message - handle both error patterns
                    string[] lines = message.Split('\n');
                    bool foundErrorLine = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // Check if this line contains "Error:" to start coloring subsequent lines
                        if (!foundErrorLine && lines[i].IndexOf("Error:", StringComparison.OrdinalIgnoreCase) != -1)
                        {
                            foundErrorLine = true;
                        }
                        
                        // Color line red if it's after "Error:" line OR if it contains error code pattern
                        if (foundErrorLine || errorCodeRegex.IsMatch(lines[i]))
                        {
                            lines[i] = $"<color=#FF0000>{lines[i]}</color>";
                        }
                    }
                    processedMessage = string.Join("\n", lines);
                }
                else
                {
                    // Single line - color from "Error:" to the end
                    string beforeError = message.Substring(0, errorIndex);
                    string errorAndAfter = message.Substring(errorIndex);
                    processedMessage = beforeError + $"<color=#FF0000>{errorAndAfter}</color>";
                }
            }
        }
        else if (hasErrorCode)
        {
            // Handle "error[E####]:" pattern - color the entire line red if it contains this pattern
            if (message.Contains("\n"))
            {
                // Process line by line
                string[] lines = message.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (errorCodeRegex.IsMatch(lines[i]))
                    {
                        lines[i] = $"<color=#FF0000>{lines[i]}</color>";
                    }
                }
                processedMessage = string.Join("\n", lines);
            }
            else
            {
                // Single line - color the entire message red
                processedMessage = $"<color=#FF0000>{message}</color>";
            }
        }
        
        if (style == 1) // Success Green
        {
            string coloredMessage = $"<color=#00FF00>{processedMessage}</color>";
            commandOutputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {coloredMessage}\n";
        } 
        else if (style == -1) // Error Red
        {
            string coloredMessage = $"<color=#FF0000>{processedMessage}</color>";
            commandOutputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {coloredMessage}\n";
        }
        else if (style == -2) // Warning Orange
        {
            string coloredMessage = $"<color=#e0a309>{processedMessage}</color>";
            commandOutputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {coloredMessage}\n";
        }
        else // Normal White (style == 0) Also catches any other style
        { 
            commandOutputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {processedMessage}\n";
        }
        
        // Trim log if it gets too long
        if (commandOutputLog.Length > 10000)
        {
            commandOutputLog = commandOutputLog.Substring(commandOutputLog.Length - 10000);
        }
        
        // Set flag to scroll to bottom if autoscroll is enabled
        if (autoscroll)
        {
            needsScrollToBottom = true;
        }
        
        Repaint();
    }
    #endregion
    
    #region Utility Methods

    // Helper method to format auth token tooltip with first and last 20 characters
    private string GetAuthTokenTooltip(string prefsKey, string baseTooltip)
    {
        string storedToken = EditorPrefs.GetString(prefsKey, "");
        if (string.IsNullOrEmpty(storedToken))
        {
            return baseTooltip;
        }
        
        if (storedToken.Length <= 40)
        {
            // If token is 40 characters or less, show the partial token
            return baseTooltip + "\n\nCurrent token: " + storedToken.Substring(0, Math.Min(20, storedToken.Length)) + " ... " + 
                   (storedToken.Length > 20 ? storedToken.Substring(Math.Max(0, storedToken.Length - 20)) : "");
        }
        else
        {
            // Show first 20 and last 20 characters
            return baseTooltip + "\n\nCurrent token: " + storedToken.Substring(0, 20) + " ... " + storedToken.Substring(storedToken.Length - 20);
        }
    }

    public bool CLIAvailableLocal()
    {
        if (hasWSL)
        {
            if (debugMode) LogMessage("SpacetimeDB Local CLI is available.", 1);
            return true;
        }
        else
        {
            if (debugMode) LogMessage("SpacetimeDB Local CLI is not available.", -2);
            return false;
        }
    }

    public bool CLIAvailableRemote()
    {
        if (isConnected)
        {
            if (debugMode) LogMessage("CLI Available on Remote Server", 1);
            return true;
        }
        else
        {
            if (debugMode) LogMessage("CLI Not Available on Remote Server", -1);
            return false;
        }
    }
    
    private string GetStatusIcon(bool status)
    {
        return status ? "✓" : "○";
    }
    
    private void CopyDirectory(string sourceDir, string destDir)
    {
        // Get all files from the source
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, true);
        }
        
        // Copy subdirectories recursively
        foreach (string directory in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(directory);
            string destSubDir = Path.Combine(destDir, dirName);
            Directory.CreateDirectory(destSubDir);
            CopyDirectory(directory, destSubDir);
        }
    }

    private void HandlePlayModeStateChange(PlayModeStateChange state)
    {
        if (debugMode) UnityEngine.Debug.Log($"[ServerWindow] PlayMode State Change Detected: {state}");
        
        // Update session state flags
        if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            // Store silent server state
            SessionState.SetBool(SessionKeyWasRunningSilently, serverManager.IsServerStarted && serverManager.SilentMode);
        }
    }

    private int ExtractPortFromUrl(string url)
    {
        try
        {
            // Look for the port pattern ":number/" or ":number" at the end
            int colonIndex = url.LastIndexOf(':');
            if (colonIndex != -1 && colonIndex < url.Length - 1)
            {
                // Find the end of the port number (either / or end of string)
                int endIndex = url.IndexOf('/', colonIndex);
                if (endIndex == -1) endIndex = url.Length;
                
                // Extract the port substring
                string portStr = url.Substring(colonIndex + 1, endIndex - colonIndex - 1);
                
                // Try to parse the port
                if (int.TryParse(portStr, out int port) && port > 0 && port < 65536)
                {
                    return port;
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) LogMessage($"Error extracting port from URL: {ex.Message}", -1);
        }
        
        return -1; // Invalid or no port found
    }

    private void UpdateServerModeState()
    {       
        // Set the appropriate flag based on the current serverMode
        switch (serverMode)
        {
            case ServerMode.WslServer:
                if (debugMode) LogMessage("Server mode set-default config: Local", 0);
                EditorUpdateHandler();
                // Configure SpacetimeDB CLI for local server
                serverManager.RunServerCommand("spacetime server set-default local", "");
                break;
            case ServerMode.CustomServer:
                if (debugMode) LogMessage("Server mode set to: Custom Remote (local in config)", 0);
                EditorUpdateHandler();
                if (serverCustomProcess == null)
                {
                    serverCustomProcess = new ServerCustomProcess(LogMessage, debugMode);
                    serverCustomProcess.LoadSettings();
                }
                // Configure SpacetimeDB CLI for local server (for publish and generate)
                serverManager.RunServerCommand("spacetime server set-default local", "");
                break;
            case ServerMode.MaincloudServer:
                if (debugMode) LogMessage("Server mode set-default config: Maincloud", 0);
                EditorUpdateHandler();
                // Configure SpacetimeDB CLI for maincloud server
                serverManager.RunServerCommand("spacetime server set-default maincloud", "");
                break;
        }

        // Update ServerManager with the new mode
        serverManager.SetServerMode((ServerManager.ServerMode)serverMode);

        // Use string representation for consistency with ServerManager
        EditorPrefs.SetString(PrefsKeyPrefix + "ServerMode", serverMode.ToString());
        Repaint();
    }

    private void LoadServerModeFromPrefs()
    {
        // Load server mode from preferences using string representation for compatibility with ServerManager
        string modeName = EditorPrefs.GetString(PrefsKeyPrefix + "ServerMode", "WslServer");
        if (Enum.TryParse(modeName, out ServerMode mode))
        {
            serverMode = mode;
        }
        else
        {
            // Fallback to reading the old INT version for backwards compatibility
            serverMode = (ServerMode)EditorPrefs.GetInt(PrefsKeyPrefix + "ServerMode", (int)ServerMode.WslServer);
            // Update to new string format
            EditorPrefs.SetString(PrefsKeyPrefix + "ServerMode", serverMode.ToString());
        }
        
        UpdateServerModeState();
    }        

    private void HandleAutoscrollBehavior(Vector2 previousScrollPosition)
    {
        if (Event.current.type == EventType.Repaint)
        {
            // Check if scroll position changed
            bool scrollPositionChanged = Vector2.Distance(scrollPosition, previousScrollPosition) > 0.1f;
            
            // Calculate content dimensions for bottom detection
            GUIStyle richTextStyle = new GUIStyle(EditorStyles.textArea);
            richTextStyle.richText = true;
            
            // Estimate scroll view width (window width minus padding and scrollbar)
            float estimatedScrollViewWidth = position.width - 40f; // Account for padding and scrollbar
            float contentHeight = richTextStyle.CalcHeight(new GUIContent(commandOutputLog.TrimEnd('\n')), estimatedScrollViewWidth);
            
            // Better estimation of scroll view height (total window height minus header/buttons area)
            float headerHeight = 120f; // Approximate height of all the UI above the scroll view
            float scrollViewHeight = Mathf.Max(100f, position.height - headerHeight);
            float maxScrollY = Mathf.Max(0, contentHeight - scrollViewHeight);
            
            // Check if we're at the bottom (within 30 pixels tolerance)
            bool isAtBottom = maxScrollY <= 30f || scrollPosition.y >= maxScrollY - 30f;
            
            if (scrollPositionChanged && !wasAutoScrolling)
            {
                if (autoscroll && !isAtBottom)
                {
                    // User manually scrolled up while autoscroll was on - turn it off
                    autoscroll = false;
                    EditorPrefs.SetBool(PrefsKeyPrefix + "Autoscroll", autoscroll);
                    Repaint();
                }
                else if (!autoscroll && isAtBottom)
                {
                    // User scrolled to the bottom while autoscroll was off - turn it on
                    autoscroll = true;
                    EditorPrefs.SetBool(PrefsKeyPrefix + "Autoscroll", autoscroll);
                    Repaint();
                }
            }
            
            // Apply autoscroll only when new content is added (flag is set) or when manually enabled
            if (autoscroll && needsScrollToBottom)
            {
                wasAutoScrolling = true;
                scrollPosition.y = float.MaxValue;
                needsScrollToBottom = false; // Clear the flag after applying scroll
            }
            else
            {
                wasAutoScrolling = false;
            }
            
            // Update last position for next frame
            lastScrollPosition = scrollPosition;
        }
    }

    // Async helper for Test Server Running button
    private async void TestServerRunningAsync()
    {
        if (cmdProcessor == null)
        {
            cmdProcessor = new ServerCMDProcess(LogMessage, debugMode);
        }
        bool isRunning = await cmdProcessor.CheckServerRunning(instantCheck: true);
        if (isRunning)
        {
            LogMessage("SpacetimeDB WSL server is running.", 1);
        }
        else
        {
            LogMessage("SpacetimeDB WSL server is not running.", -1);
        }
    }

    public void UpdateWindowStates()
    {
        viewLogsWindowOpen = IsWindowOpen<ServerOutputWindow>();
        browseDbWindowOpen = IsWindowOpen<ServerDataWindow>();
        runReducerWindowOpen = IsWindowOpen<ServerReducerWindow>();
    }

    private bool IsWindowOpen<T>() where T : EditorWindow
    {
        return HasOpenInstances<T>();
    }
    
    private void CloseWindow<T>() where T : EditorWindow
    {
        T[] windows = Resources.FindObjectsOfTypeAll<T>();
        if (windows != null && windows.Length > 0)
        {
            foreach (T window in windows)
            {
                window.Close();
            }
        }
    }
    #endregion

    #region Module Methods

    private void InitNewModule()
    {
        serverManager.InitNewModule();
        initializedFirstModule = true;
        serverManager.SetInitializedFirstModule(true);
    }

    private void SaveModulesList()
    {
        try
        {
            string json = JsonUtility.ToJson(new SerializableList<ModuleInfo>(savedModules));
            EditorPrefs.SetString(PrefsKeyPrefix + "SavedModules", json);
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"Error saving modules list: {ex.Message}");
        }
    }

    private void LoadModulesList()
    {
        try
        {
            string json = EditorPrefs.GetString(PrefsKeyPrefix + "SavedModules", "");
            if (!string.IsNullOrEmpty(json))
            {
                var serializableList = JsonUtility.FromJson<SerializableList<ModuleInfo>>(json);
                savedModules = serializableList?.items ?? new List<ModuleInfo>();
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"Error loading modules list: {ex.Message}");
            savedModules = new List<ModuleInfo>();
        }
    }

    private int AddModuleToSavedList(string name, string path)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return -1;

        // Check if module already exists and update it
        for (int i = 0; i < savedModules.Count; i++)
        {
            if (savedModules[i].name == name)
            {
                savedModules[i] = new ModuleInfo(name, path);
                SaveModulesList();
                return i;
            }
        }

        // Add new module
        savedModules.Add(new ModuleInfo(name, path));
        SaveModulesList();
        return savedModules.Count - 1;
    }

    private void SelectSavedModule(int index)
    {
        if (index >= 0 && index < savedModules.Count)
        {
            selectedModuleIndex = index;
            var module = savedModules[index];
            
            // Update current settings
            moduleName = module.name;
            serverDirectory = module.path;
            
            // Update ServerManager
            serverManager.SetModuleName(moduleName);
            serverManager.SetServerDirectory(serverDirectory);
            serverManager.SetSelectedModuleIndex(index); // To remember the place in the list
            
            // Update detection process
            if (detectionProcess != null)
            {
                detectionProcess.Configure(serverDirectory, detectServerChanges);
            }

            // Update log processes
            serverManager.SwitchModule(moduleName, true); // Clear database log

            // Refresh database and reducer windows
            if (browseDbWindowOpen)
            {
                ServerDataWindow window = GetWindow<ServerDataWindow>();
                window?.RefreshAllData();
            }
            if (runReducerWindowOpen)
            {
                ServerReducerWindow window = GetWindow<ServerReducerWindow>();
                window?.RefreshReducers();
            }

            LogMessage($"Selected module: {module.name} at {module.path}", 1);
        }
    }
    #endregion

    // Public method to get ServerManager for external access
    public ServerManager GetServerManager()
    {
        return serverManager;
    }

    // Display Cosmos Cove Control Panel title text in the menu bar
    [MenuItem("SpacetimeDB/Cosmos Cove Control Panel", priority = -11000)]
    private static void CosmosCoveControlPanel(){}
    [MenuItem("SpacetimeDB/Cosmos Cove Control Panel", true, priority = -11000)]
    private static bool ValidateCosmosCoveControlPanel(){return false;}

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games