using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using NorthernRogue.CCCP.Editor.Settings;
using ModuleInfo = NorthernRogue.CCCP.Editor.Settings.ModuleInfo;

// The main Comos Cove Control Panel that controls the server state and launches all features ///

namespace NorthernRogue.CCCP.Editor {

public class ServerWindow : EditorWindow
{
    // Server Manager
    private ServerManager serverManager;
    
    // Process Handlers
    private ServerWSLProcess wslProcess;
    private ServerCustomProcess serverCustomProcess;
    private ServerDetectionProcess detectionProcess;
    
    // Server mode
    private ServerMode serverMode = ServerMode.WSLServer;
    private ServerMode previousServerMode = ServerMode.WSLServer;

    // Pre-requisites WSL - Direct property access to settings
    private bool hasWSL { get => CCCPSettingsAdapter.GetHasWSL(); set => CCCPSettingsAdapter.SetHasWSL(value); }
    private bool hasDebian { get => CCCPSettingsAdapter.GetHasDebian(); set => CCCPSettingsAdapter.SetHasDebian(value); }
    private bool hasDebianTrixie { get => CCCPSettingsAdapter.GetHasDebianTrixie(); set => CCCPSettingsAdapter.SetHasDebianTrixie(value); }
    private bool hasCurl { get => CCCPSettingsAdapter.GetHasCurl(); set => CCCPSettingsAdapter.SetHasCurl(value); }
    private bool hasSpacetimeDBServer { get => CCCPSettingsAdapter.GetHasSpacetimeDBServer(); set => CCCPSettingsAdapter.SetHasSpacetimeDBServer(value); }
    private bool hasSpacetimeDBPath { get => CCCPSettingsAdapter.GetHasSpacetimeDBPath(); set => CCCPSettingsAdapter.SetHasSpacetimeDBPath(value); }
    private bool hasSpacetimeDBService { get => CCCPSettingsAdapter.GetHasSpacetimeDBService(); set => CCCPSettingsAdapter.SetHasSpacetimeDBService(value); }
    private bool hasSpacetimeDBLogsService { get => CCCPSettingsAdapter.GetHasSpacetimeDBLogsService(); set => CCCPSettingsAdapter.SetHasSpacetimeDBLogsService(value); }
    private bool hasRust { get => CCCPSettingsAdapter.GetHasRust(); set => CCCPSettingsAdapter.SetHasRust(value); }
    private bool hasNETSDK { get => CCCPSettingsAdapter.GetHasNETSDK(); set => CCCPSettingsAdapter.SetHasNETSDK(value); }
    private bool hasBinaryen { get => CCCPSettingsAdapter.GetHasBinaryen(); set => CCCPSettingsAdapter.SetHasBinaryen(value); }
    private bool hasGit { get => CCCPSettingsAdapter.GetHasGit(); set => CCCPSettingsAdapter.SetHasGit(value); }
    private bool wslPrerequisitesChecked { get => CCCPSettingsAdapter.GetWslPrerequisitesChecked(); set => CCCPSettingsAdapter.SetWslPrerequisitesChecked(value); }
    private bool initializedFirstModule { get => CCCPSettingsAdapter.GetInitializedFirstModule(); set => CCCPSettingsAdapter.SetInitializedFirstModule(value); }
    private bool publishFirstModule { get => CCCPSettingsAdapter.GetPublishFirstModule(); set => CCCPSettingsAdapter.SetPublishFirstModule(value); }
    private bool hasAllPrerequisites { get => CCCPSettingsAdapter.GetHasAllPrerequisites(); set => CCCPSettingsAdapter.SetHasAllPrerequisites(value); }

    // Server Configuration - Direct property access to settings
    private string userName { get => CCCPSettingsAdapter.GetUserName(); set => CCCPSettingsAdapter.SetUserName(value); }
    private string backupDirectory { get => CCCPSettingsAdapter.GetBackupDirectory(); set => CCCPSettingsAdapter.SetBackupDirectory(value); }
    private string serverDirectory { get => CCCPSettingsAdapter.GetServerDirectory(); set => CCCPSettingsAdapter.SetServerDirectory(value); }
    private string unityLang { get => CCCPSettingsAdapter.GetUnityLang(); set => CCCPSettingsAdapter.SetUnityLang(value); }
    private string clientDirectory { get => CCCPSettingsAdapter.GetClientDirectory(); set => CCCPSettingsAdapter.SetClientDirectory(value); }
    private string serverLang { get => CCCPSettingsAdapter.GetServerLang(); set => CCCPSettingsAdapter.SetServerLang(value); }
    private string moduleName { get => CCCPSettingsAdapter.GetModuleName(); set => CCCPSettingsAdapter.SetModuleName(value); }
    private string serverUrl { get => CCCPSettingsAdapter.GetServerUrl(); set => CCCPSettingsAdapter.SetServerUrl(value); }
    private int serverPort { get => CCCPSettingsAdapter.GetServerPort(); set => CCCPSettingsAdapter.SetServerPort(value); }
    private string authToken { get => CCCPSettingsAdapter.GetAuthToken(); set => CCCPSettingsAdapter.SetAuthToken(value); }

    // Pre-requisites Custom Server - Direct property access to settings
    private string sshUserName { get => CCCPSettingsAdapter.GetSSHUserName(); set => CCCPSettingsAdapter.SetSSHUserName(value); }
    private string customServerUrl { get => CCCPSettingsAdapter.GetCustomServerUrl(); set => CCCPSettingsAdapter.SetCustomServerUrl(value); }
    private int customServerPort { get => CCCPSettingsAdapter.GetCustomServerPort(); set => CCCPSettingsAdapter.SetCustomServerPort(value); }
    private string customServerAuthToken { get => CCCPSettingsAdapter.GetCustomServerAuthToken(); set => CCCPSettingsAdapter.SetCustomServerAuthToken(value); }
    private string sshPrivateKeyPath { get => CCCPSettingsAdapter.GetSSHPrivateKeyPath(); set => CCCPSettingsAdapter.SetSSHPrivateKeyPath(value); }
    private bool isConnected;

    // Pre-requisites Maincloud Server - Direct property access to settings
    private string maincloudAuthToken { get => CCCPSettingsAdapter.GetMaincloudAuthToken(); set => CCCPSettingsAdapter.SetMaincloudAuthToken(value); }
   
    // Server status
    private double lastCheckTime = 0;
    private const double checkInterval = 5.0; // Master interval for status checks
    private bool serverRunning = false;

    // Server Settings - Direct property access to settings
    public bool debugMode { get => CCCPSettingsAdapter.GetDebugMode(); set => CCCPSettingsAdapter.SetDebugMode(value); }
    private bool hideWarnings { get => CCCPSettingsAdapter.GetHideWarnings(); set => CCCPSettingsAdapter.SetHideWarnings(value); }
    private bool detectServerChanges { get => CCCPSettingsAdapter.GetDetectServerChanges(); set => CCCPSettingsAdapter.SetDetectServerChanges(value); }
    private bool serverChangesDetected { get => CCCPSettingsAdapter.GetServerChangesDetected(); set => CCCPSettingsAdapter.SetServerChangesDetected(value); }
    private bool autoPublishMode { get => CCCPSettingsAdapter.GetAutoPublishMode(); set => CCCPSettingsAdapter.SetAutoPublishMode(value); }
    private bool publishAndGenerateMode { get => CCCPSettingsAdapter.GetPublishAndGenerateMode(); set => CCCPSettingsAdapter.SetPublishAndGenerateMode(value); }
    private bool silentMode { get => CCCPSettingsAdapter.GetSilentMode(); set => CCCPSettingsAdapter.SetSilentMode(value); }
    private bool autoCloseWsl { get => CCCPSettingsAdapter.GetAutoCloseWsl(); set => CCCPSettingsAdapter.SetAutoCloseWsl(value); }
    private bool clearModuleLogAtStart { get => CCCPSettingsAdapter.GetClearModuleLogAtStart(); set => CCCPSettingsAdapter.SetClearModuleLogAtStart(value); }
    private bool clearDatabaseLogAtStart { get => CCCPSettingsAdapter.GetClearDatabaseLogAtStart(); set => CCCPSettingsAdapter.SetClearDatabaseLogAtStart(value); }

    // Update SpacetimeDB - Direct property access to settings
    private string spacetimeDBCurrentVersion { get => CCCPSettingsAdapter.GetSpacetimeDBCurrentVersion(); set => CCCPSettingsAdapter.SetSpacetimeDBCurrentVersion(value); }
    private string spacetimeDBCurrentVersionCustom { get => CCCPSettingsAdapter.GetSpacetimeDBCurrentVersionCustom(); set => CCCPSettingsAdapter.SetSpacetimeDBCurrentVersionCustom(value); }
    private string spacetimeDBLatestVersion { get => CCCPSettingsAdapter.GetSpacetimeDBLatestVersion(); set => CCCPSettingsAdapter.SetSpacetimeDBLatestVersion(value); }

