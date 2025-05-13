using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

// The main Comos Cove Control Panel that controls the server and launches all features ///

namespace NorthernRogue.CCCP.Editor {

public class ServerWindow : EditorWindow
{
    // Process Handlers
    private ServerCMDProcess cmdProcessor;
    private ServerLogProcess logProcessor;
    private ServerVersionProcess versionProcessor;
    private ServerCustomProcess serverCustomProcess;
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

    // Pre-requisites Maincloud Server
    

    // Server process
    private bool serverStarted = false;
    private bool isStartingUp = false;
    private float startupTime = 0f;
    private const float serverStartupGracePeriod = 30f; // Give server 30 seconds to start

    // Server detection of changes
    private double lastChangeCheckTime = 0;
    private const double changeCheckInterval = 3.0; // More responsive interval when window is in focus
    private Dictionary<string, long> originalFileSizes = new Dictionary<string, long>(); // Stores sizes at start/publish
    private Dictionary<string, long> currentFileSizes = new Dictionary<string, long>(); // Stores sizes from the latest scan
    private bool windowFocused = false;
    
    // Server status
    private double lastCheckTime = 0;
    private const double checkInterval = 5.0;
    private bool serverConfirmedRunning = false;
    private bool justStopped = false;
    private bool pingShowsOnline = true;
    private double stopInitiatedTime = 0;

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
    
    // Session state key for domain reload
    private const string SessionKeyWasRunningSilently = "ServerWindow_WasRunningSilently";
    private const string PrefsKeyPrefix = "CCCP_";

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
        // Load Editor Prefs - Pre-Requisites Installed items
        hasWSL = EditorPrefs.GetBool(PrefsKeyPrefix + "HasWSL", false);
        hasDebian = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebian", false);
        hasDebianTrixie = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebianTrixie", false);
        hasCurl = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCurl", false);
        hasSpacetimeDBServer = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", false);
        hasSpacetimeDBPath = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", false);
        hasRust = EditorPrefs.GetBool(PrefsKeyPrefix + "HasRust", false);
        
        // Load Editor Prefs - UX
        initializedFirstModule = EditorPrefs.GetBool(PrefsKeyPrefix + "InitializedFirstModule", false);
        
        // Load Editor Prefs  - Pre-Requisites Settings
        wslPrerequisitesChecked = EditorPrefs.GetBool(PrefsKeyPrefix + "wslPrerequisitesChecked", false);
        userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        serverUrl = EditorPrefs.GetString(PrefsKeyPrefix + "ServerURL", "http://0.0.0.0:3000/");
        serverPort = EditorPrefs.GetInt(PrefsKeyPrefix + "ServerPort", 3000);
        authToken = EditorPrefs.GetString(PrefsKeyPrefix + "AuthToken", "");

