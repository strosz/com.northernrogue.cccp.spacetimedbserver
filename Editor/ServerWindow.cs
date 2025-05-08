using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NorthernRogue.CCCP.Editor {

public class ServerWindow : EditorWindow
{
    // Process Handlers
    private ServerCMDProcess cmdProcessor;
    private ServerLogProcess logProcessor;
    private Process serverProcess;

    // Pre-requisites
    private bool hasWSL = false;
    private bool hasDebian = false;
    private bool hasDebianTrixie = false;
    private bool hasCurl = false;
    private bool hasSpacetimeDBServer = false;
    private bool hasSpacetimeDBPath = false;
    private bool hasRust = false;
    private bool prerequisitesChecked = false;

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
    private double stopInitiatedTime = 0;

    // UI
    private Vector2 scrollPosition;
    private string outputLog = "";
    private bool autoscroll = true;
    
    // Settings
    private const string PrefsKeyPrefix = "ServerWindow_";
    private string serverDirectory = "";
    private string clientDirectory = "";
    private string serverLang = "rust";
    private string serverUrl = "";
    private int spacetimePort = 3000;
    private string moduleName = "";
    private string unityLang = "rust";
    private string authToken = "";
    private string backupDirectory = "";
    private string userName = "";
    private bool hideWarnings = false;
    private bool detectServerChanges = false;
    private bool serverChangesDetected = false;
    private bool autoPublishMode = false;
    private bool publishAndGenerateMode = false;
    private bool silentMode = false;
    public bool debugMode = false;
    private bool autoCloseWsl = false;
    private bool clearModuleLogAtStart = false;
    private bool clearDatabaseLogAtStart = false;
    
    // Session state key for domain reload
    private const string SessionKeyWasRunningSilently = "ServerWindow_WasRunningSilently";

    [MenuItem("SpacetimeDB/Server Management Panel", priority = -9000)]
    public static void ShowWindow()
    {
        ServerWindow window = GetWindow<ServerWindow>("Server");
        window.minSize = new Vector2(270f, 600f);
    }

    #region OnGUI
    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();
               
        // Load and display the logo image
        Texture2D logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.northernrogue.cccp.spacetimedbserver/Editor/cosmos_logo.png");
        if (logoTexture != null)
        {
            float maxHeight = 70f;
            float aspectRatio = (float)logoTexture.width / logoTexture.height;
            float width = maxHeight * aspectRatio;
            float height = maxHeight;
            
            // Center the image
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(logoTexture, GUILayout.Width(width), GUILayout.Height(height));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(-10);
            // Add the subtitle
            GUILayout.Label("Control your SpacetimeDB server and run commands.\n If starting fresh check the pre-requisites first.", EditorStyles.centeredGreyMiniLabel);
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
        
        // Output log header with Clear button
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Command Output:", EditorStyles.boldLabel, GUILayout.Width(120));

        GUILayout.FlexibleSpace();

        // Autoscroll button - now more subtle
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
            outputLog = "";
            Repaint();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        
        // Output log with rich text support
        GUIStyle richTextStyle = new GUIStyle(EditorStyles.textArea);
        richTextStyle.richText = true;
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        EditorGUILayout.TextArea(outputLog, richTextStyle, GUILayout.ExpandHeight(true));
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
        // Load Editor Prefs - Pre-Requisites Settings
        prerequisitesChecked = EditorPrefs.GetBool(PrefsKeyPrefix + "PrerequisitesChecked", false);
        serverDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "ServerDirectory", "");
        clientDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "ClientDirectory", "");
        serverUrl = EditorPrefs.GetString(PrefsKeyPrefix + "ServerURL", "http://0.0.0.0:3000/");
        spacetimePort = EditorPrefs.GetInt(PrefsKeyPrefix + "SpacetimePort", 3000);
        serverLang = EditorPrefs.GetString(PrefsKeyPrefix + "ServerLang", "rust");
        moduleName = EditorPrefs.GetString(PrefsKeyPrefix + "ModuleName", "");
        unityLang = EditorPrefs.GetString(PrefsKeyPrefix + "UnityLang", "csharp");
        authToken = EditorPrefs.GetString(PrefsKeyPrefix + "AuthToken", "");
        backupDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "BackupDirectory", "");
        userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        // Load Editor Prefs - Settings
        hideWarnings = EditorPrefs.GetBool(PrefsKeyPrefix + "HideWarnings", true);
        detectServerChanges = EditorPrefs.GetBool(PrefsKeyPrefix + "DetectServerChanges", true);
        autoPublishMode = EditorPrefs.GetBool(PrefsKeyPrefix + "AutoPublishMode", false);
        publishAndGenerateMode = EditorPrefs.GetBool(PrefsKeyPrefix + "PublishAndGenerateMode", true);
        silentMode = EditorPrefs.GetBool(PrefsKeyPrefix + "SilentMode", true);
        debugMode = EditorPrefs.GetBool(PrefsKeyPrefix + "DebugMode", false);
        autoCloseWsl = EditorPrefs.GetBool(PrefsKeyPrefix + "AutoCloseWsl", true);
        clearModuleLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearModuleLogAtStart", false);
        clearDatabaseLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearDatabaseLogAtStart", false);

        // Initialize the processors
        cmdProcessor = new ServerCMDProcess(LogMessage, debugMode);
        cmdProcessor.SetUserName(userName); // Pass userName to cmdProcessor
        
        // Initialize LogProcessor with callbacks
        logProcessor = new ServerLogProcess(
            LogMessage,
            () => ServerOutputWindow.RefreshOpenWindow(), // Module log update callback
            () => ServerOutputWindow.RefreshOpenWindow(), // Database log update callback
            cmdProcessor,
            debugMode
        );
        
        // Configure the log processor
        logProcessor.Configure(moduleName, serverDirectory, clearModuleLogAtStart, clearDatabaseLogAtStart, userName); // Pass userName to logProcessor
        logProcessor.SetServerRunningState(serverStarted);
        
        // Register for focus events
        EditorApplication.focusChanged += OnFocusChanged;
        
        // Ensure ServerOutputWindow debug mode matches on enable
        ServerOutputWindow.debugMode = debugMode;
        
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

            // Check Pre-Requisites button
            if (GUILayout.Button("Check Pre-Requisites", GUILayout.Height(20)))
            {
                CheckPrerequisites();
            }
                       
            // Debian Username setting
            EditorGUILayout.BeginHorizontal();
            string userNameTooltip = 
            "The Debian username to use for Debian commands.\n\n"+
            "Note: Needed for most server commands and utilities.";
            EditorGUILayout.LabelField(new GUIContent("Debian Username:", userNameTooltip), GUILayout.Width(110));
            string newUserName = EditorGUILayout.DelayedTextField(userName, GUILayout.Width(130));
            if (newUserName != userName)
            {
                userName = newUserName;
                EditorPrefs.SetString(PrefsKeyPrefix + "UserName", userName);
                
                // Update processors with new username
                if(cmdProcessor != null) cmdProcessor.SetUserName(userName);
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
            if (GUILayout.Button("Set Backup Directory", GUILayout.Width(130), GUILayout.Height(20)))
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
            if (GUILayout.Button("Set Server Directory", GUILayout.Width(130), GUILayout.Height(20)))
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
            int newServerLangSelectedIndex = EditorGUILayout.Popup(serverLangSelectedIndex, serverLangOptions, GUILayout.Width(130));
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
            "The name of your SpacetimeDB module you used when you created the module.";
            EditorGUILayout.LabelField(new GUIContent("Module Name:", moduleNameTooltip), GUILayout.Width(110));
            string newModuleName = EditorGUILayout.TextField(moduleName, GUILayout.Width(130));
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
            if (GUILayout.Button("Set Client Directory", GUILayout.Width(130), GUILayout.Height(20)))
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
            int newunityLangSelectedIndex = EditorGUILayout.Popup(unityLangSelectedIndex, unityLangOptions, GUILayout.Width(130));
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
            "Required for the Server Database Window. The full URL of your SpacetimeDB server including port number.\nDefault: http://127.0.0.1:3000/";
            EditorGUILayout.LabelField(new GUIContent("URL:", urlTooltip), GUILayout.Width(110));
            string newUrl = EditorGUILayout.TextField(serverUrl, GUILayout.Width(130));
            if (newUrl != serverUrl)
            {
                serverUrl = newUrl;
                EditorPrefs.SetString(PrefsKeyPrefix + "ServerURL", serverUrl);
            }
            GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(serverUrl)), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Port setting
            EditorGUILayout.BeginHorizontal();
            string portTooltip = 
            "Required for the Server Database Window. The port of the SpacetimeDB server\nDefault: 3000";
            EditorGUILayout.LabelField(new GUIContent("Port:", portTooltip), GUILayout.Width(110));
            string newPort = EditorGUILayout.TextField(spacetimePort.ToString(), GUILayout.Width(130));
            if (newPort != spacetimePort.ToString())
            {
                spacetimePort = int.Parse(newPort);
                EditorPrefs.SetInt(PrefsKeyPrefix + "SpacetimePort", spacetimePort);
            }
            GUILayout.Label(GetStatusIcon(!string.IsNullOrEmpty(spacetimePort.ToString())), GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            // Auth Token setting
            EditorGUILayout.BeginHorizontal();
            string tokenTooltip = 
            "Required for the Server Database Window. See it by running the Show Login Info utility command after server startup and paste it here.\n\n"+
            "Important: Keep this token secret and do not share it with anyone outside of your team.";
            EditorGUILayout.LabelField(new GUIContent("Auth Token:", tokenTooltip), GUILayout.Width(110));
            string newAuthToken = EditorGUILayout.PasswordField(authToken, GUILayout.Width(130));
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
            if (GUILayout.Button("Init a new module", GUILayout.Width(130), GUILayout.Height(20)))
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
                bool result = EditorUtility.DisplayDialog("Init a new module", "Are you sure you want to init a new module?\nDon't do this if you already have a module in the server directory.", "Yes", "No");
                if (result)
                {
                    InitNewModule();
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical(); // End of Pre-Requisites section
    }
    #endregion

    #region ServerUI
    private void DrawServerSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
        
        bool serverRunning = serverStarted || isStartingUp;
        
        EditorGUI.BeginDisabledGroup(!prerequisitesChecked || !hasWSL || !hasDebian);
        if (!serverRunning)
        {
            if (GUILayout.Button("Start Server", GUILayout.Height(30)))
            {
                StartServer();
            }
        } else {
            if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
            {
                StopServer();
            }
        }
        EditorGUI.EndDisabledGroup();

        /*EditorGUI.BeginDisabledGroup(!serverRunning); // May not be needed
        if (GUILayout.Button("Restart Server", GUILayout.Height(20)))
        {
            StopServer();
            EditorApplication.delayCall += () => StartServer();
        }
        EditorGUI.EndDisabledGroup();*/

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
        bool showSettingsWindow = EditorGUILayout.Foldout(EditorPrefs.GetBool(PrefsKeyPrefix + "ShowSettingsWindow", true), "Settings and Debian", true);
        EditorPrefs.SetBool(PrefsKeyPrefix + "ShowSettingsWindow", showSettingsWindow);

        if (showSettingsWindow)
        {   
            if (GUILayout.Button("Open Debian Window", GUILayout.Height(30)))
            {
                OpenDebianWindow();
            }
        
            // Server Mode toggle
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            string serverModeTooltip = 
            "Show CMD: Displays the standard CMD process window of the server. \n\n"+
            "Silent Mode: The server runs silently in the background without any window.";
            EditorGUILayout.LabelField(new GUIContent("Server Mode:", serverModeTooltip), GUILayout.Width(120));
            GUIStyle silentToggleStyle = new GUIStyle(GUI.skin.button);
            if (silentMode)
            {
                // Use a specific color for hidden mode, e.g., gray or a custom color
                Color hiddenColor;
                ColorUtility.TryParseHtmlString("#808080", out hiddenColor);
                silentToggleStyle.normal.textColor = hiddenColor;
                silentToggleStyle.hover.textColor = hiddenColor;
            }
            if (GUILayout.Button(silentMode ? "Silent Mode" : "Show CMD", silentToggleStyle))
            {
                silentMode = !silentMode;
                EditorPrefs.SetBool(PrefsKeyPrefix + "SilentMode", silentMode);
                LogMessage(silentMode ? "Server will now start hidden (Silent Mode)." : "Server will now start with a visible CMD window.", 0);
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
                changeToggleStyle.normal.textColor = Color.green;
                changeToggleStyle.hover.textColor = Color.green;
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
                    LogMessage("Server change detection enabled", 0);
                }
                else
                {
                    serverChangesDetected = false;
                    LogMessage("Server change detection disabled", 0);
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
                autoPublishStyle.normal.textColor = Color.green;
                autoPublishStyle.hover.textColor = Color.green;
            }
            if (GUILayout.Button(autoPublishMode ? "Automatic Publishing" : "Manual Publish", autoPublishStyle))
            {
                autoPublishMode = !autoPublishMode;
                EditorPrefs.SetBool(PrefsKeyPrefix + "AutoPublishMode", autoPublishMode);
                
                if (autoPublishMode)
                {
                    if (!detectServerChanges)
                    {
                        // Auto-enable change detection if it's not already on
                        detectServerChanges = true;
                        EditorPrefs.SetBool(PrefsKeyPrefix + "DetectServerChanges", true);
                        originalFileSizes.Clear();
                        currentFileSizes.Clear();
                        DetectServerChanges();
                        LogMessage("Server change detection required and was automatically enabled", 0);
                    }
                    LogMessage("Automatic publishing enabled - modules will publish when changes are detected while the server is running", 0);
                }
                else
                {
                    LogMessage("Automatic publishing disabled - manual publishing required", 0);
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
                publishGenerateStyle.normal.textColor = Color.green;
                publishGenerateStyle.hover.textColor = Color.green;
            }
            if (GUILayout.Button(publishAndGenerateMode ? "Publish will Generate" : "Separate Generate", publishGenerateStyle))
            {
                publishAndGenerateMode = !publishAndGenerateMode;
                EditorPrefs.SetBool(PrefsKeyPrefix + "PublishAndGenerateMode", publishAndGenerateMode);
                
                if (publishAndGenerateMode)
                {
                    LogMessage("Publish will now automatically trigger Generate Unity Files upon success.", 0);
                }
                else
                {
                    LogMessage("Publish and Generate are now separate steps.", 0);
                }
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
                // Use a warning color (e.g., orange/red) to indicate WSL will shut down
                Color warningColor;
                ColorUtility.TryParseHtmlString("#FFA500", out warningColor); // Orange
                wslCloseStyle.normal.textColor = warningColor;
                wslCloseStyle.hover.textColor = warningColor;
            }
            if (GUILayout.Button(autoCloseWsl ? "Close WSL at Server Stop" : "Keep Running", wslCloseStyle))
            {
                autoCloseWsl = !autoCloseWsl;
                EditorPrefs.SetBool(PrefsKeyPrefix + "AutoCloseWsl", autoCloseWsl);
                LogMessage(autoCloseWsl ? "WSL will be shut down when the server is stopped or Unity is closed." : "WSL will keep running after the server stops or Unity is closed.", 0);
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
                // Convert hex color to Unity Color
                Color warningColor;
                ColorUtility.TryParseHtmlString("#FFA500", out warningColor);
                warningToggleStyle.normal.textColor = warningColor;
                warningToggleStyle.hover.textColor = warningColor;
            }
            if (GUILayout.Button(hideWarnings ? "Hiding Extra Warnings" : "Show All Messages", warningToggleStyle))
            {
                hideWarnings = !hideWarnings;
                EditorPrefs.SetBool(PrefsKeyPrefix + "HideWarnings", hideWarnings);
                LogMessage(hideWarnings ? "Now hiding SpacetimeDB WARNING messages" : "Now showing all messages", hideWarnings ? 0 : 1);
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
                    // Convert hex color to Unity Color
                    Color warningColor;
                    ColorUtility.TryParseHtmlString("#FFA500", out warningColor);
                    moduleLogToggleStyle.normal.textColor = warningColor;
                    moduleLogToggleStyle.hover.textColor = warningColor;
                }
                if (GUILayout.Button(clearModuleLogAtStart ? "Clear Module Log at Server Start" : "Keeping Module Log", moduleLogToggleStyle))
                {
                    clearModuleLogAtStart = !clearModuleLogAtStart;
                    EditorPrefs.SetBool(PrefsKeyPrefix + "ClearModuleLogAtStart", clearModuleLogAtStart);
                    LogMessage(clearModuleLogAtStart ? "Now clearing Module Log At Silent Server Start" : "Now keeping Module Log between server restarts", clearModuleLogAtStart ? 0 : 1);
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
                    // Convert hex color to Unity Color
                    Color warningColor;
                    ColorUtility.TryParseHtmlString("#FFA500", out warningColor);
                    databaseLogToggleStyle.normal.textColor = warningColor;
                    databaseLogToggleStyle.hover.textColor = warningColor;
                }
                if (GUILayout.Button(clearDatabaseLogAtStart ? "Clear Database Log at Server Start" : "Keeping Database Log", databaseLogToggleStyle))
                {
                    clearDatabaseLogAtStart = !clearDatabaseLogAtStart;
                    EditorPrefs.SetBool(PrefsKeyPrefix + "ClearDatabaseLogAtStart", clearDatabaseLogAtStart);
                    LogMessage(clearDatabaseLogAtStart ? "Now clearing Database Log At Silent Server Start" : "Now keeping Database Log between server restarts", clearDatabaseLogAtStart ? 0 : 1);
                }
                EditorGUILayout.EndHorizontal();
            }

            // Debug Mode toggle
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            string debugTooltip = 
            "Debug Mode: Will show all server management debug messages. \n\n"+
            "Debug Disabled: Will not show most debug messages. Errors are still shown.";
            EditorGUILayout.LabelField(new GUIContent("Debug:", debugTooltip), GUILayout.Width(120));
            GUIStyle debugToggleStyle = new GUIStyle(GUI.skin.button);
            if (debugMode)
            {
                // Use a specific color for debug mode, e.g., magenta or cyan
                Color debugColor;
                ColorUtility.TryParseHtmlString("#00FFFF", out debugColor); // Cyan
                debugToggleStyle.normal.textColor = debugColor;
                debugToggleStyle.hover.textColor = debugColor;
            }
            if (GUILayout.Button(debugMode ? "Debug Mode" : "Debug Disabled", debugToggleStyle))
            {
                debugMode = !debugMode;
                EditorPrefs.SetBool(PrefsKeyPrefix + "DebugMode", debugMode);
                LogMessage(debugMode ? "Debug Mode Enabled - Verbose logs will be shown" : "Debug Mode Hidden", 0);
                ServerOutputWindow.debugMode = debugMode; // Update ServerOutputWindow's debug mode
                ServerWindowInitializer.debugMode = debugMode; // Update ServerWindowInitializer's debug mode
                ServerUpdateProcess.debugMode = debugMode; // Update ServerUpdateProcess's debug mode
                ServerLogProcess.debugMode = debugMode; // Update ServerLogProcess's debug mode
                ServerCMDProcess.debugMode = debugMode; // Update ServerCMDProcess's debug mode
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
            
            if (GUILayout.Button("Show Version", GUILayout.Height(20)))
            {
                RunServerCommand("spacetime --version", "Showing SpacetimeDB version");
            }
            
            // Backup Server Data button
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(backupDirectory));
            if (GUILayout.Button("Backup Server Data", GUILayout.Height(20)))
            {  
                string wslBackupPath = GetWslPath(backupDirectory);
                string spacetimePath = $"/home/{userName}/.local/share/spacetime/data";
                // Ensure the converted path is valid and not just the home directory
                if (string.IsNullOrEmpty(backupDirectory) || wslBackupPath == "~")
                {
                    LogMessage("Error: Backup directory is not set or invalid.", -1);
                }
                else
                {
                    // --- Restore backup command, using cd && tar approach ---
                    // string debugCommand = $"ls -la /home/{userName}/.local/share/spacetime/data"; 
                    // Construct the backup command using cd to parent dir and relative source
                    string backupCommand = $"tar czf \"{wslBackupPath}/spacetimedb_backup_$(date +%F_%H-%M-%S).tar.gz\" {spacetimePath}"; 
                    RunServerCommand(backupCommand, "Backing up SpacetimeDB data");
                    //LogMessage("Backup Command: " + backupCommand, 0);
                }
            }
            EditorGUI.EndDisabledGroup();
            
            // Restore Server Data button
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(backupDirectory));
            if (GUILayout.Button("Restore Server Data", GUILayout.Height(20)))
            {
                string wslBackupPath = GetWslPath(backupDirectory);
                
                // Ensure the backup directory is valid
                if (!string.IsNullOrEmpty(backupDirectory) && wslBackupPath != "~")
                {
                    // Show file selection dialog
                    string backupFilePath = EditorUtility.OpenFilePanel("Select Backup File", backupDirectory, "gz");
                    if (!string.IsNullOrEmpty(backupFilePath))
                    {
                        // Convert Windows path to WSL path for the selected backup file
                        string wslBackupFilePath = GetWslPath(backupFilePath);
                        
                        // Display confirmation dialog due to overwrite risk
                        if (EditorUtility.DisplayDialog(
                            "Confirm Restore",
                            "This will extract your backup and restore the data, replacing all current data.\n\nAre you sure you want to continue?",
                            "Yes, Continue",
                            "Cancel"))
                        {
                            // Ask if the user wants to create a backup first
                            bool createBackup = EditorUtility.DisplayDialog(
                                "Create Backup",
                                "Do you want to create a backup of your current data before restoring?\n\n" +
                                "This will create a .tar.gz backup file in your backup directory.",
                                "Yes, Create Backup",
                                "No, Skip Backup");
                                
                            if (createBackup)
                            {
                                // Create backup using the same logic as the Backup Server Data button
                                LogMessage("Creating backup of current data before restoring...", 0);
                                
                                // Construct the backup command using the same logic as in Backup Server Data
                                string wslBackupPathForSaving = GetWslPath(backupDirectory);
                                string spacetimeDataPath = $"/home/{userName}/.local/share/spacetime/data";
                                
                                // Ensure the converted path is valid and not just the home directory
                                if (!string.IsNullOrEmpty(backupDirectory) && wslBackupPathForSaving != "~")
                                {
                                    // Construct backup command with timestamp
                                    string backupCommand = $"tar czf \"{wslBackupPathForSaving}/spacetimedb_pre_restore_backup_$(date +%F_%H-%M-%S).tar.gz\" {spacetimeDataPath}";
                                    
                                    // Execute the backup command
                                    RunServerCommand(backupCommand, "Creating pre-restore backup");
                                    LogMessage("Pre-restore backup created successfully in your backup directory.", 1);
                                }
                                else
                                {
                                    LogMessage("Warning: Could not create backup. Backup directory is not set or invalid.", -2);
                                    
                                    // Ask if the user wants to continue without backup
                                    if (!EditorUtility.DisplayDialog(
                                        "Continue Without Backup?",
                                        "Could not create a backup because the backup directory is not set or invalid.\n\n" +
                                        "Do you want to continue with the restore without creating a backup?",
                                        "Yes, Continue Anyway",
                                        "No, Cancel Restore"))
                                    {
                                        LogMessage("Restore canceled by user.", 0);
                                        return;
                                    }
                                }
                            }
                            
                            // Stop server if running to prevent database corruption
                            bool wasRunning = serverStarted;
                            if (wasRunning)
                            {
                                LogMessage("Stopping server before restore...", 0);
                                bool autoCloseWslWasTrue = autoCloseWsl;
                                autoCloseWsl = false; // Disable auto close WSL to prevent it from closing during restore
                                StopServer();
                                // Small delay to ensure server has time to stop
                                System.Threading.Thread.Sleep(2000);
                                if (autoCloseWslWasTrue)
                                {
                                    autoCloseWsl = true; // Enable auto close WSL after restore
                                }
                            }
                            
                            try
                            {
                                LogMessage("Starting automated file restore...", 0);
                                
                                // Create Windows temp directory for extraction
                                string windowsTempPath = Path.Combine(Path.GetTempPath(), "SpacetimeDBRestore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                                Directory.CreateDirectory(windowsTempPath);
                                LogMessage($"Created temporary directory: {windowsTempPath}", 0);
                                
                                // Use a more straightforward approach with tar (which is now available on Windows 10)
                                LogMessage("Extracting backup archive... (this may take a moment)", 0);
                                
                                // First, write a batch file to execute the extraction
                                string batchFilePath = Path.Combine(Path.GetTempPath(), "extract_backup.bat");
                                string batchContent = $@"@echo off
                                echo Extracting {backupFilePath} to {windowsTempPath}...
                                mkdir ""{windowsTempPath}"" 2>nul
                                tar -xf ""{backupFilePath}"" -C ""{windowsTempPath}""
                                echo Extraction complete
                                ";
                                
                                File.WriteAllText(batchFilePath, batchContent);
                                LogMessage($"Created extraction batch file: {batchFilePath}", 0);
                                
                                // Run the batch file
                                Process extractProcess = new Process();
                                extractProcess.StartInfo.FileName = "cmd.exe";
                                extractProcess.StartInfo.Arguments = $"/c \"{batchFilePath}\"";
                                extractProcess.StartInfo.UseShellExecute = false;
                                extractProcess.StartInfo.CreateNoWindow = true;
                                extractProcess.StartInfo.RedirectStandardOutput = true;
                                extractProcess.StartInfo.RedirectStandardError = true;
                                
                                string extractOutput = "";
                                string extractError = "";
                                
                                extractProcess.OutputDataReceived += (sender, e) => {
                                    if (!string.IsNullOrEmpty(e.Data)) extractOutput += e.Data + "\n";
                                };
                                extractProcess.ErrorDataReceived += (sender, e) => {
                                    if (!string.IsNullOrEmpty(e.Data)) extractError += e.Data + "\n";
                                };
                                
                                extractProcess.Start();
                                extractProcess.BeginOutputReadLine();
                                extractProcess.BeginErrorReadLine();
                                extractProcess.WaitForExit();
                                
                                // Clean up the batch file
                                try {
                                    File.Delete(batchFilePath);
                                } catch {
                                    // Ignore errors when deleting the batch file
                                }
                                
                                if (!string.IsNullOrEmpty(extractOutput))
                                    LogMessage("Extraction output: " + extractOutput, 0);
                                if (!string.IsNullOrEmpty(extractError))
                                    LogMessage("Extraction errors: " + extractError, -1);
                                
                                LogMessage("Extraction completed.", 1);
                                
                                // Find the spacetime directory within the extracted files
                                string expectedSpacetimePath = Path.Combine(windowsTempPath, "home", userName, ".local", "share", "spacetime");
                                string extractedFolderToOpen = windowsTempPath;
                                
                                // Check if the expected path structure exists
                                if (Directory.Exists(expectedSpacetimePath))
                                {
                                    extractedFolderToOpen = expectedSpacetimePath;
                                    LogMessage($"Found spacetime directory in extracted backup at: {expectedSpacetimePath}", 1);
                                }
                                else
                                {
                                    // Try to search for the spacetime directory
                                    LogMessage("Searching for spacetime directory in extracted files...", 0);
                                    string[] foundDirs = Directory.GetDirectories(windowsTempPath, "spacetime", SearchOption.AllDirectories);
                                    
                                    if (foundDirs.Length > 0)
                                    {
                                        // Use the first found spacetime directory
                                        extractedFolderToOpen = foundDirs[0];
                                        LogMessage($"Found spacetime directory at: {extractedFolderToOpen}", 1);
                                    }
                                    else
                                    {
                                        LogMessage("Could not find spacetime directory in extracted files. Falling back to root extraction folder.", 0);
                                    }
                                }
                                
                                // Define source and destination paths
                                string sourceDataDir = Path.Combine(extractedFolderToOpen, "data");
                                string wslPath = $@"\\wsl.localhost\Debian\home\{userName}\.local\share\spacetime";
                                string destDataDir = Path.Combine(wslPath, "data");
                                
                                // Verify data directory exists in extracted backup
                                if (!Directory.Exists(sourceDataDir))
                                {
                                    LogMessage($"Error: Data directory not found in extracted backup at {sourceDataDir}", -1);
                                    LogMessage("Falling back to manual restore method...", 0);
                                    
                                    // Open both explorer windows as fallback
                                    Process.Start("explorer.exe", $"\"{extractedFolderToOpen}\"");
                                    Process.Start("explorer.exe", $"\"{wslPath}\"");
                                    
                                    EditorUtility.DisplayDialog(
                                        "Manual Restore Required",
                                        "The data directory could not be found automatically in the extracted backup.\n\n" +
                                        "Two Explorer windows have been opened:\n" +
                                        "1. The extracted backup files\n" +
                                        "2. The WSL SpacetimeDB directory\n\n" +
                                        "Please manually find the 'data' folder in the backup and copy it to replace the one in the WSL window.",
                                        "OK"
                                    );
                                    return;
                                }
                                
                                // Perform the automated restore
                                try
                                {
                                    LogMessage("Starting automated file restore...", 0);
                                    
                                    // Delete existing data directory if it exists
                                    if (Directory.Exists(destDataDir))
                                    {
                                        LogMessage($"Removing existing data directory at {destDataDir}", 0);
                                        Directory.Delete(destDataDir, true);
                                    }
                                    
                                    // Copy the extracted data directory to the WSL path
                                    LogMessage($"Copying data directory from {sourceDataDir} to {destDataDir}", 0);
                                    
                                    // Create the destination directory
                                    Directory.CreateDirectory(destDataDir);
                                    
                                    // Copy files and subdirectories
                                    CopyDirectory(sourceDataDir, destDataDir);
                                    
                                    LogMessage("Restore completed successfully!", 1);
                                    
                                    // Clean up temporary extraction folder
                                    try
                                    {
                                        LogMessage("Cleaning up temporary extraction directory...", 0);
                                        Directory.Delete(windowsTempPath, true);
                                        LogMessage("Cleanup completed.", 0);
                                    }
                                    catch (Exception cleanupEx)
                                    {
                                        LogMessage($"Warning: Could not clean up temporary extraction directory: {cleanupEx.Message}", -2);
                                        LogMessage($"You may manually delete the directory later: {windowsTempPath}", 0);
                                    }
                                    
                                    EditorUtility.DisplayDialog(
                                        "Restore Completed",
                                        "SpacetimeDB data has been successfully restored from backup.",
                                        "OK"
                                    );
                                    
                                    // Restart server if it was running before
                                    if (wasRunning)
                                    {
                                        StartServer();
                                    }

                                    LogMessage("Restore completed successfully!", 1);
                                }
                                catch (Exception ex)
                                {
                                    LogMessage($"Error during automated restore: {ex.Message}", -1);
                                    
                                    // Fall back to manual restore
                                    LogMessage("Falling back to manual restore method...", 0);
                                    Process.Start("explorer.exe", $"\"{extractedFolderToOpen}\"");
                                    Process.Start("explorer.exe", $"\"{wslPath}\"");
                                    
                                    EditorUtility.DisplayDialog(
                                        "Automated Restore Failed",
                                        $"Error: {ex.Message}\n\n" +
                                        "Two Explorer windows have been opened for manual restore:\n" +
                                        "1. The extracted backup files\n" +
                                        "2. The WSL SpacetimeDB directory\n\n" +
                                        "Please manually copy the 'data' folder to complete the restore.",
                                        "OK"
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Error during restore preparation: {ex.Message}", -1);
                                LogMessage($"Stack trace: {ex.StackTrace}", -2);
                            }
                        }
                        else
                        {
                            LogMessage("Restore canceled by user.", 0);
                        }
                    }
                    else
                    {
                        LogMessage("Restore canceled: No backup file selected.", 0);
                    }
                }
                else
                {
                    LogMessage("Error: Backup directory is not set or invalid.", -1);
                }
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
        
        EditorGUILayout.EndVertical();
    }
    #endregion
    
    #region Server Methods
    private void CheckPrerequisites()
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
                prerequisitesChecked = true;
                
                // Save state
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasWSL", hasWSL);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebian", hasDebian);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebianTrixie", hasDebianTrixie);
                EditorPrefs.SetBool(PrefsKeyPrefix + "PrerequisitesChecked", prerequisitesChecked);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasCurl", hasCurl);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", hasSpacetimeDBServer);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", hasSpacetimeDBPath);
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasRust", hasRust);
                
                Repaint();
                if (!hasWSL || !hasDebian || !hasDebianTrixie || !hasCurl || !hasSpacetimeDBServer || !hasSpacetimeDBPath || !hasRust)
                {
                    ServerInstallerWindow.ShowWindow();
                }
            };
        });
    }

    private void InitNewModule()
    {
        string wslPath = GetWslPath(serverDirectory);
        
        // Combine cd and init command
        string command = $"cd \"{wslPath}\" && spacetime init --lang {serverLang} .";
        
        cmdProcessor.RunWslCommandSilent(command);
        LogMessage("New module initialized", 1);
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
    
    private void StartServer()
    {
        if (!hasWSL || !hasDebian || !hasDebianTrixie || !hasSpacetimeDBServer)
        {
            LogMessage("Missing required installed items. Will attempt to start server.", -1);
        }
        if (string.IsNullOrEmpty(userName))
        {
            LogMessage("Cannot start server. Debian username is not set.", -1);
            return;
        }

        if (cmdProcessor.CheckIfServerRunningWsl()) // Check if server is running
        {
            LogMessage("Server appears to be already running (checked via WSL PID).", 1);
            if (!serverStarted)
            {
                serverStarted = true;
                isStartingUp = false;
                EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", true);
                Repaint();
            }
            return; 
        }
        else
        {
             cmdProcessor.RemoveStalePidWsl(); // Clean up PID if server not running
        }

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
                // Start visible mode
                LogMessage("Starting Spacetime Server (Visible CMD)...", 0);
                serverProcess = cmdProcessor.StartVisibleServerProcess(serverDirectory);
                if (serverProcess == null) throw new Exception("Failed to start visible server process");
            }
            
            // Mark server as starting up
            isStartingUp = true;
            startupTime = (float)EditorApplication.timeSinceStartup;
            serverStarted = true; // Assume starting, CheckServerStatus will verify
            EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", true);
            
            // Update log processor state
            logProcessor.SetServerRunningState(true);
            
            LogMessage("Start sequence initiated. Waiting for confirmation...", 0);
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

    // Helper to convert path for Visible mode command
    private string GetWslPath(string windowsPath)
    {
        return cmdProcessor.GetWslPath(windowsPath);
    }

    private void StopServer()
    {
        if (debugMode) LogMessage("Stop Server button clicked.", 0);
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

            if (debugMode) LogMessage("Stop sequence finished. Server state set to stopped.", 0);
            else LogMessage("Server Successfully Stopped.", 1);
            
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

    private void ViewServerLogs()
    {
        if (silentMode)
        {
            // In silent mode, ALWAYS open the output window 
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

    #region CheckStatus
    private async void CheckServerStatus()
    {
        // Only check periodically
        if (EditorApplication.timeSinceStartup - lastCheckTime < checkInterval) return;
        lastCheckTime = EditorApplication.timeSinceStartup;

        // Check if port is in use
        bool isPortOpen = await cmdProcessor.CheckPortAsync(spacetimePort);
        
        // --- Reset justStopped flag if grace period expired ---
        const double stopGracePeriod = 10.0; // Grace period in seconds
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
                if (debugMode) LogMessage($"Startup confirmed: Port {spacetimePort} is now open.", 1);
                else LogMessage("Server Successfully Started!", 1);
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
                LogMessage($"Server failed to start within grace period (Port {spacetimePort} did not open).", -1);
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
                    ? $"Server running confirmed (Port {spacetimePort}: open)"
                    : $"Server appears to have stopped (Port {spacetimePort}: closed)";
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
                if (debugMode) LogMessage($"Port {spacetimePort} detected, but in post-stop grace period. Ignoring.", 0);
            }
            else
            {
                // Port detected, not recently stopped -> likely external start/recovery
                LogMessage($"Detected server running on port {spacetimePort}.", 1);
                serverStarted = true;
                serverConfirmedRunning = true;
                isStartingUp = false; 
                justStopped = false; // Ensure flag is clear if we recover state
                EditorPrefs.SetBool(PrefsKeyPrefix + "ServerStarted", true);
                
                // Update logProcessor state
                logProcessor.SetServerRunningState(true);
                
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
            if (originalFileSizes.Count == 0 && currentFileSizes.Count == 0 && newSizes.Count > 0)
            {
                if(debugMode) LogMessage("Establishing initial baseline for server file sizes.", 0);
                originalFileSizes = new Dictionary<string, long>(newSizes);
                currentFileSizes = new Dictionary<string, long>(newSizes);
                serverChangesDetected = false;
                Repaint();
                return;
            }

            // 3. Update currentFileSizes based on newSizes 
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
    
    #region Utility Methods
    private void OpenDebianWindow()
    {
        cmdProcessor.OpenDebianWindow();
    }
    
    private string GetStatusIcon(bool status)
    {
        return status ? "✓" : "○";
    }
    
    // Helper method to convert client directory to relative path for generate command
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
            outputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {coloredMessage}\n";
        } 
        else if (style == -1) // Error
        {
            string coloredMessage = $"<color=#FF0000>{message}</color>";
            outputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {coloredMessage}\n";
        }
        else if (style == -2) // Warning
        {
            string coloredMessage = $"<color=#e0a309>{message}</color>";
            outputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {coloredMessage}\n";
        }
        else // Normal (style == 0) Also catches any other style
        { 
            outputLog += $"<color=#575757>{DateTime.Now.ToString("HH:mm:ss")}</color> {message}\n";
        }
        
        // Trim log if it gets too long
        if (outputLog.Length > 10000)
        {
            outputLog = outputLog.Substring(outputLog.Length - 10000);
        }
        
        Repaint();
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
    #endregion

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
        if (serverStarted && silentMode && cmdProcessor.IsPortInUse(spacetimePort))
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
        if (serverStarted && silentMode && cmdProcessor.IsPortInUse(spacetimePort))
        {
            logProcessor.AttemptDatabaseLogRestartAfterReload();
        }
    }

    // Display Cosmos Cove Control Panel title text in the menu bar
    [MenuItem("SpacetimeDB/Cosmos Cove Control Panel", priority = -11000)]
    private static void CosmosCoveControlPanel(){}
    [MenuItem("SpacetimeDB/Cosmos Cove Control Panel", true, priority = -11000)]
    private static bool ValidateCosmosCoveControlPanel(){return false;}

} // Class
} // Namespace