    // UI - Direct property access to settings for persistent UI state
    private Vector2 scrollPosition;
    private string commandOutputLog = "";
    private bool autoscroll { get => CCCPSettingsAdapter.GetAutoscroll(); set => CCCPSettingsAdapter.SetAutoscroll(value); }
    private bool colorLogo { get => CCCPSettingsAdapter.GetColorLogo(); set => CCCPSettingsAdapter.SetColorLogo(value); }
    private bool publishing = false;
    private bool isUpdatingCCCP = false;
    private double cccpUpdateStartTime = 0;
    private bool wasAutoScrolling = false;
    private bool needsScrollToBottom = false; // Flag to control when to apply autoscroll
    private Texture2D logoTexture;
    private GUIStyle connectedStyle;
    private GUIStyle buttonStyle;
    private bool stylesInitialized = false;    // UI optimization
    private const double statusUICheckInterval = 3.0; // More responsive interval when window is in focus
    private bool windowFocused = false;
    
    // Window toggle states
    private bool viewLogsWindowOpen = false;
    private bool browseDbWindowOpen = false;
    private bool runReducerWindowOpen = false;
    private Color windowToggleColor = new Color(0.6f, 1.6f, 0.6f);
    
    // Session state key for domain reload - Check if yet needed
    private const string SessionKeyWasRunningSilently = "ServerWindow_WasRunningSilently";

    // Track WSL status
    private bool isWslRunning = false;
    
    // Track Docker status
    private bool isDockerRunning = false;

    // Cancellation token source for status checks
    private System.Threading.CancellationTokenSource statusCheckCTS;

    public static string Documentation = "https://docs.google.com/document/d/1HpGrdNicubKD8ut9UN4AzIOwdlTh1eO4ampZuEk5fM0/edit?usp=sharing";

    [MenuItem("Window/SpacetimeDB Server Manager/1. Main Window")]
    public static void ShowWindow()
    {
        ServerWindow window = GetWindow<ServerWindow>("Server");
        window.minSize = new Vector2(270f, 600f);
    }    
    
    public enum ServerMode
    {
        WSLServer,
        DockerServer,
        CustomServer,
        MaincloudServer,
    }

    public enum DrawerType
    {
        Prerequisites,
        Settings,
        Commands
    }

    public enum AuthTokenType
    {
        WSL,
        Custom,
        Maincloud
    }

    // Saved modules list - Direct property access to settings
    private List<ModuleInfo> savedModules 
    { 
        get 
        { 
            var modules = CCCPSettingsAdapter.GetSavedModules();
            return modules ?? new List<ModuleInfo>();
        } 
        set => CCCPSettingsAdapter.SetSavedModules(value); 
    }
    private int selectedModuleIndex { get => CCCPSettingsAdapter.GetSelectedModuleIndex(); set => CCCPSettingsAdapter.SetSelectedModuleIndex(value); }
    private string newModuleNameInput = ""; // Input field for new module name (UI state)

    // Session state keys for window management
    private const string SessionKeyBrowseDbOpen = "ServerWindow_BrowseDbOpen";
    private const string SessionKeyRunReducerOpen = "ServerWindow_RunReducerOpen";

    #region OnGUI

