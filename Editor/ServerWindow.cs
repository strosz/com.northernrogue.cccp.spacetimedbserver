using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System;

// The main Comos Cove Control Panel that controls the server and launches all features ///

namespace NorthernRogue.CCCP.Editor {

public class ServerWindow : EditorWindow
{
    // Server Manager
    private ServerManager serverManager;
    
    // Process Handlers
    private ServerCMDProcess cmdProcessor;
    private ServerLogProcess logProcessor;
    private ServerCustomProcess serverCustomProcess;
    private ServerDetectionProcess detectionProcess;
    private Process serverProcess;
    
    // Server mode
    private ServerMode serverMode = ServerMode.WslServer;

    // Pre-requisites WSL
    private bool hasWSL = false;
    private bool hasDebian = false;
    private bool hasDebianTrixie = false;
    private bool hasCurl = false;
    private bool hasSpacetimeDBServer = false;
    private bool hasSpacetimeDBPath = false;
    private bool hasRust = false;
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
   
    // Server status
    private double lastCheckTime = 0;
    private const double checkInterval = 5.0;

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

    // UI
    private Vector2 scrollPosition;
    private string commandOutputLog = "";
    private bool autoscroll = true;
    private bool colorLogo = true;
    private Texture2D logoTexture;

    // UI optimization
    private const double changeCheckInterval = 3.0; // More responsive interval when window is in focus
    private bool windowFocused = false;
    
    // Session state key for domain reload
    private const string SessionKeyWasRunningSilently = "ServerWindow_WasRunningSilently";
    private const string PrefsKeyPrefix = "CCCP_";

    // Add a field to track WSL status
    private bool isWslRunning = false;

    public static string Documentation = "https://docs.google.com/document/d/1HpGrdNicubKD8ut9UN4AzIOwdlTh1eO4ampZuEk5fM0/edit?usp=sharing";

    [MenuItem("SpacetimeDB/Server Management Panel", priority = -9000)]
    public static void ShowWindow()
    {
        ServerWindow window = GetWindow<ServerWindow>("Server");
        window.minSize = new Vector2(270f, 600f);
    }

    public enum ServerMode
    {
        WslServer,
        CustomServer,
        MaincloudServer,
    }

    #region OnGUI