        // Load Editor Prefs  - Local Settings
        backupDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "BackupDirectory", "");
        serverDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "ServerDirectory", "");
        serverLang = EditorPrefs.GetString(PrefsKeyPrefix + "ServerLang", "rust");
        clientDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "ClientDirectory", "");
        unityLang = EditorPrefs.GetString(PrefsKeyPrefix + "UnityLang", "csharp");
        moduleName = EditorPrefs.GetString(PrefsKeyPrefix + "ModuleName", "");

        // Load Editor Prefs  - Custom Server settings
        sshUserName = EditorPrefs.GetString(PrefsKeyPrefix + "SSHUserName", "");
        sshPrivateKeyPath = EditorPrefs.GetString(PrefsKeyPrefix + "SSHPrivateKeyPath", "");
        customServerUrl = EditorPrefs.GetString(PrefsKeyPrefix + "CustomServerURL", "");
        customServerPort = EditorPrefs.GetInt(PrefsKeyPrefix + "CustomServerPort", 0);
        customServerAuthToken = EditorPrefs.GetString(PrefsKeyPrefix + "CustomServerAuthToken", "");

        // Load Editor Prefs - Global Settings
        hideWarnings = EditorPrefs.GetBool(PrefsKeyPrefix + "HideWarnings", true);
        detectServerChanges = EditorPrefs.GetBool(PrefsKeyPrefix + "DetectServerChanges", true);
        autoPublishMode = EditorPrefs.GetBool(PrefsKeyPrefix + "AutoPublishMode", false);
        publishAndGenerateMode = EditorPrefs.GetBool(PrefsKeyPrefix + "PublishAndGenerateMode", true);
        silentMode = EditorPrefs.GetBool(PrefsKeyPrefix + "SilentMode", true);
        debugMode = EditorPrefs.GetBool(PrefsKeyPrefix + "DebugMode", false);
        clearModuleLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearModuleLogAtStart", false);
        clearDatabaseLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearDatabaseLogAtStart", false);
        autoCloseWsl = EditorPrefs.GetBool(PrefsKeyPrefix + "AutoCloseWsl", true); // Only WSL Mode
        
        // Load server mode
        LoadServerModeFromPrefs();

        // Initialize foldout states with default values of false
        EditorPrefs.SetBool(PrefsKeyPrefix + "ShowPrerequisites", EditorPrefs.GetBool(PrefsKeyPrefix + "ShowPrerequisites", true));
        EditorPrefs.SetBool(PrefsKeyPrefix + "ShowSettingsWindow", EditorPrefs.GetBool(PrefsKeyPrefix + "ShowSettingsWindow", false));
        EditorPrefs.SetBool(PrefsKeyPrefix + "ShowUtilityCommands", EditorPrefs.GetBool(PrefsKeyPrefix + "ShowUtilityCommands", false));
        // Other editor states
        colorLogo = EditorPrefs.GetBool(PrefsKeyPrefix+"ColorLogo", false);

        // Initialize the processors
        cmdProcessor = new ServerCMDProcess(LogMessage, debugMode);
        
        // Initialize LogProcessor with callbacks
        logProcessor = new ServerLogProcess(
            LogMessage,
            () => ServerOutputWindow.RefreshOpenWindow(), // Module log update callback
            () => ServerOutputWindow.RefreshOpenWindow(), // Database log update callback
            cmdProcessor,
            debugMode
        );
        
        // Initialize VersionProcessor
        versionProcessor = new ServerVersionProcess(cmdProcessor, LogMessage, debugMode);
        
        // Configure the server control delegates
        versionProcessor.ConfigureServerControlDelegates(
            () => serverStarted, // IsServerRunning
            () => autoCloseWsl,  // GetAutoCloseWsl
            (value) => { autoCloseWsl = value; EditorPrefs.SetBool(PrefsKeyPrefix + "AutoCloseWsl", value); }, // SetAutoCloseWsl
            () => StartServer(),  // StartServer
            () => StopServer()    // StopServer
        );

        serverCustomProcess = new ServerCustomProcess(LogMessage, debugMode);
        
        // Configure the log processor
        logProcessor.Configure(moduleName, serverDirectory, clearModuleLogAtStart, clearDatabaseLogAtStart, userName); // Pass userName to logProcessor
        logProcessor.SetServerRunningState(serverStarted);
        
        // Register for focus events
        EditorApplication.focusChanged += OnFocusChanged;
                
        // Start checking server status
        EditorApplication.update += EditorUpdateHandler;

        // Check if we were previously running silently and restore state if needed
        bool wasRunningSilently = SessionState.GetBool(SessionKeyWasRunningSilently, false);
        if (wasRunningSilently && serverStarted && silentMode)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerWindow OnEnable] Detected potentially lost tail process from previous session. Attempting restart.");
            logProcessor.AttemptTailRestartAfterReload();
        } else if (!serverStarted || !silentMode) {
            // Clear the flag if not running silently on enable
             SessionState.SetBool(SessionKeyWasRunningSilently, false);
        }

        // Ensure the flag is correctly set based on current state when enabled
        SessionState.SetBool(SessionKeyWasRunningSilently, serverStarted && silentMode);

        EditorApplication.playModeStateChanged += HandlePlayModeStateChange;
        
        // Check if we need to restart the database log process
        bool databaseLogWasRunning = SessionState.GetBool("ServerWindow_DatabaseLogRunning", false);
        if (serverStarted && silentMode && databaseLogWasRunning)
        {
            if (debugMode) LogMessage("Restarting database logs after editor reload...", 0);
            logProcessor.AttemptDatabaseLogRestartAfterReload();
        }
    }
    
    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChange; // Remove handler

        // Save state before disabling (might be domain reload)
        // Ensure SessionState reflects the state *just before* disable/reload
        SessionState.SetBool(SessionKeyWasRunningSilently, serverStarted && silentMode);

        EditorApplication.update -= EditorUpdateHandler;
        
        // Update the log processor about server state
        if (logProcessor != null)
        {
            logProcessor.SetServerRunningState(serverStarted && silentMode);
        }
    }
    
    private void OnFocusChanged(bool focused)
    {
        windowFocused = focused;
        
        // Check for changes immediately when window gets focus
        if (focused && detectServerChanges)
        {
            // Reset the timer and check right away
            lastChangeCheckTime = 0;
            DetectServerChanges();
        }
    }
    
    private void EditorUpdateHandler()
    {
        // Check server status periodically
        if (EditorApplication.timeSinceStartup - lastCheckTime > checkInterval)
        {
            CheckServerStatus();
        }

        // Check for file changes periodically only if window is focused, detection is enabled, and no changes are pending
        if (windowFocused && detectServerChanges && !serverChangesDetected &&
            EditorApplication.timeSinceStartup - lastChangeCheckTime > changeCheckInterval)
        {
            DetectServerChanges();
        }

        // Have the log processor check its processes for health
        if (silentMode && serverStarted)
        {
            logProcessor.CheckLogProcesses(EditorApplication.timeSinceStartup);
        }
        
        // For Custom Server mode, ensure UI is refreshed periodically to update connection status
        if (serverMode == ServerMode.CustomServer && windowFocused)
        {
            Repaint();
        }
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
            // Launch Server Installer Window
            if (GUILayout.Button("Launch Server Installer", GUILayout.Height(20)))
            {
                ServerInstallerWindow.ShowWindow();
            }

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
            string customModeTooltip = "Connect to your custom server and run spacetime commands";
            if (GUILayout.Button(new GUIContent("Custom", customModeTooltip), serverMode == ServerMode.CustomServer ? activeToolbarButton : inactiveToolbarButton, GUILayout.ExpandWidth(true)))
            {
                if (serverStarted)
                {
                    bool modeChange = EditorUtility.DisplayDialog("Confirm Mode Change", "Do you want to stop your WSL Server and change the server mode to Custom server?","OK","Cancel");
                    if (modeChange)
                    {
                        StopServer();
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

            GUILayout.BeginVertical(customWindowStyle); // Window of Pre-Requisites
            #endregion

            #region WSL Mode
            if (serverMode == ServerMode.WslServer)
            {
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
                    EditorPrefs.SetString(PrefsKeyPrefix + "UserName", userName);
                    // Update processors with new username
                    if(logProcessor != null) logProcessor.Configure(moduleName, serverDirectory, clearModuleLogAtStart, clearDatabaseLogAtStart, userName);
                    if (debugMode) LogMessage($"Debian username set to: {userName}", 0);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(userName)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                
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
                        EditorPrefs.SetString(PrefsKeyPrefix + "BackupDirectory", backupDirectory);
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
                        EditorPrefs.SetString(PrefsKeyPrefix + "ServerDirectory", serverDirectory);
                        LogMessage($"Server directory set to: {serverDirectory}", 1);
                        // Reset file tracking when directory changes
                        originalFileSizes.Clear();
                        currentFileSizes.Clear();
                        serverChangesDetected = false;
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
                    EditorPrefs.SetString(PrefsKeyPrefix + "ServerLang", serverLang);
                    LogMessage($"Server language set to: {serverLangOptions[newServerLangSelectedIndex]}", 0);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(serverLang)), GUILayout.Width(20));
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
                    EditorPrefs.SetString(PrefsKeyPrefix + "ModuleName", moduleName);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(moduleName)), GUILayout.Width(20));
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
                        EditorPrefs.SetString(PrefsKeyPrefix + "ClientDirectory", clientDirectory);
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
                    EditorPrefs.SetString(PrefsKeyPrefix + "unityLang", unityLang);
                    LogMessage($"Module language set to: {unityLangOptions[newunityLangSelectedIndex]}", 0);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(unityLang)), GUILayout.Width(20));
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
                    EditorPrefs.SetString(PrefsKeyPrefix + "ServerURL", serverUrl);
                    // Extract port from URL
                    int extractedPort = ExtractPortFromUrl(serverUrl);
                    if (extractedPort > 0)
                    {
                        serverPort = extractedPort;
                        EditorPrefs.SetInt(PrefsKeyPrefix + "ServerPort", serverPort);
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
                    EditorPrefs.SetString(PrefsKeyPrefix + "AuthToken", authToken);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(authToken)), GUILayout.Width(20));
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

                // WSL Check Pre-Requisites button
                if (GUILayout.Button("Check Pre-Requisites", GUILayout.Height(20)))
                {
                    CheckPrerequisites();
                }
            }
            #endregion

            #region Custom Mode
            if (serverMode == ServerMode.CustomServer)
            {
                // SSH Username
                EditorGUILayout.BeginHorizontal();
                string userNameTooltip = 
                "The SSH username to use to login to your distro.";
                EditorGUILayout.LabelField(new GUIContent("Distro Username:", userNameTooltip), GUILayout.Width(110));
                string newUserName = EditorGUILayout.DelayedTextField(sshUserName, GUILayout.Width(150));
                if (newUserName != sshUserName)
                {
                    sshUserName = newUserName;
                    EditorPrefs.SetString(PrefsKeyPrefix + "SSHUserName", sshUserName);
                                       
                    if (debugMode) LogMessage($"SSH username set to: {sshUserName}", 0);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(sshUserName)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                
                // SSH Private Key Path
                EditorGUILayout.BeginHorizontal();
                string keyPathTooltip = "The full path to your SSH private key file (e.g., C:\\Users\\YourUser\\.ssh\\id_ed25519).";
                EditorGUILayout.LabelField(new GUIContent("Private Key Path:", keyPathTooltip), GUILayout.Width(110));
                string newKeyPath = EditorGUILayout.TextField(sshPrivateKeyPath, GUILayout.Width(90));
                if (GUILayout.Button("Browse", GUILayout.Width(57)))
                {
                    string path = EditorUtility.OpenFilePanel("Select SSH Private Key", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.ssh", "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        newKeyPath = path;
                    }
                }
                if (newKeyPath != sshPrivateKeyPath)
                {
                    sshPrivateKeyPath = newKeyPath;
                    EditorPrefs.SetString(PrefsKeyPrefix + "SSHPrivateKeyPath", sshPrivateKeyPath);
                    if (serverCustomProcess != null) // Update ServerCustomProcess if it exists
                    {
                        serverCustomProcess.SetPrivateKeyPath(sshPrivateKeyPath);
                    }
                    if (debugMode) LogMessage($"SSH Private Key Path set to: {sshPrivateKeyPath}", 0);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(sshPrivateKeyPath) && System.IO.File.Exists(sshPrivateKeyPath)), GUILayout.Width(20));
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
                    EditorPrefs.SetString(PrefsKeyPrefix + "CustomServerURL", customServerUrl);
                    
                    // Also set the main serverUrl for consistency
                    serverUrl = customServerUrl;
                    EditorPrefs.SetString(PrefsKeyPrefix + "ServerURL", serverUrl);
                    
                    // Extract port from URL
                    int extractedPort = ExtractPortFromUrl(customServerUrl);
                    if (extractedPort > 0)
                    {
                        customServerPort = extractedPort;
                        EditorPrefs.SetInt(PrefsKeyPrefix + "CustomServerPort", customServerPort);
                        
                        // Also set the main serverPort for consistency
                        serverPort = customServerPort;
                        EditorPrefs.SetInt(PrefsKeyPrefix + "ServerPort", serverPort);
                        
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
                    EditorPrefs.SetString(PrefsKeyPrefix + "CustomServerAuthToken", customServerAuthToken);
                }
                GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(customServerAuthToken)), GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                // Connection status display
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Connection Status:", GUILayout.Width(110));
                // Only use cached value, update in background
                if (serverCustomProcess != null)
                {
                    serverCustomProcess.UpdateSessionStatusIfNeeded();
                }
                GUIStyle connectionStatusStyle = new GUIStyle(EditorStyles.label);
                bool isConnected = serverCustomProcess != null && serverCustomProcess.IsSessionActive();
                connectionStatusStyle.normal.textColor = isConnected ? Color.green : Color.gray;
                string connectionStatusText = isConnected ? "Connected SSH" : "Disconnected";
                EditorGUILayout.LabelField(connectionStatusText, connectionStatusStyle);
                EditorGUILayout.EndHorizontal();

                // Custom Server Check Pre-Requisites
                if (GUILayout.Button("Check Pre-Requisites", GUILayout.Height(20)))
                {
                    CheckPrerequisitesCustom();
                }
            }
            #endregion
            
            #region Maincloud Mode
            if (serverMode == ServerMode.MaincloudServer)
            {
                // Maincloud Check Pre-Requisites
                if (GUILayout.Button("Check Pre-Requisites", GUILayout.Height(20)))
                {
                    //CheckPrerequisitesMaincloud();
                }
            }

            EditorGUILayout.EndVertical(); // GUI Background
        }
        EditorGUILayout.EndVertical(); // End of Entire Pre-Requisites section
    }
    #endregion

    #region ServerUI

    private void DrawServerSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
        
        bool serverRunning = serverStarted || isStartingUp;

        if (!wslPrerequisitesChecked || !hasWSL || !hasDebian)
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
                    StartServer();
                }
            } 
            else 
            {
                if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
                {
                    StopServer();
                }
            }
        }

        EditorGUI.BeginDisabledGroup(!serverStarted && !silentMode);
        if (GUILayout.Button("View Server Logs", GUILayout.Height(20)))
        {
            ViewServerLogs();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!serverStarted);
        if (GUILayout.Button("View Server Database", GUILayout.Height(20)))
        {
            ServerDataWindow.ShowWindow();
        }
        EditorGUI.EndDisabledGroup();
        
        EditorGUI.BeginDisabledGroup(!serverStarted);
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
        
        if (isStartingUp)
        {
            statusStyle.normal.textColor = Color.green;
            float elapsed = (float)(EditorApplication.timeSinceStartup - startupTime);
            int seconds = Mathf.FloorToInt(elapsed);
            statusText = $"Starting ({seconds}s)...";
        }
        else if (serverStarted)
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
            EditorGUILayout.LabelField(new GUIContent("Server Visiblity:", serverModeTooltip), GUILayout.Width(120));
            GUIStyle silentToggleStyle = new GUIStyle(GUI.skin.button);
            if (silentMode)
            {
                silentToggleStyle.normal.textColor = hiddenColor;
                silentToggleStyle.hover.textColor = hiddenColor;
            }
            if (GUILayout.Button(silentMode ? "Silent Mode" : "Show CMD", silentToggleStyle))
            {
                silentMode = !silentMode;
                EditorPrefs.SetBool(PrefsKeyPrefix + "SilentMode", silentMode);
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
            if (detectServerChanges)
            {
                // Use green for active detection
                changeToggleStyle.normal.textColor = recommendedColor;
                changeToggleStyle.hover.textColor = recommendedColor;
            }
            if (GUILayout.Button(detectServerChanges ? "Detecting Changes" : "Not Detecting Changes", changeToggleStyle))
            {
                detectServerChanges = !detectServerChanges;
                EditorPrefs.SetBool(PrefsKeyPrefix + "DetectServerChanges", detectServerChanges);
                
                if (detectServerChanges)
                {
                    // Clear previous state and start fresh
                    originalFileSizes.Clear();
                    currentFileSizes.Clear();
                    serverChangesDetected = false;
                    // Check immediately
                    DetectServerChanges();
                }
                else
                {
                    serverChangesDetected = false;
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
            if (autoPublishMode)
            {
                // Use green for active auto-publish
                autoPublishStyle.normal.textColor = recommendedColor;
                autoPublishStyle.hover.textColor = recommendedColor;
            }
            if (GUILayout.Button(autoPublishMode ? "Automatic Publishing" : "Manual Publish", autoPublishStyle))
            {
                autoPublishMode = !autoPublishMode;
                EditorPrefs.SetBool(PrefsKeyPrefix + "AutoPublishMode", autoPublishMode);
                
                if (autoPublishMode && !detectServerChanges)
                {
                    detectServerChanges = true;
                    EditorPrefs.SetBool(PrefsKeyPrefix + "DetectServerChanges", true);
                    originalFileSizes.Clear();
                    currentFileSizes.Clear();
                    DetectServerChanges();
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
            if (publishAndGenerateMode)
            {
                // Use green for active auto-generate
                publishGenerateStyle.normal.textColor = recommendedColor;
                publishGenerateStyle.hover.textColor = recommendedColor;
            }
            if (GUILayout.Button(publishAndGenerateMode ? "Publish will Generate" : "Separate Generate", publishGenerateStyle))
            {
                publishAndGenerateMode = !publishAndGenerateMode;
                EditorPrefs.SetBool(PrefsKeyPrefix + "PublishAndGenerateMode", publishAndGenerateMode);
            }
            EditorGUILayout.EndHorizontal();

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
            if (autoCloseWsl)
            {
                wslCloseStyle.normal.textColor = warningColor;
                wslCloseStyle.hover.textColor = warningColor;
            }
            if (GUILayout.Button(autoCloseWsl ? "Close WSL at Server Stop" : "Keep Running", wslCloseStyle))
            {
                autoCloseWsl = !autoCloseWsl;
                EditorPrefs.SetBool(PrefsKeyPrefix + "AutoCloseWsl", autoCloseWsl);
            }
            EditorGUILayout.EndHorizontal();

            // Command Output toggle button
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            string commandOutputTooltip = 
            "Hiding Extra Warnings: Will hide extra SpacetimeDB warning messages in the command output. \n\n"+
            "Showing All Messages: Will show all messages in the command output.\n\n"+
            "Recommended: Hide Extra Warnings.";
            EditorGUILayout.LabelField(new GUIContent("Command Output:", commandOutputTooltip), GUILayout.Width(120));
            GUIStyle warningToggleStyle = new GUIStyle(GUI.skin.button);
            if (hideWarnings)
            {
                warningToggleStyle.normal.textColor = warningColor;
                warningToggleStyle.hover.textColor = warningColor;
            }
            if (GUILayout.Button(hideWarnings ? "Hiding Extra Warnings" : "Show All Messages", warningToggleStyle))
            {
                hideWarnings = !hideWarnings;
                EditorPrefs.SetBool(PrefsKeyPrefix + "HideWarnings", hideWarnings);
            }
            EditorGUILayout.EndHorizontal();

            if (silentMode)
            {
                // Module clear log at start toggle button in Silent Mode
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                string moduleLogTooltip = 
                "Clear Module Log at Server Start: The server will clear the module log at start. \n\n"+
                "Keep Module Log: The server will keep the module log between server restarts.";
                EditorGUILayout.LabelField(new GUIContent("Module Log:", moduleLogTooltip), GUILayout.Width(120));
                GUIStyle moduleLogToggleStyle = new GUIStyle(GUI.skin.button);
                if (clearModuleLogAtStart)
                {
                    moduleLogToggleStyle.normal.textColor = warningColor;
                    moduleLogToggleStyle.hover.textColor = warningColor;
                }
                if (GUILayout.Button(clearModuleLogAtStart ? "Clear at Server Start" : "Keeping Module Log", moduleLogToggleStyle))
                {
                    clearModuleLogAtStart = !clearModuleLogAtStart;
                    EditorPrefs.SetBool(PrefsKeyPrefix + "ClearModuleLogAtStart", clearModuleLogAtStart);
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
                if (clearDatabaseLogAtStart)
                {
                    databaseLogToggleStyle.normal.textColor = warningColor;
                    databaseLogToggleStyle.hover.textColor = warningColor;
                }
                if (GUILayout.Button(clearDatabaseLogAtStart ? "Clear at Server Start" : "Keeping Database Log", databaseLogToggleStyle))
                {
                    clearDatabaseLogAtStart = !clearDatabaseLogAtStart;
                    EditorPrefs.SetBool(PrefsKeyPrefix + "ClearDatabaseLogAtStart", clearDatabaseLogAtStart);
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
            if (debugMode)
            {
                debugToggleStyle.normal.textColor = debugColor;
                debugToggleStyle.hover.textColor = debugColor;
            }
            if (GUILayout.Button(debugMode ? "Debug Mode" : "Debug Disabled", debugToggleStyle))
            {
                debugMode = !debugMode;
                EditorPrefs.SetBool(PrefsKeyPrefix + "DebugMode", debugMode);
                ServerOutputWindow.debugMode = debugMode;
                ServerWindowInitializer.debugMode = debugMode;
                ServerUpdateProcess.debugMode = debugMode;
                ServerLogProcess.debugMode = debugMode;
                ServerCMDProcess.debugMode = debugMode;
                ServerCustomProcess.debugMode = debugMode;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }
    #endregion

    #region CommandsUI

    private void DrawCommandsSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Commands", EditorStyles.boldLabel);
        
        EditorGUI.BeginDisabledGroup(!serverStarted);
        
        // Add a foldout for utility commands
        bool showUtilityCommands = EditorGUILayout.Foldout(EditorPrefs.GetBool(PrefsKeyPrefix + "ShowUtilityCommands", false), "Utility Commands", true);
        EditorPrefs.SetBool(PrefsKeyPrefix + "ShowUtilityCommands", showUtilityCommands);
        
        if (showUtilityCommands)
        {
            EditorGUILayout.LabelField(
            "SpacetimeDB Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            if (GUILayout.Button("Show Login Info", GUILayout.Height(20)))
            {
                RunServerCommand("spacetime login show --token", "Showing SpacetimeDB login info and token");
            }

            if (GUILayout.Button("Show Server Config", GUILayout.Height(20)))
            {
                RunServerCommand("spacetime server list", "Showing SpacetimeDB server config");
            }

            if (GUILayout.Button("Show Active Modules", GUILayout.Height(20)))
            {
                RunServerCommand("spacetime list", "Showing active modules");
            }
            
            if (GUILayout.Button("Ping Server", GUILayout.Height(20)))
            {
                PingServer(true);
            }
            
            if (GUILayout.Button("Show Version", GUILayout.Height(20)))
            {
                RunServerCommand("spacetime --version", "Showing SpacetimeDB version");
            }

            EditorGUILayout.LabelField(
            "WSL Server Commands", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(10));

            if (GUILayout.Button("Open Debian Window", GUILayout.Height(20)))
            {
                OpenDebianWindow();
            }
            
            string backupTooltip = "Creates a tar archive of your DATA folder in your SpacetimeDB server.";
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(backupDirectory));
            if (GUILayout.Button(new GUIContent("Backup Server Data", backupTooltip), GUILayout.Height(20)))
            {  
                versionProcessor.BackupServerData(backupDirectory, userName);
            }
            EditorGUI.EndDisabledGroup();

            string restoreTooltip = "Unpacks and copies over the selected backup archive. DELETES the current DATA folder of your SpacetimeDB server. You will asked to backup before if you have not done so.";
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(backupDirectory));
            if (GUILayout.Button(new GUIContent("Restore Server Data", restoreTooltip), GUILayout.Height(20)))
            {
                versionProcessor.RestoreServerData(backupDirectory, userName);
            }
            EditorGUI.EndDisabledGroup();
        }
        
        // Display server changes notification if detected
        if (serverChangesDetected)
        {
            GUIStyle updateStyle = new GUIStyle(EditorStyles.boldLabel);
            updateStyle.normal.textColor = Color.green;
            if (!autoPublishMode)
            {
                EditorGUILayout.LabelField("New Server Update!", updateStyle);
            }
            else
            {
                EditorGUILayout.LabelField("New Server Update! (Auto Publish Mode)", updateStyle);
            }
        }
        
        if (publishAndGenerateMode) {
        EditorGUILayout.LabelField("Will Publish then Generate Unity Files automatically.\n" + 
                                    "Ctrl + Alt + Click to also reset the database.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30));
        } else {
        EditorGUILayout.LabelField("First Publish then Generate Unity Files.\n" + 
                        "Ctrl + Alt + Click to also reset the database.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30));
        }
        
        // Add Publish Module button
        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(moduleName));
        
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
        
        // Dynamic button text based on control key state
        string buttonText = resetDatabase ? "Publish Module and Reset Database" : "Publish Module";
        
        if (GUILayout.Button(buttonText, publishButtonStyle, GUILayout.Height(37)))
        {
            Publish(resetDatabase);
        }
        EditorGUI.EndDisabledGroup();
        
        // Add Generate Unity Files button
        if (!publishAndGenerateMode)
        {
            if (GUILayout.Button("Generate Unity Files", GUILayout.Height(37)))
            {
                string outDir = GetRelativeClientPath();
                RunServerCommand($"spacetime generate --out-dir {outDir} --lang {unityLang}", "Generating Unity files");
                LogMessage($"Generated Unity files to: {outDir}", 1);
            }
        }
        
        EditorGUI.EndDisabledGroup();

        // To debug server online state
        /*if (GUILayout.Button("Ping Server", GUILayout.Height(20)))
        {
            LogMessage("Online: "+PingServerStatus(),0);
        }*/
        
        EditorGUILayout.EndVertical();
    }
    #endregion
    
    #region Check Pre-reqs

    public void CheckPrerequisites()
    {
        cmdProcessor.CheckPrerequisites((wsl, debian, trixie, curl, spacetime, spacetimePath, rust) => {
            EditorApplication.delayCall += () => {
                hasWSL = wsl;
                hasDebian = debian;
                hasDebianTrixie = trixie;
                hasCurl = curl;
                hasSpacetimeDBServer = spacetime;
                hasSpacetimeDBPath = spacetimePath;
                hasRust = rust;
                wslPrerequisitesChecked = true;
                
                // Save state
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasWSL", hasWSL);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebian", hasDebian);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebianTrixie", hasDebianTrixie);
                EditorPrefs.SetBool(PrefsKeyPrefix + "wslPrerequisitesChecked", wslPrerequisitesChecked);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasCurl", hasCurl);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", hasSpacetimeDBServer);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", hasSpacetimeDBPath);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasRust", hasRust);
                
                // Load state
                userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");

                Repaint();
                
                bool essentialSoftware = 
                    hasWSL && hasDebian && hasDebianTrixie && hasCurl && 
                    hasSpacetimeDBServer && hasSpacetimeDBPath && hasRust;

                bool essentialUserSettings = 
                    !string.IsNullOrEmpty(userName) && 
                    !string.IsNullOrEmpty(serverDirectory) && 
                    !string.IsNullOrEmpty(moduleName) && 
                    !string.IsNullOrEmpty(serverLang) && 
                    !string.IsNullOrEmpty(unityLang);
                
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
                    EditorPrefs.SetBool(PrefsKeyPrefix + "InitializedFirstModule", true);
                    
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

        if (serverCustomProcess != null)
        {
            serverCustomProcess.SetPrivateKeyPath(sshPrivateKeyPath);
        }
        
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
        
        // Make sure key path is set in ServerCustomProcess
        if (serverCustomProcess != null)
        {
            serverCustomProcess.SetPrivateKeyPath(sshPrivateKeyPath);
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
    #endregion

    #region Server Methods

    private void Publish(bool resetDatabase)
    {
        if (String.IsNullOrEmpty(clientDirectory) || String.IsNullOrEmpty(serverDirectory))
        {
            LogMessage("Please set your client directory and server directory in the pre-requisites first.",-2);
            return;
        }
        if (String.IsNullOrEmpty(moduleName))
        {
            LogMessage("Please set the module name in the pre-requisites.",-2);
            return;
        }
        if (resetDatabase)
        {
            // Display confirmation dialog when resetting database
            if (EditorUtility.DisplayDialog(
                    "Confirm Database Reset",
                    "Are you sure you wish to delete the entire database and publish the module?",
                    "Yes, Reset Database",
                    "Cancel"))
            {
                RunServerCommand($"spacetime publish --server local {moduleName} --delete-data -y", $"Publishing module '{moduleName}' and resetting database");
            }
        }
        else
        {
            RunServerCommand($"spacetime publish --server local {moduleName}", $"Publishing module '{moduleName}'");
        }
        
        // Reset change detection after publishing
        if (detectServerChanges)
        {
            serverChangesDetected = false;
            originalFileSizes.Clear();
            currentFileSizes.Clear();
        }

        // publishAndGenerateMode will run generate after publish has been run successfully in RunServerCommand().
    }

    private void InitNewModule()
    {
        if (string.IsNullOrEmpty(serverDirectory))
        {
            LogMessage("Please set the server directory first.", -1);
            return;
        }
        if (string.IsNullOrEmpty(serverLang))
        {
            LogMessage("Please set the server language first.", -1);
            return;
        }

        // Use EditorApplication.delayCall to ensure we're not in the middle of a GUI layout
        EditorApplication.delayCall += () =>
        {
            bool result = EditorUtility.DisplayDialog("Init a new module", 
                "Are you sure you want to init a new module?\n"+
                "Don't do this if you already have a module in your set server directory.", 
                "Yes", "No");
            
            if (result)
            {
                string wslPath = GetWslPath(serverDirectory);
                // Combine cd and init command
                string command = $"cd \"{wslPath}\" && spacetime init --lang {serverLang} .";
                cmdProcessor.RunWslCommandSilent(command);
                LogMessage("New module initialized", 1);
                
                // Set the flag so the initialization dialog doesn't show again
                initializedFirstModule = true;
                EditorPrefs.SetBool(PrefsKeyPrefix + "InitializedFirstModule", true);
            }
        };
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

    #region Start
    
    private void StartServer()
    {
        if (!wslPrerequisitesChecked)
        {
            LogMessage("Prerequisites need to be checked before starting the server.", -2);
            CheckPrerequisites();
            return;
        }

        switch (serverMode)
        {
            case ServerMode.WslServer:
                StartWslServer();
                break;
            case ServerMode.CustomServer:
                StartCustomServer();
                break;
            case ServerMode.MaincloudServer:
                StartMaincloudServer();
                break;
            default:
                LogMessage("Unknown server mode. Cannot start server.", -1);
                break;
        }
    }

    private void StartWslServer()
    {
        if (!hasWSL || !hasDebian || !hasDebianTrixie || !hasSpacetimeDBServer)
        {
            LogMessage("Missing required installed items. Will attempt to start server.", -2);
        }
        if (string.IsNullOrEmpty(userName))
        {
            LogMessage("Cannot start server. Debian username is not set.", -1);
            return;
        }
        
        LogMessage("Start sequence initiated for WSL server. Waiting for confirmation...", 0);
        
        try
        {
            // Configure log processor with current settings
            logProcessor.Configure(moduleName, serverDirectory, clearModuleLogAtStart, clearDatabaseLogAtStart, userName);
            
            if (silentMode)
            {
                if (debugMode) LogMessage($"Starting Spacetime Server (Silent Mode, File Logging to {ServerLogProcess.WslCombinedLogPath})...", 0);
                
                // Start the silent server process
                serverProcess = cmdProcessor.StartSilentServerProcess(ServerLogProcess.WslCombinedLogPath);
                if (serverProcess == null) throw new Exception("Failed to start silent server process");
                
                // Start log monitoring
                logProcessor.StartLogging();
            }
            else
            {
                // Start visible CMD server process
                LogMessage("Starting Spacetime Server (Visible CMD)...", 0);
                serverProcess = cmdProcessor.StartVisibleServerProcess(serverDirectory);
                if (serverProcess == null) throw new Exception("Failed to start visible server process");
            }

            LogMessage("Server Succesfully Started!",1);
        
            // Mark server as starting up
            isStartingUp = true;
            startupTime = (float)EditorApplication.timeSinceStartup;
            serverStarted = true; // Assume starting, CheckServerStatus will verify
            EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", true);
            
            // Update log processor state
            logProcessor.SetServerRunningState(true);
        }
        catch (Exception ex)
        {
            LogMessage($"Error during server start sequence: {ex.Message}", -1);
            serverStarted = false;
            isStartingUp = false;
            EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", false);
            logProcessor.StopLogging();
            serverProcess = null; 
            
            // Update log processor state
            logProcessor.SetServerRunningState(false);
        }
        finally
        {
            Repaint();
        }
    }

    private void StartCustomServer()
    {
        if (string.IsNullOrEmpty(customServerUrl))
        {
            LogMessage("Please enter a custom server URL first.", -2);
            return;
        }
        
        if (customServerPort <= 0)
        {
            LogMessage("Could not detect a valid port in the custom server URL. Please ensure the URL includes a port number (e.g., http://example.com:3000/).", -2);
            return;
        }
        
        LogMessage($"Connecting to custom server at {customServerUrl}", 1);
        
        // Mark as connected to the custom server
        serverStarted = true;
        serverConfirmedRunning = true;
        isStartingUp = false;
        EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", true);
        
        // Ping the server to verify it's reachable
        PingServer(true);
    }

    private void StartMaincloudServer()
    {
        // TODO: Implement maincloud server startup logic
        LogMessage("Maincloud server start not yet implemented", -1);
    }
    #endregion

    #region Stop

    private void StopServer()
    {
        if (debugMode) LogMessage("Stop Server process has been called.", 0);
        isStartingUp = false; // Ensure startup flag is cleared
        serverConfirmedRunning = false; // Reset confirmed state

        try
        {
            // Use the cmdProcessor to stop the server
            cmdProcessor.StopServer();
            
            // Stop the log processors
            logProcessor.StopLogging();
            
        }
        catch (Exception ex)
        {
            LogMessage($"Error during server stop sequence: {ex.Message}", -1);
        }
        finally
        {
            // Force state update
            serverStarted = false;
            isStartingUp = false;
            serverConfirmedRunning = false;
            EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", false);
            serverProcess = null; 
            justStopped = true; // Set flag indicating stop was just initiated
            stopInitiatedTime = EditorApplication.timeSinceStartup; // Record time

            LogMessage("Server Successfully Stopped.", 1);
            
            // Update log processor state
            logProcessor.SetServerRunningState(false);
            
            // WSL Shutdown Logic
            if (autoCloseWsl)
            {
                cmdProcessor.ShutdownWsl();
            }

            Repaint();
        }
    }
    #endregion

    #region CheckStatus

    private async void CheckServerStatus()
    {
        // Only check periodically
        if (EditorApplication.timeSinceStartup - lastCheckTime < checkInterval) return;
        lastCheckTime = EditorApplication.timeSinceStartup;

        // Check if port is in use
        bool isPortOpen = await cmdProcessor.CheckPortAsync(serverPort);
        
        // --- Reset justStopped flag after 5 seconds if grace period expired ---
        const double stopGracePeriod = 5.0;
        if (justStopped && (EditorApplication.timeSinceStartup - stopInitiatedTime >= stopGracePeriod))
        {
            if (debugMode) LogMessage("Stop grace period expired, allowing normal status checks to resume.", 0);
            justStopped = false;
        }
        
        // --- Startup Phase Check ---
        if (isStartingUp)
        {
            float elapsedTime = (float)(EditorApplication.timeSinceStartup - startupTime);
            
            // If port is open during startup phase, confirm immediately
            if (isPortOpen)
            {
                if (debugMode) LogMessage($"Startup confirmed: Port {serverPort} is now open.", 1);
                isStartingUp = false;
                serverStarted = true; // Explicitly confirm started state
                serverConfirmedRunning = true;
                justStopped = false; // Reset flag on successful start confirmation
                EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", true);
                
                // Update logProcessor state
                logProcessor.SetServerRunningState(true);
                
                Repaint();
                
                // Auto-publish check if applicable
                if (autoPublishMode && serverChangesDetected && !string.IsNullOrEmpty(moduleName))
                {
                    LogMessage("Server running with pending changes - auto-publishing...", 0);
                    RunServerCommand($"spacetime publish --server local {moduleName}", $"Auto-publishing module '{moduleName}'");
                    serverChangesDetected = false;
                    originalFileSizes.Clear();
                    currentFileSizes.Clear();
                }
                return; // Confirmed, skip further checks this cycle
            }
            // If grace period expires and port *still* isn't open, assume failure
            else if (elapsedTime >= serverStartupGracePeriod)
            {
                LogMessage($"Server failed to start within grace period (Port {serverPort} did not open).", -1);
                isStartingUp = false;
                serverStarted = false;
                serverConfirmedRunning = false;
                justStopped = false; // Reset flag on failed start
                EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", false);
                
                // Update logProcessor state
                logProcessor.SetServerRunningState(false);
                
                if (serverProcess != null && !serverProcess.HasExited) { try { serverProcess.Kill(); } catch {} }
                serverProcess = null;
                Repaint();
                return; // Failed, skip further checks
            }
            else
            {
                // Still starting up, update UI and wait
                Repaint();
                return;
            }
        }
        
        // --- Standard Running Check (Only if not starting up) ---
        if (serverStarted) // Check if we *think* it should be running
        {
            // Determine current actual running state based only on port
            bool isActuallyRunning = isPortOpen;

            // State Change Detection:
            if (serverConfirmedRunning != isActuallyRunning)
            {
                serverConfirmedRunning = isActuallyRunning; // Update confirmed state
                string msg = isActuallyRunning
                    ? $"Server running confirmed (Port {serverPort}: open)"
                    : $"Server appears to have stopped (Port {serverPort}: closed)";
                LogMessage(msg, isActuallyRunning ? 1 : -2);

                // If state changed to NOT running, update the main serverStarted flag
                if (!isActuallyRunning)
                {
                    serverStarted = false;
                    EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", false);
                    
                    // Update logProcessor state
                    logProcessor.SetServerRunningState(false);
                    
                    if (debugMode) LogMessage("Server state updated to stopped due to port closed.", -1);
                } else {
                    // If we confirmed it IS running again, clear the stop flag
                    justStopped = false;
                    
                    // Update logProcessor state
                    logProcessor.SetServerRunningState(true);
                }
                Repaint();
            }
        }
        // --- Check for External Start/Recovery ---
        // Only check if not already started, not starting up, and port is in use
        else if (!serverStarted && !isStartingUp && isPortOpen)
        {
            // If the 'justStopped' flag is set, ignore this check during the grace period
            if (justStopped)
            {
                if (debugMode) LogMessage($"Port {serverPort} detected, but in post-stop grace period. Ignoring.", 0);
            }
            else
            {
                if (PingServerStatus()) // Also ping the server to check if the port information is correct
                {
                    // Port detected, not recently stopped -> likely external start/recovery
                    LogMessage($"Detected server running on port {serverPort}.", 1);
                    serverStarted = true;
                    serverConfirmedRunning = true;
                    isStartingUp = false; 
                    justStopped = false; // Ensure flag is clear if we recover state
                    EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", true);
                    
                    // Update logProcessor state
                    logProcessor.SetServerRunningState(true);
                }
                
                Repaint();
            }
        }
    }
    #endregion

    #region DetectChanges

    private void DetectServerChanges()
    {
        lastChangeCheckTime = EditorApplication.timeSinceStartup;

        if (!detectServerChanges || string.IsNullOrEmpty(serverDirectory))
            return;

        try
        {
            string srcDirectory = Path.Combine(serverDirectory, "src");
            string cargoTomlPath = Path.Combine(serverDirectory, "Cargo.toml");
            
            // Initialize dictionaries if empty
            var newSizes = new Dictionary<string, long>();
            
            // Check for Cargo.toml existence and add it to tracking
            if (File.Exists(cargoTomlPath))
            {
                try 
                {
                    newSizes[cargoTomlPath] = new FileInfo(cargoTomlPath).Length;
                }
                catch (IOException ex)
                {
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerWindow] Could not get size for Cargo.toml: {ex.Message}");
                }
            }
            
            // No src directory means we're only tracking Cargo.toml
            if (!Directory.Exists(srcDirectory))
            {
                if (originalFileSizes.Count > 0 || currentFileSizes.Count > 0 || newSizes.Count > 0) {
                    // If src dir disappeared but we were tracking files or have Cargo.toml, that's a change
                    originalFileSizes.Clear();
                    currentFileSizes.Clear();
                    if (newSizes.Count > 0) {
                        originalFileSizes = new Dictionary<string, long>(newSizes);
                        currentFileSizes = new Dictionary<string, long>(newSizes);
                    }
                    serverChangesDetected = newSizes.Count == 0; // Only mark as changed if we lost everything
                    Repaint();
                }
                return;
            }

            // Add src directory files to tracking
            string[] currentFiles = Directory.GetFiles(srcDirectory, "*.*", SearchOption.AllDirectories);
            foreach (string file in currentFiles)
            {
                try
                {
                    newSizes[file] = new FileInfo(file).Length;
                }
                catch (IOException ex) {
                    // Handle potential file access issues gracefully
                    if (debugMode) UnityEngine.Debug.LogWarning($"[ServerWindow] Could not get size for file {file}: {ex.Message}");
                }
            }

            // 2. Handle first run or post-publish state
            // Check for changed/new files
            foreach (var kvp in newSizes)
            {
                if (!currentFileSizes.TryGetValue(kvp.Key, out long currentSize) || currentSize != kvp.Value)
                {
                    currentFileSizes[kvp.Key] = kvp.Value;
                }
            }
            // Check for deleted files
            var filesToRemove = currentFileSizes.Keys.Except(newSizes.Keys).ToList();
            foreach (var fileToRemove in filesToRemove)
            {
                currentFileSizes.Remove(fileToRemove);
            }

            // 4. Compare current state (currentFileSizes) with original state (originalFileSizes)
            bool differenceFromOriginal = false;
            if (currentFileSizes.Count != originalFileSizes.Count)
            {
                differenceFromOriginal = true;
            }
            else
            {
                foreach (var kvp in currentFileSizes)
                {
                    if (!originalFileSizes.TryGetValue(kvp.Key, out long originalSize) || originalSize != kvp.Value)
                    {
                        differenceFromOriginal = true;
                        break;
                    }
                }
            }

            // 5. Update serverChangesDetected state and UI
            if (serverChangesDetected != differenceFromOriginal)
            {
                serverChangesDetected = differenceFromOriginal;
                if(debugMode) LogMessage($"Server changes detected state set to: {serverChangesDetected}", 0);
                Repaint(); // Update UI only if state actually changes

                 // Trigger auto-publish if changes are now detected and mode is on
                 if (serverChangesDetected && autoPublishMode && serverStarted && !string.IsNullOrEmpty(moduleName))
                 {
                    LogMessage("Auto-publishing module due to changes detected...", 0);
                    RunServerCommand($"spacetime publish --server local {moduleName}", $"Auto-publishing module '{moduleName}'");
                    // NOTE: RunServerCommand should clear original/current sizes upon SUCCESSFUL publish
                 }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerWindow] Error checking server changes: {ex.Message}\n{ex.StackTrace}");
            // Optionally reset state on error?
            // originalFileSizes.Clear();
            // currentFileSizes.Clear();
            // serverChangesDetected = false;
        }
    }
    #endregion

    #region RunCommands
    private async void RunServerCommand(string command, string description)
    {
        if (!serverStarted)
        {
            LogMessage("Server is not running. Please start the server first.", -1);
            return;
        }
        
        try
        {
            // Run the command silently and capture the output
            LogMessage($"{description}...", 0);
            
            // Execute the command through the command processor
            var result = await cmdProcessor.RunServerCommandAsync(command, serverDirectory);
            
            // Display the results in the output log
            if (!string.IsNullOrEmpty(result.output))
            {
                LogMessage(result.output, 0);
            }
            
            if (!string.IsNullOrEmpty(result.error))
            {
                LogMessage(result.error, -2);
            }
            
            if (string.IsNullOrEmpty(result.output) && string.IsNullOrEmpty(result.error))
            {
                LogMessage("Command completed with no output.", 0);
            }
            
            // Handle special cases for publish and generate
            bool isPublishCommand = command.Contains("spacetime publish");
            bool isGenerateCommand = command.Contains("spacetime generate");
            
            if (result.success)
            {
                // Reset change detection state after successful publish
                if (isPublishCommand)
                {
                    if (detectServerChanges)
                    {
                        serverChangesDetected = false;
                        originalFileSizes.Clear();
                        currentFileSizes.Clear();
                        if(debugMode) LogMessage("Cleared file size tracking after successful publish.", 0);
                    }

                    // Auto-generate if publish was successful and mode is enabled
                    if (publishAndGenerateMode) 
                    {
                        LogMessage("Publish successful, automatically generating Unity files...", 0);
                        string outDir = GetRelativeClientPath();
                        RunServerCommand($"spacetime generate --out-dir {outDir} --lang {unityLang}", "Generating Unity files (auto)");
                    }
                }
                else if (isGenerateCommand && description == "Generating Unity files (auto)")
                {
                    LogMessage("Publish and generate successful, requesting script compilation...", 1);
                    UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Error running command: {ex.Message}", -1);
        }
    }
    #endregion

    #region LogMessage
    
    public void LogMessage(string message, int style)
    {
        // Skip warning messages if hideWarnings is enabled
        if (hideWarnings && message.Contains("WARNING"))
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
        if (silentMode)
        {
            if (debugMode) LogMessage("Opening/focusing silent server output window...", 0);
            ServerOutputWindow.ShowWindow(); // This finds existing or creates new
        }
        else if (serverStarted)
        {
            // In CMD mode, both remind about server logs and open a new window for database logs
            LogMessage("Server logs are in the SpacetimeDB server CMD window.", 0);
            
            // Open a new Debian window to view database logs
            LogMessage("Opening a new window for database logs...", 0);
            
            try
            {
                // Create a process to run "spacetime logs moduleName -f" in a visible window
                Process dbLogProcess = new Process();
                dbLogProcess.StartInfo.FileName = "cmd.exe";
                
                // Build command to show database logs
                string wslPath = cmdProcessor.GetWslPath(serverDirectory);
                string logCommand = $"cd \"{wslPath}\" && spacetime logs {moduleName} -f";
                
                // Build full command with appropriate escaping
                string escapedCommand = logCommand.Replace("\"", "\\\"");
                dbLogProcess.StartInfo.Arguments = $"/k wsl -d Debian -u {userName} --exec bash -l -c \"{escapedCommand}\"";
                dbLogProcess.StartInfo.UseShellExecute = true;
                dbLogProcess.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                dbLogProcess.StartInfo.CreateNoWindow = false;
                
                dbLogProcess.Start();
                LogMessage("Database logs window opened. Close the window when finished.", 0);
            }
            catch (Exception ex)
            {
                LogMessage($"Error opening database logs window: {ex.Message}", -1);
            }
        }
        else
        {
            // Not silent, not running
            LogMessage("Server is not running.", -1);
        }
    }
    #endregion
    
    #region Utility Methods

    private void OpenDebianWindow()
    {
        bool userNameReq = false;
        cmdProcessor.OpenDebianWindow(userNameReq);
    }

    // Any PingServer method will start WSL to check if Server is running
    public bool PingServerStatus()
    {
        PingServer(false);
        return pingShowsOnline;
    }
    private void PingServer(bool showLog)
    {
        string url = !string.IsNullOrEmpty(serverUrl) ? serverUrl : "http://127.0.0.1:3000";
        if (url.EndsWith("/"))
        {
            url = url.TrimEnd('/');
        }
        if (debugMode) LogMessage($"Pinging server at {url}...", 0);
        
        cmdProcessor.PingServer(url, (isOnline, message) => {
            EditorApplication.delayCall += () => {
                if (isOnline)
                {
                    if (showLog) LogMessage($"Server is online: {url}", 1);
                    pingShowsOnline = true;
                }
                else
                {
                    if (showLog) LogMessage($"Server is offline: {message}", -1);
                    pingShowsOnline = false;
                }
                
                Repaint();
            };
        });
    }

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
            SessionState.SetBool(SessionKeyWasRunningSilently, serverStarted && silentMode);
            
            // Update log processor state
            if (logProcessor != null)
            {
                logProcessor.SetServerRunningState(serverStarted && silentMode);
            }
        }
    }

    public void AttemptTailRestartAfterReload()
    {
        if (debugMode) UnityEngine.Debug.Log($"[ServerWindow] Attempting tail restart in ServerWindow.");
        
        // Delegate to the logProcessor for tail restart
        if (serverStarted && silentMode && cmdProcessor.IsPortInUse(serverPort))
        {
            logProcessor.AttemptTailRestartAfterReload();
        }
        else
        {
            if (debugMode) UnityEngine.Debug.LogWarning("[ServerWindow] Cannot restart tail process - server not running or not in silent mode");
            // Server likely stopped during play mode or reload, update state
            serverStarted = false;
            EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", false);
            SessionState.SetBool(SessionKeyWasRunningSilently, false); // Update persisted state
            Repaint();
        }
    }

    public void StopTailProcessExplicitly()
    {
        if (logProcessor != null)
        {
            logProcessor.StopTailProcessExplicitly();
        }
    }

    public void AttemptDatabaseLogRestartAfterReload()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerWindow] Checking database log process");
        
        // Delegate to the logProcessor
        if (serverStarted && silentMode && cmdProcessor.IsPortInUse(serverPort))
        {
            logProcessor.AttemptDatabaseLogRestartAfterReload();
        }
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
                break;
            case ServerMode.MaincloudServer:
                if (debugMode) LogMessage("Server mode set to: Maincloud", 0);
                break;
        }

        EditorPrefs.SetInt(PrefsKeyPrefix + "ServerMode", (int)serverMode);
        Repaint();
    }

    private void LoadServerModeFromPrefs()
    {
        // Load server mode from preferences
        serverMode = (ServerMode)EditorPrefs.GetInt(PrefsKeyPrefix + "ServerMode", (int)ServerMode.WslServer);
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