    private void OnGUI()
    {
        // Ensure serverManager is initialized before drawing GUI
        if (serverManager == null)
        {
            EditorGUILayout.LabelField("Initializing...", EditorStyles.centeredGreyMiniLabel);
            return;
        }
        
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
            if (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)
                EditorGUILayout.LabelField("Local Server Mode", subTitleStyle);
            else if (serverMode == ServerMode.CustomServer)
                EditorGUILayout.LabelField("Remote Server Mode", subTitleStyle);
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
            string tooltipVersion = "Click to change logo color\n" + "Type: " + ServerUpdateProcess.GetCachedDistributionType();
            if (GUILayout.Button(new GUIContent("version " + ServerUpdateProcess.GetCurrentPackageVersion(), tooltipVersion), titleControlStyle))
            {
                colorLogo = !colorLogo;
                CCCPSettingsAdapter.SetColorLogo(colorLogo);
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
        EditorGUILayout.LabelField("Command Output:", EditorStyles.boldLabel, GUILayout.Width(110));

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
            CCCPSettingsAdapter.SetAutoscroll(autoscroll);
            
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
        if (GUILayout.Button(new GUIContent("clear"), clearStyle, GUILayout.Width(35)))
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

        // CCCP Update Button (GitHub or Asset Store, ServerUpdateProcess checks which version is available)
        bool githubUpdateAvailable = ServerUpdateProcess.IsGithubUpdateAvailable();
        bool assetStoreUpdateAvailable = ServerUpdateProcess.IsAssetStoreUpdateAvailable();
        
        if ((githubUpdateAvailable || assetStoreUpdateAvailable) && !SessionState.GetBool("CCCPUpdateMessageDismissed", false))
        {
            // Check if we need to reset the updating state after 10 seconds
            if (isUpdatingCCCP && EditorApplication.timeSinceStartup - cccpUpdateStartTime > 10.0)
            {
                isUpdatingCCCP = false;
            }
            
            string buttonText;
            if (isUpdatingCCCP)
            {
                buttonText = "Updating CCCP Package...";
            }
            else if (githubUpdateAvailable)
            {
                buttonText = "New CCCP Update Available (GitHub)";
            }
            else
            {
                buttonText = "New CCCP Update Available (Asset Store)";
            }
            
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
            
            // Create horizontal layout for update button and dismiss button
            EditorGUILayout.BeginHorizontal();

            string updateButtonTooltip = "Update CCCP Package";

            if (GUILayout.Button(new GUIContent(buttonText, updateButtonTooltip), updateButtonStyle))
            {
                isUpdatingCCCP = true;
                cccpUpdateStartTime = EditorApplication.timeSinceStartup;
                
                if (githubUpdateAvailable)
                {
                    ServerUpdateProcess.UpdateGithubPackage();
                }
                else if (assetStoreUpdateAvailable)
                {
                    ServerUpdateProcess.UpdateAssetStorePackage();
                }
            }
            
            // Dismiss button (X)
            GUIStyle dismissButtonStyle = new GUIStyle(GUI.skin.button);
            dismissButtonStyle.normal.textColor = Color.white;
            dismissButtonStyle.hover.textColor = Color.orange;
            dismissButtonStyle.active.textColor = Color.orange;
            dismissButtonStyle.focused.textColor = Color.white;
            dismissButtonStyle.fontSize = 12;

            string dismissButtonTooltip = "Dismiss Update Notification";

            if (GUILayout.Button(new GUIContent("✕", dismissButtonTooltip), dismissButtonStyle, GUILayout.Width(25), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
            {
                SessionState.SetBool("CCCPUpdateMessageDismissed", true);
            }
            EditorGUILayout.EndHorizontal();
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
        // Refresh settings cache to ensure we have the latest data (including migrated modules)
        // This is critical after EditorPrefs migration as the cached settings may not reflect
        // the newly migrated data until the cache is explicitly refreshed
        CCCPSettingsAdapter.RefreshSettingsCache();
        CCCPSettings.RefreshInstance();
        
        if (debugMode) 
            UnityEngine.Debug.Log($"[ServerWindow] OnEnable: Initial refresh complete. Module count: {savedModules?.Count ?? 0}");
        
        // Initialize ServerManager with logging callback
        serverManager = new ServerManager(LogMessage, Repaint);
        
        // After creating ServerManager, ensure it has the latest settings
        serverManager.LoadSettings();
        serverManager.Configure();
        
        // Initialize wslProcess to avoid null reference exceptions
        if (wslProcess == null)
        {
            wslProcess = new ServerWSLProcess(LogMessage, debugMode);
        }

        // Load server mode from Settings
        LoadServerModeFromSettings();
        
        // Load the currently selected module if any (after initial settings refresh)
        LoadSelectedModuleFromSettings();
        
        // Restore window states from SessionState
        browseDbWindowOpen = SessionState.GetBool(SessionKeyBrowseDbOpen, false);
        runReducerWindowOpen = SessionState.GetBool(SessionKeyRunReducerOpen, false);
        
        // Also do a delayed refresh to handle any timing issues with asset database
        EditorApplication.delayCall += () => {
            CCCPSettingsAdapter.RefreshSettingsCache();
            CCCPSettings.RefreshInstance();
            
            // Reconfigure ServerManager with refreshed settings
            if (serverManager != null)
            {
                serverManager.LoadSettings();
                serverManager.Configure();
            }
            
            // Load the currently selected module if any (after settings refresh)
            LoadSelectedModuleFromSettings();
            
            if (debugMode) 
                UnityEngine.Debug.Log($"[ServerWindow] OnEnable: Delayed refresh complete. Module count: {savedModules?.Count ?? 0}");
            Repaint(); // Force a repaint to update the UI
        };

        // Register for focus events
        EditorApplication.focusChanged += OnFocusChanged;

        // Start checking server status
        EditorApplication.update += EditorUpdateHandler;

        // Check if we were previously running silently and restore state if needed
        if (serverManager != null && (!serverManager.IsServerStarted || !serverManager.SilentMode))
        {
            // Clear the flag if not running silently on enable
            SessionState.SetBool(SessionKeyWasRunningSilently, false);
        }

        // Ensure the flag is correctly set based on current state when enabled
        if (serverManager != null)
        {
            SessionState.SetBool(SessionKeyWasRunningSilently, serverManager.IsServerStarted && serverManager.SilentMode);
        }

        EditorApplication.playModeStateChanged += HandlePlayModeStateChange;

        // Check if we need to restart the database log process
        bool databaseLogWasRunning = SessionState.GetBool("ServerWindow_DatabaseLogRunning", false);
        if (serverManager != null && serverManager.IsServerStarted && serverManager.SilentMode && databaseLogWasRunning)
        {
            if (serverManager.DebugMode) LogMessage("Restarting database logs after editor reload...", 0);
            AttemptDatabaseLogRestartAfterReload();
        }

        // Update the publishing state from serverManager
        if (serverManager != null)
        {
            publishing = serverManager.Publishing;
        } 
        
        // Status checks (awaited)
        if (serverManager != null && (serverManager.CurrentServerMode == ServerManager.ServerMode.WSLServer || serverManager.CurrentServerMode == ServerManager.ServerMode.DockerServer))
        {
            EditorApplication.delayCall += async () =>
            {
                try
                {
                    if (serverManager != null)
                    {
                        if (serverManager.CurrentServerMode == ServerManager.ServerMode.DockerServer)
                        {
                            await serverManager.CheckDockerStatus();
                            isDockerRunning = serverManager.IsDockerRunning;
                        }
                        else if (serverManager.CurrentServerMode == ServerManager.ServerMode.WSLServer)
                        {
                            await serverManager.CheckWslStatus();
                            isWslRunning = serverManager.IsWslRunning;
                        }
                        Repaint();
                    }
                }
                catch (Exception ex)
                {
                    if (serverManager != null && serverManager.DebugMode) 
                    {
                        string serviceType = serverManager.CurrentServerMode == ServerManager.ServerMode.DockerServer ? "Docker" : "WSL";
                        UnityEngine.Debug.LogWarning($"Error in {serviceType} status check: {ex.Message}");
                    }
                }
            };
        }

        // Status checks
        if (serverManager != null)
        {
            try
            {
                serverRunning = serverManager.IsServerStarted;
                Repaint();

                // Update SSH connection status
                if (serverMode == ServerMode.CustomServer)
                {
                    serverManager.SSHConnectionStatusAsync();
                    isConnected = serverManager.IsSSHConnectionActive;
                }
            }
            catch (Exception ex)
            {
                if (serverManager != null && serverManager.DebugMode) UnityEngine.Debug.LogWarning($"Error in Custom Server running check: {ex.Message}");
            }
        }

    }

    private async void EditorUpdateHandler()
    {
        if (serverManager == null) return;
        
        // Update window states regularly to keep UI in sync
        UpdateWindowStates();
        
        // Continuously sync publishing state with ServerManager
        bool newPublishingState = serverManager.Publishing;
        if (publishing != newPublishingState)
        {
            publishing = newPublishingState;
            Repaint(); // Refresh UI when publishing state changes
        }
        
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
        
        // Background status checks to update the UI without having to interact with it
        if (windowFocused)
        {
            if (currentTime - lastCheckTime > statusUICheckInterval)
            {
                if (serverMode == ServerMode.CustomServer) 
                {
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
                }
                Repaint();
            }
        }
    }
    
    private async Task CheckStatusAsync(System.Threading.CancellationToken token)
    {
        try
        {
            if (serverManager != null)
            {
                await serverManager.CheckAllStatus();
                //UnityEngine.Debug.Log($"[ServerWindow CheckStatusAsync] Status check completed at {DateTime.Now}"); // Keep for debugging
                // Only update UI if the operation wasn't cancelled
                if (!token.IsCancellationRequested)
                {
                    // Update local state for UI display - only update if value actually changed
                    bool newServerChangesDetected = serverManager.ServerChangesDetected;
                    if (serverChangesDetected != newServerChangesDetected)
                    {
                        serverChangesDetected = newServerChangesDetected;
                    }
                    isWslRunning = serverManager.IsWslRunning;
                }
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
        if (serverManager != null)
        {
            SessionState.SetBool(SessionKeyWasRunningSilently, serverManager.IsServerStarted && serverManager.SilentMode);
            // Also save the ServerManager state for domain reload persistence
            serverManager.SaveServerStateToSessionState();
        }

        // Cleanup event handlers
        EditorApplication.update -= EditorUpdateHandler;
        EditorApplication.focusChanged -= OnFocusChanged;
        
        // Force save any pending UI settings changes
        CCCPSettingsAdapter.ForceUISettingsSave();
        
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
                //UnityEngine.Debug.Log("[ServerWindow] Editor focus regained - resetting timing to prevent log processing backlog");
            }
        }
    }
    #endregion
    
    #region Pre-RequisitesUI

    private void DrawPrerequisitesSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox); // Start of Pre-Requisites section

        string prerequisitesTitle;
        if (hasAllPrerequisites)
        {
            prerequisitesTitle = "Pre-Requisites";
        }
        else
        {
            prerequisitesTitle = "Pre-Requisites (Check Needed)";
        }

        // Pre Requisites foldout state
        bool previousShowPrerequisites = CCCPSettingsAdapter.GetShowPrerequisites();
        bool showPrerequisites = EditorGUILayout.Foldout(previousShowPrerequisites, prerequisitesTitle, true);
        
        // Handle mutually exclusive drawer state
        if (showPrerequisites != previousShowPrerequisites)
        {
            SetDrawerState(DrawerType.Prerequisites, showPrerequisites);
            Repaint();
        }

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
            string localModeTooltip = "Run a local server with SpacetimeDB";
            bool isLocalMode = (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer);
            if (GUILayout.Button(new GUIContent("Local", localModeTooltip), isLocalMode ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                if (serverMode == ServerMode.MaincloudServer) // Maincloud is always started so we don't have to check for serverStarted
                {
                    bool modeChange = EditorUtility.DisplayDialog("Confirm Mode Change", 
                    "Do you want to stop your Maincloud log process and change the server mode to Local server?",
                    "OK","Cancel");
                    if (modeChange)
                    {
                        serverManager.StopMaincloudLog();
                        if (debugMode) LogMessage("Stopped Maincloud log process before mode switch", 0);

                        // Restore to last used local server mode (WSL or Docker)
                        serverMode = (ServerMode)CCCPSettingsAdapter.GetLastLocalServerMode();
                        UpdateServerModeState();
                    }
                } 
                else if (serverMode == ServerMode.CustomServer)
                {
                    // Restore to last used local server mode (WSL or Docker)
                    serverMode = (ServerMode)CCCPSettingsAdapter.GetLastLocalServerMode();
                    UpdateServerModeState();
                    ClearModuleLogFile();
                    ClearDatabaseLog();
                }
                // Else we are already in Local mode and don't have to do anything
            }
            string customModeTooltip = "Connect to your custom remote server and run spacetime commands";
            if (GUILayout.Button(new GUIContent("Remote", customModeTooltip), serverMode == ServerMode.CustomServer ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                if (serverMode == ServerMode.MaincloudServer)
                {
                    serverMode = ServerMode.CustomServer;
                    UpdateServerModeState();
                    ClearModuleLogFile();
                    ClearDatabaseLog();
                }
                else if (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)
                {
                    // Save the current local mode before switching away
                    CCCPSettingsAdapter.SetLastLocalServerMode((ServerManager.ServerMode)serverMode);
                    serverMode = ServerMode.CustomServer;
                    UpdateServerModeState();
                    ClearModuleLogFile();
                    ClearDatabaseLog();
                }
                // Else we are already in Custom mode and don't have to do anything
            }
            string maincloudModeTooltip = "Connect to the official SpacetimeDB cloud server and run spacetime commands";
            if (GUILayout.Button(new GUIContent("Maincloud", maincloudModeTooltip), serverMode == ServerMode.MaincloudServer ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                if (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)
                {
                    bool modeChange = EditorUtility.DisplayDialog("Confirm Mode Change", 
                    "Do you want to stop your Local server and change the server mode to Maincloud server?",
                    "OK","Cancel");
                    if (modeChange)
                    {
                        // Save the current local mode before switching away
                        CCCPSettingsAdapter.SetLastLocalServerMode((ServerManager.ServerMode)serverMode);
                        serverManager.StopServer();
                        serverMode = ServerMode.MaincloudServer;
                        UpdateServerModeState();
                    }
                } 
                else if (serverMode == ServerMode.CustomServer)
                {
                    serverMode = ServerMode.MaincloudServer;
                    UpdateServerModeState();
                    ClearModuleLogFile();
                    ClearDatabaseLog();
                }
                // Else we are already in Maincloud mode and don't have to do anything
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

            // CLI Provider dropdown (only shown in Local mode) Equals to which local server mode is selected
            if (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)
            {
                EditorGUILayout.BeginHorizontal();
                string cliProviderTooltip = 
                "WSL: Use Windows Subsystem for Linux to run a SpacetimeDB CLI and Server locally. \n\n"+
                "Docker: Use Docker containers to run a SpacetimeDB CLI and Server locally. Docker is available for Linux, MacOS or Windows. \n\n"+
                "Both options provide a local development environment.";
                EditorGUILayout.LabelField(new GUIContent("CLI Provider:", cliProviderTooltip), GUILayout.Width(110));
                string[] cliProviderOptions = new string[] { "WSL (Windows)", "Docker (Any OS)" };
                int cliProviderSelectedIndex = serverMode == ServerMode.WSLServer ? 0 : 1;
                int newCliProviderSelectedIndex = EditorGUILayout.Popup(cliProviderSelectedIndex, cliProviderOptions, GUILayout.Width(150));
                if (newCliProviderSelectedIndex != cliProviderSelectedIndex)
                {
                    ServerMode newMode = newCliProviderSelectedIndex == 0 ? ServerMode.WSLServer : ServerMode.DockerServer;
                    serverMode = newMode;
                    // Save the selected CLI provider as the last local mode
                    CCCPSettingsAdapter.SetLastLocalServerMode((ServerManager.ServerMode)newMode);
                    UpdateServerModeState();
                    LogMessage($"Local server mode changed to: {cliProviderOptions[newCliProviderSelectedIndex]}", 0);
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(true), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
            }

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
                CCCPSettingsAdapter.SetUnityLang(unityLang);
                LogMessage($"Module language set to: {unityLangOptions[newunityLangSelectedIndex]}", 0);
            }
            GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(unityLang)), GUILayout.Width(20));
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
                    CCCPSettingsAdapter.SetClientDirectory(clientDirectory);
                    LogMessage($"Client path set to: {clientDirectory}", 1);
                }
            }
            GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(clientDirectory)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Module Language dropdown
            EditorGUILayout.BeginHorizontal();
            string serverLangTooltip = 
            "Rust: The default programming language for SpacetimeDB server modules. You need to install Rust in the Setup Window to be able to Publish.\n\n"+
            "C-Sharp: The C# programming language for SpacetimeDB server modules. You need to install the .NET SDK in the Setup Window to be able to Publish.\n\n"+
            "Recommended: Rust which can be up to 2x faster than C#. If you are more comfortable with C# it will work fine as well.";
            EditorGUILayout.LabelField(new GUIContent("Module Language:", serverLangTooltip), GUILayout.Width(110));
            string[] serverLangOptions = new string[] { "Rust", "C-Sharp"};
            string[] serverLangValues = new string[] { "rust", "csharp" };
            int serverLangSelectedIndex = Array.IndexOf(serverLangValues, serverLang);
            if (serverLangSelectedIndex < 0) serverLangSelectedIndex = 0; // Default to Rust if not found
            int newServerLangSelectedIndex = EditorGUILayout.Popup(serverLangSelectedIndex, serverLangOptions, GUILayout.Width(150));
            if (newServerLangSelectedIndex != serverLangSelectedIndex)
            {
                serverLang = serverLangValues[newServerLangSelectedIndex];
                CCCPSettingsAdapter.SetServerLang(serverLang);
                LogMessage($"Server language set to: {serverLangOptions[newServerLangSelectedIndex]}", 0);
            }
            GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(serverLang)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();            
            
            // Add New Module Entry
            EditorGUILayout.BeginHorizontal();
            string moduleSettingsTooltip = 
            "Set a new module name and path for your SpacetimeDB module.\n\n"+
            "Name: The name of your existing SpacetimeDB module you used when you created the module,\n"+
            "OR the name you want your SpacetimeDB module to have when initializing a new one.\n\n"+
            "Path: Directory of where Cargo.toml is located or to be created at.\n"+
            "Note: Create a new empty folder if the module has not been created yet.";            
            EditorGUILayout.LabelField(new GUIContent("Module New Entry:", moduleSettingsTooltip), GUILayout.Width(110));
            newModuleNameInput = EditorGUILayout.TextField(newModuleNameInput, GUILayout.Width(100));
            string serverDirButtonTooltip = "Current set path: " + (string.IsNullOrEmpty(serverDirectory) ? "Not Set" : serverDirectory);
            if (GUILayout.Button(new GUIContent("Add", serverDirButtonTooltip), GUILayout.Width(47), GUILayout.Height(20)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Module Path", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    serverDirectory = path;
                    serverManager.UpdateServerDetectionDirectory(serverDirectory);

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
                        serverManager.moduleName = moduleNameToAdd;
                        
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
            GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(serverDirectory) && !string.IsNullOrEmpty(moduleName)), GUILayout.Width(20));
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
            GUILayout.Label(ServerUtilityProvider.GetStatusIcon(selectedModuleIndex != -1), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Init a new module / Delete Selected Module
            EditorGUILayout.BeginHorizontal();
            bool deleteMode = Event.current.control && Event.current.alt;
            bool hasSelectedModule = selectedModuleIndex >= 0 && selectedModuleIndex < savedModules.Count;
            string buttonText = deleteMode && hasSelectedModule ? "Delete Selected Module" : "Init New Module";
            string baseTooltip = deleteMode && hasSelectedModule ? 
                "Delete Selected Module: Removes the currently selected saved module from the list." :
                "Init a new module: Initializes a new SpacetimeDB module with the selected name, path and language.";
            
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
                    CheckPrerequisites();
                    if (hasAllPrerequisites)
                    {
                        // initFirstModule is set to true in InitNewModule
                        InitNewModule();
                        EditorUtility.DisplayDialog("Module Initialized", "The new module has been initialized successfully.", "OK");
                    } else {
                        EditorUtility.DisplayDialog("Missing Prerequisites", "Please ensure all prerequisites are met and all necessary software has been installed in the Setup Window before initializing a new module.", "OK");
                    }
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            #endregion

            #region Local Mode (WSL or Docker)
            if (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)
            {
                // Local Server Settings
                string modeLabel = serverMode == ServerMode.WSLServer ? "WSL Server Settings" : "Docker Server Settings";
                GUILayout.Label(modeLabel, EditorStyles.centeredGreyMiniLabel);

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
                        CCCPSettingsAdapter.SetBackupDirectory(backupDirectory);
                        LogMessage($"Backup directory set to: {backupDirectory}", 1);
                    }
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(backupDirectory)), GUILayout.Width(20));
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
                    serverManager.userName = userName;
                    if (debugMode) LogMessage($"Debian username set to: {userName}", 0);
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(userName)), GUILayout.Width(20));
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
                    CCCPSettingsAdapter.SetServerUrl(serverUrl);
                    // Extract port from URL
                    int extractedPort = ServerUtilityProvider.ExtractPortFromUrl(serverUrl);
                    if (extractedPort > 0) // If a valid port is found
                    {
                        if (extractedPort != serverPort) // If the port is different from the current customServerPort
                        {
                            serverPort = extractedPort;
                            CCCPSettingsAdapter.SetServerPort(serverPort);

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
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(serverUrl)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();                
                
                // Auth Token setting
                EditorGUILayout.BeginHorizontal();
                string tokenTooltip = GetAuthTokenTooltip(AuthTokenType.WSL,
                "Required to modify the database and run reducers. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.");
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(authToken, GUILayout.Width(150));
                if (newAuthToken != authToken)
                {
                    authToken = newAuthToken;
                    CCCPSettingsAdapter.SetAuthToken(authToken);
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(authToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(3);

                if (GUILayout.Button("Server Setup Window"))
                        ServerSetupWindow.ShowWindow();
                if (GUILayout.Button("Check Pre-Requisites", GUILayout.Height(20)))
                    CheckPrerequisites();

                // Status display - show WSL/Docker status based on server mode
                EditorGUILayout.BeginHorizontal();
                string statusLabel = serverManager.CurrentServerMode == ServerManager.ServerMode.DockerServer ? "Docker:" : "WSL:";
                EditorGUILayout.LabelField(statusLabel, GUILayout.Width(110));
                Color originalStatusColor = connectedStyle.normal.textColor;
                
                bool serviceRunning = serverManager.CurrentServerMode == ServerManager.ServerMode.DockerServer ? isDockerRunning : isWslRunning;
                connectedStyle.normal.textColor = serviceRunning ? originalStatusColor : Color.gray;
                string statusText = serviceRunning ? "RUNNING" : "STOPPED";
                EditorGUILayout.LabelField(statusText, connectedStyle);
                // Restore the original color after using it
                connectedStyle.normal.textColor = originalStatusColor;
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
                    if (wslProcess == null)
                    {
                        wslProcess = new ServerWSLProcess(LogMessage, debugMode);
                    }
                    // Generate SSH key pair with default path and empty passphrase non-interactively
                    string sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
                    string defaultKeyPath = Path.Combine(sshDir, "id_ed25519");
                    // Create .ssh directory first, then generate the key pair
                    wslProcess.RunPowerShellCommand($"New-Item -ItemType Directory -Path '{sshDir}' -Force | Out-Null; ssh-keygen -t ed25519 -f '{defaultKeyPath}' -N '' -q", LogMessage);
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
                        CCCPSettingsAdapter.SetSSHPrivateKeyPath(sshPrivateKeyPath);
                        if (serverCustomProcess != null) // Update ServerCustomProcess if it exists
                        {
                            serverCustomProcess.SetPrivateKeyPath(sshPrivateKeyPath);
                        }
                        if (debugMode) LogMessage($"SSH Private Key Path set to: {sshPrivateKeyPath}", 0);
                    }
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(sshPrivateKeyPath) && System.IO.File.Exists(sshPrivateKeyPath)), GUILayout.Width(20));
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
                    CCCPSettingsAdapter.SetSSHUserName(sshUserName);

                    if (debugMode) LogMessage($"SSH username set to: {sshUserName}", 0);
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(sshUserName)), GUILayout.Width(20));
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
                    CCCPSettingsAdapter.SetCustomServerUrl(customServerUrl);