    private void OnGUI()
    {
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
            GUILayout.Label("Begin by checking the pre-requisites", subTitleStyle);
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(-15);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // Logo version and color control
            GUIStyle titleControlStyle = new GUIStyle(EditorStyles.miniLabel);
            titleControlStyle.fontSize = 10;
            titleControlStyle.normal.textColor = new Color(0.43f, 0.43f, 0.43f);
            GUILayout.Label("v" + ServerUpdateProcess.GetCurrentPackageVersion(), titleControlStyle, GUILayout.Width(33));
            if (GUILayout.Button("color", titleControlStyle, GUILayout.Width(30)))
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
        EditorGUILayout.LabelField("Command Output:", EditorStyles.boldLabel, GUILayout.Width(120));

        GUILayout.FlexibleSpace();

        EditorGUILayout.BeginVertical();
        EditorGUILayout.Space(2);
        GUIStyle autoscrollStyle = new GUIStyle(EditorStyles.miniLabel);
        autoscrollStyle.fontSize = 12;
        autoscrollStyle.normal.textColor = autoscroll ? new Color(0.43f, 0.43f, 0.43f) : new Color(0.3f, 0.3f, 0.3f);
        if (GUILayout.Button("autoscroll", autoscrollStyle, GUILayout.Width(75)))
        {
            autoscroll = !autoscroll;
            Repaint();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(-10);

        // Clear button
        EditorGUILayout.BeginVertical();
        EditorGUILayout.Space(2);
        GUIStyle clearStyle = new GUIStyle(EditorStyles.miniLabel);
        clearStyle.fontSize = 12;
        clearStyle.normal.textColor = new Color(0.43f, 0.43f, 0.43f);
        if (GUILayout.Button("clear", clearStyle, GUILayout.Width(50)))
        {
            commandOutputLog = "";
            Repaint();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        
        // Output log with rich text support
        GUIStyle richTextStyle = new GUIStyle(EditorStyles.textArea);
        richTextStyle.richText = true;
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        EditorGUILayout.TextArea(commandOutputLog.TrimEnd('\n'), richTextStyle, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        
        // Auto-scroll to bottom if enabled
        if (autoscroll && Event.current.type == EventType.Repaint)
        {
            scrollPosition.y = float.MaxValue;
        }

        // Github Update Button
        if (ServerUpdateProcess.IsGithubUpdateAvailable())
        {
            if (GUILayout.Button("New Update for CCCP Available"))
            {
                ServerUpdateProcess.UpdateGithubPackage();
            }
        }
        
        EditorGUILayout.EndVertical();

        // Register for focus events
        EditorApplication.focusChanged += OnFocusChanged;

        // Start checking server status and file changes
        EditorApplication.update += EditorUpdateHandler;
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
        
        // Register for focus events
        EditorApplication.focusChanged += OnFocusChanged;
                
        // Start checking server status
        EditorApplication.update += EditorUpdateHandler;

        // Check if we were previously running silently and restore state if needed
        bool wasRunningSilently = SessionState.GetBool(SessionKeyWasRunningSilently, false);
        if (wasRunningSilently && serverManager.IsServerStarted && serverManager.SilentMode)
        {
            if (serverManager.DebugMode) UnityEngine.Debug.Log("[ServerWindow OnEnable] Detected potentially lost tail process from previous session. Attempting restart.");
            AttemptTailRestartAfterReload();
        } else if (!serverManager.IsServerStarted || !serverManager.SilentMode) {
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
            EditorApplication.delayCall += async () => {
                try {
                    await serverManager.CheckWslStatus();
                    isWslRunning = serverManager.IsWslRunning;
                    Repaint();
                } catch (Exception ex) {
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
        hasRust = serverManager.HasRust;
        
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

        // Add this line to initialize WSL status
        isWslRunning = serverManager.IsWslRunning;
    }
   
    private async void EditorUpdateHandler()
    {
        if (serverManager == null) return;
        
        // Throttle how often we check things to not overload the main thread
        double currentTime = EditorApplication.timeSinceStartup;
        
        // Check server status periodically
        if (currentTime - lastCheckTime > checkInterval)
        {
            lastCheckTime = currentTime;
            await serverManager.CheckAllStatus();
            
            // Update local state for UI display
            serverChangesDetected = serverManager.ServerChangesDetected;
            isWslRunning = serverManager.IsWslRunning;
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
    
    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChange; // Remove handler

        // Save state before disabling (might be domain reload)
        // Ensure SessionState reflects the state *just before* disable/reload
        SessionState.SetBool(SessionKeyWasRunningSilently, serverManager.IsServerStarted && serverManager.SilentMode);

        EditorApplication.update -= EditorUpdateHandler;
    }
    
    private void OnFocusChanged(bool focused)
    {
        windowFocused = focused;
    }
    
    private void OnServerChangesDetected(bool changesDetected)
    {
        // Update local state for UI
        serverChangesDetected = changesDetected;
        Repaint();
    }
    #endregion
    
    #region Pre-RequisitesUI

    private void DrawPrerequisitesSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox); // Start of Pre-Requisites section

        bool showPrerequisites = EditorGUILayout.Foldout(EditorPrefs.GetBool(PrefsKeyPrefix + "ShowPrerequisites", true), "Pre-Requisites", true);
        EditorPrefs.SetBool(PrefsKeyPrefix + "ShowPrerequisites", showPrerequisites);

        if (showPrerequisites)
        {
            EditorGUILayout.Space(-10);

            EditorGUILayout.LabelField("Server Mode", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            // Active Server Mode
            GUIStyle activeToolbarButton = new GUIStyle(EditorStyles.toolbarButton);
            activeToolbarButton.normal.textColor = Color.green;

            // Inactive Server Mode
            GUIStyle inactiveToolbarButton = new GUIStyle(EditorStyles.toolbarButton);
            inactiveToolbarButton.normal.textColor = Color.gray5;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            string wslModeTooltip = "Run a local server with SpacetimeDB on Debian WSL";
            if (GUILayout.Button(new GUIContent("WSL Local", wslModeTooltip), serverMode == ServerMode.WslServer ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                serverMode = ServerMode.WslServer;
                UpdateServerModeState();
            }
            string customModeTooltip = "Connect to your custom remote server and run spacetime commands";
            if (GUILayout.Button(new GUIContent("Custom Remote", customModeTooltip), serverMode == ServerMode.CustomServer ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                if (serverManager.serverStarted)
                {
                    bool modeChange = EditorUtility.DisplayDialog("Confirm Mode Change", "Do you want to stop your WSL Server and change the server mode to Custom server?","OK","Cancel");
                    if (modeChange)
                    {
                        serverManager.StopServer();
                        serverMode = ServerMode.CustomServer;
                        UpdateServerModeState();
                    }
                } 
                else 
                {
                    serverMode = ServerMode.CustomServer;
                    UpdateServerModeState();
                }
            }
            string maincloudModeTooltip = "Connect to the official SpacetimeDB cloud server and run spacetime commands";
            if (GUILayout.Button(new GUIContent("Maincloud", maincloudModeTooltip), serverMode == ServerMode.MaincloudServer ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                serverMode = ServerMode.MaincloudServer;
                UpdateServerModeState();
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

            // Backup Directory setting
            EditorGUILayout.BeginHorizontal();
            string backupDirectoryTooltip = 
            "Directory where SpacetimeDB server backups will be saved.\n\n"+
            "Note: Create a new empty folder if the server backups have not been created yet.\n"+
            "Backups for the server use little space, so you can commit this folder to your repository.";
            EditorGUILayout.LabelField(new GUIContent("Backup Directory:", backupDirectoryTooltip), GUILayout.Width(110));
            if (GUILayout.Button("Set Backup Directory", GUILayout.Width(150), GUILayout.Height(20)))
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

            // Server Directory setting
            EditorGUILayout.BeginHorizontal();
            string serverDirectoryTooltip = 
            "Directory of your SpacetimeDB server module where Cargo.toml is located.\n\n"+
            "Note: Create a new empty folder if the module has not been created yet.";
            EditorGUILayout.LabelField(new GUIContent("Server Directory:", serverDirectoryTooltip), GUILayout.Width(110));
            if (GUILayout.Button("Set Server Directory", GUILayout.Width(150), GUILayout.Height(20)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Server Directory", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    serverDirectory = path;
                    serverManager.SetServerDirectory(serverDirectory);
                    LogMessage($"Server directory set to: {serverDirectory}", 1);
                    
                    // Update the detection process with the new directory
                    if (detectionProcess != null)
                    {
                        detectionProcess.Configure(serverDirectory, detectServerChanges);
                    }
                }
            }
            GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(serverDirectory)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();
                        
            // Server Language dropdown
            EditorGUILayout.BeginHorizontal();
            string serverLangTooltip = 
            "Rust: The default programming language for SpacetimeDB server modules. \n\n"+
            "C-Sharp: The C# programming language for SpacetimeDB server modules. \n\n"+
            "Recommended: Rust which is 2x faster than C#.";
            EditorGUILayout.LabelField(new GUIContent("Server Language:", serverLangTooltip), GUILayout.Width(110));
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

            // Unity Autogenerated files Directory setting
            EditorGUILayout.BeginHorizontal();
            string clientDirectoryTooltip = 
            "Directory where Unity client scripts will be generated.\n\n"+
            "Note: This should be placed in the Assets folder of your Unity project.";
            EditorGUILayout.LabelField(new GUIContent("Client Directory:", clientDirectoryTooltip), GUILayout.Width(110));
            if (GUILayout.Button("Set Client Directory", GUILayout.Width(150), GUILayout.Height(20)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Client Directory", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    clientDirectory = path;
                    serverManager.SetClientDirectory(clientDirectory);
                    LogMessage($"Client directory set to: {clientDirectory}", 1);
                }
            }
            GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(clientDirectory)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Unity Autogenerated files Language dropdown
            EditorGUILayout.BeginHorizontal();
            string unityLangTooltip = 
            "C-Sharp: The default programming language for auto-generated Unity client scripts. \n\n"+
            "Rust: Programming language for auto-generated Unity client scripts. \n\n"+
            "Typescript: Programming language for auto-generated Unity client scripts. \n\n"+
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

            // Module Name setting
            EditorGUILayout.BeginHorizontal();
            string moduleNameTooltip = 
            "The name of your existing SpacetimeDB module you used when you created the module.\n"+
            "OR the name you want your SpacetimeDB module to have when initializing a new one";
            EditorGUILayout.LabelField(new GUIContent("Module Name:", moduleNameTooltip), GUILayout.Width(110));
            string newModuleName = EditorGUILayout.TextField(moduleName, GUILayout.Width(150));
            if (newModuleName != moduleName)
            {
                moduleName = newModuleName;
                serverManager.SetModuleName(moduleName);
            }
            GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(moduleName)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Init a new module
            EditorGUILayout.BeginHorizontal();
            string initModuleTooltip = 
            "Init a new module: Initializes a new SpacetimeDB module in the server directory.";
            EditorGUILayout.LabelField(new GUIContent("New module:", initModuleTooltip), GUILayout.Width(110));
            if (GUILayout.Button("Init a new module", GUILayout.Width(150), GUILayout.Height(20)))
            {
                InitNewModule();
            }
            EditorGUILayout.EndHorizontal();
            #endregion

            #region WSL Mode
            if (serverMode == ServerMode.WslServer)
            {
                // WSL Settings
                GUILayout.Label("WSL Server Settings", EditorStyles.centeredGreyMiniLabel);

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
                "Default: http://127.0.0.1:3000/\n" +
                "Note: The port number is required.";
                EditorGUILayout.LabelField(new GUIContent("URL:", urlTooltip), GUILayout.Width(110));
                string newUrl = EditorGUILayout.TextField(serverUrl, GUILayout.Width(150));
                if (newUrl != serverUrl)
                {
                    serverUrl = newUrl;
                    serverManager.SetServerUrl(serverUrl);
                    // Extract port from URL
                    int extractedPort = ExtractPortFromUrl(serverUrl);
                    if (extractedPort > 0 && extractedPort != serverPort)
                    {
                        serverPort = extractedPort;
                        serverManager.SetServerPort(serverPort);
                        if (debugMode) LogMessage($"Port extracted from URL: {serverPort}", 0);
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
                string tokenTooltip = 
                "Required for the Server Database Window. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.";
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(authToken, GUILayout.Width(150));
                if (newAuthToken != authToken)
                {
                    authToken = newAuthToken;
                    serverManager.SetAuthToken(authToken);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(authToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Launch WSL Server Installer"))
                    ServerInstallerWindow.ShowWindow();
                if (GUILayout.Button("Check Pre-Requisites", GUILayout.Height(20)))
                    CheckPrerequisites();

                // WSL Status display - add after the Check Pre-requisites button
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("WSL:", GUILayout.Width(110));
                GUIStyle wslStatusStyle = new GUIStyle(EditorStyles.label);
                wslStatusStyle.normal.textColor = isWslRunning ? Color.green : Color.gray;
                string wslStatusText = isWslRunning ? "Running" : "Stopped";
                EditorGUILayout.LabelField(wslStatusText, wslStatusStyle);
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
                if (GUILayout.Button("Generate SSH Key Pair", GUILayout.Width(150)))
                {
                    if (cmdProcessor == null)
                    {
                        cmdProcessor = new ServerCMDProcess(LogMessage, debugMode);
                    }
                    cmdProcessor.RunPowerShellCommand("ssh-keygen -t ed25519", LogMessage);
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
                    string path = EditorUtility.OpenFilePanel("Select SSH Private Key", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.ssh", "");
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
                EditorGUILayout.LabelField(new GUIContent("Distro Username:", userNameTooltip), GUILayout.Width(110));
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
                "The full URL of your SpacetimeDB server including port number.\n" +
                "Note: The port number is required.";
                EditorGUILayout.LabelField(new GUIContent("URL:", urlTooltip), GUILayout.Width(110));
                string newUrl = EditorGUILayout.TextField(customServerUrl, GUILayout.Width(150));
                if (newUrl != customServerUrl)
                {
                    customServerUrl = newUrl;
                    serverManager.SetCustomServerUrl(customServerUrl);
                    
                    // Extract port from URL
                    int extractedPort = ExtractPortFromUrl(customServerUrl);
                    if (extractedPort > 0 && extractedPort != customServerPort)
                    {
                        customServerPort = extractedPort;
                        serverManager.SetCustomServerPort(customServerPort);
                        
                        // Also set the main serverPort for consistency
                        serverPort = customServerPort;
                        serverManager.SetServerPort(serverPort);
                        
                        if (debugMode) LogMessage($"Port extracted from URL: {customServerPort}", 0);
                    }
                    else
                    {
                        LogMessage("No valid port found in URL. Please include port in format 'http://127.0.0.1:3000/'", -2);
                    }
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(customServerUrl)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                // Custom Serer Auth Token
                EditorGUILayout.BeginHorizontal();
                string tokenTooltip = 
                "Required for the Server Database Window. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.";
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(customServerAuthToken, GUILayout.Width(150));
                if (newAuthToken != customServerAuthToken)
                {
                    customServerAuthToken = newAuthToken;
                    serverManager.SetCustomServerAuthToken(customServerAuthToken);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(customServerAuthToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Show Documentation"))
                    Application.OpenURL(ServerWindow.Documentation);
                if (GUILayout.Button("Check Pre-Requisites and Connect", GUILayout.Height(20)))
                    CheckPrerequisitesCustom();

                // Connection status display
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connection:", GUILayout.Width(110));
                // Only use cached value, update in background
                if (serverCustomProcess != null)
                {
                    serverCustomProcess.UpdateSessionStatusIfNeeded();
                }
                GUIStyle connectionStatusStyle = new GUIStyle(EditorStyles.label);
                isConnected = serverCustomProcess != null && serverCustomProcess.IsSessionActive();
                connectionStatusStyle.normal.textColor = isConnected ? Color.green : Color.gray;
                string connectionStatusText = isConnected ? "Connected SSH" : "Disconnected";
                EditorGUILayout.LabelField(connectionStatusText, connectionStatusStyle);
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
                    serverManager.RunServerCommand("spacetime login", "Logging in");
                }
                EditorGUILayout.EndHorizontal();

                // Auth Token setting
                EditorGUILayout.BeginHorizontal();
                string tokenTooltip = 
                "Required for the Server Database Window. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
                "Important: Keep this token secret and do not share it with anyone outside of your team.";
                EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
                string newAuthToken = EditorGUILayout.PasswordField(authToken, GUILayout.Width(150));
                if (newAuthToken != authToken)
                {
                    authToken = newAuthToken;
                    serverManager.SetAuthToken(authToken);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(authToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Launch Official Webpanel"))
                    Application.OpenURL("https://spacetimedb.com/login");
                if (GUILayout.Button("Check Pre-Requisites and Connect", GUILayout.Height(20)))
                    CheckPrerequisitesMaincloud();

                // Connection status display
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connection:", GUILayout.Width(110));
                GUIStyle maincloudConnectionStyle = new GUIStyle(EditorStyles.label);
                bool isMaincloudConnected = serverManager.IsMaincloudConnected;
                maincloudConnectionStyle.normal.textColor = isMaincloudConnected ? Color.green : Color.gray;
                string maincloudStatusText = isMaincloudConnected ? "Connected" : "Disconnected";
                EditorGUILayout.LabelField(maincloudStatusText, maincloudConnectionStyle);
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
        bool showSettingsWindow = EditorGUILayout.Foldout(EditorPrefs.GetBool(PrefsKeyPrefix + "ShowSettingsWindow", true), "Settings", true);
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
            "Publish will Generate: Publish button publishes the module and generates the Unity files. \n\n"+
            "Separate Generate: Separate generate button to generate the Unity files.\n\n"+
            "Recommended: Publish will Generate.";
            EditorGUILayout.LabelField(new GUIContent("Publish and Generate:", publishGenerateTooltip), GUILayout.Width(120));
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
                "Close WSL at Server Stop: The server will close the WSL process when it is stopped or Unity is closed. \n"+
                "Saves resources when server is not in use. WSL may otherwise leave several processes running.\n\n"+
                "Keep Running: The server will keep the WSL process running after it is stopped or Unity is closed.\n\n"+
                "Recommended: Close WSL at Server Stop";
                EditorGUILayout.LabelField(new GUIContent("WSL Auto Close:", wslCloseTooltip), GUILayout.Width(120));
                GUIStyle wslCloseStyle = new GUIStyle(GUI.skin.button);
                if (serverManager.AutoCloseWsl)
                {
                    wslCloseStyle.normal.textColor = warningColor;
                    wslCloseStyle.hover.textColor = warningColor;
                }
                if (GUILayout.Button(serverManager.AutoCloseWsl ? "Close WSL at Server Stop" : "Keep Running", wslCloseStyle))
                {
                    bool newAutoClose = !serverManager.AutoCloseWsl;
                    serverManager.SetAutoCloseWsl(newAutoClose);
                    autoCloseWsl = newAutoClose; // Keep local field in sync
                }
                EditorGUILayout.EndHorizontal();
            }

            // Command Output toggle button
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            string commandOutputTooltip = 
            "Hiding Extra Warnings: Will hide extra SpacetimeDB warning messages in the command output. \n\n"+
            "Showing All Messages: Will show all messages in the command output.\n\n"+
            "Recommended: Hide Extra Warnings.";
            EditorGUILayout.LabelField(new GUIContent("Command Output:", commandOutputTooltip), GUILayout.Width(120));
            GUIStyle warningToggleStyle = new GUIStyle(GUI.skin.button);
            if (serverManager.HideWarnings)
            {
                warningToggleStyle.normal.textColor = warningColor;
                warningToggleStyle.hover.textColor = warningColor;
            }
            if (GUILayout.Button(serverManager.HideWarnings ? "Hiding Extra Warnings" : "Show All Messages", warningToggleStyle))
            {
                bool newHideWarnings = !serverManager.HideWarnings;
                serverManager.SetHideWarnings(newHideWarnings);
                hideWarnings = newHideWarnings; // Keep local field in sync
            }
            EditorGUILayout.EndHorizontal();

            if (serverManager.SilentMode && serverMode == ServerMode.WslServer)
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
                debugMode = newDebugMode; // Keep local field in sync
                
                // Update other components that need to know about debug mode
                ServerOutputWindow.debugMode = newDebugMode;
                ServerWindowInitializer.debugMode = newDebugMode;
                ServerUpdateProcess.debugMode = newDebugMode;
                ServerLogProcess.debugMode = newDebugMode;
                ServerCMDProcess.debugMode = newDebugMode;
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

        if (serverMode == ServerMode.WslServer)
        EditorGUILayout.LabelField("WSL Local Mode", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(13));
        else if (serverMode == ServerMode.CustomServer)
        EditorGUILayout.LabelField("Custom Remote Mode", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(13));
        else if (serverMode == ServerMode.MaincloudServer)
        EditorGUILayout.LabelField("Maincloud Mode", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(13));
        
        if (serverMode != ServerMode.MaincloudServer)
        {
            bool serverRunning = serverManager.IsServerStarted || serverManager.IsStartingUp;
            if (!serverManager.WslPrerequisitesChecked || !serverManager.HasWSL || !serverManager.HasDebian)
            {
                if (GUILayout.Button("Check Prerequisites to Start Server", GUILayout.Height(30)))
                {
                    CheckPrerequisites();
                }
            }
            else // If Prerequisites are checked then show normal server controls
            {
                if (!serverRunning)
                {
                    if (GUILayout.Button("Start Server", GUILayout.Height(30)))
                    {
                        serverManager.StartServer();
                    }
                } 
                else 
                {
                    if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
                    {
                        serverManager.StopServer();
                    }
                }
            }
        }
        // Activation of Server Windows
        bool wslServerActive = serverManager.IsServerStarted && serverMode == ServerMode.WslServer;
        bool wslServerActiveSilent = serverManager.SilentMode && serverMode == ServerMode.WslServer;
        bool customServerActive = isConnected && serverMode == ServerMode.CustomServer;
        bool maincloudActive = serverManager.IsMaincloudConnected && serverMode == ServerMode.MaincloudServer;

        EditorGUI.BeginDisabledGroup(!wslServerActiveSilent);
        if (GUILayout.Button("View Server Logs", GUILayout.Height(20)))
        {
            serverManager.ViewServerLogs();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!wslServerActive && !customServerActive && !maincloudActive);
        if (GUILayout.Button("View Server Database", GUILayout.Height(20)))
        {
            ServerDataWindow.ShowWindow();
        }
        EditorGUI.EndDisabledGroup();
        
        EditorGUI.BeginDisabledGroup(!wslServerActive && !customServerActive && !maincloudActive);
        if (GUILayout.Button("Run Server Reducer", GUILayout.Height(20)))
        {
            ServerReducerWindow.ShowWindow();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Server Status:", GUILayout.Width(110));
        
        // Use green for running/starting, gray for stopped
        GUIStyle statusStyle = new GUIStyle(EditorStyles.label);
        string statusText;
        
        if (serverMode == ServerMode.MaincloudServer && serverManager.IsMaincloudConnected)
        {
            statusStyle.normal.textColor = Color.green;
            statusText = "Maincloud";
        }
        else if (serverManager.IsStartingUp)
        {
            statusStyle.normal.textColor = Color.green;
            statusText = "Starting...";
        }
        else if (serverManager.IsServerStarted)
        {
            statusStyle.normal.textColor = Color.green;
            statusText = "Running";
        }
        else
        {
            statusStyle.normal.textColor = Color.gray;
            statusText = "Stopped";
        }
        
        EditorGUILayout.LabelField(statusText, statusStyle);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }
    #endregion

    #region UtilityUI

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

            if (GUILayout.Button("Show Login Info", GUILayout.Height(20)))
            {
                serverManager.RunServerCommand("spacetime login show --token", "Showing SpacetimeDB login info and token");
            }

            if (GUILayout.Button("Show Server Config", GUILayout.Height(20)))
            {
                serverManager.RunServerCommand("spacetime server list", "Showing SpacetimeDB server config");
            }

            if (GUILayout.Button("Show Active Modules", GUILayout.Height(20)))
            {
                serverManager.RunServerCommand("spacetime list", "Showing active modules");
            }
            
            if (GUILayout.Button("Ping Server", GUILayout.Height(20)))
            {
                serverManager.PingServer(true);
            }
            
            if (GUILayout.Button("Show Version", GUILayout.Height(20)))
            {
                serverManager.RunServerCommand("spacetime --version", "Showing SpacetimeDB version");
            }

            EditorGUILayout.LabelField("WSL Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            if (GUILayout.Button("Open Debian Window", GUILayout.Height(20)))
            {
                serverManager.OpenDebianWindow();
            }
            
            string backupTooltip = "Creates a tar archive of the DATA folder in your SpacetimeDB server, which contains the database, logs and settings of your module.";
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(serverManager.BackupDirectory));
            if (GUILayout.Button(new GUIContent("Backup Server Data", backupTooltip), GUILayout.Height(20)))
            {  
                serverManager.BackupServerData();
            }
            EditorGUI.EndDisabledGroup();

            string restoreTooltip = "Unpacks and copies over the selected backup archive. DELETES the current DATA folder of your SpacetimeDB server. You will asked to backup before if you have not done so.";
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(serverManager.BackupDirectory));
            if (GUILayout.Button(new GUIContent("Restore Server Data", restoreTooltip), GUILayout.Height(20)))
            {
                serverManager.RestoreServerData();
            }
            EditorGUI.EndDisabledGroup();
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
            string displayText = !serverManager.AutoPublishMode ? "New Server Update! (Click to dismiss)" : "New Server Update! (Auto Publish Mode) (Click to dismiss)";
            string tooltip = "Server Changes have been detected and are ready to be published as a new version of your module. Click to dismiss this notification until new changes are detected.";
            
            // Create a button-like appearance
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            // Use a button that looks like a label for better click response
            if (GUILayout.Button(new GUIContent(displayText, tooltip), updateStyle))
            {
                // Call serverManager to reset change detection
                // TODO: Add method to ServerManager to reset detection state
                Repaint();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        
        if (serverManager.PublishAndGenerateMode) {

                if (!serverManager.AutoPublishMode)
                {
                    EditorGUILayout.LabelField("Will Publish then Generate Unity Files automatically.\n" + 
                        "Ctrl + Alt + Click to also reset the database.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30));
                } else {
                    EditorGUILayout.LabelField("Will Publish then Generate Unity Files automatically on detected changes.\n" + 
                        "Ctrl + Alt + Click to also reset the database.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30));
                }
        } else {
            EditorGUILayout.LabelField("First Publish then Generate Unity Files.\n" + 
                        "Ctrl + Alt + Click to also reset the database.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30));
        }
        
        // Add Publish Module button
        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(serverManager.ModuleName));
        
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

        string buttonText;
        if (serverMode == ServerMode.MaincloudServer)
        buttonText = resetDatabase ? "Publish Module and Reset Database" : "Publish Module to Maincloud";
        else
        buttonText = resetDatabase ? "Publish Module and Reset Database" : "Publish Module";
        
        if (GUILayout.Button(buttonText, publishButtonStyle, GUILayout.Height(37)))
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
            }
        }
        
        // Add Generate Unity Files button
        if (!serverManager.PublishAndGenerateMode)
        {
            if (GUILayout.Button("Generate Unity Files", GUILayout.Height(37)))
            {
                string outDir = serverManager.GetRelativeClientPath();
                serverManager.RunServerCommand($"spacetime generate --out-dir {outDir} --lang {serverManager.UnityLang}", "Generating Unity files");
                LogMessage($"Generated Unity files to: {outDir}", 1);
            }
        }
        
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.EndVertical();
    }
    #endregion

    #region Check Pre-reqs

    public void CheckPrerequisites()
    {
        serverManager.CheckPrerequisites((wsl, debian, trixie, curl, spacetime, spacetimePath, rust) => {
            EditorApplication.delayCall += () => {
                // Update local state for UI
                hasWSL = wsl;
                hasDebian = debian;
                hasDebianTrixie = trixie;
                hasCurl = curl;
                hasSpacetimeDBServer = spacetime;
                hasSpacetimeDBPath = spacetimePath;
                hasRust = rust;
                wslPrerequisitesChecked = true;
                
                // No need to directly update ServerManager properties as this is now handled in the ServerManager
                
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
        
        bool connectionSuccessful = serverCustomProcess.StartSession();
        
        if (connectionSuccessful)
        {           
            await serverCustomProcess.CheckSpacetimeDBInstalled();
            await serverCustomProcess.GetSpacetimeDBVersion();
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
            if (!string.IsNullOrEmpty(serverManager.AuthToken))
            {
                LogMessage("Auth token found, verifying authentication...", 0);
                RunServerCommand("spacetime login show --token", "Verifying Maincloud authentication");
            }
            else
            {
                LogMessage("Warning: No auth token set. Please login to Maincloud using the 'Login to Maincloud' button.", -2);
            }
        }
        else
        {
            LogMessage("Failed to connect to Maincloud. Please check your internet connection and make sure your module name is correct.", -1);
        }
    }
    #endregion

    #region Server Methods

    private void Publish(bool resetDatabase)
    {
        serverManager.Publish(resetDatabase);
    }

    private void InitNewModule()
    {
        serverManager.InitNewModule();
        initializedFirstModule = true;
        serverManager.SetInitializedFirstModule(true);
    }

    public void ClearModuleLogFile() // Clears the module tmp log file
    {
        if (debugMode) LogMessage("Clearing log file...", 0);
        logProcessor.ClearModuleLogFile();
        if (debugMode) LogMessage("Log file cleared successfully", 1);
    }
    
    public void ClearDatabaseLog() // Clears the database log content (only kept in memory for performance)
    {
        if (debugMode) LogMessage("Clearing database log...", 0);
        logProcessor.ClearDatabaseLog();
        if (debugMode) LogMessage("Database log cleared successfully", 1);
    }
    #endregion

    private void RunServerCommand(string command, string description)
    {
        serverManager.RunServerCommand(command, description);
    }

    #region LogMessage
    
    public void LogMessage(string message, int style)
    {
        // Skip warning messages if hideWarnings is enabled
        if (serverManager.HideWarnings && message.Contains("WARNING"))
        {
            return;
        }
        
        if (style == 1) // Success
        {
            string coloredMessage = $"<color=#00FF00>{message}</color>";
            commandOutputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {coloredMessage}\n";
        } 
        else if (style == -1) // Error
        {
            string coloredMessage = $"<color=#FF0000>{message}</color>";
            commandOutputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {coloredMessage}\n";
        }
        else if (style == -2) // Warning
        {
            string coloredMessage = $"<color=#e0a309>{message}</color>";
            commandOutputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {coloredMessage}\n";
        }
        else // Normal (style == 0) Also catches any other style
        { 
            commandOutputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {message}\n";
        }
        
        // Trim log if it gets too long
        if (commandOutputLog.Length > 10000)
        {
            commandOutputLog = commandOutputLog.Substring(commandOutputLog.Length - 10000);
        }
        
        Repaint();
    }

    // Opens Output Log window in silent mode or CMD window with Database logs in CMD mode
    private void ViewServerLogs()
    {
        serverManager.ViewServerLogs();
    }

    private void OpenDebianWindow()
    {
        serverManager.OpenDebianWindow();
    }

    private bool PingServerStatus()
    {
        return serverManager.PingServerStatus();
    }

    private void PingServer(bool showLog)
    {
        serverManager.PingServer(showLog);
    }
    #endregion
    
    #region Utility Methods

    private string GetWslPath(string windowsPath)
    {
        return cmdProcessor.GetWslPath(windowsPath);
    }
    
    private string GetStatusIcon(bool status)
    {
        return status ? "✓" : "○";
    }
    
    private string GetRelativeClientPath()
    {
        // Default path if nothing else works
        string defaultPath = "../Assets/Scripts/Server";
        
        if (string.IsNullOrEmpty(clientDirectory))
        {
            return defaultPath;
        }
        
        try
        {
            // Normalize path to forward slashes
            string normalizedPath = clientDirectory.Replace('\\', '/');
            
            // If the path already starts with "../Assets", use it directly
            if (normalizedPath.StartsWith("../Assets"))
            {
                return normalizedPath;
            }
            
            // Find the "Assets" directory in the path
            int assetsIndex = normalizedPath.IndexOf("Assets/");
            if (assetsIndex < 0)
            {
                assetsIndex = normalizedPath.IndexOf("Assets");
            }
            
            if (assetsIndex >= 0)
            {
                // Extract from "Assets" to the end and prepend "../"
                string relativePath = "../" + normalizedPath.Substring(assetsIndex);
                
                // Ensure it has proper structure
                if (!relativePath.Contains("/"))
                {
                    relativePath += "/";
                }
                
                // Add quotes if path contains spaces
                if (relativePath.Contains(" "))
                {
                    return $"\"{relativePath}\"";
                }
                return relativePath;
            }
            
            // If no "Assets" in path, just return default
            return defaultPath;
        }
        catch (Exception ex)
        {
            if (debugMode) LogMessage($"Error in path handling: {ex.Message}", -1);
            return defaultPath;
        }
    }
    #endregion

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

    public void AttemptTailRestartAfterReload()
    {
        if (debugMode) UnityEngine.Debug.Log($"[ServerWindow] Attempting tail restart in ServerWindow.");
        serverManager.AttemptTailRestartAfterReload();
    }

    public void StopTailProcessExplicitly()
    {
        serverManager.StopTailProcessExplicitly();
    }

    public void AttemptDatabaseLogRestartAfterReload()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerWindow] Checking database log process");
        serverManager.AttemptDatabaseLogRestartAfterReload();
    }

    // Separate Exited handler for the background process (Silent Mode)
    private void HandleBackgroundProcessExited(object sender, EventArgs e)
    {
         EditorApplication.delayCall += () => {
            try {
                int exitCode = -999;
                string exitMsg = "Background WSL process exited.";
                Process p = sender as Process;
                if(p != null) { try { exitCode = p.ExitCode; } catch { exitCode = -998; } }
                
                // This exit doesn't necessarily mean the *server* stopped, just the initial WSL command finished.
                // Only log it, don't change serverStarted state here. CheckServerStatus handles actual server state.
                if (debugMode) LogMessage($"{exitMsg} Exit Code: {exitCode}. (CheckServerStatus confirms actual server state via port).", 0);

                if(p == serverProcess) serverProcess = null; // Clear our reference to this process
                Repaint();
            } catch (Exception ex) {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerWindow Background Exited Handler Error]: {ex}");
            }
        };
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
                if (debugMode) LogMessage("Server mode set to: WSL Local", 0);
                break;
            case ServerMode.CustomServer:
                if (debugMode) LogMessage("Server mode set to: Custom", 0);
                // Initialize serverCustomProcess when switching to Custom Server mode
                if (serverCustomProcess == null)
                {
                    serverCustomProcess = new ServerCustomProcess(LogMessage, debugMode);
                    serverCustomProcess.LoadSettings();
                }
                break;
            case ServerMode.MaincloudServer:
                if (debugMode) LogMessage("Server mode set to: Maincloud", 0);
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
    
    // Display Cosmos Cove Control Panel title text in the menu bar
    [MenuItem("SpacetimeDB/Cosmos Cove Control Panel", priority = -11000)]
    private static void CosmosCoveControlPanel(){}
    [MenuItem("SpacetimeDB/Cosmos Cove Control Panel", true, priority = -11000)]
    private static bool ValidateCosmosCoveControlPanel(){return false;}

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games