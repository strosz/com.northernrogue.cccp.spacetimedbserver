using UnityEngine;
using UnityEditor;
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
    private ServerDockerProcess dockerProcess;
    
    // Server mode
    private ServerMode serverMode = ServerMode.DockerServer;
    private ServerMode previousServerMode = ServerMode.DockerServer;
    private string localCLIProvider { get => CCCPSettingsAdapter.GetLocalCLIProvider(); set => CCCPSettingsAdapter.SetLocalCLIProvider(value); }

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

    // Pre-requisites Docker - Direct property access to settings
    private bool hasDocker { get => CCCPSettingsAdapter.GetHasDocker(); set => CCCPSettingsAdapter.SetHasDocker(value); }
    private bool hasDockerCompose { get => CCCPSettingsAdapter.GetHasDockerCompose(); set => CCCPSettingsAdapter.SetHasDockerCompose(value); }
    private bool hasDockerImage { get => CCCPSettingsAdapter.GetHasDockerImage(); set => CCCPSettingsAdapter.SetHasDockerImage(value); }
    private bool hasDockerContainerMounts { get => CCCPSettingsAdapter.GetHasDockerContainerMounts(); set => CCCPSettingsAdapter.SetHasDockerContainerMounts(value); }

    // Pre-requisites General - Direct property access to settings
    private bool initializedFirstModule { get => CCCPSettingsAdapter.GetInitializedFirstModule(); set => CCCPSettingsAdapter.SetInitializedFirstModule(value); }
    private bool publishFirstModule { get => CCCPSettingsAdapter.GetPublishFirstModule(); set => CCCPSettingsAdapter.SetPublishFirstModule(value); }
    private bool hasSpacetimeDBUnitySDK { get => CCCPSettingsAdapter.GetHasSpacetimeDBUnitySDK(); set => CCCPSettingsAdapter.SetHasSpacetimeDBUnitySDK(value); }
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

    // Docker Server Configuration - Direct property access to settings
    private string serverUrlDocker { get => CCCPSettingsAdapter.GetServerUrlDocker(); set => CCCPSettingsAdapter.SetServerUrlDocker(value); }
    private int serverPortDocker { get => CCCPSettingsAdapter.GetServerPortDocker(); set => CCCPSettingsAdapter.SetServerPortDocker(value); }
    private string authTokenDocker { get => CCCPSettingsAdapter.GetAuthTokenDocker(); set => CCCPSettingsAdapter.SetAuthTokenDocker(value); }

    // Pre-requisites Custom Server - Direct property access to settings
    private string sshUserName { get => CCCPSettingsAdapter.GetSSHUserName(); set => CCCPSettingsAdapter.SetSSHUserName(value); }
    private string customServerUrl { get => CCCPSettingsAdapter.GetCustomServerUrl(); set => CCCPSettingsAdapter.SetCustomServerUrl(value); }
    private int customServerPort { get => CCCPSettingsAdapter.GetCustomServerPort(); set => CCCPSettingsAdapter.SetCustomServerPort(value); }
    private string customServerAuthToken { get => CCCPSettingsAdapter.GetCustomServerAuthToken(); set => CCCPSettingsAdapter.SetCustomServerAuthToken(value); }
    private string sshPrivateKeyPath { get => CCCPSettingsAdapter.GetSSHPrivateKeyPath(); set => CCCPSettingsAdapter.SetSSHPrivateKeyPath(value); }
    private bool isConnectedCustomSSH;

    // Pre-requisites Maincloud Server - Direct property access to settings
    private string maincloudAuthToken { get => CCCPSettingsAdapter.GetMaincloudAuthToken(); set => CCCPSettingsAdapter.SetMaincloudAuthToken(value); }
   
    // Server status
    private double lastCheckTime = 0;
    private const double checkInterval = 5.0; // Master interval for status checks
    private bool serverRunning = false;
    private bool previousServerRunning = false; // Track previous state to detect server start
    
    // Identity check guard to prevent concurrent executions
    private bool isCheckingIdentity = false;

    // Server Settings - Direct property access to settings
    public bool debugMode { get => CCCPSettingsAdapter.GetDebugMode(); set => CCCPSettingsAdapter.SetDebugMode(value); }
    private bool hideWarnings { get => CCCPSettingsAdapter.GetHideWarnings(); set => CCCPSettingsAdapter.SetHideWarnings(value); }
    private bool detectServerChanges { get => CCCPSettingsAdapter.GetDetectServerChanges(); set => CCCPSettingsAdapter.SetDetectServerChanges(value); }
    private bool serverChangesDetected { get => CCCPSettingsAdapter.GetServerChangesDetected(); set => CCCPSettingsAdapter.SetServerChangesDetected(value); }
    private bool autoPublishMode { get => CCCPSettingsAdapter.GetAutoPublishMode(); set => CCCPSettingsAdapter.SetAutoPublishMode(value); }
    private bool publishAndGenerateMode { get => CCCPSettingsAdapter.GetPublishAndGenerateMode(); set => CCCPSettingsAdapter.SetPublishAndGenerateMode(value); }
    private bool silentMode { get => CCCPSettingsAdapter.GetSilentMode(); set => CCCPSettingsAdapter.SetSilentMode(value); }
    private bool autoCloseCLI { get => CCCPSettingsAdapter.GetAutoCloseCLI(); set => CCCPSettingsAdapter.SetAutoCloseCLI(value); }
    private bool clearModuleLogAtStart { get => CCCPSettingsAdapter.GetClearModuleLogAtStart(); set => CCCPSettingsAdapter.SetClearModuleLogAtStart(value); }
    private bool clearDatabaseLogAtStart { get => CCCPSettingsAdapter.GetClearDatabaseLogAtStart(); set => CCCPSettingsAdapter.SetClearDatabaseLogAtStart(value); }
    private bool devMode { get => CCCPSettingsAdapter.GetDevMode(); set => CCCPSettingsAdapter.SetDevMode(value); }

    // Update SpacetimeDB - Direct property access to settings
    private string spacetimeDBCurrentVersionWSL { get => CCCPSettingsAdapter.GetSpacetimeDBCurrentVersionWSL(); set => CCCPSettingsAdapter.SetSpacetimeDBCurrentVersionWSL(value); }
    private string spacetimeDBCurrentVersionDocker { get => CCCPSettingsAdapter.GetSpacetimeDBCurrentVersionDocker(); set => CCCPSettingsAdapter.SetSpacetimeDBCurrentVersionDocker(value); }
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
    private Texture2D logoTextureColor;
    private Texture2D logoTextureMono;
    private GUIStyle connectedStyle;
    private GUIStyle buttonStyle;
    private GUIStyle subTitleStyle;
    private GUIStyle titleControlStyle;
    private GUIStyle autoscrollStyle;
    private GUIStyle clearStyle;
    private GUIStyle richTextStyle;
    private GUIStyle updateButtonStyle;
    private GUIStyle dismissButtonStyle;
    private GUIContent versionButtonContent;
    private GUIContent autoscrollButtonContent;
    private GUIContent clearButtonContent;
    private GUIContent trimmedLogContent;
    private GUIStyle activeToolbarButton;
    private GUIStyle inactiveToolbarButton;
    private GUIStyle customWindowStyle;
    private GUIStyle moduleInitButtonStyle;
    private GUIStyle titleStyle;
    private GUIContent emptyContent;
    
    // Cached GUILayoutOption arrays to prevent per-frame allocations
    private GUILayoutOption[] width110;
    private GUILayoutOption[] width75;
    private GUILayoutOption[] width47;
    private GUILayoutOption[] width35;
    private GUILayoutOption[] width25;
    private GUILayoutOption[] width20;
    private GUILayoutOption[] height20;
    private GUILayoutOption[] height10;
    private GUILayoutOption[] height1_7;
    private GUILayoutOption[] expandWidth;
    private GUILayoutOption[] expandHeight;
    private GUILayoutOption[] width25Height;
    private GUILayoutOption[] width47Height20;
    
    private bool stylesInitialized = false;    // UI optimization
    private const double statusUICheckInterval = 3.0; // More responsive interval when window is in focus
    private bool windowFocused = false;
    private bool previousDevMode = false; // Track previous dev mode state for change detection
    
    // Window toggle states
    private bool viewLogsWindowOpen = false;
    private bool browseDbWindowOpen = false;
    private bool runReducerWindowOpen = false;
    
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
        Docker,
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
        
        // Update window states to detect when child windows are closed
        UpdateWindowStates();

        EditorGUILayout.BeginVertical();
               
        // Display the logo image (loaded once during initialization)
        logoTexture = colorLogo ? logoTextureColor : logoTextureMono;

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

            // Logo version and color control (using cached GUIContent and GUIStyle)
            if (GUILayout.Button(versionButtonContent, titleControlStyle))
            {
                colorLogo = !colorLogo;
                CCCPSettingsAdapter.SetColorLogo(colorLogo);
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            // Fallback if image not found
            GUILayout.Label("SpacetimeDB Server Management", titleStyle);
            GUILayout.Label("Control your SpacetimeDB server and run commands.\n If starting fresh check the pre-requisites first.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space();
        }
        
        DrawPrerequisitesSection();
        EditorGUILayout.Space(5);
        
        DrawSettingsSection();
        EditorGUILayout.Space(5);

        DrawServerControlSection();
        EditorGUILayout.Space(5);
        
        DrawCommandsSection();
        EditorGUILayout.Space(5);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Command Output:", EditorStyles.boldLabel);

        GUILayout.FlexibleSpace();
        EditorGUILayout.Space(-70);

        // Autoscroll button
        EditorGUILayout.BeginVertical();
        EditorGUILayout.Space(2);
        autoscrollStyle.normal.textColor = autoscroll ? ServerUtilityProvider.ColorManager.AutoscrollEnabled : ServerUtilityProvider.ColorManager.AutoscrollDisabled;
        autoscrollStyle.hover.textColor = autoscrollStyle.normal.textColor; // Explicitly define hover textColor        
        if (GUILayout.Button(autoscrollButtonContent, autoscrollStyle, width75))
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

        EditorGUILayout.Space(5);

        // Clear button
        EditorGUILayout.BeginVertical();
        EditorGUILayout.Space(2);
        if (GUILayout.Button(clearButtonContent, clearStyle, width35))
        {
            commandOutputLog = "";
            Repaint();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        
        // Output log with rich text support (using cached style and content)
        // Store previous scroll position to detect user scrolling
        Vector2 previousScrollPosition = scrollPosition;        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        EditorGUILayout.TextArea(commandOutputLog, richTextStyle, GUILayout.ExpandHeight(true));
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
            
            // Update cached style colors based on the state
            Color targetColor = isUpdatingCCCP ? ServerUtilityProvider.ColorManager.Processing : ServerUtilityProvider.ColorManager.ButtonText;
            updateButtonStyle.normal.textColor = targetColor;
            updateButtonStyle.hover.textColor = targetColor;
            updateButtonStyle.active.textColor = targetColor;
            updateButtonStyle.focused.textColor = targetColor;
            
            // Create horizontal layout for update button and dismiss button
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent(buttonText, "Update CCCP Package"), updateButtonStyle))
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
            if (GUILayout.Button(new GUIContent("✕", "Dismiss Update Notification"), dismissButtonStyle, GUILayout.Width(25)))
            {
                SessionState.SetBool("CCCPUpdateMessageDismissed", true);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }
    
    private void InitializeStyles()
    {
        // Load logo textures once
        logoTextureColor = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.northernrogue.cccp.spacetimedbserver/Editor/cosmos_logo_azure.png");
        logoTextureMono = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.northernrogue.cccp.spacetimedbserver/Editor/cosmos_logo.png");
        
        // For all connection status labels
        connectedStyle = new GUIStyle(EditorStyles.label);
        connectedStyle.fontSize = 11;
        connectedStyle.normal.textColor = ServerUtilityProvider.ColorManager.ConnectedText;
        connectedStyle.fontStyle = FontStyle.Bold;
        
        // Create custom button style with white text
        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.normal.textColor = ServerUtilityProvider.ColorManager.ButtonText;
        buttonStyle.hover.textColor = ServerUtilityProvider.ColorManager.ButtonText;
        buttonStyle.active.textColor = ServerUtilityProvider.ColorManager.ButtonText;
        buttonStyle.focused.textColor = ServerUtilityProvider.ColorManager.ButtonText;
        buttonStyle.fontSize = 12;
        buttonStyle.fontStyle = FontStyle.Normal;
        buttonStyle.alignment = TextAnchor.MiddleCenter;
        
        // Subtitle style
        subTitleStyle = new GUIStyle(EditorStyles.label);
        subTitleStyle.fontSize = 10;
        subTitleStyle.normal.textColor = ServerUtilityProvider.ColorManager.SubtitleText;
        subTitleStyle.hover.textColor = ServerUtilityProvider.ColorManager.SubtitleText;
        subTitleStyle.alignment = TextAnchor.MiddleCenter;
        
        // Title control style (version button)
        titleControlStyle = new GUIStyle(EditorStyles.miniLabel);
        titleControlStyle.fontSize = 10;
        titleControlStyle.normal.textColor = ServerUtilityProvider.ColorManager.SubtitleText;
        
        // Autoscroll button style
        autoscrollStyle = new GUIStyle(EditorStyles.miniLabel);
        autoscrollStyle.fontSize = 12;
        
        // Clear button style
        clearStyle = new GUIStyle(EditorStyles.miniLabel);
        clearStyle.fontSize = 12;
        clearStyle.normal.textColor = ServerUtilityProvider.ColorManager.ClearButton;
        clearStyle.hover.textColor = ServerUtilityProvider.ColorManager.ClearButton;
        
        // Rich text style for output log
        richTextStyle = new GUIStyle(EditorStyles.textArea);
        richTextStyle.richText = true;
        
        // Update button style
        updateButtonStyle = new GUIStyle(GUI.skin.button);
        
        // Dismiss button style
        dismissButtonStyle = new GUIStyle(GUI.skin.button);
        dismissButtonStyle.normal.textColor = ServerUtilityProvider.ColorManager.ButtonText;
        dismissButtonStyle.hover.textColor = ServerUtilityProvider.ColorManager.Warning;
        dismissButtonStyle.active.textColor = ServerUtilityProvider.ColorManager.Warning;
        dismissButtonStyle.focused.textColor = ServerUtilityProvider.ColorManager.ButtonText;
        dismissButtonStyle.fontSize = 12;
        
        // Active/Inactive toolbar button styles
        activeToolbarButton = new GUIStyle(EditorStyles.toolbarButton);
        activeToolbarButton.normal.textColor = ServerUtilityProvider.ColorManager.ActiveToolbar;
        
        inactiveToolbarButton = new GUIStyle(EditorStyles.toolbarButton);
        inactiveToolbarButton.normal.textColor = ServerUtilityProvider.ColorManager.InactiveToolbar;
        
        // Custom window style
        customWindowStyle = new GUIStyle(GUI.skin.window);
        customWindowStyle.padding = new RectOffset(5, 5, 5, 5);
        customWindowStyle.contentOffset = Vector2.zero;
        customWindowStyle.alignment = TextAnchor.UpperLeft;
        
        // Module init button style
        moduleInitButtonStyle = new GUIStyle(GUI.skin.button);
        
        // Title style for fallback
        titleStyle = new GUIStyle(EditorStyles.whiteLargeLabel);
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.fontSize = 15;
        
        // Initialize GUIContent objects
        string tooltipVersion = "Click to change logo color\n" + "Type: " + ServerUpdateProcess.GetCachedDistributionType();
        versionButtonContent = new GUIContent("version " + ServerUpdateProcess.GetCurrentPackageVersion(), tooltipVersion);
        autoscrollButtonContent = new GUIContent("autoscroll");
        clearButtonContent = new GUIContent("clear");
        trimmedLogContent = new GUIContent("");
        emptyContent = new GUIContent("");
        
        // Initialize cached GUILayoutOption arrays
        width110 = new[] { GUILayout.Width(110) };
        width75 = new[] { GUILayout.Width(75) };
        width47 = new[] { GUILayout.Width(47) };
        width35 = new[] { GUILayout.Width(35) };
        width25 = new[] { GUILayout.Width(25) };
        width20 = new[] { GUILayout.Width(20) };
        height20 = new[] { GUILayout.Height(20) };
        height10 = new[] { GUILayout.Height(10) };
        height1_7 = new[] { GUILayout.Height(1.7f) };
        expandWidth = new[] { GUILayout.ExpandWidth(true) };
        expandHeight = new[] { GUILayout.ExpandHeight(true) };
        width25Height = new[] { GUILayout.Width(25), GUILayout.Height(EditorGUIUtility.singleLineHeight) };
        width47Height20 = new[] { GUILayout.Width(47), GUILayout.Height(20) };
        
        stylesInitialized = true;
    }
    #endregion

    #region OnEnable

    private void OnEnable()
    {
        // Prevent unnecessary repaints from mouse movement
        wantsMouseMove = false;
        
        // Ensure colors are initialized from the centralized ColorManager
        ServerUtilityProvider.ColorManager.EnsureInitialized();
        
        // Check identity state on enable
        CheckIdentityState();
        
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
        
        // Initialize previous dev mode state for change detection
        previousDevMode = devMode;
        
        // Initialize wslProcess to avoid null reference exceptions
        if (wslProcess == null)
        {
            wslProcess = new ServerWSLProcess(LogMessage, debugMode);
        }

        // Initialize dockerProcess to avoid null reference exceptions
        if (dockerProcess == null)
        {
            dockerProcess = new ServerDockerProcess(LogMessage, debugMode);
        }

        // Load server mode from Settings
        LoadServerModeFromSettings();
        
        // Initialize localCLIProvider based on current serverMode
        if (string.IsNullOrEmpty(localCLIProvider))
        {
            localCLIProvider = serverMode == ServerMode.DockerServer ? "Docker" : "WSL";
        }
        
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
                previousServerRunning = serverRunning; // Initialize tracking state
                Repaint();

                // Update SSH connection status
                if (serverMode == ServerMode.CustomServer)
                {
                    serverManager.SSHConnectionStatusAsync();
                    isConnectedCustomSSH = serverManager.IsSSHConnectionActive;
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
        
        // Continuously sync publishing state with ServerManager
        bool newPublishingState = serverManager.Publishing;
        if (publishing != newPublishingState)
        {
            publishing = newPublishingState;
            Repaint(); // Refresh UI when publishing state changes
        }
        
        // Check identity when server transitions from stopped to running
        bool currentServerRunning = serverManager.IsServerStarted;
        if (currentServerRunning && !previousServerRunning)
        {
            // Server just started - check identity
            if (debugMode)
                UnityEngine.Debug.Log("[ServerWindow] Server started, checking identity state...");
            CheckIdentityState();
        }
        previousServerRunning = currentServerRunning;
        
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
        // Note: Removed redundant Repaint() here since OnGUI already runs regularly
        // Only trigger when absolutely necessary (e.g., SSH status changes)
        if (currentTime - lastCheckTime > statusUICheckInterval)
        {
            if (serverMode == ServerMode.CustomServer) 
            {
                bool previousSSHState = isConnectedCustomSSH;
                serverManager.SSHConnectionStatusAsync();
                isConnectedCustomSSH = serverManager.IsSSHConnectionActive;
                
                // Only repaint if SSH connection state actually changed
                if (previousSSHState != isConnectedCustomSSH)
                {
                    Repaint();
                }
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
                    isDockerRunning = serverManager.IsDockerRunning;
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
            
            // Update window states only when gaining focus
            UpdateWindowStates();
            
            // Check identity state when window regains focus
            if (!isCheckingIdentity)
            {
                CheckIdentityState();
            }
            
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
            Color lineColor = EditorGUIUtility.isProSkin ? ServerUtilityProvider.ColorManager.LineDark : ServerUtilityProvider.ColorManager.LineLight;
            EditorGUI.DrawRect(lineRect, lineColor);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            bool isLocalMode = (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer);
            if (GUILayout.Button(new GUIContent("Local", "Run a local server with SpacetimeDB"), isLocalMode ? activeToolbarButton : inactiveToolbarButton, expandWidth))
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
            if (GUILayout.Button(new GUIContent("Remote", "Connect to your custom remote server and run spacetime commands"), serverMode == ServerMode.CustomServer ? activeToolbarButton : inactiveToolbarButton, expandWidth))
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
            if (GUILayout.Button(new GUIContent("Maincloud", "Connect to the official SpacetimeDB cloud server and run spacetime commands"), serverMode == ServerMode.MaincloudServer ? activeToolbarButton : inactiveToolbarButton, expandWidth))
            {
                if (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)
                {
                    if (serverMode == ServerMode.WSLServer)
                    {
                        bool modeChange = EditorUtility.DisplayDialog("Confirm Mode Change", 
                        "Do you want to stop your Local server and change the server mode to Maincloud server?",
                        "OK","Cancel");
                        if (modeChange)
                        {
                            serverManager.StopServer();
                        }
                    }

                    // Save the current local mode before switching away
                    CCCPSettingsAdapter.SetLastLocalServerMode((ServerManager.ServerMode)serverMode);
                    serverMode = ServerMode.MaincloudServer;
                    UpdateServerModeState();
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
                "Docker: Use Docker containers to run a SpacetimeDB CLI and Server locally.\n\n"+
                "Docker supports Linux, MacOS or Windows. Fast 3 step setup, but requires Docker Desktop to run on your PC.\n\n"+
                "WSL: Use Windows Subsystem for Linux to run a SpacetimeDB CLI and Server locally.\n\n"+
                "WSL supports Windows. Slower 9 step setup, but can run silently in the background.\n\n"+
                "Both options provide a local development environment.";
                EditorGUILayout.LabelField(new GUIContent("CLI Provider:", cliProviderTooltip), GUILayout.Width(110));
                string[] cliProviderOptions = new string[] { "Docker (Windows, Linux or MacOS)", "WSL Debian (Windows)" };
                int cliProviderSelectedIndex = serverMode == ServerMode.DockerServer ? 0 : 1;
                int newCliProviderSelectedIndex = EditorGUILayout.Popup(cliProviderSelectedIndex, cliProviderOptions);
                if (newCliProviderSelectedIndex != cliProviderSelectedIndex)
                {
                    ServerMode newMode = newCliProviderSelectedIndex == 0 ? ServerMode.DockerServer : ServerMode.WSLServer;
                    serverMode = newMode;
                    // Save the selected CLI provider as the last local mode
                    CCCPSettingsAdapter.SetLastLocalServerMode((ServerManager.ServerMode)newMode);
                    // Update localCLIProvider variable
                    localCLIProvider = newCliProviderSelectedIndex == 0 ? "Docker" : "WSL";
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
            int newunityLangSelectedIndex = EditorGUILayout.Popup(unityLangSelectedIndex, unityLangOptions);
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
            if (GUILayout.Button(new GUIContent("Set Client Path", clientDirButtonTooltip), GUILayout.Height(20)))
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
            int newServerLangSelectedIndex = EditorGUILayout.Popup(serverLangSelectedIndex, serverLangOptions);
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
            newModuleNameInput = EditorGUILayout.TextField(newModuleNameInput);
            string serverDirButtonTooltip = "Current set path: " + (string.IsNullOrEmpty(serverDirectory) ? "Not Set" : serverDirectory);
            if (GUILayout.Button(new GUIContent("Add", serverDirButtonTooltip), GUILayout.Width(47), GUILayout.Height(20)))
            {
                // Show guidance dialog if this is the first module
                if (savedModules.Count == 0)
                {
                    EditorUtility.DisplayDialog(
                        "First Module Setup",
                        "If this is your first new server module you can create the module folder anywhere outside of the Assets folder.\n\n" +
                        "For example, you can create a /Server folder in the root of your project directory.\n\n" +
                        "Click OK to open the folder browser and select or create a folder for your module.",
                        "OK"
                    );
                }

                // Open folder panel to select module path
                string projectPath;
                try
                {
                    var parent = Directory.GetParent(Application.dataPath);
                    projectPath = parent != null ? parent.FullName : Application.dataPath;
                }
                catch (Exception ex)
                {
                    // Fallback to Assets folder if we can't determine project root
                    projectPath = Application.dataPath;
                    if (debugMode) LogMessage($"Failed to determine project root, opening Assets folder: {ex.Message}", -2);
                }
                string path = EditorUtility.OpenFolderPanel("Select Module Path", projectPath, "");
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
                int newDropdownIndex = EditorGUILayout.Popup(dropdownIndex, moduleOptions);
                
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
                EditorGUILayout.Popup(0, new string[] { "No saved modules" });
                EditorGUI.EndDisabledGroup();
            }
            GUILayout.Label(ServerUtilityProvider.GetStatusIcon(selectedModuleIndex != -1), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            GUILayout.Label("Module Initialization", EditorStyles.centeredGreyMiniLabel);

            // Init a new module / Delete Selected Module
            EditorGUILayout.BeginHorizontal();
            // OS-specific key combination: Ctrl+Alt for Windows/Linux, Ctrl+Command for macOS
            bool deleteMode = ServerUtilityProvider.IsMacOS() 
                ? (Event.current.control && Event.current.command) 
                : (Event.current.control && Event.current.alt);
            bool hasSelectedModule = selectedModuleIndex >= 0 && selectedModuleIndex < savedModules.Count;
            string buttonText = deleteMode && hasSelectedModule ? "Delete Selected Module" : "Init New Module";
            string baseTooltip = deleteMode && hasSelectedModule ? 
                "Delete Selected Module: Removes the currently selected saved module from the list." :
                "Init a new module: Initializes a new SpacetimeDB module with the selected name, path and language.";
            
            string keyComboText = ServerUtilityProvider.IsMacOS() ? "Ctrl + Cmd" : "Ctrl + Alt";
            string fullTooltip = baseTooltip + $"\n\nTip: Hold {keyComboText} while clicking to delete the selected saved module instead (The path and files remain on the disk).";
            
            EditorGUILayout.LabelField(new GUIContent("Module Init or Del:", fullTooltip), GUILayout.Width(110));
            
            // Update cached button style colors for delete mode
            if (deleteMode && hasSelectedModule)
            {
                // Orange color for delete warning
                Color warningColor;
                ColorUtility.TryParseHtmlString("#FFA500", out warningColor); // Orange
                moduleInitButtonStyle.normal.textColor = warningColor;
                moduleInitButtonStyle.hover.textColor = warningColor;
                Repaint();
            }
            else
            {
                // Reset to default button text color
                moduleInitButtonStyle.normal.textColor = ServerUtilityProvider.ColorManager.ButtonText;
                moduleInitButtonStyle.hover.textColor = ServerUtilityProvider.ColorManager.ButtonText;
            }
            
            EditorGUI.BeginDisabledGroup(deleteMode && !hasSelectedModule);
            if (GUILayout.Button(new GUIContent(buttonText, fullTooltip), moduleInitButtonStyle, GUILayout.Height(20)))
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
            GUILayout.Space(24); // Instead of a status icon we use space to align with other fields
            EditorGUILayout.EndHorizontal();
            #endregion

            #region Local Modes
            // Settings for local modes (WSL and Docker)
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
                if (GUILayout.Button(new GUIContent("Set Backup Directory", backupDirButtonTooltip), GUILayout.Height(20)))
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
            }
            #endregion

            #region Local Docker
            if (serverMode == ServerMode.DockerServer)
            {
                // Local Docker URL
                string dockerUrlTooltip = "The full URL of the Docker server with the OS port mapping you wish to use.\n\n" +
                    "Example: http://0.0.0.0:3011/ \n\n" +
                    "Note: The port number is required. Internally the spacetimedb server always runs on port 3000 by default.";
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("URL:", dockerUrlTooltip), GUILayout.Width(110));
                string newUrlDocker = EditorGUILayout.DelayedTextField(serverUrlDocker);
                if (newUrlDocker != serverUrlDocker)
                {
                    serverUrlDocker = newUrlDocker;
                    // Extract port from Docker URL
                    int extractedPort = ServerUtilityProvider.ExtractPortFromUrl(serverUrlDocker);
                    if (extractedPort > 0)
                    {
                        if (extractedPort != serverPortDocker)
                        {
                            serverPortDocker = extractedPort;
                            
                            if (debugMode) UnityEngine.Debug.Log($"[ServerWindow] Docker port updated to {serverPortDocker}");
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning("[ServerWindow] Could not extract port from Docker URL");
                    }
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(serverUrlDocker)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                // Local Docker Auth Token                
                string tokenTooltip = GetAuthTokenTooltip(AuthTokenType.Docker,
                "Required to modify the database and run reducers. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthTokenDocker = EditorGUILayout.PasswordField(authTokenDocker);
                if (newAuthTokenDocker != authTokenDocker)
                {
                    authTokenDocker = newAuthTokenDocker;
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(authTokenDocker)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
            }
            #endregion

            #region Local WSL
            if (serverMode == ServerMode.WSLServer)
            {
                // Debian Username setting
                EditorGUILayout.BeginHorizontal();
                string userNameTooltip = 
                "The Debian username to use for Debian commands.\n\n"+
                "Note: Needed for most server commands and utilities.";
                EditorGUILayout.LabelField(new GUIContent("Debian Username:", userNameTooltip), GUILayout.Width(110));
                string newUserName = EditorGUILayout.DelayedTextField(userName);
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
                string newUrl = EditorGUILayout.DelayedTextField(serverUrl);
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
                string newAuthToken = EditorGUILayout.PasswordField(authToken);
                if (newAuthToken != authToken)
                {
                    authToken = newAuthToken;
                    CCCPSettingsAdapter.SetAuthToken(authToken);
                }
                GUILayout.Label(ServerUtilityProvider.GetStatusIcon(!string.IsNullOrEmpty(authToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(3);
            }

            if (serverMode == ServerMode.DockerServer)
            {
                if (GUILayout.Button("Server Setup Window"))
                    ServerSetupWindow.ShowDockerWindow();
            }
            else if (serverMode == ServerMode.WSLServer)
            {
                if (GUILayout.Button("Server Setup Window"))
                    ServerSetupWindow.ShowWSLWindow();
            }

            // Status and Setup for local modes (WSL and Docker)
            if (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)
            {
                if (GUILayout.Button("Check Pre-Requisites", GUILayout.Height(20)))
                    CheckPrerequisites();

                // Status display - show WSL/Docker status based on server mode
                EditorGUILayout.BeginHorizontal();
                string tooltipStatus;
                if (isDockerRunning && serverMode == ServerMode.DockerServer)
                {
                    tooltipStatus = "Docker Desktop is running on your PC.";
                }
                else if (!isDockerRunning && serverMode == ServerMode.DockerServer)
                {
                    tooltipStatus = "Docker Desktop is not running on your PC. Please start Docker Desktop.";
                }
                else if (isWslRunning && serverMode == ServerMode.WSLServer)
                {
                    tooltipStatus = "Windows Subsystem for Linux is running on your PC.";
                }
                else // !isWslRunning && serverMode == ServerMode.WSLServer
                {
                    tooltipStatus = "Windows Subsystem for Linux is not running on your PC. It will start automatically when needed.";
                }
                string statusLabel = serverManager.CurrentServerMode == ServerManager.ServerMode.DockerServer ? "Docker:" : "WSL:";
                EditorGUILayout.LabelField(new GUIContent(statusLabel, tooltipStatus), GUILayout.Width(110));
                Color connectedStatusColor = connectedStyle.normal.textColor;
                
                bool cliProviderRunning = serverManager.CurrentServerMode == ServerManager.ServerMode.DockerServer ? isDockerRunning : isWslRunning;
                connectedStyle.normal.textColor = cliProviderRunning ? connectedStatusColor : Color.gray;
                string statusText = cliProviderRunning ? "RUNNING" : "STOPPED";
                EditorGUILayout.LabelField(statusText, connectedStyle);
                // Restore the original color after using it
                connectedStyle.normal.textColor = connectedStatusColor;
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
                EditorGUILayout.LabelField(new GUIContent("SSH Keygen:", keygenTooltip), GUILayout.Width(110));                
                if (GUILayout.Button(new GUIContent("Generate SSH Key Pair", keygenTooltip)))
                {
                    // Display editor dialog to confirm overwriting existing keys
                    if (EditorUtility.DisplayDialog("Confirm SSH Key Generation", 
                    "Are you sure you want to overwrite any existing SSH key pair?\n\n"+ 
                    "If you have not yet generated an SSH key pair, one will be created.", 
                    "Generate SSH Key Pair", "Cancel"))
                    {
                        GenerateSSHKeyPairAsync();
                    }
                }
                GUILayout.Space(24); // Instead of a status icon we use space to align with other fields
                EditorGUILayout.EndHorizontal();
                
                // SSH Private Key Path (button only)
                EditorGUILayout.BeginHorizontal();
                string keyPathTooltip = "The full path to your SSH private key file (e.g., C:\\Users\\YourUser\\.ssh\\id_ed25519).";
                if (!string.IsNullOrEmpty(sshPrivateKeyPath))
                {
                    keyPathTooltip += $"\n\nCurrent path: {sshPrivateKeyPath}";
                }                
                EditorGUILayout.LabelField(new GUIContent("Private Key Path:", keyPathTooltip), GUILayout.Width(110));
                if (GUILayout.Button(new GUIContent("Set Private Key Path", keyPathTooltip)))
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
                string newUserName = EditorGUILayout.DelayedTextField(sshUserName);
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
                string newUrl = EditorGUILayout.DelayedTextField(customServerUrl);
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
                string newAuthToken = EditorGUILayout.PasswordField(customServerAuthToken);
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
                
                Color originalColor = connectedStyle.normal.textColor;
                connectedStyle.normal.textColor = isConnectedCustomSSH ? originalColor : Color.gray;
                string connectionStatusText = isConnectedCustomSSH ? "CONNECTED SSH" : "DISCONNECTED SSH";
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
                if (GUILayout.Button("Login to Maincloud"))
                {
                    if (wslProcess == null)
                    {
                        wslProcess = new ServerWSLProcess(LogMessage, debugMode);
                    }
                    LogoutAndLogin(manual:false);
                }
                GUILayout.Space(24); // Instead of a status icon we use space to align with other fields
                EditorGUILayout.EndHorizontal();                

                // Auth Token setting
                EditorGUILayout.BeginHorizontal();
                string tokenTooltip = GetAuthTokenTooltip(AuthTokenType.Maincloud,
                "Required to modify the database and run reducers. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.");
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(maincloudAuthToken);
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
            // All colors are pre-initialized in OnEnable for memory efficiency
            // Just use the cached color variables directly
                   
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
                changeToggleStyle.normal.textColor = ServerUtilityProvider.ColorManager.Recommended;
                changeToggleStyle.hover.textColor = ServerUtilityProvider.ColorManager.Recommended;
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
            $"Automatic Publishing: The {localCLIProvider} CLI will automatically publish the module to your server when changes are detected. \n\n"+
            $"Manual Publish: The {localCLIProvider} CLI will not automatically publish the module and will require manual publishing.";
            EditorGUILayout.LabelField(new GUIContent("Auto Publish Mode:", autoPublishTooltip), GUILayout.Width(120));
            GUIStyle autoPublishStyle = new GUIStyle(GUI.skin.button);
            if (serverManager.AutoPublishMode)
            {
                // Use green for active auto-publish
                autoPublishStyle.normal.textColor = ServerUtilityProvider.ColorManager.Recommended;
                autoPublishStyle.hover.textColor = ServerUtilityProvider.ColorManager.Recommended;
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
                publishGenerateStyle.normal.textColor = ServerUtilityProvider.ColorManager.Recommended;
                publishGenerateStyle.hover.textColor = ServerUtilityProvider.ColorManager.Recommended;
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
            string cliCloseTooltip = 
            $"Close {localCLIProvider} at Unity Quit: Your {localCLIProvider} virtualization and CLI will close when Unity is closed. \n"+
            $"Saves resources when the virtualization is not in use. {localCLIProvider} may otherwise leave several processes running.\n\n"+
            $"Keep Running: Your {localCLIProvider} virtualization and CLI will keep running after Unity is closed.\n\n"+
            $"Recommended: Close {localCLIProvider} at Unity Quit unless having other applications besides SpacetimeDB depending on {localCLIProvider}.";
            EditorGUILayout.LabelField(new GUIContent($"{localCLIProvider} Auto Close:", cliCloseTooltip), GUILayout.Width(120));
            GUIStyle wslCloseStyle = new GUIStyle(GUI.skin.button);
            if (serverManager.AutoCloseCLI)
            {
                wslCloseStyle.normal.textColor = ServerUtilityProvider.ColorManager.Warning;
                wslCloseStyle.hover.textColor = ServerUtilityProvider.ColorManager.Warning;
            }
            if (GUILayout.Button(serverManager.AutoCloseCLI ? $"Close {localCLIProvider} at Unity Quit" : "Keep Running", wslCloseStyle))
            {
                bool newAutoClose = !serverManager.AutoCloseCLI;
                serverManager.autoCloseCLI = newAutoClose;
                autoCloseCLI = newAutoClose; // Keep local field in sync
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
                    moduleLogToggleStyle.normal.textColor = ServerUtilityProvider.ColorManager.Warning;
                    moduleLogToggleStyle.hover.textColor = ServerUtilityProvider.ColorManager.Warning;
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
                    databaseLogToggleStyle.normal.textColor = ServerUtilityProvider.ColorManager.Warning;
                    databaseLogToggleStyle.hover.textColor = ServerUtilityProvider.ColorManager.Warning;
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
                debugToggleStyle.normal.textColor = ServerUtilityProvider.ColorManager.Debug;
                debugToggleStyle.hover.textColor = ServerUtilityProvider.ColorManager.Debug;
            }
            if (GUILayout.Button(serverManager.DebugMode ? "Debug Mode" : "Debug Disabled", debugToggleStyle))
            {
                bool newDebugMode = !serverManager.DebugMode;
                serverManager.debugMode = newDebugMode;
                debugMode = newDebugMode;
                
                // Update other components that need to know about debug mode
                ServerSetupWindow.debugMode = newDebugMode;
                ServerOutputWindow.debugMode = newDebugMode;
                ServerEditorStates.debugMode = newDebugMode;
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
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }
    #endregion

    #region ServerControlUI

    private void DrawServerControlSection()
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
        serverRunning = serverManager.IsServerStarted || serverManager.IsStartingUp; // Update running state
        if (!serverManager.HasAllPrerequisites)
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
            } 
            else if (serverMode == ServerMode.DockerServer)
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
            } 
            else if (serverMode == ServerMode.CustomServer)
            {
                if (!serverManager.IsCliProviderRunning)
                {
                    if (GUILayout.Button($"Start SpacetimeDB Local {localCLIProvider} CLI", GUILayout.Height(30)))
                    {
                        serverManager.StartServer();
                    }
                }
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
            else if (serverMode == ServerMode.MaincloudServer)
            {
                if (localCLIProvider == "Docker" && !serverManager.IsCliProviderRunning)
                {
                    if (GUILayout.Button("Start SpacetimeDB Local Docker CLI", GUILayout.Height(30)))
                    {
                        serverManager.StartServer();
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
        
        // Not using Hex ServerUtilityProvider.ColorManager.WindowToggle here since backgroundColor is funky and appears darker
        Color originalColor = GUI.backgroundColor;
        if (viewLogsWindowOpen)
            GUI.backgroundColor = new Color(0.6f, 1.6f, 0.6f); // Light green tint when active

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
            GUI.backgroundColor = new Color(0.6f, 1.6f, 0.6f); // Light green tint when active
        
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
            GUI.backgroundColor = new Color(0.6f, 1.6f, 0.6f); // Light green tint when active

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
        
        // Use green for running/starting OR gray for stopped/local CLI not connected
        string statusText;
        Color connectedStatusColor = connectedStyle.normal.textColor;
        bool spacetimedbConnected = (serverMode == ServerMode.MaincloudServer && serverManager.IsMaincloudConnected) || 
                                    (serverMode == ServerMode.CustomServer && serverManager.IsSSHConnectionActive) ||
                                    (serverManager.IsCliProviderRunning && (serverManager.IsStartingUp || serverManager.IsServerStarted));

        if (serverMode == ServerMode.MaincloudServer && serverManager.IsMaincloudConnected)
        {
            // For Maincloud, show in gray if CLI provider is not running
            connectedStyle.normal.textColor = serverManager.IsCliProviderRunning ? connectedStatusColor : Color.gray;
            statusText = "MAINCLOUD";
        }
        else if (serverManager.IsStartingUp)
        {
            connectedStyle.normal.textColor = spacetimedbConnected ? connectedStatusColor : Color.gray;
            statusText = "STARTING...";
        }
        else if (serverManager.IsServerStarted)
        {
            connectedStyle.normal.textColor = spacetimedbConnected ? connectedStatusColor : Color.gray;
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
        connectedStyle.normal.textColor = connectedStatusColor;

        // SpacetimeDB version display
        GUIStyle versionStyle = new GUIStyle(EditorStyles.miniLabel);
        versionStyle.fontSize = 10;
        versionStyle.normal.textColor = ServerUtilityProvider.ColorManager.VersionText;

        EditorGUILayout.LabelField("v", versionStyle, GUILayout.Width(10));
        if (serverMode == ServerMode.WSLServer)
            EditorGUILayout.LabelField(spacetimeDBCurrentVersionWSL, versionStyle, GUILayout.Width(32));
        else if (serverMode == ServerMode.DockerServer)
            EditorGUILayout.LabelField(spacetimeDBCurrentVersionDocker, versionStyle, GUILayout.Width(32));
        else if (serverMode == ServerMode.CustomServer)
            EditorGUILayout.LabelField(spacetimeDBCurrentVersionCustom, versionStyle, GUILayout.Width(32));
        else if (serverMode == ServerMode.MaincloudServer)
            EditorGUILayout.LabelField(spacetimeDBLatestVersion, versionStyle, GUILayout.Width(32));

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

            if (serverMode == ServerMode.DockerServer)
                EditorGUILayout.LabelField("SpacetimeDB Local Server", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));
            else if (serverMode == ServerMode.WSLServer)
                EditorGUILayout.LabelField("SpacetimeDB Local Server", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));
            else if (serverMode == ServerMode.CustomServer)
                EditorGUILayout.LabelField("SpacetimeDB Remote Server", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));
            else if (serverMode == ServerMode.MaincloudServer)
                EditorGUILayout.LabelField("SpacetimeDB Maincloud Server", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            if (debugMode && GUILayout.Button(new GUIContent("Login (Without Refresh)", "Login without refreshing the current session."), GUILayout.Height(20))) // Only does login without refresh
            {
                if (serverMode != ServerMode.CustomServer && CLIAvailableLocal()) 
                    serverManager.RunServerCommand("spacetime login", "Logging in to SpacetimeDB");
                #pragma warning disable CS4014 // Because this call is not awaited we disable the warning, it works anyhow
                else if (serverMode == ServerMode.CustomServer && CLIAvailableRemote()) 
                    serverCustomProcess.RunVisibleSSHCommand($"/home/{sshUserName}/.local/bin/spacetime login");
                #pragma warning restore CS4014
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL/Docker) or remote (SSH) and it is available. Ensure you have started your Docker container if using Docker.", -2);
            }

            if (debugMode && GUILayout.Button(new GUIContent("Logout", "Logout from the current SpacetimeDB session."), GUILayout.Height(20)))
            {
                if (serverMode != ServerMode.CustomServer && CLIAvailableLocal()) 
                    serverManager.RunServerCommand("spacetime logout", "Logging out of SpacetimeDB");
                #pragma warning disable CS4014 // Because this call is not awaited we disable the warning, it works anyhow
                else if (serverMode == ServerMode.CustomServer && CLIAvailableRemote()) 
                    serverCustomProcess.RunVisibleSSHCommand($"/home/{sshUserName}/.local/bin/spacetime logout");
                #pragma warning restore CS4014
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (Docker or WSL) or remote (SSH) and it is available. Ensure you have started your Docker container if using Docker.", -2);
            }

            if (GUILayout.Button(new GUIContent("Login", "Logout and login with a fresh SpacetimeDB verified SSO session."), GUILayout.Height(20))) // Does the complete logout and login with refresh
            {
                if (serverMode != ServerMode.CustomServer && CLIAvailableLocal()) 
                    LogoutAndLogin(manual:true);
                else if (serverMode == ServerMode.CustomServer && CLIAvailableRemote()) 
                    LogoutAndLogin(manual:true);
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (Docker or WSL) or remote (SSH) and it is available. Ensure you have started your Docker container if using Docker.", -2);
            }

            if (GUILayout.Button(new GUIContent("Show Login Info With Auth Token", "Display the current login information including authentication token."), GUILayout.Height(20)))
            {
                if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                serverManager.RunServerCommand("spacetime login show --token", "Showing SpacetimeDB login info and token");
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (Docker or WSL) or remote (SSH) and it is available. Ensure you have started your Docker container if using Docker.", -2);
            }

            if (GUILayout.Button(new GUIContent("Show Server Config", "Display the current server configuration."), GUILayout.Height(20)))
            {
                if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                serverManager.RunServerCommand("spacetime server list", "Showing SpacetimeDB server config");
                else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (Docker or WSL) or remote (SSH) and it is available. Ensure you have started your Docker container if using Docker.", -2);
            }

            EditorGUI.BeginDisabledGroup(serverMode != ServerMode.MaincloudServer && !serverManager.IsServerStarted);
                if (GUILayout.Button(new GUIContent("Show Active Modules", "Display all active modules on the server."), GUILayout.Height(20)))
                {
                    if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                    serverManager.RunServerCommand("spacetime list", "Showing active modules");
                    else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (Docker or WSL) or remote (SSH) and it is available. Ensure you have started your Docker container if using Docker.", -2);
                }
            EditorGUI.EndDisabledGroup();

            if (serverMode != ServerMode.MaincloudServer)
            {
                // Maincloud does not support ping command
                if (GUILayout.Button(new GUIContent("Ping Server", "Test connectivity to the server."), GUILayout.Height(20)))
                {
                    if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                    serverManager.PingServer(true);
                    else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (Docker or WSL) or remote (SSH) and it is available. Ensure you have started your Docker container if using Docker.", -2);
                }
                // Maincloud always uses the latest version
                if (GUILayout.Button(new GUIContent("Show Version", "Display the SpacetimeDB version."), GUILayout.Height(20)))
                {
                    if ((serverMode != ServerMode.CustomServer && CLIAvailableLocal()) || (serverMode == ServerMode.CustomServer && CLIAvailableRemote()))
                    serverManager.RunServerCommand("spacetime --version", "Showing SpacetimeDB version");
                    else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (Docker or WSL) or remote (SSH) and it is available. Ensure you have started your Docker container if using Docker.", -2);
                }
            }

            // Maincloud energy command
            if (serverMode == ServerMode.MaincloudServer)
            {
                if (GUILayout.Button(new GUIContent("Show Energy Balance", "Display your Maincloud energy balance."), GUILayout.Height(20)))
                {
                    if (CLIAvailableLocal()) 
                        serverManager.RunServerCommand("spacetime energy balance", "Showing SpacetimeDB Maincloud energy");
                    else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a local (WSL or Docker) and it is available. Ensure you have started your Docker container if using Docker.", -2);
                }
            }

            // Service Status button (only in Custom Server mode)
            if (serverMode == ServerMode.CustomServer)
            {
                EditorGUILayout.LabelField("Custom Server Utility Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

                if (GUILayout.Button(new GUIContent("Service Status", "Check the current service status on the remote server."), buttonStyle))
                {
                    if (CLIAvailableRemote())
                    CheckServiceStatus();
                    else LogMessage("SpacetimeDB CLI disconnected. Make sure you have installed a remote server (SSH) and it is available. Ensure you have started your Docker container if using Docker.", -2);
                }
                // Add a button which opens a cmd window with the ssh username
                if (GUILayout.Button(new GUIContent("Open SSH Window", "Open an SSH terminal to the remote server."), buttonStyle))
                {
                    serverManager.OpenSSHWindow();
                }
            }

            EditorGUILayout.LabelField($"Local {localCLIProvider} CLI Tools", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            // Open WSL or Docker CLI terminal
            if (localCLIProvider == "WSL")
            {
                if (GUILayout.Button(new GUIContent("Open Debian Window", "Open a terminal to the Debian/WSL environment."), GUILayout.Height(20)))
                {
                    serverManager.OpenDebianWindow();
                }
            } else if (localCLIProvider == "Docker")
            {
                if (GUILayout.Button(new GUIContent("Open Docker Window", "Open a terminal to the Docker container."), GUILayout.Height(20)))
                {
                    serverManager.OpenDockerWindow();
                }
            }

            // Clean Temp Files button - covers both Cargo cache and generated client bindings
            string cleanTempFilesTooltip = "Remove temporary build and generated files to fix compilation issues.";
            if (GUILayout.Button(new GUIContent("Clean Temp Files", cleanTempFilesTooltip), GUILayout.Height(20)))
            {
                // Show a dialog with options to clean cargo cache and/or generated files
                int dialogResult = EditorUtility.DisplayDialogComplex(
                    "Clean Temporary Files",
                    "Select which temporary files to clean:\n\n" +
                    "Generated Client Code: Removes generated bindings. Useful when client API changes or to force regeneration.\n\n" +
                    "Cargo Cache: Removes the build cache. Fixes permission errors and compilation issues.\n\n" +
                    "Both operations are safe and don't affect your source code or database.",
                    "Both",           // Button 0
                    "Cancel",         // Button 1
                    "Cargo Only"      // Button 2
                );

                if (dialogResult == 0)
                {
                    // Clean both
                    serverManager.CleanServerCargo();
                    serverManager.CleanServerGeneratedClientBindings();
                }
                else if (dialogResult == 2)
                {
                    // Clean cargo only
                    serverManager.CleanServerCargo();
                }
                // dialogResult == 1 means Cancel, do nothing
            }

            // Backup, Restore and Clear Server Data buttons (Only supports local WSL and Docker servers)
            if (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)
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
                        CloseDatabaseAndReducerWindow();
                        serverManager.ClearServerData();
                    }
                }
            }

            EditorGUI.BeginDisabledGroup(!serverManager.IsServerStarted && !serverManager.HasAllPrerequisites);
            string checkForUpdateTooltip = $"Checks for SpacetimeDB and {localCLIProvider} CLI updates if available.";
            if (GUILayout.Button(new GUIContent($"Check for SpacetimeDB Updates", checkForUpdateTooltip), GUILayout.Height(20)))
            {
                CheckCLIUpdates();
            }
            EditorGUI.EndDisabledGroup();

            // Identity Manager button with warning if offline identity
            string identityButtonText = "Identity Manager";
            // Check if we have an offline identity
            IdentityType currentIdentityTypeCommand = ServerIdentityManager.GetSavedIdentityType();
            bool hasOfflineIdentityCommand = currentIdentityTypeCommand == IdentityType.OfflineServerIssued;
            // Add warning triangle if offline identity
            if (hasOfflineIdentityCommand)
            {
                identityButtonText = "⚠ " + identityButtonText;
            }
            string identityManagerTooltip = "Open the Identity Manager to verify your SpacetimeDB identity.";
            IdentityType currentIdentityTypeForManager = ServerIdentityManager.GetSavedIdentityType();
            bool hasOfflineIdentityForManager = currentIdentityTypeForManager == IdentityType.OfflineServerIssued;
            if (hasOfflineIdentityForManager)
            {
                identityManagerTooltip += "\n\n<color=orange>⚠ WARNING: Currently using offline identity. Consider authenticating with SSO for better security and recovery options.</color>";
            }
            else if (!hasOfflineIdentityForManager)
            {
                identityManagerTooltip += "\n\n<color=green>🔒 Currently using secure SSO identity.</color>";
            }
            if (GUILayout.Button(new GUIContent(identityButtonText, identityManagerTooltip), GUILayout.Height(20)))
            {
                OpenIdentityWindow();
            }

            if (debugMode && serverMode == ServerMode.WSLServer)
            {
                if (GUILayout.Button(new GUIContent("Test Server Running", "Test if the WSL server is currently running."), GUILayout.Height(20)))
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
            updateStyle.normal.textColor = ServerUtilityProvider.ColorManager.Recommended;
            updateStyle.hover.textColor = ServerUtilityProvider.ColorManager.HoverGreen;
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

        // Display dev mode change notification if dev mode state has changed
        if (devMode != previousDevMode)
        {
            GUIStyle devModeStyle = new GUIStyle(EditorStyles.boldLabel);
            devModeStyle.normal.textColor = ServerUtilityProvider.ColorManager.Recommended;
            devModeStyle.hover.textColor = ServerUtilityProvider.ColorManager.HoverGreen;
            devModeStyle.fontStyle = FontStyle.Bold;
            
            string displayText = devMode ? "Publish to Enable Dev Mode" : "Publish to Disable Dev Mode";
            string tooltip = devMode ? 
                "Dev Mode is enabled. Publish to apply this change to the server." : 
                "Dev Mode is disabled. Publish to apply this change to the server.";
            
            // Create a button-like appearance
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            // Use a button that looks like a label for better click response
            if (GUILayout.Button(new GUIContent(displayText, tooltip), devModeStyle))
            {
                previousDevMode = devMode;
                Repaint();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        if (publishFirstModule)
        {
            GUIStyle firstModuleStyle = new GUIStyle(EditorStyles.boldLabel);
            firstModuleStyle.normal.textColor = ServerUtilityProvider.ColorManager.Recommended;
            firstModuleStyle.hover.textColor = ServerUtilityProvider.ColorManager.HoverGreen;
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

        // Disable publish if module name is empty OR if CLI provider isn't running
        bool publishDisabled = string.IsNullOrEmpty(serverManager.ModuleName) || !serverManager.IsCliProviderRunning;
        EditorGUI.BeginDisabledGroup(publishDisabled);
        
        string editModuleTooltip = "Edit the module script (lib.rs or lib.cs) of the selected module.";
        if (GUILayout.Button(new GUIContent("Edit Module", editModuleTooltip), GUILayout.Height(20)))
        {
            // Open the module script in the default editor using cross-platform utility
            // Get the actual project root (handles both old and new directory structures)
            string projectRoot = ServerUtilityProvider.GetProjectRoot(serverDirectory);
            
            string modulePathRs = Path.Combine(projectRoot, "src", "lib.rs");
            string modulePathCs = Path.Combine(projectRoot, "lib.cs");
            
            string fileToOpen = null;
            
            if (File.Exists(modulePathRs))
            {
                fileToOpen = modulePathRs;
            }
            else if (File.Exists(modulePathCs))
            {
                fileToOpen = modulePathCs;
            }
            
            if (fileToOpen != null)
            {
                // Try to open the file with the default application
                bool opened = ServerUtilityProvider.OpenFileInDefaultApplication(fileToOpen);
                
                if (ServerUtilityProvider.IsMacOS())
                {
                    // On macOS, also reveal the file in Finder
                    ServerUtilityProvider.RevealFileInFolder(fileToOpen);
                }

                if (!opened)
                {
                    // If open failed, try to reveal the folder instead
                    if (ServerUtilityProvider.RevealFileInFolder(fileToOpen))
                    {
                        if (debugMode) LogMessage("Module script folder opened", 1);
                    }
                    else
                    {
                        LogMessage("Failed to open module script or folder", -2);
                    }
                }
                else
                {
                    // Successfully opened
                    if (debugMode) LogMessage("Opened module script with text editor", 1);
                }
            }
            else
            {
                LogMessage("Module script not found", -2);
            }
        }
        #endregion

        // Publish Button with advanced features
        #region Publish Button

        // Check if control key is held - OS-specific key combination
        // Ctrl+Alt for Windows/Linux, Ctrl+Command for macOS (to avoid Unity crashes)
        bool resetDatabase = ServerUtilityProvider.IsMacOS() 
            ? (Event.current.control && Event.current.command) 
            : (Event.current.control && Event.current.alt);
        
        // Check if we have an offline identity
        IdentityType currentIdentityType = ServerIdentityManager.GetSavedIdentityType();
        bool hasOfflineIdentity = currentIdentityType == IdentityType.OfflineServerIssued;
        
        // Create button style based on control key state and identity status
        GUIStyle publishButtonStyle = new GUIStyle(GUI.skin.button);
        if (resetDatabase)
        {
            // Orange color for database reset warning
            publishButtonStyle.normal.textColor = ServerUtilityProvider.ColorManager.Warning;
            publishButtonStyle.hover.textColor = ServerUtilityProvider.ColorManager.Warning;
            Repaint();
        }
        if (serverManager.ServerChangesDetected || publishFirstModule)
        {
            publishButtonStyle.normal.textColor = ServerUtilityProvider.ColorManager.Recommended;
            publishButtonStyle.hover.textColor = ServerUtilityProvider.ColorManager.Recommended;
        }

        string buttonText;
        if (serverMode == ServerMode.MaincloudServer)
            buttonText = resetDatabase ? "Publish Module and Reset Database" : "Publish Module to Maincloud";
        else
            buttonText = resetDatabase ? "Publish Module and Reset Database" : "Publish Module";

        // Add warning triangle if offline identity
        if (hasOfflineIdentity && !publishing)
        {
            buttonText = "⚠ " + buttonText;
        }

        if (publishing) 
        {
            buttonText = "Publishing...";
            publishButtonStyle.normal.textColor = Color.green;
            publishButtonStyle.hover.textColor = Color.green;
        }

        string keyComboText = ServerUtilityProvider.IsMacOS() ? "Ctrl + Cmd" : "Ctrl + Alt";
        string publishTooltip = "Publish the selected module to the server.\n\n" +
                                $"{keyComboText} + Click to also reset the database.";
        
        if (hasOfflineIdentity)
        {
            publishTooltip += "\n\n<color=orange>⚠ WARNING: Currently using offline identity. Consider authenticating with SSO for better security and recovery options.</color>";
        }
        else if (!hasOfflineIdentity)
        {
            publishTooltip += "\n\n<color=green>🔒 Currently using secure SSO identity.</color>";
        }
        
        if (!serverManager.IsCliProviderRunning)
        {
            publishTooltip += "\n\nRequires the local CLI provider to be running";
        }

        if (GUILayout.Button(new GUIContent(buttonText, publishTooltip), publishButtonStyle, GUILayout.Height(30)))
        {
            // Check identity state before publishing (on demand)
            if (!isCheckingIdentity)
            {
                EditorApplication.delayCall += () => CheckIdentityState();
            }
            
            // Check if offline identity and show warning dialog
            if (hasOfflineIdentity && !resetDatabase)
            {
                // Use delayCall to avoid GUI layout errors when showing dialog
                EditorApplication.delayCall += () =>
                {
                    int result = EditorUtility.DisplayDialogComplex(
                        "Offline Identity Detected",
                        "You are currently using an offline/server-issued identity which cannot be recovered if lost.\n\n" +
                        "It is recommended to authenticate with SpacetimeDB SSO for better security and recovery options.\n\n" +
                        "What would you like to do?",
                        "Open Identity Manager",
                        "Publish Anyway",
                        "Cancel");
                    
                    if (result == 0) // Open Identity Manager
                    {
                        OpenIdentityWindow();
                    }
                    else if (result == 1) // Publish Anyway
                    {
                        // Perform the publish operation
                        if (!serverRunning)
                        {
                            serverManager.StartServer();
                        }
                        serverManager.Publish(false);
                        previousDevMode = devMode;
                        publishFirstModule = false;
                        CCCPSettingsAdapter.SetPublishFirstModule(false);
                    }
                    // If result == 2 (Cancel), do nothing
                };
                // Don't execute the rest of the button handler
            }
            // Use reset database if control+alt key is held
            else if (resetDatabase)
            {
                // Display confirmation dialog when resetting database
                if (EditorUtility.DisplayDialog(
                        "Confirm Database Reset",
                        "Are you sure you wish to delete the entire database and publish the module?",
                        "Yes, Reset Database",
                        "Cancel"))
                {
                    serverManager.Publish(true); // Publish with a database reset
                    previousDevMode = devMode; // Sync dev mode state after publish
                }
            }
            else
            {
                if (!serverRunning)
                {
                    serverManager.StartServer();
                }
                serverManager.Publish(false); // Publish without a database reset
                previousDevMode = devMode; // Sync dev mode state after publish
                publishFirstModule = false;
                CCCPSettingsAdapter.SetPublishFirstModule(false);
            }
        }
        
        // Add Generate Unity Files button
        if (!serverManager.PublishAndGenerateMode)
        {
            if (GUILayout.Button("Generate Client Code", GUILayout.Height(30)))
            {
                // Get the project root directory for generate command (same as publish)
                string generateProjectRoot = ServerUtilityProvider.GetProjectRoot(serverManager.ServerDirectory);
                
                // For Docker, convert the Windows path to the container path (/app mount)
                string generateProjectRootForCommand = generateProjectRoot;
                if (serverManager.LocalCLIProvider == "Docker")
                {
                    try
                    {
                        string relativePath = generateProjectRoot.Substring(serverManager.ServerDirectory.Length).TrimStart('\\', '/');
                        generateProjectRootForCommand = string.IsNullOrEmpty(relativePath) ? "/app" : ("/app/" + relativePath).Replace('\\', '/');
                    }
                    catch (Exception ex)
                    {
                        if (debugMode) UnityEngine.Debug.LogError($"[ServerWindow] Failed to convert project root for Docker in generate: {ex.Message}");
                        generateProjectRootForCommand = "/app";
                    }
                }
                else if (serverManager.LocalCLIProvider == "WSL")
                {
                    // WSL needs Windows paths converted to WSL format: C:\path -> /mnt/c/path
                    generateProjectRootForCommand = wslProcess.GetWslPath(generateProjectRoot);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerManager] Converted project root for WSL: {generateProjectRootForCommand}");
                }
                
                // Use absolute path for --out-dir to avoid cross-project generation
                string outDir = serverManager.ClientDirectory;
                
                // Convert client directory path for the CLI provider
                if (serverManager.LocalCLIProvider == "Docker")
                {
                    // Docker mount: /unity points to the Unity project root
                    // Convert C:\path\to\Assets\SpacetimeDBGeneratedClientBindings to /unity/Assets/SpacetimeDBGeneratedClientBindings
                    try
                    {
                        string normalizedPath = outDir.Replace('\\', '/');
                        int unityIndex = normalizedPath.IndexOf("Assets");
                        if (unityIndex >= 0)
                        {
                            outDir = "/unity/" + normalizedPath.Substring(unityIndex);
                        }
                        else
                        {
                            outDir = "/unity" + normalizedPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (debugMode) UnityEngine.Debug.LogError($"[ServerWindow] Failed to convert client path for Docker: {ex.Message}");
                    }
                }
                else if (serverManager.LocalCLIProvider == "WSL")
                {
                    // WSL needs Windows paths converted to WSL format
                    outDir = wslProcess.GetWslPath(outDir);
                }
                
                serverManager.RunServerCommand($"sh -c \"cd '{generateProjectRootForCommand}' && spacetime generate --out-dir '{outDir}' --lang {serverManager.UnityLang} -y\"", "Generating Client Code");
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
        // Refresh SDK status to get the latest state (checks assembly first, then package manager)
        ServerSpacetimeSDKInstaller.IsSDKInstalled((sdkInstalled) => {
            hasSpacetimeDBUnitySDK = sdkInstalled;
            CCCPSettingsAdapter.SetHasSpacetimeDBUnitySDK(sdkInstalled);
        });

        if (localCLIProvider == "Docker")
        {
            if (dockerProcess == null)
            {
                dockerProcess = new ServerDockerProcess(LogMessage, debugMode);
            }

            dockerProcess.CheckPrerequisites((docker, compose, image, mounts) => {
                EditorApplication.delayCall += () => {
                    // Update local state for UI
                    hasDocker = docker;
                    hasDockerCompose = compose;
                    hasDockerImage = image;
                    hasDockerContainerMounts = mounts;
                    
                    Repaint();
                    
                    bool essentialSoftwareDocker = 
                        docker && image && hasSpacetimeDBUnitySDK;

                    bool essentialUserSettingsDocker = 
                        !string.IsNullOrEmpty(serverDirectory) &&
                        !string.IsNullOrEmpty(clientDirectory) &&
                        !string.IsNullOrEmpty(moduleName) &&
                        !string.IsNullOrEmpty(serverLang);

                    List<string> missingSoftware = new List<string>();
                    if (!docker) missingSoftware.Add("- Docker Desktop");
                    if (!image) missingSoftware.Add("- SpacetimeDB Docker Image");
                    if (!hasSpacetimeDBUnitySDK) missingSoftware.Add("- SpacetimeDB Unity SDK");

                    List<string> missingUserSettings = new List<string>();
                    if (string.IsNullOrEmpty(serverDirectory)) missingUserSettings.Add("- Server Directory");
                    if (string.IsNullOrEmpty(clientDirectory)) missingUserSettings.Add("- Client Directory");
                    if (string.IsNullOrEmpty(moduleName)) missingUserSettings.Add("- Server Module");
                    if (string.IsNullOrEmpty(serverLang)) missingUserSettings.Add("- Server Language");

                    HandlePrerequisitesResult(essentialSoftwareDocker, essentialUserSettingsDocker, missingSoftware, missingUserSettings);
                };
            });
        }
        else if (localCLIProvider == "WSL")
        {
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
                    CCCPSettingsAdapter.SetWslPrerequisitesChecked(true); // If we need to see if WSL prerequisites have been checked
                    
                    // Load userName value 
                    userName = serverManager.UserName;
                    
                    Repaint();
                    
                    bool essentialSoftwareWSL = 
                        wsl && debian && trixie && curl && 
                        spacetime && spacetimePath && spacetimeService && git && (rust || netSdk) && hasSpacetimeDBUnitySDK;

                    bool essentialUserSettingsWSL = 
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
                    if (!hasSpacetimeDBUnitySDK) missingSoftware.Add("- SpacetimeDB Unity SDK");

                    List<string> missingUserSettings = new List<string>();
                    if (string.IsNullOrEmpty(userName)) missingUserSettings.Add("- Debian Username");
                    if (string.IsNullOrEmpty(serverDirectory)) missingUserSettings.Add("- Server Directory");
                    if (string.IsNullOrEmpty(clientDirectory)) missingUserSettings.Add("- Client Directory");
                    if (string.IsNullOrEmpty(moduleName)) missingUserSettings.Add("- Server Module");
                    if (string.IsNullOrEmpty(serverLang)) missingUserSettings.Add("- Server Language");

                    HandlePrerequisitesResult(essentialSoftwareWSL, essentialUserSettingsWSL, missingSoftware, missingUserSettings);
                };
            });
        }
    }

    private void HandlePrerequisitesResult(bool essentialSoftware, bool essentialUserSettings, List<string> missingSoftware, List<string> missingUserSettings)
    {
        if (!essentialSoftware || !essentialUserSettings)
        {
            bool needsInstallation = EditorUtility.DisplayDialog(
                $"{localCLIProvider} Setup Required", 
                $"You are missing some essential {localCLIProvider} software and/or settings to run SpacetimeDB.\n" +
                $"Please setup this Software:\n" +
                string.Join("\n", missingSoftware) + "\n" +
                $"Please set these Pre-Requisites:\n" +
                string.Join("\n", missingUserSettings),
                "Software Setup Window", "Pre-Requisites Window"
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
            hasAllPrerequisites = true;
            CCCPSettingsAdapter.SetHasAllPrerequisites(hasAllPrerequisites);

            publishFirstModule = true;
            CCCPSettingsAdapter.SetPublishFirstModule(publishFirstModule);

            if (!serverRunning) serverManager.StartServer();

            // Schedule showing the dialog after a brief delay to let the startup process begin
            EditorApplication.delayCall += () => {
                // Give the server a moment to initialize
                EditorApplication.delayCall += () => {
                    int initModuleAndLogout = EditorUtility.DisplayDialogComplex(
                    $"{localCLIProvider} Setup Complete", 
                    $"All pre-requisites are met to run SpacetimeDB on {localCLIProvider}! \n\n" +
                    "Do the following if this is your first time setting up:\n\n" +
                    "Init New Module (if using a new module)\n\n" +
                    "Logout and then Login again (Refresh Login) to switch from the default SpacetimeDB offline Login to an online Login which is easier to recover.\n\n" +
                    "Note: Remember to copy your auth token to the Pre-Requisites section after your first publish to enable all functionality.",
                    "Init Module and Refresh Login", "Refresh Login", "Return to Main Window"
                    );
                    if (initModuleAndLogout == 0)
                    {
                        InitNewModule();
                        // Also logout and login again to ensure a safe online login
                        LogoutAndLogin(manual:false);
                    } 
                    else if (initModuleAndLogout == 1)
                    {
                        // Only logout and login again to ensure a safe online login
                        LogoutAndLogin(manual:false);
                    }
                };
            };
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

    public async void LogoutAndLogin(bool manual)
    {
        if (serverManager == null)
        {
            LogMessage("[LogoutAndLogin] Server Manager not initialized. Please restart Unity.", -1);
            return;
        }
        if (manual)
        {
            // Shows dialog when manually triggered
            if (!EditorUtility.DisplayDialog(
                "Refresh Login",
                "This will logout and then login again to refresh your SpacetimeDB CLI login. \n\n" +
                "Ensure you keep using the same SSO (i.e. Github etc) when the browser opens to ensure your identity is persistent.\n\n" +
                "If you acquire a different SSO identity, you lose access to any databases that were published under the previous identity unless you backed up that identity.\n\n",
                "Yes, Refresh Login",
                "Cancel"))
            {
                return; // User cancelled the action
            }
        }
        if (serverMode != ServerMode.CustomServer && localCLIProvider == "Docker") {
            if (!serverRunning) // Docker starts and stops the whole container, so start it first if not running
            {
                serverManager.StartServer();
                LogMessage("Waiting for Docker server to start in order to refresh login...", 0);
                await Task.Delay(3000); // Wait for Docker server to start
            }
            serverManager.RunServerCommand("spacetime logout", "Logging out to clear possible offline local login...");
            await Task.Delay(500); // Wait for logout to complete
            serverManager.RunServerCommand("spacetime login", "Launching official SpacetimeDB online login...");
        } else 
        if (serverMode != ServerMode.CustomServer && localCLIProvider == "WSL") {
            if (serverRunning) // WSL is service based, so stop the spacetimedb service first if running
            {
                serverManager.StopServer();
            }
            await Task.Delay(3000); // Wait for WSL server to stop
            serverManager.RunServerCommand("spacetime logout", "Logging out to clear possible offline local login...");
            await Task.Delay(500); // Wait for logout to complete
            serverManager.RunServerCommand("spacetime login", "Launching official SpacetimeDB SEO online login...");
            if (!serverRunning)
            {
                serverManager.StartServer();
            }
        } else
        if (serverMode == ServerMode.CustomServer && CLIAvailableRemote())
        {
            if (!serverRunning) // Docker starts and stops the whole container, so start it first if not running
            {
                serverManager.StartServer();
                LogMessage("Waiting for Docker server to start in order to refresh login...", 0);
                await Task.Delay(3000); // Wait for Docker server to start
            }
            #pragma warning disable CS4014
            serverCustomProcess.RunVisibleSSHCommand($"/home/{sshUserName}/.local/bin/spacetime logout");
            await Task.Delay(1000); // Wait for logout to complete
            serverCustomProcess.RunVisibleSSHCommand($"/home/{sshUserName}/.local/bin/spacetime login");
            #pragma warning restore CS4014
        }
    }

    private async void CheckCLIUpdates()
    {
        bool manualCheck = true;
        if (localCLIProvider == "Docker")
        {
            await serverManager.CheckDockerImageTag(manualCheck);
            await serverManager.CheckSpacetimeDBVersionDocker(manualCheck);
        }
        else if (localCLIProvider == "WSL")
        {
            await serverManager.CheckSpacetimeDBVersionWSL(manualCheck);
            await serverManager.CheckRustVersionWSL(manualCheck);
        }
        await serverManager.CheckSpacetimeSDKVersion(manualCheck);
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
    
    // Has its own color system since the colors sometimes need to be applied to specific parts of the message
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

        // Check for Maincloud balance JSON pattern: {"balance": "123.45"}
        var balanceMatch = System.Text.RegularExpressions.Regex.Match(
            message,
            @"^\s*\{\s*""balance""\s*:\s*""?(?<val>[^\""}]+)""?\s*\}\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (balanceMatch.Success)
        {
            string val = balanceMatch.Groups["val"].Value.Trim();
            // Show only the balance value in green with timestamp
            commandOutputLog += $"<color=#575757>{DateTime.Now:HH:mm:ss}</color> <color=#00FF00>{val}</color>\n";

            EditorApplication.delayCall += Repaint;
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
            case AuthTokenType.Docker:
                storedToken = CCCPSettingsAdapter.GetAuthTokenDocker();
                break;
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

    private void OpenIdentityWindow()
    {
        // Open or focus the Identity Manager window
        ServerIdentityWindow window = EditorWindow.GetWindow<ServerIdentityWindow>();
        
        // Update the window with current settings
        if (window != null && serverManager != null)
        {
            string currentServerMode = serverMode.ToString();
            window.UpdateSettings(this, serverManager, currentServerMode);
        }
    }

    private async void GenerateSSHKeyPairAsync()
    {
        try
        {
            LogMessage("Starting SSH key pair generation...", 0);
            
            // Use the cross-platform utility method
            var result = await ServerUtilityProvider.GenerateSSHKeyPairAsync(null, "", LogMessage);
            
            if (result.success)
            {
                LogMessage("SSH key pair generated successfully!", 1);
                LogMessage($"Private key: {result.privateKeyPath}", 1);
                LogMessage($"Public key: {result.publicKeyPath}", 1);
                
                // Automatically set the private key path in settings
                if (!string.IsNullOrEmpty(result.privateKeyPath))
                {
                    sshPrivateKeyPath = result.privateKeyPath;
                    LogMessage($"Private key path automatically set in settings.", 1);
                }
            }
            else
            {
                LogMessage($"SSH key generation failed: {result.errorMessage}", -1);
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Exception during SSH key generation: {ex.Message}", -1);
        }
    }

    public bool CLIAvailableLocal()
    {
        if (localCLIProvider == "Docker" && hasDocker && hasDockerImage && serverManager.IsCliProviderRunning) // Docker doesn't start automatically, check if running
        {
            if (debugMode) LogMessage("SpacetimeDB Local CLI is available via Docker.", 1);
            return true;
        }
        else if (localCLIProvider == "WSL" && hasWSL && hasDebianTrixie && hasSpacetimeDBServer && hasSpacetimeDBService)
        {
            if (debugMode) LogMessage("SpacetimeDB Local CLI is available via WSL.", 1);
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
        if (isConnectedCustomSSH)
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

        // Update ServerManager with the new mode
        serverManager.SetServerMode((ServerManager.ServerMode)serverMode);

        // Use string representation for consistency with ServerManager
        CCCPSettingsAdapter.SetServerMode((ServerManager.ServerMode)serverMode); 
        
        // Only run set-default commands when there's an actual mode transition and CLI provider is running
        if (previousServerMode != serverMode && (localCLIProvider == "Docker" && serverManager.IsCliProviderRunning) || (localCLIProvider == "WSL" && serverManager.IsCliProviderRunning))
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
            
            // Calculate content dimensions for bottom detection using cached style and content
            // Estimate scroll view width (window width minus padding and scrollbar)
            float estimatedScrollViewWidth = position.width - 40f; // Account for padding and scrollbar
            trimmedLogContent.text = commandOutputLog;
            float contentHeight = richTextStyle.CalcHeight(trimmedLogContent, estimatedScrollViewWidth);
            
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

    private async void InitNewModule()
    {
        serverManager.InitNewModule();
        
        // Wait for the async operation in serverManager to complete
        await System.Threading.Tasks.Task.Delay(1500);
        
        // Verify that the module was successfully initialized before setting flags
        bool moduleCreated = false;
        try
        {
            if (!string.IsNullOrEmpty(serverDirectory) && System.IO.Directory.Exists(serverDirectory))
            {
                var entries = System.IO.Directory.GetFileSystemEntries(serverDirectory);
                moduleCreated = entries != null && entries.Length > 0;
            }
        }
        catch (System.Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"Error verifying module creation: {ex.Message}");
        }
        
        // Only set initialization flags if module was successfully created
        if (moduleCreated)
        {
            initializedFirstModule = true;
            publishFirstModule = true;
            CCCPSettingsAdapter.SetInitializedFirstModule(true);
            CCCPSettingsAdapter.SetPublishFirstModule(publishFirstModule);
        }
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

            // Reconfigure Docker container mount if in Docker mode and module directory changed
            if (isModuleChange && localCLIProvider == "Docker" && dockerProcess != null)
            {
                ReconfigureDockerContainerForNewModule(serverDirectory);
            }

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
                if (localCLIProvider == "Docker")
                {
                    LogMessage($"Please wait - Docker container is being reconfigured to use module: {module.name} at {module.path}", 0);
                }
                else
                {
                    LogMessage($"Selected module: {module.name} at {module.path}", 1);
                }
            }
        }
    }

    /// <summary>
    /// Reconfigures Docker container's /app mount to point to the new module directory.
    /// Only runs if Docker and Docker image are available. The container is removed
    /// if it exists, and will be recreated with correct mounts on next server start.
    /// If the server was running, it will be automatically restarted after reconfiguration.
    /// </summary>
    private void ReconfigureDockerContainerForNewModule(string newServerDirectory)
    {
        try
        {
            // Check Docker prerequisites first
            dockerProcess.CheckPrerequisites((hasDocker, hasDockerCompose, hasDockerImage, hasDockerContainerMounts) =>
            {
                if (!hasDocker || !hasDockerImage)
                {
                    if (debugMode) LogMessage("[Docker] Skipping container reconfiguration - Docker or Docker image not available", 0);
                    return;
                }

                // Run the reconfiguration asynchronously
                EditorApplication.delayCall += async () =>
                {
                    try
                    {
                        EditorUtility.DisplayProgressBar("Docker Container Reconfiguration", "Checking container status...", 0.0f);
                        
                        if (debugMode) LogMessage($"[Docker] Reconfiguring container mount for new module directory: {newServerDirectory}", 0);
                        
                        EditorUtility.DisplayProgressBar("Docker Container Reconfiguration", "Stopping and removing container...", 0.3f);
                        
                        var (success, wasRunning) = await dockerProcess.ReconfigureDockerContainerMount(newServerDirectory);
                        
                        if (success)
                        {
                            EditorUtility.DisplayProgressBar("Docker Container Reconfiguration", "Container reconfigured successfully", 0.7f);
                            
                            if (debugMode) LogMessage("[Docker] Container reconfigured successfully. New mount will apply on next server start.", 1);
                            
                            // Check identity state after reconfiguration to update GUI
                            // Use delayCall to avoid blocking
                            EditorApplication.delayCall += () => CheckIdentityState();
                            
                            // Restart server if it was running before reconfiguration
                            if (wasRunning)
                            {
                                EditorUtility.DisplayProgressBar("Docker Container Reconfiguration", "Restarting server...", 0.8f);
                                
                                if (debugMode) LogMessage("[Docker] Server was running before reconfiguration, restarting...", 0);
                                
                                // Give a brief moment before restarting
                                await Task.Delay(1000);
                                
                                // Start the server again
                                EditorApplication.delayCall += () =>
                                {
                                    if (serverManager != null)
                                    {
                                        serverManager.StartServer();
                                        LogMessage("Local Docker SpacetimeDB Server restarted with new module mount.", 1);
                                    }
                                    
                                    EditorUtility.ClearProgressBar();
                                };
                            }
                            else
                            {
                                EditorUtility.ClearProgressBar();
                            }
                        }
                        else
                        {
                            EditorUtility.ClearProgressBar();
                            LogMessage("[Docker] Failed to reconfigure container mount. You may need to manually restart the container.", -1);
                        }
                    }
                    catch (Exception ex)
                    {
                        EditorUtility.ClearProgressBar();
                        LogMessage($"[Docker] Error during container reconfiguration: {ex.Message}", -1);
                        if (debugMode) UnityEngine.Debug.LogError($"[ServerWindow] Container reconfiguration exception: {ex}");
                    }
                };
            });
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            LogMessage($"[Docker] Error during container reconfiguration: {ex.Message}", -1);
            if (debugMode) UnityEngine.Debug.LogError($"[ServerWindow] Container reconfiguration exception: {ex}");
        }
    }
    #endregion

    // Public method to get ServerManager for external access
    public ServerManager GetServerManager()
    {
        return serverManager;
    }

    /// <summary>
    /// Checks the current identity state and updates persistent storage
    /// </summary>
    private async void CheckIdentityState()
    {
        // Prevent concurrent executions
        if (isCheckingIdentity)
        {
            if (debugMode)
                UnityEngine.Debug.Log("[ServerWindow] Identity check already in progress, skipping");
            return;
        }
        
        if (serverManager == null || !serverManager.HasAllPrerequisites)
        {
            if (debugMode)
                UnityEngine.Debug.Log("[ServerWindow] Skipping identity check - prerequisites not met");
            return;
        }

        if (!serverRunning)
        {
            if (debugMode)
                UnityEngine.Debug.Log("[ServerWindow] Skipping identity check - server not running");
            return;
        }

        isCheckingIdentity = true;
        try
        {
            // Only check if CLI provider is running (for local modes)
            if ((serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer) && !serverManager.IsCliProviderRunning)
            {
                if (debugMode)
                    UnityEngine.Debug.Log("[ServerWindow] Skipping identity check - CLI provider not running");
                return;
            }

            if (debugMode)
                UnityEngine.Debug.Log("[ServerWindow] Checking identity state...");

            // Fetch CLI identity
            var result = await ServerIdentityManager.FetchCliIdentityAsync(serverManager, debugMode);
            
            if (result.info != null)
            {
                // Identity state is automatically saved in FetchCliIdentityAsync
                if (debugMode)
                    UnityEngine.Debug.Log($"[ServerWindow] Identity check complete. Type: {result.info.Type}");
            }
            else
            {
                if (debugMode)
                    UnityEngine.Debug.Log("[ServerWindow] Identity check returned no info");
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
                UnityEngine.Debug.LogError($"[ServerWindow] Error checking identity state: {ex.Message}");
        }
        finally
        {
            isCheckingIdentity = false;
        }
    }

    // Display Cosmos Cove Control Panel title text in the menu bar
    [MenuItem("Window/SpacetimeDB Server Manager/- Cosmos Cove Control Panel -")]
    private static void CosmosCoveControlPanel(){}
    [MenuItem("Window/SpacetimeDB Server Manager/- Cosmos Cove Control Panel -", true)]
    private static bool ValidateCosmosCoveControlPanel(){return false;}

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games