                    // Extract port from URL
                    int extractedPort = ServerUtilityProvider.ExtractPortFromUrl(customServerUrl);
                    if (extractedPort > 0) // If a valid port is found
                    {
                        if (extractedPort != customServerPort) // If the port is different from the current customServerPort
                        {
                            customServerPort = extractedPort;
                            CCCPSettingsAdapter.SetCustomServerPort(customServerPort);

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
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(customServerUrl)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();                

                // Custom Serer Auth Token
                EditorGUILayout.BeginHorizontal();
                string tokenTooltip = GetAuthTokenTooltip(AuthTokenType.Custom,
                "Required to modify the database and run reducers. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.");
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(customServerAuthToken, GUILayout.Width(150));
                if (newAuthToken != customServerAuthToken)
                {
                    customServerAuthToken = newAuthToken;
                    CCCPSettingsAdapter.SetCustomServerAuthToken(customServerAuthToken);
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(customServerAuthToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(3);

                if (GUILayout.Button("Server Setup Window"))
                    ServerSetupWindow.ShowCustomWindow();
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
                    if (wslProcess == null)
                    {
                        wslProcess = new ServerWSLProcess(LogMessage, debugMode);
                    }
                    LogoutAndLogin();
                }
                EditorGUILayout.EndHorizontal();                

                // Auth Token setting
                EditorGUILayout.BeginHorizontal();
                string tokenTooltip = GetAuthTokenTooltip(AuthTokenType.Maincloud,
                "Required to modify the database and run reducers. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.");
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(maincloudAuthToken, GUILayout.Width(150));
                if (newAuthToken != maincloudAuthToken)
                {
                    maincloudAuthToken = newAuthToken;
                    CCCPSettingsAdapter.SetMaincloudAuthToken(maincloudAuthToken);
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(maincloudAuthToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(3);

                if (GUILayout.Button("Launch Official Webpanel"))
                    Application.OpenURL("https://spacetimedb.com/login");
                if (GUILayout.Button("Check Pre-Requisites and Connect", GUILayout.Height(20)))
                    CheckPrerequisitesMaincloud();
                if (GUILayout.Button("Server Setup Window"))
                    ServerSetupWindow.ShowWindow();

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
        bool previousShowSettings = CCCPSettingsAdapter.GetShowSettings();
        bool showSettings = EditorGUILayout.Foldout(previousShowSettings, "Settings", true);
        
        // Handle mutually exclusive drawer state
        if (showSettings != previousShowSettings)
        {
            SetDrawerState(DrawerType.Settings, showSettings);
        }

        if (showSettings && serverManager != null)
        {   
            Color recommendedColor = Color.green;
            Color warningColor;
            ColorUtility.TryParseHtmlString("#FFA500", out warningColor); // Orange
            Color hiddenColor;
            ColorUtility.TryParseHtmlString("#808080", out hiddenColor); // Grey
            Color debugColor;
            ColorUtility.TryParseHtmlString("#30C099", out debugColor); // Cyan
                   
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
                serverManager.UpdateDetectServerChanges(newDetectChanges);
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
            "Automatic Publishing: The WSL CLI will automatically publish the module to your server when changes are detected. \n\n"+
            "Manual Publish: The WSL CLI will not automatically publish the module and will require manual publishing.";
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
                serverManager.autoPublishMode = newAutoPublish;
                autoPublishMode = newAutoPublish; // Keep local field in sync
                
                if (newAutoPublish && !serverManager.DetectServerChanges)
                {
                    serverManager.UpdateDetectServerChanges(true);
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
            "Publish will Generate: Publish button publishes the module and generates the client code. \n\n"+
            "Separate Generate: Separate generate button to generate the client code.\n\n"+
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
                serverManager.publishAndGenerateMode = newPublishGenerate;
                publishAndGenerateMode = newPublishGenerate; // Keep local field in sync
            }
            EditorGUILayout.EndHorizontal();

            // WSL Auto Close toggle
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            string wslCloseTooltip = 
            "Close WSL at Unity Quit: The WSL CLI will close WSL when Unity is closed. \n"+
            "Saves resources when WSL is not in use. WSL may otherwise leave several processes running.\n\n"+
            "Keep Running: The WSL CLI will keep the WSL process running after Unity is closed.\n\n"+
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
                serverManager.autoCloseWsl = newAutoClose;
                autoCloseWsl = newAutoClose; // Keep local field in sync
            }
            EditorGUILayout.EndHorizontal();

            // Clear Module and Database Log at Start toggle buttons
            if ((serverManager.SilentMode && (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)) || serverMode == ServerMode.CustomServer && serverMode != ServerMode.MaincloudServer)
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
                    serverManager.clearModuleLogAtStart = newClearModule;
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
                    serverManager.clearDatabaseLogAtStart = newClearDatabase;
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
                serverManager.debugMode = newDebugMode;
                debugMode = newDebugMode;
                
                // Update other components that need to know about debug mode
                ServerSetupWindow.debugMode = newDebugMode;
                ServerOutputWindow.debugMode = newDebugMode;
                ServerWindowInitializer.debugMode = newDebugMode;
                ServerUpdateProcess.debugMode = newDebugMode;
                ServerLogProcess.debugMode = newDebugMode;
                ServerWSLProcess.debugMode = newDebugMode;
                ServerDockerProcess.debugMode = newDebugMode;
                ServerCustomProcess.debugMode = newDebugMode;
                ServerDataWindow.debugMode = newDebugMode;
                ServerReducerWindow.debugMode = newDebugMode;
                ServerDetectionProcess.debugMode = newDebugMode;
                ServerSpacetimeSDKInstaller.debugMode = newDebugMode;
                CCCPSettings.debug = newDebugMode;
                CCCPSettingsProvider.debugMode = newDebugMode;
                CCCPSettingsAdapter.debugMode = newDebugMode;
            }
            
            // Debug: Refresh Settings Cache button - only show in debug mode
            if (debugMode)
            {
                if (GUILayout.Button("Refresh Settings", GUILayout.Width(100)))
                {
                    CCCPSettingsAdapter.RefreshSettingsCache();
                    CCCPSettings.RefreshInstance();
                    
                    // Reconfigure ServerManager with refreshed settings
                    if (serverManager != null)
                    {
                        serverManager.LoadSettings();
                        serverManager.Configure();
                    }
                    
                    // Reload the selected module
                    LoadSelectedModuleFromSettings();
                    
                    UnityEngine.Debug.Log($"[ServerWindow] Manual settings refresh. Module count: {savedModules?.Count ?? 0}");
                    Repaint();
                }
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
        
        // Early return if serverManager is not initialized
        if (serverManager == null)
        {
            EditorGUILayout.LabelField("Server manager not initialized", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
            return;
        }
        
        // Start or Stop Server button
        if (serverMode != ServerMode.MaincloudServer) // Maincloud is always running
        {
            serverRunning = serverManager.IsServerStarted || serverManager.IsStartingUp; // Update running state
            if (!serverManager.WslPrerequisitesChecked || !serverManager.HasAllPrerequisites)
            {
                if (GUILayout.Button("Check Pre-Requisites to Start SpacetimeDB", GUILayout.Height(30)))
                {
                    CheckPrerequisites();
                }
            }
            else // If Prerequisites are checked then show normal server controls
            {
                if (serverMode == ServerMode.WSLServer)
                {
                    if (!serverRunning)
                    {
                        if (GUILayout.Button("Start SpacetimeDB Local", GUILayout.Height(30)))
                        {
                            serverManager.StartServer();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Stop SpacetimeDB Local", GUILayout.Height(30)))
                        {
                            serverManager.StopServer();
                            CloseDatabaseAndReducerWindow();
                        }
                    }
                } else if (serverMode == ServerMode.DockerServer)
                {
                    if (!serverRunning)
                    {
                        if (GUILayout.Button("Start SpacetimeDB Docker", GUILayout.Height(30)))
                        {
                            serverManager.StartServer();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Stop SpacetimeDB Docker", GUILayout.Height(30)))
                        {
                            serverManager.StopServer();
                            CloseDatabaseAndReducerWindow();
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
                            CloseDatabaseAndReducerWindow();
                        }
                    }
                }
            }
        }

        // Activation of Server Windows
        bool WSLServerActive = serverManager.IsServerStarted && serverMode == ServerMode.WSLServer;
        bool WSLServerActiveSilent = serverManager.SilentMode && serverMode == ServerMode.WSLServer;
        bool dockerServerActive = serverManager.IsServerStarted && serverMode == ServerMode.DockerServer;
        bool dockerServerActiveSilent = serverManager.SilentMode && serverMode == ServerMode.DockerServer;
        bool customServerActive = serverManager.IsServerStarted && serverMode == ServerMode.CustomServer;
        bool customServerActiveSilent = serverMode == ServerMode.CustomServer;
        bool maincloudActive = serverManager.IsMaincloudConnected && serverMode == ServerMode.MaincloudServer;

        // Begin horizontal layout for the three buttons
        EditorGUILayout.BeginHorizontal();
               
        // View Logs
        EditorGUI.BeginDisabledGroup(!WSLServerActiveSilent && !dockerServerActiveSilent && !customServerActiveSilent && !maincloudActive);
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
                ServerUtilityProvider.CloseWindow<ServerOutputWindow>();
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
        EditorGUI.BeginDisabledGroup(!WSLServerActive && !dockerServerActive && !customServerActive && !maincloudActive);
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
                ServerUtilityProvider.CloseWindow<ServerDataWindow>();
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
        EditorGUI.BeginDisabledGroup(!WSLServerActive && !dockerServerActive && !customServerActive && !maincloudActive);
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
                ServerUtilityProvider.CloseWindow<ServerReducerWindow>();
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

        // SpacetimeDB version display
        GUIStyle versionStyle = new GUIStyle(EditorStyles.miniLabel);
        versionStyle.fontSize = 10;
        versionStyle.normal.textColor = new Color(0.43f, 0.43f, 0.43f);

        EditorGUILayout.LabelField("v", versionStyle, GUILayout.Width(10));
        if (serverMode == ServerMode.WSLServer)
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

        bool previousShowCommands = CCCPSettingsAdapter.GetShowCommands();
        bool showCommands = EditorGUILayout.Foldout(previousShowCommands, "Commands", true);
        
        // Handle mutually exclusive drawer state
        if (showCommands != previousShowCommands)
        {
            SetDrawerState(DrawerType.Commands, showCommands);
        }

        if (showCommands && serverManager != null)
        {
            EditorGUILayout.Space(-10);

            if (serverMode == ServerMode.WSLServer)
                EditorGUILayout.LabelField("SpacetimeDB Local", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));
            else if (serverMode == ServerMode.CustomServer)
                EditorGUILayout.LabelField("SpacetimeDB Remote", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));
            else if (serverMode == ServerMode.MaincloudServer)
                EditorGUILayout.LabelField("SpacetimeDB Local (Maincloud)", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            if (GUILayout.Button("Login", GUILayout.Height(20)))
            {
                if ((serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer) && CLIAvailableLocal()) 
                    serverManager.RunServerCommand("spacetime login", "Logging in to SpacetimeDB");
                #pragma warning disable CS4014 // Because this call is not awaited we disable the warning, it works anyhow
                else if (serverMode == ServerMode.CustomServer && CLIAvailableRemote()) 
                    serverCustomProcess.RunVisibleSSHCommand($"/home/{sshUserName}/.local/bin/spacetime login");
                #pragma warning restore CS4014
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL/Docker) or remote (SSH) and it is available.", -1);
            }

            if (GUILayout.Button("Logout", GUILayout.Height(20)))
            {
                if ((serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer) && CLIAvailableLocal()) 
                    serverManager.RunServerCommand("spacetime logout", "Logging out of SpacetimeDB");
                #pragma warning disable CS4014 // Because this call is not awaited we disable the warning, it works anyhow
                else if (serverMode == ServerMode.CustomServer && CLIAvailableRemote()) 
                    serverCustomProcess.RunVisibleSSHCommand($"/home/{sshUserName}/.local/bin/spacetime logout");
                #pragma warning restore CS4014
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL/Docker) or remote (SSH) and it is available.", -1);
            }

            if (GUILayout.Button("Show Login Info With Token", GUILayout.Height(20)))
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
            if (GUILayout.Button("Ping Server", GUILayout.Height(20)))
            {
                if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                serverManager.PingServer(true);
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL) or remote (SSH) and it is available.", -1);
            }

            if (GUILayout.Button("Show Version", GUILayout.Height(20)))
            {
                if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                serverManager.RunServerCommand("spacetime --version", "Showing SpacetimeDB version");
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL) or remote (SSH) and it is available.", -1);
            }

            // Service Status button (only in Custom Server mode)
            if (serverMode == ServerMode.CustomServer)
            {
                EditorGUILayout.LabelField("Custom Server Utility Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

                if (GUILayout.Button("Service Status", buttonStyle))
                {
                    if (CLIAvailableRemote())
                    CheckServiceStatus();
                    else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a remote (SSH) and it is available.", -1);
                }
                // Add a button which opens a cmd window with the ssh username
                if (GUILayout.Button("Open SSH Window", buttonStyle))
                {
                    serverManager.OpenSSHWindow();
                }
            }

            EditorGUILayout.LabelField("WSL Local Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            if (GUILayout.Button("Open Debian Window", GUILayout.Height(20)))
            {
                serverManager.OpenDebianWindow();
            }

            if (serverMode == ServerMode.WSLServer)
            {
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(serverManager.BackupDirectory));
                string backupTooltip = "Creates a tar archive of the DATA folder in your SpacetimeDB server, which contains the database, logs and settings of your module.\n\nRequires a Backup Directory to be set in Pre-Requisites.";
                if (GUILayout.Button(new GUIContent("Backup Server Data", backupTooltip), GUILayout.Height(20)))
                {
                    serverManager.BackupServerData();
                }
                string restoreTooltip = "Restores the DATA folder in your SpacetimeDB server from a backup tar archive.\n\nRequires a Backup Directory to be set in Pre-Requisites.";
                if (GUILayout.Button(new GUIContent("Restore Server Data", restoreTooltip), GUILayout.Height(20)))
                {
                    serverManager.RestoreServerData();
                }
                EditorGUI.EndDisabledGroup();

                string clearTooltip = "WARNING: This will clear your server database and all its contents will be lost. Please do a backup before if you wish to be able to restore the current database.";
                if (GUILayout.Button(new GUIContent("Clear Server Data", clearTooltip), GUILayout.Height(20)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Clear Server Data",
                        "WARNING: This will clear your server database and all its contents will be lost. Please do a backup before if you wish to be able to restore the current database.\n\n"+
                        "This can be helpful if you want to start fresh or have a new user claim ownership of the database.\n\n"+
                        "Do you wish to continue to clear the current database? Remember to do a new publish afterwards.",
                        "Clear Database",
                        "Cancel"))
                    {
                        serverManager.StopServer();
                        CloseDatabaseAndReducerWindow();
                        serverManager.ClearServerData();
                    }
                }
            }

            if (debugMode && serverMode == ServerMode.WSLServer)
            {
                if (GUILayout.Button("Test Server Running", GUILayout.Height(20)))
                {
                    if (wslProcess == null)
                    {
                        wslProcess = new ServerWSLProcess(LogMessage, debugMode);
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

        if (publishFirstModule)
        {
            GUIStyle firstModuleStyle = new GUIStyle(EditorStyles.boldLabel);
            firstModuleStyle.normal.textColor = Color.green;
            firstModuleStyle.hover.textColor = new Color(0.0f, 0.8f, 0.0f); // Darker green on hover
            firstModuleStyle.fontStyle = FontStyle.Bold;
            firstModuleStyle.alignment = TextAnchor.MiddleCenter;

            string displayText = "Ready to Publish Server Module! \n Initial compiling may take a minute.";
            string tooltip = "You have all necessary prerequisites to publish a module. \nClick to dismiss this notification.";

            // Create a button-like appearance
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // Use a button that looks like a label for better click response
            if (GUILayout.Button(new GUIContent(displayText, tooltip), firstModuleStyle))
            {
                publishFirstModule = false;
                CCCPSettingsAdapter.SetPublishFirstModule(false);
                Repaint();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        
        // Centered Grey Mini Label for Publish/Generate instructions
        if (serverManager.PublishAndGenerateMode) {
            if (!serverManager.AutoPublishMode) {
                EditorGUILayout.LabelField("Will Publish then Generate client code automatically.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(20));
            } else {
                EditorGUILayout.LabelField("Will Publish then Generate client code automatically on detected changes.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(20));
            }
        } else {
            EditorGUILayout.LabelField("First Publish then Generate client code.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(20));
        }

        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(serverManager.ModuleName));
        
        string editModuleTooltip = "Edit the module script (lib.rs or lib.cs) of the selected module.";
        if (GUILayout.Button(new GUIContent("Edit Module", editModuleTooltip), GUILayout.Height(20)))
        {
            // Open the module script in the default editor
            string modulePathRs = Path.Combine(serverDirectory, "src", "lib.rs");
            string modulePathCs = Path.Combine(serverDirectory, "lib.cs");
            if (File.Exists(modulePathRs))
            {
                Process.Start(modulePathRs);
            }
            else if (File.Exists(modulePathCs))
            {
                Process.Start(modulePathCs);
            }
            else
            {
                LogMessage("Module script not found", -2);
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
        if (serverManager.ServerChangesDetected || publishFirstModule)
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
            publishButtonStyle.normal.textColor = Color.green;
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
                    serverManager.Publish(true); // Publish with a database reset
                }
            }
            else
            {
                if (!serverRunning)
                {
                    serverManager.StartServer();
                }
                serverManager.Publish(false); // Publish without a database reset
                publishFirstModule = false;
                CCCPSettingsAdapter.SetPublishFirstModule(false);
            }
        }
        
        // Add Generate Unity Files button
        if (!serverManager.PublishAndGenerateMode)
        {
            if (GUILayout.Button("Generate Client Code", GUILayout.Height(30)))
            {
                string outDir = ServerUtilityProvider.GetRelativeClientPath(serverManager.ClientDirectory);
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
        // Ensure wslProcess is initialized before use
        if (wslProcess == null)
        {
            wslProcess = new ServerWSLProcess(LogMessage, debugMode);
        }
        
        wslProcess.CheckPrerequisites((wsl, debian, trixie, curl, spacetime, spacetimePath, rust, spacetimeService, spacetimeLogsService, binaryen, git, netSdk) => {
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
                hasGit = git;
                hasNETSDK = netSdk;
                wslPrerequisitesChecked = true;
                
                // Load userName value 
                userName = serverManager.UserName;
                
                Repaint();
                
                bool essentialSoftware = 
                    wsl && debian && trixie && curl && 
                    spacetime && spacetimePath && spacetimeService && git && (rust || netSdk);

                bool essentialUserSettings = 
                    !string.IsNullOrEmpty(userName) &&
                    !string.IsNullOrEmpty(serverDirectory) &&
                    !string.IsNullOrEmpty(clientDirectory) &&
                    !string.IsNullOrEmpty(moduleName) &&
                    !string.IsNullOrEmpty(serverLang);

                List<string> missingSoftware = new List<string>();
                if (!wsl) missingSoftware.Add("- WSL");
                if (!debian) missingSoftware.Add("- Debian");
                if (!trixie) missingSoftware.Add("- Debian Trixie Update");
                if (!spacetime) missingSoftware.Add("- SpacetimeDB Server");
                if (!spacetimePath) missingSoftware.Add("- SpacetimeDB Path");
                if (!spacetimeService) missingSoftware.Add("- SpacetimeDB Service");
                if (!rust && !netSdk) missingSoftware.Add("- Either Rust or .Net (C#)");
                if (!git) missingSoftware.Add("- Git");

                List<string> missingUserSettings = new List<string>();
                if (string.IsNullOrEmpty(userName)) missingUserSettings.Add("- Debian Username");
                if (string.IsNullOrEmpty(serverDirectory)) missingUserSettings.Add("- Server Directory");
                if (string.IsNullOrEmpty(clientDirectory)) missingUserSettings.Add("- Client Directory");
                if (string.IsNullOrEmpty(moduleName)) missingUserSettings.Add("- Server Module");
                if (string.IsNullOrEmpty(serverLang)) missingUserSettings.Add("- Server Language");

                if (!essentialSoftware || !essentialUserSettings)
                {
                    bool needsInstallation = EditorUtility.DisplayDialog(
                        "Missing Software", 
                        "You are missing some essential software and/or settings to run SpacetimeDB.\n" +
                        "Please install the following Software:\n" +
                        string.Join("\n", missingSoftware) + "\n" +
                        "Please set the following Pre-Requisites:\n" +
                        string.Join("\n", missingUserSettings),
                        "Server Setup Window", "Pre-Requisites"
                    );
                    if (needsInstallation)
                    {
                        ServerSetupWindow.ShowWindow();
                    }
                    else 
                    {
                        // Open the pre-requisites drawer
                        SetDrawerState(DrawerType.Prerequisites, true);
                        Repaint();
                    }

                    hasAllPrerequisites = false;
                    CCCPSettingsAdapter.SetHasAllPrerequisites(hasAllPrerequisites);
                }
                else if (essentialSoftware && essentialUserSettings)
                {
                    bool initModuleAndLogout = EditorUtility.DisplayDialog(
                        "All Software Installed", 
                        "You have everything necessary to run SpacetimeDB! \n\n" +
                        "Please proceed to Init New Module (if using a new module)\n\n" +
                        "Recommended: Logout and then Login again to switch to an online Login which is safer.\n\n" +
                        "Remember to copy your auth token to the Pre-Requisites section after your first publish to enable all functionality.",
                        "Init Module and Refresh Login", "Only Refresh Login"
                    );
                    if (initModuleAndLogout)
                    {
                        InitNewModule();
                        // Also logout and login again to ensure a safe online login
                        LogoutAndLogin();
                    } 
                    else
                    {
                        // Only logout and login again to ensure a safe online login
                        LogoutAndLogin();
                    }
                    hasAllPrerequisites = true;
                    CCCPSettingsAdapter.SetHasAllPrerequisites(hasAllPrerequisites);

                    publishFirstModule = true;
                    CCCPSettingsAdapter.SetPublishFirstModule(publishFirstModule);
                }
                // After writing module name this will appear (when essential software and user settings)
                else if (essentialSoftware && essentialUserSettings && !initializedFirstModule)
                {
                    EditorUtility.DisplayDialog(
                        "Initialize First Module",
                        "All requirements met to Initialize new server module. Please do this now and then Publish Module.\n" +
                        "You can do this in Pre-Requisites > Shared Settings of the Main Window",
                        "OK"
                    );
                }
                else if (essentialSoftware && essentialUserSettings && initializedFirstModule && publishFirstModule)
                {
                    EditorUtility.DisplayDialog(
                        "Ready to Publish", 
                        "You are now ready to publish and start SpacetimeDB for the first time.\n" +
                        "Please copy your token to the Pre-Requisites section to enable all functionality after your first publish.",
                        "OK"
                    );
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
            LogMessage("Server is not reachable. Please check if the server is online at the URL and if your credentials are valid.", -1);
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
    
    public void ForceSSHLogContinuation()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerWindow] Force triggering SSH log continuation after compilation");
        if (serverManager != null && serverManager.CurrentServerMode == ServerManager.ServerMode.CustomServer)
        {
            serverManager.ForceSSHLogContinuation();
        }
    }

    public void ForceWSLLogRefresh()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerWindow] Force triggering WSL log refresh");
        if (serverManager != null && serverManager.CurrentServerMode == ServerManager.ServerMode.WSLServer)
        {
            serverManager.ForceWSLLogRefresh();
        }
    }

    public async void LogoutAndLogin()
    {
        if (serverManager == null)
        {
            LogMessage("[LogoutAndLogin] Server Manager not initialized. Please restart Unity.", -1);
            return;
        }
        if (serverRunning)
        {
            serverManager.StopServer();
        }
        await Task.Delay(2000); // Wait for server to stop
        serverManager.RunServerCommand("spacetime logout", "Logging out to clear possible offline local login...");
        await Task.Delay(1000); // Wait for logout to complete
        serverManager.RunServerCommand("spacetime login", "Launching official SpacetimeDB SEO online login...");
        if (!serverRunning)
        {
            serverManager.StartServer();
        }
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
        if (debugMode) UnityEngine.Debug.Log($"[LogMessage] Received message: '{message}' (length: {message?.Length ?? 0}, trimmed length: {message?.Trim()?.Length ?? 0}, style: {style})");
        
        // Filter out messages that are too short (prevents "T" messages and other truncated output)
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length < 3)
        {
            if (debugMode) UnityEngine.Debug.Log($"[LogMessage] Filtered out short message: '{message}' (length: {message?.Length ?? 0}, style: {style})");
            return;
        }
        
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
        
        // Trim log if it gets too long, but preserve message boundaries
        if (commandOutputLog.Length > 10000)
        {
            string truncated = commandOutputLog.Substring(commandOutputLog.Length - 10000);
            
            // Find the first complete line (after the first newline) to avoid partial messages
            int firstNewlineIndex = truncated.IndexOf('\n');
            if (firstNewlineIndex >= 0 && firstNewlineIndex < truncated.Length - 1)
            {
                commandOutputLog = truncated.Substring(firstNewlineIndex + 1);
            }
            else
            {
                // Fallback to original behavior if no newline found
                commandOutputLog = truncated;
            }
        }
        
        // Set flag to scroll to bottom if autoscroll is enabled
        if (autoscroll)
        {
            needsScrollToBottom = true;
        }
        
        // Use EditorApplication.delayCall to ensure Repaint runs on main thread
        EditorApplication.delayCall += Repaint;
    }
    #endregion
    
    #region Utility Methods

    /// <summary>
    /// Manages mutually exclusive drawer states. When one drawer is set to true, others are automatically set to false.
    /// </summary>
    /// <param name="drawerType">The drawer type to set</param>
    /// <param name="value">True to open the drawer, false to close it</param>
    private void SetDrawerState(DrawerType drawerType, bool value)
    {
        if (value)
        {
            // Close all other drawers when opening one
            switch (drawerType)
            {
                case DrawerType.Prerequisites:
                    CCCPSettingsAdapter.SetShowPrerequisites(true);
                    CCCPSettingsAdapter.SetShowSettings(false);
                    CCCPSettingsAdapter.SetShowCommands(false);
                    break;
                case DrawerType.Settings:
                    CCCPSettingsAdapter.SetShowPrerequisites(false);
                    CCCPSettingsAdapter.SetShowSettings(true);
                    CCCPSettingsAdapter.SetShowCommands(false);
                    break;
                case DrawerType.Commands:
                    CCCPSettingsAdapter.SetShowPrerequisites(false);
                    CCCPSettingsAdapter.SetShowSettings(false);
                    CCCPSettingsAdapter.SetShowCommands(true);
                    break;
            }
        }
        else
        {
            // Just close the specified drawer
            switch (drawerType)
            {
                case DrawerType.Prerequisites:
                    CCCPSettingsAdapter.SetShowPrerequisites(false);
                    break;
                case DrawerType.Settings:
                    CCCPSettingsAdapter.SetShowSettings(false);
                    break;
                case DrawerType.Commands:
                    CCCPSettingsAdapter.SetShowCommands(false);
                    break;
            }
        }
    }

    // Helper method to format auth token tooltip with first and last 20 characters
    private string GetAuthTokenTooltip(AuthTokenType tokenType, string baseTooltip)
    {
        string storedToken = "";
        switch (tokenType)
        {
            case AuthTokenType.WSL:
                storedToken = CCCPSettingsAdapter.GetAuthToken();
                break;
            case AuthTokenType.Custom:
                storedToken = CCCPSettingsAdapter.GetCustomServerAuthToken();
                break;
            case AuthTokenType.Maincloud:
                storedToken = CCCPSettingsAdapter.GetMaincloudAuthToken();
                break;
        }

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

    private void CloseDatabaseAndReducerWindow()
    {
        ServerUtilityProvider.CloseWindow<ServerDataWindow>();
        ServerUtilityProvider.CloseWindow<ServerReducerWindow>();
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

    private void UpdateServerModeState()
    {       
        // Ensure serverManager is initialized before proceeding
        if (serverManager == null)
        {
            if (debugMode) LogMessage("ServerManager not initialized, skipping mode state update", -1);
            return;
        }
        
        // Only run set-default commands when there's an actual mode transition
        if (previousServerMode != serverMode)
        {
            if (debugMode) LogMessage($"Server mode transition: {previousServerMode} -> {serverMode}", 0);
            
            // Reset server status when switching modes to prevent false positives
            serverManager.ResetServerStatusOnModeChange();
            if (debugMode) LogMessage("Server status reset for mode change", 0);
            
            // Determine if we need to run a set-default command based on the transition
            bool needsMaincloudSetDefault = (previousServerMode == ServerMode.WSLServer || previousServerMode == ServerMode.DockerServer || previousServerMode == ServerMode.CustomServer) && serverMode == ServerMode.MaincloudServer;
            bool needsLocalSetDefault = previousServerMode == ServerMode.MaincloudServer && (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer || serverMode == ServerMode.CustomServer);
            
            if (needsMaincloudSetDefault)
            {
                if (debugMode) LogMessage("Setting SpacetimeDB CLI default to maincloud", 0);
                serverManager.RunServerCommand("spacetime server set-default maincloud", "Configuring SpacetimeDB CLI for Maincloud");
            }
            else if (needsLocalSetDefault)
            {
                if (debugMode) LogMessage("Setting SpacetimeDB CLI default to local", 0);
                serverManager.RunServerCommand("spacetime server set-default local", "Configuring SpacetimeDB CLI for Local/Custom server");
            }
            else
            {
                // Transitioning between WSL, Docker and Custom modes doesn't require set-default changes
                if (debugMode) LogMessage("No set-default command needed for this transition", 0);
            }
            
            // Update previous mode tracking
            previousServerMode = serverMode;
        }
        
        // Handle mode-specific setup
        switch (serverMode)
        {
            case ServerMode.WSLServer:
                if (debugMode) LogMessage("Server mode: WSL Local", 0);
                EditorUpdateHandler();
                break;
            case ServerMode.CustomServer:
                if (debugMode) LogMessage("Server mode: Custom Remote", 0);
                EditorUpdateHandler();
                if (serverCustomProcess == null)
                {
                    serverCustomProcess = new ServerCustomProcess(LogMessage, debugMode);
                    serverCustomProcess.LoadSettings();
                }
                break;
            case ServerMode.MaincloudServer:
                if (debugMode) LogMessage("Server mode: Maincloud", 0);
                EditorUpdateHandler();
                break;
        }

        // Update ServerManager with the new mode
        serverManager.SetServerMode((ServerManager.ServerMode)serverMode);

        // Use string representation for consistency with ServerManager
        CCCPSettingsAdapter.SetServerMode((ServerManager.ServerMode)serverMode);
        
        // Update ServerOutputWindow tab visibility if window is open
        ServerOutputWindow.UpdateTabVisibilityForServerMode(serverMode.ToString());
        
        EditorApplication.delayCall += Repaint;
    }

    private void LoadServerModeFromSettings()
    {
        // Load server mode from settings using string representation for compatibility with ServerManager
        string modeName = CCCPSettingsAdapter.GetServerMode().ToString();
        if (Enum.TryParse(modeName, out ServerMode mode))
        {
            serverMode = mode;
            previousServerMode = mode; // Initialize previous mode to current mode
        }
        else
        {
            UnityEngine.Debug.Log($"Unknown server mode in preferences: {modeName}. Defaulting to WSLServer.");
            serverMode = ServerMode.WSLServer;
            previousServerMode = ServerMode.WSLServer; // Initialize previous mode to default
        }
        
        // Only update server mode state if serverManager is initialized
        if (serverManager != null)
        {
            UpdateServerModeState();
        }
    }

    private void LoadSelectedModuleFromSettings()
    {
        // Load the selected module if one is saved in settings
        if (selectedModuleIndex >= 0 && selectedModuleIndex < savedModules.Count)
        {
            if (debugMode)
                UnityEngine.Debug.Log($"[ServerWindow] Loading selected module: index {selectedModuleIndex}, module count: {savedModules.Count}");
            
            SelectSavedModule(selectedModuleIndex);
        }
        else if (debugMode)
        {
            UnityEngine.Debug.Log($"[ServerWindow] No valid selected module to load. Index: {selectedModuleIndex}, Count: {savedModules.Count}");
        }
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
                    CCCPSettingsAdapter.SetAutoscroll(autoscroll);
                    EditorApplication.delayCall += Repaint;
                }
                else if (!autoscroll && isAtBottom)
                {
                    // User scrolled to the bottom while autoscroll was off - turn it on
                    autoscroll = true;
                    CCCPSettingsAdapter.SetAutoscroll(autoscroll);
                    EditorApplication.delayCall += Repaint;
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
        }
    }

    // Async helper for Test Server Running button
    private async void TestServerRunningAsync()
    {
        if (wslProcess == null)
        {
            wslProcess = new ServerWSLProcess(LogMessage, debugMode);
        }
        bool isRunning = await wslProcess.CheckServerRunning(instantCheck: true);
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
        viewLogsWindowOpen = ServerUtilityProvider.IsWindowOpen<ServerOutputWindow>();
        browseDbWindowOpen = ServerUtilityProvider.IsWindowOpen<ServerDataWindow>();
        runReducerWindowOpen = ServerUtilityProvider.IsWindowOpen<ServerReducerWindow>();
        
        // Persist the state
        SessionState.SetBool(SessionKeyBrowseDbOpen, browseDbWindowOpen);
        SessionState.SetBool(SessionKeyRunReducerOpen, runReducerWindowOpen);
    }

    #endregion

    #region Module Methods

    private void InitNewModule()
    {
        serverManager.InitNewModule();
        initializedFirstModule = true;
        publishFirstModule = true;
        CCCPSettingsAdapter.SetInitializedFirstModule(true);
        CCCPSettingsAdapter.SetPublishFirstModule(publishFirstModule);
    }

    private void SaveModulesList()
    {
        try
        {
            CCCPSettingsAdapter.SetSavedModules(savedModules);
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"Error saving modules list: {ex.Message}");
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
                savedModules[i] = new ModuleInfo { name = name, path = path };
                SaveModulesList();
                return i;
            }
        }

        // Add new module
        savedModules.Add(new ModuleInfo { name = name, path = path });
        SaveModulesList();
        return savedModules.Count - 1;
    }

    private void SelectSavedModule(int index)
    {
        if (index >= 0 && index < savedModules.Count)
        {
            var module = savedModules[index];
            bool isModuleChange = selectedModuleIndex != index || moduleName != module.name || serverDirectory != module.path;
            
            selectedModuleIndex = index;
            
            // Update current settings
            moduleName = module.name;
            serverDirectory = module.path;
            
            // Update ServerManager
            serverManager.moduleName = moduleName;
            serverManager.UpdateServerDetectionDirectory(serverDirectory);
            CCCPSettingsAdapter.SetSelectedModuleIndex(index);

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

            // Only log when there's an actual change in module selection
            if (isModuleChange)
            {
                LogMessage($"Selected module: {module.name} at {module.path}", 1);
            }
        }
    }
    #endregion

    // Public method to get ServerManager for external access
    public ServerManager GetServerManager()
    {
        return serverManager;
    }

    // Display Cosmos Cove Control Panel title text in the menu bar
    [MenuItem("Window/SpacetimeDB Server Manager/- Cosmos Cove Control Panel -")]
    private static void CosmosCoveControlPanel(){}
    [MenuItem("Window/SpacetimeDB Server Manager/- Cosmos Cove Control Panel -", true)]
    private static bool ValidateCosmosCoveControlPanel(){return false;}

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games