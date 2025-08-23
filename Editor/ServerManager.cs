using UnityEditor;
using System.Diagnostics;
using System;
using System.Threading.Tasks;
using UnityEngine;

// Runs the methods related to managing and controlling the WSL server and Maincloud ///

namespace NorthernRogue.CCCP.Editor {

public class ServerManager
{
    // EditorPrefs key prefix
    private const string PrefsKeyPrefix = "CCCP_";
        
    // Process Handlers
    private ServerCMDProcess cmdProcessor;
    private ServerLogProcess logProcessor;
    private ServerVersionProcess versionProcessor;
    private ServerCustomProcess serverCustomProcess;
    private ServerDetectionProcess detectionProcess;
    private Process serverProcess;
    
    // Server mode
    private ServerMode serverMode = ServerMode.WslServer;

    // Server process
    public bool serverStarted = false;
    private bool isStartingUp = false;
    private float startupTime = 0f;
    private const float serverStartupGracePeriod = 10f;
    
    // Server status
    private const double checkInterval = 5.0;
    private bool serverConfirmedRunning = false;
    private bool justStopped = false;
    private bool pingShowsOnline = true;
    private double stopInitiatedTime = 0;
    private bool publishing = false;
    private bool isStopping = false;
    
    // Server status resilience (prevent false positives during compilation/brief hiccups)
    private int consecutiveFailedChecks = 0;
    private const int maxConsecutiveFailuresBeforeStop = 3;

    // Configuration properties - accessed directly from EditorPrefs
    private string userName;
    private string backupDirectory;
    private string serverDirectory;
    private string unityLang;
    private string clientDirectory;
    private string serverLang;
    private string moduleName;
    private int selectedModuleIndex;
    private string serverUrl;
    private int serverPort;
    private string authToken;

    private string maincloudUrl;
    private string maincloudAuthToken;
    
    // Custom server properties
    private string sshUserName;
    private string customServerUrl;
    private int customServerPort;
    private string customServerAuthToken;
    private string sshPrivateKeyPath;
    
    // Server settings
    private bool debugMode;
    private bool hideWarnings;
    private bool detectServerChanges;
    private bool serverChangesDetected;
    private bool autoPublishMode;
    private bool publishAndGenerateMode;
    private bool silentMode;
    private bool autoCloseWsl;
    private bool clearModuleLogAtStart;
    private bool clearDatabaseLogAtStart;

    // Prerequisites state
    private bool hasWSL;
    private bool hasDebian;
    private bool hasDebianTrixie;
    private bool hasCurl;
    private bool hasSpacetimeDBServer;
    private bool hasSpacetimeDBPath;
    private bool hasSpacetimeDBService;
    private bool hasSpacetimeDBLogsService;
    private bool hasRust;
    private bool hasBinaryen;
    private bool hasGit;
    private bool wslPrerequisitesChecked;
    private bool initializedFirstModule;
    private bool publishFirstModule;
    private bool hasAllPrerequisites;

    // Update SpacetimeDB
    public string spacetimeDBCurrentVersion;
    public string spacetimeDBCurrentVersionCustom;
    public string spacetimeDBCurrentVersionTool;
    public string spacetimeDBLatestVersion;

    // Update Rust
    public string rustCurrentVersion;
    public string rustLatestVersion;
    public string rustupVersion;

    // Server output window settings
    private bool echoToConsole;

    // Properties for external access
    public string UserName => userName;
    public string BackupDirectory => backupDirectory;
    public string ServerDirectory => serverDirectory;
    public string UnityLang => unityLang;
    public string ClientDirectory => clientDirectory;
    public string ServerLang => serverLang;
    public string ModuleName => moduleName;
    public int SelectedModuleIndex => selectedModuleIndex;
    public string ServerUrl => serverUrl;
    public int ServerPort => serverPort;
    public string AuthToken => authToken;
    
    public string SSHUserName => sshUserName;
    public string CustomServerUrl => customServerUrl;
    public int CustomServerPort => customServerPort;
    public string CustomServerAuthToken => customServerAuthToken;
    public string SSHPrivateKeyPath => sshPrivateKeyPath;
    
    public bool DebugMode => debugMode;
    public bool HideWarnings => hideWarnings;
    public bool DetectServerChanges => detectServerChanges;
    public bool ServerChangesDetected { get => serverChangesDetected; set => serverChangesDetected = value; }
    public bool AutoPublishMode => autoPublishMode;
    public bool PublishAndGenerateMode => publishAndGenerateMode;
    public bool SilentMode => silentMode;
    public bool AutoCloseWsl { get => autoCloseWsl; set => SetAutoCloseWsl(value); }
    public bool ClearModuleLogAtStart => clearModuleLogAtStart;
    public bool ClearDatabaseLogAtStart => clearDatabaseLogAtStart;

    public string MaincloudUrl => maincloudUrl;
    public string MaincloudAuthToken => maincloudAuthToken;

    // Status properties
    public bool IsServerStarted => serverStarted;
    public bool IsStartingUp => isStartingUp;
    public bool IsServerRunning => serverConfirmedRunning;
    public bool IsStopping => isStopping;
    public ServerMode CurrentServerMode => serverMode;
    public bool Publishing => publishing;

    // Prerequisites properties
    public bool HasWSL => hasWSL;
    public bool HasDebian => hasDebian;
    public bool HasDebianTrixie => hasDebianTrixie;
    public bool HasCurl => hasCurl;
    public bool HasSpacetimeDBServer => hasSpacetimeDBServer;
    public bool HasSpacetimeDBPath => hasSpacetimeDBPath;
    public bool HasSpacetimeDBService => hasSpacetimeDBService;
    public bool HasSpacetimeDBLogsService => hasSpacetimeDBLogsService;
    public bool HasRust => hasRust;
    public bool HasBinaryen => hasBinaryen;
    public bool HasGit => hasGit;
    public bool WslPrerequisitesChecked => wslPrerequisitesChecked;
    public bool InitializedFirstModule => initializedFirstModule;
    public bool PublishFirstModule => publishFirstModule;
    public bool HasAllPrerequisites => hasAllPrerequisites;

    // Process getters
    public ServerCMDProcess GetCmdProcessor() => cmdProcessor;

    // Callbacks
    public Action<string, int> LogCallback { get; set; }
    public Action RepaintCallback { get; set; }

    // WSL Connection Status
    public bool IsWslRunning => isWslRunning;
    private bool isWslRunning = false;
    private double lastWslCheckTime = 0;
    private const double wslCheckInterval = 5.0;

    // Maincloud Connection Status
    public bool IsMaincloudConnected => isMaincloudConnected;
    private bool isMaincloudConnected = false;
    private double lastMaincloudCheckTime = 0;
    private const double maincloudCheckInterval = 10.0;

    // SSH Connection Status
    public bool IsSSHConnectionActive => cachedSSHConnectionStatus;
    private bool cachedSSHConnectionStatus = false;
    private double lastSSHConnectionCheck = 0;
    private const double sshCheckInterval = 1.0;

    // Editor Focus Status - to prevent background processing accumulation
    private bool hasEditorFocus = true; // Default to true

    public enum ServerMode
    {
        WslServer,
        CustomServer,
        MaincloudServer,
    }

    public ServerManager(Action<string, int> logCallback, Action repaintCallback)
    {
        LogCallback = logCallback;
        RepaintCallback = repaintCallback;
        
        // Load settings from EditorPrefs
        LoadEditorPrefs();
        
        // Initialize the processors
        cmdProcessor = new ServerCMDProcess(LogMessage, debugMode);
        
        // Initialize LogProcessor with callbacks
        logProcessor = new ServerLogProcess(
            LogMessage,
            () => ServerOutputWindow.RefreshOpenWindow(), // Module log update callback
            () => ServerOutputWindow.RefreshDatabaseLogs(), // Database log update callback - uses high-priority refresh
            cmdProcessor,
            debugMode
        );
        
        // Initialize VersionProcessor
        versionProcessor = new ServerVersionProcess(cmdProcessor, LogMessage, debugMode);
        
        // Initialize ServerDetectionProcess
        detectionProcess = new ServerDetectionProcess(debugMode);
        if (!string.IsNullOrEmpty(serverDirectory))
        {
            detectionProcess.Configure(serverDirectory, detectServerChanges);
        }
        detectionProcess.OnServerChangesDetected += OnServerChangesDetected;
        
        // Configure the server control delegates
        versionProcessor.ConfigureServerControlDelegates(
            () => serverStarted, // IsServerRunning
            () => autoCloseWsl,  // GetAutoCloseWsl
            (value) => { SetAutoCloseWsl(value); }, // SetAutoCloseWsl
            () => StartServer(),  // StartServer
            () => StopServer()    // StopServer
        );

        serverCustomProcess = new ServerCustomProcess(LogMessage, debugMode);
        
        // Configure
        Configure();
    }

    private void LoadEditorPrefs()
    {
        // Load prerequisites state
        hasWSL = EditorPrefs.GetBool(PrefsKeyPrefix + "HasWSL", false);
        hasDebian = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebian", false);
        hasDebianTrixie = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebianTrixie", false);
        hasCurl = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCurl", false);
        hasSpacetimeDBServer = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", false);
        hasSpacetimeDBPath = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", false);
        hasSpacetimeDBService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBService", false);
        hasSpacetimeDBLogsService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBLogsService", false);
        hasRust = EditorPrefs.GetBool(PrefsKeyPrefix + "HasRust", false);
        hasBinaryen = EditorPrefs.GetBool(PrefsKeyPrefix + "HasBinaryen", false);
        hasGit = EditorPrefs.GetBool(PrefsKeyPrefix + "HasGit", false);
        
        // Load UX state
        initializedFirstModule = EditorPrefs.GetBool(PrefsKeyPrefix + "InitializedFirstModule", false);
        publishFirstModule = EditorPrefs.GetBool(PrefsKeyPrefix + "PublishFirstModule", false);
        hasAllPrerequisites = EditorPrefs.GetBool(PrefsKeyPrefix + "HasAllPrerequisites", false);
        
        // Load prerequisites settings
        wslPrerequisitesChecked = EditorPrefs.GetBool(PrefsKeyPrefix + "wslPrerequisitesChecked", false);
        userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        serverUrl = EditorPrefs.GetString(PrefsKeyPrefix + "ServerURL", "http://0.0.0.0:3000/");
        serverPort = EditorPrefs.GetInt(PrefsKeyPrefix + "ServerPort", 3000);
        authToken = EditorPrefs.GetString(PrefsKeyPrefix + "AuthToken", "");

        maincloudUrl = EditorPrefs.GetString(PrefsKeyPrefix + "MaincloudURL", "https://maincloud.spacetimedb.com/");
        maincloudAuthToken = EditorPrefs.GetString(PrefsKeyPrefix + "MaincloudAuthToken", "");

        // Load local settings
        backupDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "BackupDirectory", "");
        serverDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "ServerDirectory", "");
        serverLang = EditorPrefs.GetString(PrefsKeyPrefix + "ServerLang", "rust");
        clientDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "ClientDirectory", "");
        unityLang = EditorPrefs.GetString(PrefsKeyPrefix + "UnityLang", "csharp");
        moduleName = EditorPrefs.GetString(PrefsKeyPrefix + "ModuleName", "");
        selectedModuleIndex = EditorPrefs.GetInt(PrefsKeyPrefix + "SelectedModuleIndex", -1);

        // Load custom server settings
        sshUserName = EditorPrefs.GetString(PrefsKeyPrefix + "SSHUserName", "");
        sshPrivateKeyPath = EditorPrefs.GetString(PrefsKeyPrefix + "SSHPrivateKeyPath", "");
        customServerUrl = EditorPrefs.GetString(PrefsKeyPrefix + "CustomServerURL", "");
        customServerPort = EditorPrefs.GetInt(PrefsKeyPrefix + "CustomServerPort", 0);
        customServerAuthToken = EditorPrefs.GetString(PrefsKeyPrefix + "CustomServerAuthToken", "");

        // Load global settings
        hideWarnings = EditorPrefs.GetBool(PrefsKeyPrefix + "HideWarnings", true);
        detectServerChanges = EditorPrefs.GetBool(PrefsKeyPrefix + "DetectServerChanges", true);
        autoPublishMode = EditorPrefs.GetBool(PrefsKeyPrefix + "AutoPublishMode", false);
        publishAndGenerateMode = EditorPrefs.GetBool(PrefsKeyPrefix + "PublishAndGenerateMode", true);
        silentMode = EditorPrefs.GetBool(PrefsKeyPrefix + "SilentMode", true);
        debugMode = EditorPrefs.GetBool(PrefsKeyPrefix + "DebugMode", false);
        clearModuleLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearModuleLogAtStart", true);
        clearDatabaseLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearDatabaseLogAtStart", true);
        autoCloseWsl = EditorPrefs.GetBool(PrefsKeyPrefix + "AutoCloseWsl", true);

        // Server output window settings
        echoToConsole = EditorPrefs.GetBool(PrefsKeyPrefix + "EchoToConsole", true);

        spacetimeDBCurrentVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersion", "");
        spacetimeDBCurrentVersionCustom = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersionCustom", "");
        spacetimeDBCurrentVersionTool = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersionTool", "");
        spacetimeDBLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBLatestVersion", "");
        
        rustCurrentVersion = EditorPrefs.GetString(PrefsKeyPrefix + "RustVersion", "");
        rustLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "RustLatestVersion", "");
        rustupVersion = EditorPrefs.GetString(PrefsKeyPrefix + "RustupVersion", "");
        
        // Load server mode
        string modeName = EditorPrefs.GetString(PrefsKeyPrefix + "ServerMode", "WslServer"); // CustomServer // MaincloudServer
        if (Enum.TryParse(modeName, out ServerMode mode))
        {
            serverMode = mode;
        }
    }

    // Helper methods to update settings with persistence
    public void SetUserName(string value) { userName = value; EditorPrefs.SetString(PrefsKeyPrefix + "UserName", value); }
    public void SetBackupDirectory(string value) { backupDirectory = value; EditorPrefs.SetString(PrefsKeyPrefix + "BackupDirectory", value); }
    public void SetServerDirectory(string value) 
    { 
        serverDirectory = value; 
        EditorPrefs.SetString(PrefsKeyPrefix + "ServerDirectory", value);
        
        // Update ServerDetectionProcess if it exists
        if (detectionProcess != null)
        {
            detectionProcess.Configure(serverDirectory, detectServerChanges);
        }
    }
    public void SetUnityLang(string value) { unityLang = value; EditorPrefs.SetString(PrefsKeyPrefix + "UnityLang", value); }
    public void SetClientDirectory(string value) { clientDirectory = value; EditorPrefs.SetString(PrefsKeyPrefix + "ClientDirectory", value); }
    public void SetServerLang(string value) { serverLang = value; EditorPrefs.SetString(PrefsKeyPrefix + "ServerLang", value); }
    public void SetModuleName(string value) { moduleName = value; EditorPrefs.SetString(PrefsKeyPrefix + "ModuleName", value); }
    public void SetSelectedModuleIndex(int value) { selectedModuleIndex = value; EditorPrefs.SetInt(PrefsKeyPrefix + "SelectedModuleIndex", value); }
    public void SetServerPort(int value) { serverPort = value; EditorPrefs.SetInt(PrefsKeyPrefix + "ServerPort", value); }
    public void SetServerUrl(string value) { serverUrl = value; EditorPrefs.SetString(PrefsKeyPrefix + "ServerURL", value); }
    public void SetAuthToken(string value) { authToken = value; EditorPrefs.SetString(PrefsKeyPrefix + "AuthToken", value); }

    public void SetMaincloudUrl(string value)
    {
        // Always ensure Maincloud URL uses HTTPS
        string cleanedUrl = value.Trim();
        
        // Remove any existing protocol
        if (cleanedUrl.StartsWith("http://"))
        {
            cleanedUrl = cleanedUrl.Substring(7);
        }
        else if (cleanedUrl.StartsWith("https://"))
        {
            cleanedUrl = cleanedUrl.Substring(8);
        }
        
        // Add HTTPS protocol
        maincloudUrl = "https://" + cleanedUrl;
        
        // Save to EditorPrefs
        EditorPrefs.SetString(PrefsKeyPrefix + "MaincloudURL", maincloudUrl);
        
        if (debugMode) LogMessage($"Maincloud URL set to: {maincloudUrl}", 0);
    }
    public void SetMaincloudAuthToken(string value) { maincloudAuthToken = value; EditorPrefs.SetString(PrefsKeyPrefix + "MaincloudAuthToken", value); }

    public void SetSSHUserName(string value) { sshUserName = value; EditorPrefs.SetString(PrefsKeyPrefix + "SSHUserName", value); }
    public void SetCustomServerUrl(string value) { customServerUrl = value; EditorPrefs.SetString(PrefsKeyPrefix + "CustomServerURL", value); }
    public void SetCustomServerPort(int value) { customServerPort = value; EditorPrefs.SetInt(PrefsKeyPrefix + "CustomServerPort", value); }
    public void SetCustomServerAuthToken(string value) { customServerAuthToken = value; EditorPrefs.SetString(PrefsKeyPrefix + "CustomServerAuthToken", value); }
    public void SetSSHPrivateKeyPath(string value) { sshPrivateKeyPath = value; EditorPrefs.SetString(PrefsKeyPrefix + "SSHPrivateKeyPath", value); }
    
    public void SetDebugMode(bool value) { debugMode = value; EditorPrefs.SetBool(PrefsKeyPrefix + "DebugMode", value); }
    public void SetHideWarnings(bool value) { hideWarnings = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HideWarnings", value); }
    public void SetDetectServerChanges(bool value) 
    { 
        detectServerChanges = value; 
        EditorPrefs.SetBool(PrefsKeyPrefix + "DetectServerChanges", value);
        
        // Update ServerDetectionProcess if it exists
        if (detectionProcess != null)
        {
            detectionProcess.SetDetectChanges(value);
        }
    }
    public void SetAutoPublishMode(bool value) { autoPublishMode = value; EditorPrefs.SetBool(PrefsKeyPrefix + "AutoPublishMode", value); }
    public void SetPublishAndGenerateMode(bool value) { publishAndGenerateMode = value; EditorPrefs.SetBool(PrefsKeyPrefix + "PublishAndGenerateMode", value); }
    public void SetSilentMode(bool value) { silentMode = value; EditorPrefs.SetBool(PrefsKeyPrefix + "SilentMode", value); }
    public void SetAutoCloseWsl(bool value) { autoCloseWsl = value; EditorPrefs.SetBool(PrefsKeyPrefix + "AutoCloseWsl", value); }
    public void SetClearModuleLogAtStart(bool value) { clearModuleLogAtStart = value; EditorPrefs.SetBool(PrefsKeyPrefix + "ClearModuleLogAtStart", value); }
    public void SetClearDatabaseLogAtStart(bool value) { clearDatabaseLogAtStart = value; EditorPrefs.SetBool(PrefsKeyPrefix + "ClearDatabaseLogAtStart", value); }
    
    // Set editor focus state to prevent background log processing accumulation
    public void SetEditorFocus(bool hasFocus) { hasEditorFocus = hasFocus; }
    
    public void SetHasWSL(bool value) { hasWSL = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasWSL", value); }
    
    // Force refresh logs from SessionState - useful after compilation when logs appear empty
    public void ForceRefreshLogsFromSessionState()
    {
        if (logProcessor != null)
        {
            logProcessor.ForceRefreshLogsFromSessionState();
        }
    }
    
    // Force WSL log refresh - triggers new journalctl commands for WSL server
    public void ForceWSLLogRefresh()
    {
        if (logProcessor != null && serverMode == ServerMode.WslServer)
        {
            logProcessor.ForceWSLLogRefresh();
        }
    }
    
    // Force SSH log refresh - triggers new journalctl commands for custom server
    public void ForceSSHLogRefresh()
    {
        if (logProcessor != null && serverMode == ServerMode.CustomServer)
        {
            logProcessor.ForceSSHLogRefresh();
        }
    }
    
    public void SetHasDebian(bool value) { hasDebian = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebian", value); }
    public void SetHasDebianTrixie(bool value) { hasDebianTrixie = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebianTrixie", value); }
    public void SetHasCurl(bool value) { hasCurl = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasCurl", value); }
    public void SetHasSpacetimeDBServer(bool value) { hasSpacetimeDBServer = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", value); }    
    public void SetHasSpacetimeDBPath(bool value) { hasSpacetimeDBPath = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", value); }
    public void SetHasSpacetimeDBService(bool value) { hasSpacetimeDBService = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBService", value); }
    public void SetHasSpacetimeDBLogsService(bool value) { hasSpacetimeDBLogsService = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBLogsService", value); }
    public void SetHasRust(bool value) { hasRust = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasRust", value); }
    public void SetHasBinaryen(bool value) { hasBinaryen = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasBinaryen", value); }
    public void SetHasGit(bool value) { hasGit = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasGit", value); }
    public void SetWslPrerequisitesChecked(bool value) { wslPrerequisitesChecked = value; EditorPrefs.SetBool(PrefsKeyPrefix + "wslPrerequisitesChecked", value); }
    public void SetInitializedFirstModule(bool value) { initializedFirstModule = value; EditorPrefs.SetBool(PrefsKeyPrefix + "InitializedFirstModule", value); }
    public void SetPublishFirstModule(bool value) { publishFirstModule = value; EditorPrefs.SetBool(PrefsKeyPrefix + "PublishFirstModule", value); }
    public void SetHasAllPrerequisites(bool value) { hasAllPrerequisites = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasAllPrerequisites", value); }
    
    public void SetServerMode(ServerMode mode)
    {
        serverMode = mode;
        EditorPrefs.SetString(PrefsKeyPrefix + "ServerMode", mode.ToString());
        
        // Clear status cache when changing server modes
        if (cmdProcessor != null)
        {
            cmdProcessor.ClearStatusCache();
        }
    }

    public void Configure()
    {
        // Configure the log processor
        logProcessor.Configure(moduleName, serverDirectory, clearModuleLogAtStart, clearDatabaseLogAtStart, userName);
        logProcessor.SetServerRunningState(serverStarted);
        
        // Initialize the detection processor
        detectionProcess = new ServerDetectionProcess(debugMode);
        detectionProcess.Configure(serverDirectory, detectServerChanges);
        detectionProcess.OnServerChangesDetected += OnServerChangesDetected;
    }

    private void LogMessage(string message, int style)
    {
        LogCallback?.Invoke(message, style);
    }

    private void OnServerChangesDetected(bool changesDetected)
    {
        // Update UI when changes are detected
        serverChangesDetected = changesDetected;
        RepaintCallback?.Invoke();
        
        // Trigger auto-publish if changes are detected and auto-publish is enabled
        if (serverChangesDetected && autoPublishMode && serverStarted && !string.IsNullOrEmpty(moduleName))
        {
            LogMessage("Auto-publishing module due to changes detected...", 0);
            RunServerCommand($"spacetime publish --server local {moduleName}", $"Auto-publishing module '{moduleName}'");
        }
    }

    #region Server Start

    public void StartServer()
    {
        if (!WslPrerequisitesChecked)
        {
            LogMessage("Prerequisites need to be checked before starting the server.", -2);
            return;
        }

        switch (serverMode)
        {
            case ServerMode.WslServer:
                StartWslServer();
                break;
            case ServerMode.CustomServer:
                EditorApplication.delayCall += async () => { await StartCustomServer(); };
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
        if (!HasWSL || !HasDebian || !HasDebianTrixie || !HasSpacetimeDBService)
        {
            LogMessage("Missing required installed items. Will attempt to start server.", -2);
        }
        if (string.IsNullOrEmpty(UserName))
        {
            LogMessage("Cannot start server. Debian username is not set.", -1);
            return;
        }
        
        LogMessage("Start sequence initiated for WSL server. Waiting for confirmation...", 0);
        
        // Clear the status cache since we're starting the server
        if (cmdProcessor != null)
        {
            cmdProcessor.ClearStatusCache();
        }
        
        EditorApplication.delayCall += async () => {
            try
            {
                // Configure log processor with current settings
                logProcessor.Configure(ModuleName, ServerDirectory, ClearModuleLogAtStart, ClearDatabaseLogAtStart, UserName);
                
                // Start SpacetimeDB services using systemctl
                if (DebugMode) LogMessage("Starting SpacetimeDB service...", 0);
                bool serviceStarted = await cmdProcessor.StartSpacetimeDBServices();
                if (!serviceStarted)
                {
                    throw new Exception("Failed to start SpacetimeDB services");
                }

                LogMessage("Server Successfully Started!", 1);
                
                bool serviceRunning = await cmdProcessor.CheckServerRunning(instantCheck: true);
                
                if (DebugMode) LogMessage($"Immediate startup verification - Service: {(serviceRunning ? "active" : "inactive")}", 0);
                
                if (serviceRunning)
                {
                    if (DebugMode) LogMessage("Server service confirmed running immediately!", 1);
                    serverStarted = true;
                    serverConfirmedRunning = true;
                    isStartingUp = false; // Skip the startup grace period since we confirmed service is running
                    
                    // Configure log processor immediately
                    logProcessor.ConfigureWSL(true);
                    logProcessor.SetServerRunningState(true);
                    
                    if (silentMode)
                    {
                        logProcessor.StartLogging();
                        if (debugMode) LogMessage("WSL log processors started successfully.", 1);
                    }
                    
                    // Check ping in background for additional confirmation, but don't block on it
                    _ = Task.Run(async () => {
                        await Task.Delay(2000); // Give HTTP endpoint time to initialize
                        bool pingResponding = await PingServerStatusAsync();
                        if (DebugMode)
                        {
                            LogMessage($"Background ping check result: {(pingResponding ? "responding" : "not responding")}", 0);
                        }
                    });
                }
                else
                {
                    LogMessage("Service started, waiting for server to become ready...", 0);
                    // Mark server as starting up for grace period monitoring
                    isStartingUp = true;
                    startupTime = (float)EditorApplication.timeSinceStartup;
                    serverStarted = true; // Assume starting, CheckServerStatus will verify
                    
                    // For service-based approach, configure WSL
                    logProcessor.ConfigureWSL(true); // isLocalServer = true
                    
                    // Start logging if in silent mode
                    if (silentMode)
                    {
                        logProcessor.StartLogging();
                        if (debugMode) LogMessage("WSL log processors started successfully.", 1);
                    }
                    
                    // Update log processor state
                    logProcessor.SetServerRunningState(true);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error during server start sequence: {ex.Message}", -1);
                serverStarted = false;
                isStartingUp = false;
                logProcessor.StopLogging();
                
                // Update log processor state
                logProcessor.SetServerRunningState(false);
            }
            finally
            {
                RepaintCallback?.Invoke();
            }
        };
    }

    private async Task StartCustomServer()
    {
        if (string.IsNullOrEmpty(CustomServerUrl))
        {
            LogMessage("Please enter a custom server URL first.", -2);
            return;
        }
        if (CustomServerPort <= 0)
        {
            LogMessage("Could not detect a valid port in the custom server URL. Please ensure the URL includes a port number (e.g., http://example.com:3000/).", -2);
            return;
        }
        
        LogMessage($"Connecting to custom server at {CustomServerUrl}", 1);

        bool success = await serverCustomProcess.StartCustomServer();

        if (!success)
        {
            LogMessage("Custom server process failed to start.", -1);
            return;
        }

        if (success)
        {
            if (debugMode) LogMessage("Custom server process started, waiting for confirmation...", 1);
            // Mark as starting up, but do not confirm running yet
            serverStarted = true;
            isStartingUp = true;
            serverConfirmedRunning = false;
            startupTime = (float)EditorApplication.timeSinceStartup;

            // Configure log processor for custom server if in silent mode
            if (silentMode && logProcessor != null)
            {
                // Extract hostname from CustomServerUrl
                string sshHost = ExtractHostname(CustomServerUrl);

                // Configure SSH details for the log processor
                logProcessor.ConfigureSSH(
                    SSHUserName,
                    sshHost,
                    SSHPrivateKeyPath,
                    true // isCustomServer = true
                );

                logProcessor.SetServerRunningState(true);
                logProcessor.StartSSHLogging();
                if (debugMode) LogMessage("Custom server log processors started successfully.", 1);
            }
        }
        RepaintCallback?.Invoke();
    }

    private void StartMaincloudServer()
    {
        // Check if module name is set
        if (string.IsNullOrEmpty(ModuleName))
        {
            LogMessage("Error: Module Name is not set. Cannot start log processors.", -1);
            return;
        }

        if (!IsMaincloudConnected)
        {
            LogMessage("Error: Not connected to Maincloud. Please check prerequisites first.", -1);
            return;
        }

        // Only start the log processes since Maincloud server is already running remotely
        if (debugMode) LogMessage("Starting log processors for Maincloud server...", 0);

        // Set server as running
        serverStarted = true;
        isStartingUp = false;
        serverConfirmedRunning = true;
        
        // Initialize the log processor if it's null
        if (logProcessor == null)
        {
            logProcessor = new ServerLogProcess(
                LogMessage,
                () => { RepaintCallback?.Invoke(); },
                () => { RepaintCallback?.Invoke(); },
                cmdProcessor,
                debugMode
            );
            logProcessor.Configure(ModuleName, ServerDirectory, ClearModuleLogAtStart, ClearDatabaseLogAtStart, UserName);
        }
        
        // Start log process if using silent mode
        if (silentMode && logProcessor != null)
        {
            logProcessor.SetServerRunningState(true);
            logProcessor.StartLogging();
            if (debugMode) LogMessage("Maincloud log processors started successfully.", 1);
        }
        
        // Call the repaint callback to update the UI
        RepaintCallback?.Invoke();
    }

    #endregion    

    #region Server Stop

    public void StopServer()
    {
        if (DebugMode) LogMessage("Stop Server process has been called.", 0);
        
        // Prevent multiple concurrent stop attempts
        if (isStopping)
        {
            LogMessage("Server stop already in progress. Please wait...", 0);
            return;
        }
        
        // Clear startup and status flags immediately to prevent status conflicts
        isStartingUp = false;
        serverConfirmedRunning = false;
        consecutiveFailedChecks = 0;

        if (serverMode == ServerMode.CustomServer)
        {
            EditorApplication.delayCall += async () => {
                await StopCustomServer();
            };
            RepaintCallback?.Invoke();
            return;
        }
        else if (serverMode == ServerMode.WslServer)
        {
            EditorApplication.delayCall += async () => {
                await StopWslServer();
            };
            RepaintCallback?.Invoke();
            return;
        }
        else if (serverMode == ServerMode.MaincloudServer)
        {
            StopMaincloudLog();
            RepaintCallback?.Invoke();
            return;
        }
    }    

    private async Task StopWslServer()
    {
        // Set the stopping flag to prevent concurrent stops
        isStopping = true;
        
        try
        {
            // Clear the status cache since we're stopping the server
            if (cmdProcessor != null)
            {
                cmdProcessor.ClearStatusCache();
            }
            
            LogMessage("Stopping SpacetimeDB services and processes...", 0);
            
            // Use the cmdProcessor to stop the services
            bool stopSuccessful = await cmdProcessor.StopSpacetimeDBServices();
            
            if (stopSuccessful)
            {
                if (debugMode) LogMessage("Stop commands completed. Verifying server is fully stopped...", 0);
                
                // Clear status cache again and force immediate status check
                cmdProcessor.ClearStatusCache();
                
                // Check if server is actually stopped (with instant check to bypass cache)
                bool stillRunning = await cmdProcessor.CheckServerRunning(instantCheck: true);
                bool pingStillResponding = await PingServerStatusAsync();
                
                if (!stillRunning && !pingStillResponding)
                {
                    // Server confirmed stopped
                    serverStarted = false;
                    isStartingUp = false;
                    serverConfirmedRunning = false;
                    serverProcess = null; 
                    justStopped = true;
                    stopInitiatedTime = EditorApplication.timeSinceStartup;
                    consecutiveFailedChecks = 0; // Reset failure counter

                    LogMessage("Server Successfully Stopped.", 1);
                    
                    // Stop the log processors after confirming server is stopped
                    logProcessor.StopLogging();
                    logProcessor.SetServerRunningState(false);
                }
                else
                {
                    if (stillRunning)
                        LogMessage("Warning: Some SpacetimeDB processes may still be running.", -1);
                    if (pingStillResponding)
                        LogMessage("Warning: Server is still responding to ping requests.", -1);
                        
                    // Still mark as stopped since we did our best
                    serverStarted = false;
                    isStartingUp = false;
                    serverConfirmedRunning = false;
                    serverProcess = null;
                    justStopped = true;
                    stopInitiatedTime = EditorApplication.timeSinceStartup;
                    consecutiveFailedChecks = 0;

                    LogMessage("Stop sequence completed. Check server status manually if needed.", 0);
                    logProcessor.StopLogging();
                    logProcessor.SetServerRunningState(false);
                }
            }
            else
            {
                LogMessage("Stop commands failed or timed out. Server may still be running.", -1);
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Error during server stop sequence: {ex.Message}", -1);
        }
        finally
        {
            // Always clear the stopping flag
            isStopping = false;
            RepaintCallback?.Invoke();
        }
    }

    private async Task StopCustomServer()
    {
        try
        {
            await serverCustomProcess.StopCustomServer();
            
            // Stop SSH logging if we were using it
            if (silentMode && logProcessor != null)
            {
                logProcessor.StopSSHLogging();
                if (DebugMode) LogMessage("Custom server log processors stopped.", 0);
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Error during custom server stop sequence: {ex.Message}", -1);
        }
        finally
        {
            // Force state update
            serverStarted = false;
            isStartingUp = false;
            serverConfirmedRunning = false;
            serverProcess = null; 
            justStopped = true; // Set flag indicating stop was just initiated
            stopInitiatedTime = EditorApplication.timeSinceStartup; // Record time

            LogMessage("Custom remote SpacetimeDB Successfully Stopped!", 1);
            
            // Update log processor state
            logProcessor.SetServerRunningState(false);
            
            RepaintCallback?.Invoke();
        }
    }

    public void StopMaincloudLog()
    {
        // Stop the log processors
        logProcessor.StopLogging();
        
        // Force state update
        serverStarted = false;
        isStartingUp = false;
        serverConfirmedRunning = false;
        justStopped = true; // Set flag indicating stop was just initiated
        stopInitiatedTime = EditorApplication.timeSinceStartup; // Record time

        LogMessage("Maincloud Server Successfully Stopped.", 1);
        
        // Update log processor state
        logProcessor.SetServerRunningState(false);
        
        RepaintCallback?.Invoke();
    }

    #endregion

    #region Server Status

    public async Task CheckServerStatus()
    {
        //UnityEngine.Debug.Log("CheckServerStatus called");

        const double stopGracePeriod = 5.0;
        if (justStopped && (EditorApplication.timeSinceStartup - stopInitiatedTime >= stopGracePeriod))
        {
            if (DebugMode) LogMessage("Stop grace period expired, allowing normal status checks to resume.", 0);
            justStopped = false;
        }        
        
        // Startup Phase Check
        if (isStartingUp)
        {
            float elapsedTime = (float)(EditorApplication.timeSinceStartup - startupTime);
            bool isActuallyRunning = false;
            
            try {
                if (serverMode == ServerMode.CustomServer)
                {
                    // For custom server, we trust that StartCustomServer() already verified it's running
                    // This avoids false negatives from CheckServerRunning() right after startup
                    if (elapsedTime < 5.0f) // Trust the startup for at least 5 seconds
                    {
                        isActuallyRunning = true; // Assume it's running during initial grace period
                        if (DebugMode) LogMessage("Custom server in startup grace period, assuming running", 0);
                    }
                    else
                    {
                        // After grace period, verify with actual check
                        await serverCustomProcess.CheckServerRunning(true);
                        isActuallyRunning = serverCustomProcess.cachedServerRunningStatus;
                        if (DebugMode) LogMessage($"Custom server check after grace period: {(isActuallyRunning ? "running" : "not running")}", 0);
                    }                
                }                
                else // WSL and other modes
                {
                    // Use instantCheck=true to bypass cache during startup for immediate status verification
                    bool serviceRunning = await cmdProcessor.CheckServerRunning(instantCheck: true);
                    
                    // During startup phase, prioritize service status since it's more reliable
                    // Ping can be slow or fail temporarily even when server is actually running
                    if (serviceRunning)
                    {
                        // Service is running - server is considered running during startup
                        isActuallyRunning = true;
                        
                        // Optionally check ping for additional confirmation, but don't block on it
                        if (elapsedTime > 3.0f) // Only ping after giving server time to initialize
                        {
                            bool pingResponding = await PingServerStatusAsync();
                            if (DebugMode) 
                            {
                                LogMessage($"WSL startup check - Service: active, Ping: {(pingResponding ? "responding" : "not responding")}, Elapsed: {elapsedTime:F1}s, Result: running (service confirmed)", 0);
                            }
                        }
                        else
                        {
                            if (DebugMode) 
                            {
                                LogMessage($"WSL startup check - Service: active, Elapsed: {elapsedTime:F1}s, Result: running (early startup, ping skipped)", 0);
                            }
                        }
                    }
                    else
                    {
                        // Service not running - definitely not ready
                        isActuallyRunning = false;
                        if (DebugMode) 
                        {
                            LogMessage($"WSL startup check - Service: inactive, Elapsed: {elapsedTime:F1}s, Result: not running", 0);
                        }
                    }
                }                // If running during startup phase, confirm immediately
                if (isActuallyRunning)
                {
                    if (DebugMode) LogMessage($"Startup confirmed: Server service is active and running.", 1);
                    LogMessage("Server confirmed running!", 1);
                    isStartingUp = false;
                    serverStarted = true; // Explicitly confirm started state
                    serverConfirmedRunning = true;
                    justStopped = false; // Reset flag on successful start confirmation
                    consecutiveFailedChecks = 0; // Reset failure counter on successful start

                    // Update logProcessor state
                    logProcessor.SetServerRunningState(true);

                    // Auto-publish check if applicable
                    if (AutoPublishMode && ServerChangesDetected && !string.IsNullOrEmpty(ModuleName))
                    {
                        LogMessage("Server running with pending changes - auto-publishing...", 0);
                        RunServerCommand($"spacetime publish --server local {ModuleName}", $"Auto-publishing module '{ModuleName}'");
                        ServerChangesDetected = false;
                        if (detectionProcess != null)
                        {
                            detectionProcess.ResetTrackingAfterPublish();
                        }
                    }
                    RepaintCallback?.Invoke();
                    return;                }                // If grace period expires and still not running, assume failure
                else if (elapsedTime >= serverStartupGracePeriod)
                {
                    LogMessage($"Server failed to start within grace period ({serverStartupGracePeriod} seconds).", -1);
                    
                    // Before giving up, do one final service check to be absolutely sure
                    try
                    {
                        bool finalServiceCheck = await cmdProcessor.CheckServerRunning(instantCheck: true);
                        if (finalServiceCheck)
                        {
                            LogMessage("Final service check shows server is actually running - recovering!", 1);
                            isStartingUp = false;
                            serverStarted = true;
                            serverConfirmedRunning = true;
                            justStopped = false;
                            consecutiveFailedChecks = 0;
                            logProcessor.SetServerRunningState(true);
                            RepaintCallback?.Invoke();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (DebugMode) LogMessage($"Final service check failed: {ex.Message}", -1);
                    }
                    
                    isStartingUp = false;
                    serverStarted = false;
                    serverConfirmedRunning = false;
                    justStopped = false; // Reset flag on failed start
                    consecutiveFailedChecks = 0; // Reset failure counter on failed start

                    // Update logProcessor state
                    logProcessor.SetServerRunningState(false);

                    if (serverProcess != null && !serverProcess.HasExited) { try { serverProcess.Kill(); } catch {} }
                    serverProcess = null;
                    
                    RepaintCallback?.Invoke();
                    return; // Failed, skip further checks
                }
                else
                {
                    // Still starting up, update UI and wait
                    if (DebugMode && elapsedTime % 2.0f < 0.1f) // Log every 2 seconds during startup
                    {
                        LogMessage($"Startup in progress... elapsed: {elapsedTime:F1}s / {serverStartupGracePeriod}s", 0);
                    }
                    RepaintCallback?.Invoke();
                    return;
                }
            }
            catch (Exception ex) {
                if (DebugMode) LogMessage($"Error during server status check: {ex.Message}", -1);
                RepaintCallback?.Invoke();
                return;
            }
        }

        // Standard Running Check (Only if not starting up)
        if (serverStarted)
        {
            bool isActuallyRunning = false;
            
            // Keep track of time since startup for recently started custom servers
            float timeSinceStart = (float)(EditorApplication.timeSinceStartup - startupTime);
            bool recentlyStarted = timeSinceStart < 10.0f; // Consider "recently started" for 10 seconds
            
            try {
                if (serverMode == ServerMode.CustomServer)
                {
                    await serverCustomProcess.CheckServerRunning(true);
                    isActuallyRunning = serverCustomProcess.cachedServerRunningStatus;

                    // If recently started and previously confirmed running, but now reports not running,
                    // this might be a false negative during server initialization
                    if (recentlyStarted && serverConfirmedRunning && !isActuallyRunning)
                    {
                        if (DebugMode) LogMessage("Ignoring possible false negative server status check (server recently started)", 0);
                        return; // Skip this check, maintain current state
                    }                
                }                
                else // WSL and other modes
                {
                    // Use a combination of service check and HTTP ping for better reliability
                    bool serviceRunning = await cmdProcessor.CheckServerRunning();
                    bool pingResponding = await PingServerStatusAsync();
                    
                    // Special handling for recently stopped servers
                    double timeSinceStop = EditorApplication.timeSinceStartup - stopInitiatedTime;
                    bool recentlyStopped = justStopped && timeSinceStop < 10.0; // 10 second grace period after stop
                    
                    if (recentlyStopped)
                    {
                        // If we just stopped the server, require BOTH service AND ping to be down
                        // to prevent false positives from lingering connections
                        isActuallyRunning = serviceRunning && pingResponding;
                        
                        if (DebugMode)
                        {
                            LogMessage($"WSL Server status (recently stopped) - Service: {(serviceRunning ? "active" : "inactive")}, Ping: {(pingResponding ? "responding" : "not responding")}, Result: {(isActuallyRunning ? "running" : "stopped")}", 0);
                        }
                    }
                    else
                    {
                        // Normal operation: Server is considered running if either service is active OR ping responds
                        // This prevents false negatives when service check fails but server is actually responding
                        isActuallyRunning = serviceRunning || pingResponding;
                        
                        if (DebugMode)
                        {
                            //LogMessage($"WSL Server status - Service: {(serviceRunning ? "active" : "inactive")}, Ping: {(pingResponding ? "responding" : "not responding")}, Result: {(isActuallyRunning ? "running" : "stopped")}", 0);
                        }
                    }
                }
                
                // State Change Detection with Resilience:
                if (serverConfirmedRunning != isActuallyRunning)
                {
                    if (isActuallyRunning)
                    {
                        // Server came back online - reset failure counter and update state immediately
                        consecutiveFailedChecks = 0;
                        serverConfirmedRunning = true;
                        justStopped = false;
                        
                        string msg = $"Server running confirmed ({(serverMode == ServerMode.CustomServer ? "Custom Remote Server" : "WSL Server")})";
                        LogMessage(msg, 1);

                        // Update logProcessor state
                        logProcessor.SetServerRunningState(true);
                        
                        if (DebugMode) LogMessage("Server state updated to running.", 1);
                        RepaintCallback?.Invoke();
                    }
                    else
                    {
                        // Server appears to have stopped - increment failure counter
                        consecutiveFailedChecks++;
                        
                        if (DebugMode) LogMessage($"Server check failed ({consecutiveFailedChecks}/{maxConsecutiveFailuresBeforeStop}) - {(serverMode == ServerMode.CustomServer ? "Custom Remote Server" : "WSL Server")}", 0);
                          // Only mark as stopped after multiple consecutive failures
                        if (consecutiveFailedChecks >= maxConsecutiveFailuresBeforeStop)
                        {
                            // Before marking as stopped, do one final ping verification to avoid false positives
                            bool finalPingCheck = await PingServerStatusAsync();
                            if (finalPingCheck)
                            {
                                // Ping succeeded, server is actually running - reset failure counter
                                consecutiveFailedChecks = 0;
                                serverConfirmedRunning = true;
                                if (DebugMode) LogMessage("Final ping check succeeded - server is actually running, resetting failure counter", 1);
                                RepaintCallback?.Invoke();
                                return;
                            }
                            
                            string msg = $"SpacetimeDB Server confirmed stopped after {maxConsecutiveFailuresBeforeStop} consecutive failed checks and final ping verification ({(serverMode == ServerMode.CustomServer ? "Custom Remote Server" : "WSL Server")})";
                            if (DebugMode) LogMessage(msg, -2);
                            
                            // Update state
                            serverConfirmedRunning = false;
                            serverStarted = false;

                            // Update logProcessor state
                            logProcessor.SetServerRunningState(false);

                            if (DebugMode) LogMessage("Server state updated to stopped.", -1);
                            RepaintCallback?.Invoke();
                        }
                        else
                        {
                            // Don't change state yet, just log the temporary failure
                            if (DebugMode) LogMessage("Temporary server check failure - maintaining current state", 0);
                        }
                    }
                }
                else if (isActuallyRunning)
                {
                    // Server is running and status matches - reset failure counter
                    consecutiveFailedChecks = 0;
                }
            }
            catch (Exception ex) {
                if (DebugMode) LogMessage($"Error during server status check: {ex.Message}", -1);
            }
        }

        // Check for External Start/Recovery
        // Only check if not already started, not starting up, and server is running
        else if (!serverStarted && !isStartingUp)
        {
            //UnityEngine.Debug.Log("Checking if server is running externally...");
            bool isActuallyRunning = false;
            try {
                if (serverMode == ServerMode.CustomServer)
                {
                    await serverCustomProcess.CheckServerRunning(true);
                    isActuallyRunning = serverCustomProcess.cachedServerRunningStatus;
                }                
                else // WSL and other modes
                {
                    // Use both process check and ping for external recovery detection
                    bool processRunning = await cmdProcessor.CheckWslProcessAsync(IsWslRunning);
                    bool pingResponding = false;
                    if (processRunning)
                    {
                        // If process is running, verify with ping for complete confirmation
                        pingResponding = await PingServerStatusAsync();
                        isActuallyRunning = pingResponding; // Only consider it running if both process exists AND ping responds
                        
                        if (DebugMode)
                        {
                            LogMessage($"External recovery check - Process: {(processRunning ? "running" : "not running")}, Ping: {(pingResponding ? "responding" : "not responding")}", 0);
                        }
                    }
                    else
                    {
                        isActuallyRunning = false;
                        if (DebugMode) LogMessage("External recovery check - No spacetimedb process found", 0);
                    }
                }

                if (isActuallyRunning)
                {
                    // If the 'justStopped' flag is set, ignore this check during the grace period
                    if (justStopped)
                    {
                        if (DebugMode) LogMessage($"Server detected running, but in post-stop grace period. Ignoring.", 0);
                    }                    
                    else
                    {
                        // For WSL server, we already verified ping in the check above, so isActuallyRunning is already the final result
                        bool confirmed = serverMode == ServerMode.CustomServer ? isActuallyRunning : isActuallyRunning;
                        
                        if (confirmed)
                        {                            
                            // Detected server running, probably it was already running when Unity started
                            if (debugMode) LogMessage($"Detected SpacetimeDB running ({(serverMode == ServerMode.CustomServer ? "Custom Remote Server" : "WSL Server")})", 1);
                            serverStarted = true;
                            serverConfirmedRunning = true;
                            isStartingUp = false;
                            justStopped = false; // Ensure flag is clear if we recover state
                            consecutiveFailedChecks = 0; // Reset failure counter on external recovery

                            // Update logProcessor state
                            logProcessor.SetServerRunningState(true);
                            
                            // If we detected a custom server is running and we are in silent mode,
                            // start the SSH log processors
                            if (serverMode == ServerMode.CustomServer && isActuallyRunning && silentMode && logProcessor != null)
                            {
                                // Extract hostname from CustomServerUrl
                                string sshHost = ExtractHostname(CustomServerUrl);
                                
                                // Configure SSH details for the log processor
                                logProcessor.ConfigureSSH(
                                    SSHUserName,
                                    sshHost,
                                    SSHPrivateKeyPath,
                                    true // isCustomServer = true
                                );
                                
                                logProcessor.StartSSHLogging();
                                if (DebugMode) LogMessage("Custom server log processors started automatically.", 1);
                            }
                        }

                        RepaintCallback?.Invoke();
                    }
                }
            }
            catch (Exception ex) {
                if (DebugMode) LogMessage($"Error during server status check: {ex.Message}", -1);
            }
        }
    }
    #endregion

    #region Server Commands

    public async void RunServerCommand(string command, string description)
    {
        if (!hasAllPrerequisites)
        {
            if (debugMode) LogMessage("Server command <" + command + "> tried to run without pre-requisites met.", -2);
            return;
        }

        try
        {
            // Run the command silently and capture the output
            if (!string.IsNullOrWhiteSpace(description))
            LogMessage($"{description}...", 0);

            // Publish and Generate uses the local CLI and is not run on SSH
            if (serverMode == ServerMode.CustomServer && !command.Contains("spacetime publish") && !command.Contains("spacetime generate"))
            {
                var result = await serverCustomProcess.RunSpacetimeDBCommandAsync(command); // SSH command
                
                // Display the results in the output log
                if (!string.IsNullOrEmpty(result.output))
                {
                    // Check if this is login info output and apply color formatting
                    string formattedOutput = FormatLoginInfoOutput(result.output);
                    LogMessage(formattedOutput, 0);
                }
                
                // Most results will be output here
                if (!string.IsNullOrEmpty(result.error))
                {
                    // Filter out formatting errors for generate commands that don't affect functionality
                    bool isGenerateCmd = command.Contains("spacetime generate");
                    string filteredError = isGenerateCmd ? FilterGenerateErrors(result.error) : result.error;
                    
                    if (!string.IsNullOrEmpty(filteredError))
                    {
                        LogMessage(filteredError, -2);
                        publishing = false;
                    }
                }
                
                if (string.IsNullOrEmpty(result.output) && string.IsNullOrEmpty(result.error))
                {
                    if (debugMode) LogMessage("Command completed with no output.", 0);
                    publishing = false;
                }

                // Handle special cases for publish and generate
                bool isPublishCommand = command.Contains("spacetime publish");
                bool isGenerateCommand = command.Contains("spacetime generate");
                
                // Marked as success if the command was run, but may yet contain errors
                if (result.success)
                {
                    if (isPublishCommand)
                    {
                        PublishResult(result.output, result.error);
                    }
                    else if (isGenerateCommand && description == "Generating Unity files (auto)")
                    {
                        GenerateResult(result.output, result.error);
                    }
                }
            }
            else // WSL local or Maincloud mode
            {
                // Execute the command through the command processor
                var result = await cmdProcessor.RunServerCommandAsync(command, ServerDirectory);
                
                // Display the results in the output log
                if (!string.IsNullOrEmpty(result.output))
                {
                    // Check if this is login info output and apply color formatting
                    string formattedOutput = FormatLoginInfoOutput(result.output);
                    LogMessage(formattedOutput, 0);
                    UnityEngine.Debug.Log("Command output: " + formattedOutput);
                }
                
                if (!string.IsNullOrEmpty(result.error))
                {
                    // Filter out formatting errors for generate commands that don't affect functionality
                    bool isGenerateCmd = command.Contains("spacetime generate");
                    string filteredError = isGenerateCmd ? FilterGenerateErrors(result.error) : result.error;
                    
                    if (!string.IsNullOrEmpty(filteredError))
                    {
                        LogMessage(filteredError, -2);
                        publishing = false;
                    }
                }
                
                if (string.IsNullOrEmpty(result.output) && string.IsNullOrEmpty(result.error))
                {
                    if (debugMode) LogMessage("Command completed with no output.", 0);
                    publishing = false;
                }
                
                // Handle special cases for publish and generate
                bool isPublishCommand = command.Contains("spacetime publish");
                bool isGenerateCommand = command.Contains("spacetime generate");
                
                // Marked as success if the command was run, but may yet contain errors
                if (result.success)
                {
                    if (isPublishCommand)
                    {
                        PublishResult(result.output, result.error);
                    }
                    else if (isGenerateCommand && description == "Generating Unity files (auto)")
                    {
                        GenerateResult(result.output, result.error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Error running command: {ex.Message}", -1);
            publishing = false;
        }
    }
    #endregion

    #region Publish

    public void Publish(bool resetDatabase)
    {
        if (String.IsNullOrEmpty(ClientDirectory) || String.IsNullOrEmpty(ServerDirectory))
        {
            LogMessage("Please set your client directory and server directory in the pre-requisites first.",-2);
            return;
        }
        if (String.IsNullOrEmpty(ModuleName))
        {
            LogMessage("Please set the module name in the pre-requisites.",-2);
            return;
        }

        // Always trim trailing slashes from CustomServerUrl for all usages
        string customServerUrl = !string.IsNullOrEmpty(CustomServerUrl) ? CustomServerUrl.TrimEnd('/') : "";
        
        if (resetDatabase)
        {
            if (serverMode == ServerMode.WslServer)
                RunServerCommand($"spacetime publish --server local {ModuleName} --delete-data -y", $"Publishing module '{ModuleName}' and resetting database");
            else if (serverMode == ServerMode.CustomServer)
                RunServerCommand($"spacetime publish --server {customServerUrl} {ModuleName} --delete-data -y", $"Publishing module '{ModuleName}' and resetting database");

            UnityEngine.Debug.Log($"spacetime publish --server {customServerUrl} {ModuleName} --delete-data -y");
        }
        else
        {
            if (serverMode == ServerMode.MaincloudServer)
            {
                RunServerCommand($"spacetime publish --server maincloud {ModuleName} -y", $"Publishing module '{ModuleName}' to Maincloud");
            }
            else if (serverMode == ServerMode.CustomServer)
            {
                RunServerCommand($"spacetime publish --server {customServerUrl} {ModuleName} -y", $"Publishing module '{ModuleName}' to Custom Server at '{customServerUrl}'");
            }
            else
            {
                // Default to local server for WSL and CustomServer modes
                RunServerCommand($"spacetime publish --server local {ModuleName} -y", $"Publishing module '{ModuleName}' to Local");
            }
        }

        // Reset change detection after publishing
        if (detectionProcess != null && detectionProcess.IsDetectingChanges())
        {
            detectionProcess.ResetTrackingAfterPublish();
            // Update local UI state
            ServerChangesDetected = false;
        }

        if (publishFirstModule)
        {
            RunServerCommand("spacetime login show --token", "Showing SpacetimeDB login info and token");
            LogMessage("Please paste your token into the Pre-Requisites section.", 0);
        }

        // publishAndGenerateMode will run generate after publish has been run successfully in RunServerCommand().
    }

    private void PublishResult(string output, string error)
    {
        bool successfulPublish = true;

        if (debugMode) UnityEngine.Debug.Log("Publish output: " + output);
        if (debugMode) UnityEngine.Debug.Log("Publish error: " + error);

        // Extra information about SpacetimeDB tables and constraints
        if (error.Contains("migration"))
        {
            // Extract the table names from the output
            var tableNames = new System.Collections.Generic.List<string>();
            var constraintNames = new System.Collections.Generic.List<string>();
            string[] lines = error.Split('\n');
            
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                
                // Check for table migrations
                if (trimmedLine.Contains("table") && trimmedLine.Contains("requires a manual migration"))
                {
                    UnityEngine.Debug.Log($"Processing line for table migration: {trimmedLine}");
                    // Extract table name using regex pattern
                    var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"table\s+(\w+)\s+requires a manual migration");
                    if (match.Success)
                    {
                        string newEntry = match.Groups[1].Value;
                        if (!tableNames.Contains(newEntry))
                        {
                            tableNames.Add(newEntry);
                        }
                    }
                }
                
                // Check for unique constraint changes
                if (trimmedLine.Contains("constraint") && trimmedLine.Contains("requires a manual migration"))
                {
                    UnityEngine.Debug.Log($"Processing line for constraint migration: {trimmedLine}");
                    // Extract constraint name using regex pattern
                    var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"constraint\s+(\w+)\s+requires a manual migration");
                    if (match.Success)
                    {
                        string newEntry = match.Groups[1].Value;
                        if (!constraintNames.Contains(newEntry))
                        {
                            constraintNames.Add(newEntry);
                        }
                    }
                }
            }

            // Build the message based on what was found
            var messageParts = new System.Collections.Generic.List<string>();
            
            if (tableNames.Count > 0)
            {
                messageParts.Add("Tables requiring manual migration:\n" + string.Join(", ", tableNames));
            }
            
            if (constraintNames.Count > 0)
            {
                messageParts.Add("Unique constraints requiring manual migration:\n" + string.Join(", ", constraintNames));
            }
            
            string migrationInfo = messageParts.Count > 0 ? string.Join("\n\n", messageParts) : "Unknown migration requirements";

            EditorUtility.DisplayDialog("Migration Required", 
            "Detected database changes that requires a manual migration or a database reset.\n\n" + 
            migrationInfo +
            "\n\nYou will need to either create a new table and manually migrate the old table data by writing and running a reducer for this, OR re-publish with Ctrl+Alt+Click to clear the database. Be sure to make a backup of your database first."
            ,"OK");

            successfulPublish = false; // Mark as unsuccessful due to migration requirement
        }

        if (!string.IsNullOrEmpty(error) && error.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            successfulPublish = false; // Mark as unsuccessful due to error
        }

        // Go on to Auto-Generate if mode is enabled (continues even if unsuccessful to get all logs)
        if (PublishAndGenerateMode)
        {
            LogMessage("Publish successful, automatically generating Unity files...", 0);
            string outDir = GetRelativeClientPath();
            RunServerCommand($"spacetime generate --out-dir {outDir} --lang {UnityLang} -y", "Generating Unity files (auto)");
        }

        // Reset change detection state after successful publish
        if (detectionProcess != null && detectionProcess.IsDetectingChanges() && successfulPublish)
        {
            detectionProcess.ResetTrackingAfterPublish();
            ServerChangesDetected = false;
            if (DebugMode) LogMessage("Cleared file size tracking after successful publish.", 0);
        }

        if (!successfulPublish)
        {
            LogMessage("Publish failed!", -1);
        }
        else
        {
            LogMessage("Publish successful!", 1);
        }

        publishing = false;
    }

    private async void GenerateResult(string output, string error)
    {
        LogMessage("Waiting for generated files to be fully written...", 0);
        await Task.Delay(3000); // Wait 3 seconds for files to be fully generated
        LogMessage("Requesting script compilation...", 0);
        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
        if (!string.IsNullOrEmpty(error) && error.Contains("Error"))
        {
            LogMessage("Publish and Generate failed!", -1);
        }
        else
        {
            LogMessage("Publish and Generate successful!", 1);
        }
    }

    #endregion

    #region Server Methods

    public void SwitchModule(string newModuleName, bool clearDatabaseLogOnSwitch = true)
    {
        if (string.IsNullOrEmpty(ModuleName))
        {
            LogMessage("Please set the module name first.", -1);
            return;
        }
        
        // Run the switch command
        else if (serverMode == ServerMode.CustomServer)
        {
            logProcessor.SwitchModuleSSH(newModuleName, clearDatabaseLogOnSwitch);
        }
        else
        {
            logProcessor.SwitchModule(newModuleName, clearDatabaseLogOnSwitch);
        }
    }

    public void InitNewModule()
    {
        if (string.IsNullOrEmpty(ServerDirectory))
        {
            LogMessage("Please set the server directory first.", -1);
            return;
        }
        if (string.IsNullOrEmpty(ServerLang))
        {
            LogMessage("Please set the server language first.", -1);
            return;
        }

        string wslPath = GetWslPath(ServerDirectory);
        // Combine cd and init command
        string command = $"cd \"{wslPath}\" && spacetime init --lang {ServerLang} .";
        cmdProcessor.RunWslCommandSilent(command);
        LogMessage("New module initialized", 1);
        
        // Reset the detection process tracking
        if (detectionProcess != null)
        {
            detectionProcess.ResetTracking();
            ServerChangesDetected = false;
        }
    }

    public void CheckPrerequisites(Action<bool, bool, bool, bool, bool, bool, bool, bool, bool, bool, bool> callback)
    {        
        cmdProcessor.CheckPrerequisites((wsl, debian, trixie, curl, spacetime, spacetimePath, rust, spacetimeService, spacetimeLogsService, binaryen, git) => {
            // Save state in ServerManager
            SetHasWSL(wsl);
            SetHasDebian(debian);
            SetHasDebianTrixie(trixie);
            SetHasCurl(curl);
            SetHasSpacetimeDBServer(spacetime);
            SetHasSpacetimeDBPath(spacetimePath);
            SetHasRust(rust);
            SetHasSpacetimeDBService(spacetimeService);
            SetHasSpacetimeDBLogsService(spacetimeLogsService);
            SetHasBinaryen(binaryen);
            SetHasGit(git);
            SetWslPrerequisitesChecked(true);
            
            // Save state to EditorPrefs - moved here from ServerWindow
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasWSL", wsl);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebian", debian);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebianTrixie", trixie);
            EditorPrefs.SetBool(PrefsKeyPrefix + "wslPrerequisitesChecked", true);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasCurl", curl);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", spacetime);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", spacetimePath);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBService", spacetimeService);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBLogsService", spacetimeLogsService);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasRust", rust);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasBinaryen", binaryen);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasGit", git);

            // Read userName from EditorPrefs - moved here from ServerWindow
            string storedUserName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
            if (!string.IsNullOrEmpty(storedUserName) && string.IsNullOrEmpty(userName))
            {
                SetUserName(storedUserName);
            }
              // Then call the original callback
            callback(wsl, debian, trixie, curl, spacetime, spacetimePath, rust, spacetimeService, spacetimeLogsService, binaryen, git);
        });
    }

    public void ViewServerLogs()
    {
        if (SilentMode || serverMode == ServerMode.CustomServer)
        {
            if (DebugMode) LogMessage("Opening/focusing silent server output window...", 0);
            if (serverMode == ServerMode.MaincloudServer)
            {
                ServerOutputWindow.ShowWindow(2); // Database All Tab
            }
            else
            {
                ServerOutputWindow.ShowWindow();
            }
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
                string wslPath = cmdProcessor.GetWslPath(ServerDirectory);
                string logCommand = $"cd \"{wslPath}\" && spacetime logs {ModuleName} -f";
                
                // Build full command with appropriate escaping
                string escapedCommand = logCommand.Replace("\"", "\\\"");
                dbLogProcess.StartInfo.Arguments = $"/k wsl -d Debian -u {UserName} --exec bash -l -c \"{escapedCommand}\"";
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

    public void OpenDebianWindow()
    {
        bool userNameReq = false;
        cmdProcessor.OpenDebianWindow(userNameReq);
    }

    public bool PingServerStatus()
    {
        PingServer(false);
        return pingShowsOnline;
    }
    
    public async Task<bool> PingServerStatusAsync()
    {
        string url;
        if (serverMode == ServerMode.CustomServer)
        {
            url = !string.IsNullOrEmpty(CustomServerUrl) ? CustomServerUrl : "";
        }
        else if (serverMode == ServerMode.MaincloudServer)
        {
            url = !string.IsNullOrEmpty(maincloudUrl) ? maincloudUrl : "https://maincloud.spacetimedb.com/";
        }
        else
        {
            url = !string.IsNullOrEmpty(ServerUrl) ? ServerUrl : "http://127.0.0.1:3000";
        }

        if (url.EndsWith("/"))
        {
            url = url.TrimEnd('/');
        }

        try
        {
            // Use cmdProcessor's synchronous ping method for immediate result with timeout
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            cmdProcessor.PingServer(url, (isOnline, message) => {
                tcs.TrySetResult(isOnline); // Use TrySetResult to avoid exceptions if timeout occurs
            });
            
            // Wait for the ping result with a 5-second timeout to prevent hanging during startup
            using (var timeoutCTS = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                timeoutCTS.Token.Register(() => tcs.TrySetResult(false));
                bool result = await tcs.Task.ConfigureAwait(false);
                return result;
            }
        }
        catch (Exception ex)
        {
            if (DebugMode) LogMessage($"Error during async ping: {ex.Message}", -1);
            return false;
        }
    }

    public void PingServer(bool showLog)
    {
        string url;
        if (serverMode == ServerMode.CustomServer)
        {
            url = !string.IsNullOrEmpty(CustomServerUrl) ? CustomServerUrl : "";
        }
        else if (serverMode == ServerMode.MaincloudServer)
        {
            url = !string.IsNullOrEmpty(maincloudUrl) ? maincloudUrl : "https://maincloud.spacetimedb.com/";
        }
        else
        {
            url = !string.IsNullOrEmpty(ServerUrl) ? ServerUrl : "http://127.0.0.1:3000";
        }

        if (url.EndsWith("/"))
        {
            url = url.TrimEnd('/');
        }
        if (DebugMode) LogMessage($"Pinging server at {url}...", 0);
        
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
                
                RepaintCallback?.Invoke();
            };
        });
    }

    private string FilterGenerateErrors(string error)
    {
        if (string.IsNullOrEmpty(error))
            return error;

        // Filter out the formatting error that doesn't affect functionality
        var lines = error.Split('\n');
        var filteredLines = new System.Collections.Generic.List<string>();
        
        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            // Skip the specific formatting error that confuses users but doesn't affect functionality
            if (trimmedLine.Contains("Could not format generated files: No such file or directory (os error 2)"))
            {
                if (DebugMode)
                {
                    LogMessage("Filtered out formatting warning: " + trimmedLine, 0);
                }
                continue;
            }
            
            // Keep all other error lines
            if (!string.IsNullOrWhiteSpace(trimmedLine))
            {
                filteredLines.Add(line);
            }
        }
        
        return string.Join("\n", filteredLines).Trim();
    }

    private string GetWslPath(string windowsPath)
    {
        return cmdProcessor.GetWslPath(windowsPath);
    }

    public string GetRelativeClientPath()
    {
        // Default path if nothing else works
        string defaultPath = "../Assets/Scripts/Server";
        
        if (string.IsNullOrEmpty(ClientDirectory))
        {
            return defaultPath;
        }
        
        try
        {
            // Normalize path to forward slashes
            string normalizedPath = ClientDirectory.Replace('\\', '/');
            
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
            if (DebugMode) LogMessage($"Error in path handling: {ex.Message}", -1);
            return defaultPath;
        }
    }

    public void BackupServerData()
    {
        versionProcessor.BackupServerData(BackupDirectory, UserName);
    }

    public void RestoreServerData()
    {
        versionProcessor.RestoreServerData(BackupDirectory, UserName);
    }

    public void ClearModuleLogFile()
    {
        if (DebugMode) LogMessage("Clearing log file...", 0);
        logProcessor.ClearModuleLogFile();
        if (DebugMode) LogMessage("Log file cleared successfully", 1);
    }
    
    public void ClearDatabaseLog()
    {
        if (DebugMode) LogMessage("Clearing database log...", 0);
        logProcessor.ClearDatabaseLog();
        if (DebugMode) LogMessage("Database log cleared successfully", 1);
    }

    public void AttemptDatabaseLogRestartAfterReload()
    {
        if (DebugMode) UnityEngine.Debug.Log("[ServerCommandManager] Checking database log process");

        // For journalctl-based approach, database logs are part of the main logging
        if (serverStarted && silentMode)
        {
            if (serverMode == ServerMode.WslServer)
            {
                // Configure WSL and start logging for journalctl-based approach
                logProcessor.ConfigureWSL(true);
                logProcessor.StartLogging();
            }
        }
        else
        {
            if (DebugMode) UnityEngine.Debug.LogWarning("[ServerCommandManager] Cannot restart database log process - server not running or not in silent mode");
        }
    }

    // Simplify the CheckWslStatus method to focus on WSL processes
    public async Task CheckWslStatus()
    {
        // Only check periodically to avoid excessive checks
        double currentTime = EditorApplication.timeSinceStartup;
        if (currentTime - lastWslCheckTime < wslCheckInterval)
            return;
        
        lastWslCheckTime = currentTime;
        
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "powershell.exe";
            
            // Simplify to just check for running WSL processes and Debian installation
            process.StartInfo.Arguments = "-Command \"" +
                // Look for any WSL-related processes
                "$wslProcesses = Get-Process | Where-Object { $_.Name -match 'vmmem' } | Select-Object Name, Id; " +
                "if ($wslProcesses) { " +
                    "Write-Host 'WSL_PROCESSES=Running'; " +
                    "foreach ($p in $wslProcesses) { " +
                        "Write-Host \"Process: $($p.Name) (PID: $($p.Id))\"; " +
                    "} " +
                "} else { " +
                    "Write-Host 'WSL_PROCESSES=None'; " +
                "}; " +
                
                // Check for Debian specifically in the registry (keep this for hasDebian)
                "$debianDistro = Get-ItemProperty HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Lxss\\* -ErrorAction SilentlyContinue | " +
                    "Where-Object { $_.DistributionName -match 'Debian' }; " +
                "if ($debianDistro) { " +
                    "Write-Host 'DEBIAN_FOUND=TRUE'; " +
                "} else { " +
                    "Write-Host 'DEBIAN_FOUND=FALSE'; " +
                "}; " +
                "\"";
            
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            
            // Use await with Task.Run to make the process output reading asynchronous
            string output = await Task.Run(() => process.StandardOutput.ReadToEnd());
            await Task.Run(() => process.WaitForExit());
            
            // Log the output // runs every wslCheckInterval
            //if (debugMode) LogMessage($"WSL status check output: {output}", 0);
            
            // Parse status - now simply using the WSL_PROCESSES indicator
            bool wslRunning = output.Contains("WSL_PROCESSES=Running");
            bool debianFound = output.Contains("DEBIAN_FOUND=TRUE");
            
            // Update running status
            if (isWslRunning != wslRunning)
            {
                isWslRunning = wslRunning;
                if (debugMode) LogMessage($"WSL status updated to: {(isWslRunning ? "Running" : "Stopped")}", 0);
                
                // Check SpacetimeDB version and update once every time WSL is confirmed running
                if (isWslRunning)
                {
                    await CheckSpacetimeDBVersion();
                    await CheckRustVersion();
                }
            }
            
            // Update Debian installed status
            if (hasDebian != debianFound)
            {
                hasDebian = debianFound;
                if (debugMode) LogMessage($"Debian installed status updated to: {(hasDebian ? "Installed" : "Not installed")}", 0);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) LogMessage($"Exception in CheckWslStatus: {ex.Message}", -1);
            isWslRunning = false;
        }
    }

    public void SSHConnectionStatusAsync()
    {
        if (serverCustomProcess == null || serverMode != ServerMode.CustomServer)
        {
            cachedSSHConnectionStatus = false;
            return;
        }

        double currentTime = EditorApplication.timeSinceStartup;
        
        // Only check if enough time has passed
        if (currentTime - lastSSHConnectionCheck >= sshCheckInterval)
        {
            lastSSHConnectionCheck = currentTime;
            
            // Start async check without blocking UI
            Task.Run(async () =>
            {
                try
                {
                    bool status = await serverCustomProcess.IsSessionActiveAsync();
                    // Update cached status on main thread
                    EditorApplication.delayCall += () =>
                    {
                        cachedSSHConnectionStatus = status;
                        RepaintCallback?.Invoke();
                    };
                }
                catch (Exception ex)
                {
                    if (debugMode)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            LogMessage($"Connection check failed: {ex.Message}", -1);
                            cachedSSHConnectionStatus = false;
                            RepaintCallback?.Invoke();
                        };
                    }
                    else
                    {
                        EditorApplication.delayCall += () =>
                        {
                            cachedSSHConnectionStatus = false;
                            RepaintCallback?.Invoke();
                        };
                    }
                }
            });
        }
    }
    
    public async Task CheckMaincloudConnectivity()
    {
        // Only check periodically to avoid excessive resource usage
        double currentTime = EditorApplication.timeSinceStartup;
        if (currentTime - lastMaincloudCheckTime < maincloudCheckInterval)
            return;
        
        lastMaincloudCheckTime = currentTime;
        
        try
        {
            // Use a lightweight endpoint that requires authentication - similar to how ServerDataWindow does it
            if (string.IsNullOrEmpty(moduleName))
            {
                if (DebugMode) LogMessage("No module name set, can't check Maincloud connectivity", 0);
                isMaincloudConnected = false;
                RepaintCallback?.Invoke();
                return;
            }

            string url = maincloudUrl;
            if (!url.EndsWith("/")) url += "/";
            url += $"v1/database/{moduleName}/schema?version=9";
            
            if (DebugMode) LogMessage($"Checking Maincloud connectivity at {url}...", 0);
            
            // Create a HttpClient instance with timeout
            using (var httpClient = new System.Net.Http.HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                // Add authorization header if token exists
                if (!string.IsNullOrEmpty(authToken))
                {
                    httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
                }
                
                // Make the request
                var response = await httpClient.GetAsync(url);
                
                // Consider both successful responses and auth errors (401/403) as "connected"
                bool connected = response.IsSuccessStatusCode || 
                                 (int)response.StatusCode == 401 || 
                                 (int)response.StatusCode == 403;
                
                bool authError = (int)response.StatusCode == 401 || (int)response.StatusCode == 403;
                
                // Get response content for debugging if needed
                string responseContent = await response.Content.ReadAsStringAsync();
                
                if (DebugMode)
                {
                    LogMessage($"Maincloud response: Status={response.StatusCode}, Connected={connected}, AuthError={authError}", 0);
                    if (!connected)
                    {
                        LogMessage($"Response content: {responseContent}", 0);
                    }
                }
                
                // Update status
                if (isMaincloudConnected != connected)
                {
                    isMaincloudConnected = connected;
                    if (DebugMode)
                    {
                        string status = connected ? "Connected" : "Disconnected";
                        if (connected && authError)
                        {
                            status += " (Auth error, but server is reachable)";
                        }
                        LogMessage($"Maincloud status updated to: {status}", 0);
                    }

                    if (isMaincloudConnected)
                    {
                        StartServer();
                    }
                    
                    // Trigger UI update
                    RepaintCallback?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            if (DebugMode) LogMessage($"Exception in CheckMaincloudConnectivity: {ex.Message}", -1);
            isMaincloudConnected = false;
            RepaintCallback?.Invoke();
        }
    }

    public void ResetServerDetection()
    {
        if (detectionProcess != null && detectionProcess.IsDetectingChanges())
        {
            detectionProcess.ResetTrackingAfterPublish(); // New baseline of current file sizes
            ServerChangesDetected = false;
            if (DebugMode) LogMessage("Reset server change detection tracking.", 0);
        }
    }

    private string ExtractHostname(string url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;
            
        string hostname = url;
        
        // Remove protocol if present
        if (hostname.StartsWith("http://")) 
            hostname = hostname.Substring(7);
        else if (hostname.StartsWith("https://")) 
            hostname = hostname.Substring(8);
        
        // Remove path and port if present
        int colonIndex = hostname.IndexOf(':');
        if (colonIndex > 0) 
            hostname = hostname.Substring(0, colonIndex);
            
        int slashIndex = hostname.IndexOf('/');
        if (slashIndex > 0) 
            hostname = hostname.Substring(0, slashIndex);
            
        return hostname;
    }

    private string FormatLoginInfoOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
            return output;
        
        // Color to use for login ID and auth token - Color(0.3f, 0.8f, 0.3f) = #4CCC4C
        string colorTag = "#4CCC4C";
        
        string formattedOutput = output;
        
        // Format login ID line: "You are logged in as <username>"
        string loginPattern = @"(You are logged in as\s+)([^\r\n]+)";
        formattedOutput = System.Text.RegularExpressions.Regex.Replace(
            formattedOutput, 
            loginPattern, 
            $"$1<color={colorTag}>$2</color>", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        // Format auth token line: "Your auth token (don't share this!) is <token>"
        string tokenPattern = @"(Your auth token \(don't share this!\) is\s+)([^\r\n]+)";
        formattedOutput = System.Text.RegularExpressions.Regex.Replace(
            formattedOutput, 
            tokenPattern, 
            $"$1<color={colorTag}>$2</color>", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        return formattedOutput;
    }

    public void OpenSSHWindow()
    {
        // Extract just the IP/hostname from the customServerUrl (remove protocol, port, and path)
        string serverHost = ExtractHostname(customServerUrl);
        
        // Validate that we have all required values
        if (string.IsNullOrEmpty(serverHost) || string.IsNullOrEmpty(sshUserName))
        {
            LogMessage("Cannot open SSH window: Missing server host or username", -1);
            return;
        }
        
        // Construct the SSH command with proper escaping
        string sshCommand = $"ssh {sshUserName}@{serverHost}";
        
        if (DebugMode)
        {
            LogMessage($"Opening SSH window with command: {sshCommand}", 0);
        }

        // Open a new command window and execute the SSH command, keeping the window open
        Process.Start("cmd.exe", $"/K {sshCommand}");
    }
    #endregion

    #region Check All Status

    // Only checks if editor has focus
    public async Task CheckAllStatus()
    {
        // Check appropriate status based on server mode
        if (serverMode == ServerMode.WslServer)
        {
            await CheckWslStatus();
            await CheckServerStatus();
            
            // Check WSL journalctl log processes only if editor has focus
            if (serverStarted && silentMode && logProcessor != null && hasEditorFocus)
            {
                logProcessor.CheckLogProcesses(EditorApplication.timeSinceStartup);
            }
        }
        else if (serverMode == ServerMode.CustomServer)
        {
            await CheckServerStatus();
            
            // Check SSH log processes for custom server mode only if editor has focus
            if (serverStarted && silentMode && logProcessor != null && hasEditorFocus)
            {
                logProcessor.CheckSSHLogProcesses(EditorApplication.timeSinceStartup);
            }
        }
        else if (serverMode == ServerMode.MaincloudServer)
        {
            await CheckMaincloudConnectivity();
            
            // Check log processes for Maincloud mode only if editor has focus
            if (serverStarted && silentMode && logProcessor != null && hasEditorFocus)
            {
                logProcessor.CheckLogProcesses(EditorApplication.timeSinceStartup);
            }
        }

        if (detectServerChanges && detectionProcess != null)
        {
            detectionProcess.CheckForChanges();
        }

        if (echoToConsole)
        {
            ServerOutputWindow.EchoLogsToConsole();
            echoToConsole = EditorPrefs.GetBool(PrefsKeyPrefix + "EchoToConsole", true); // Update the value
        }
    }
    #endregion

    #region Spacetime Version

    public async Task CheckSpacetimeDBVersion() // Only runs in WSL once when WSL has started
    {
        if (debugMode) LogMessage("Checking SpacetimeDB version...", 0);
        
        // Only proceed if prerequisites are met
        if (!hasAllPrerequisites)
        {
            if (debugMode) LogMessage("Skipping SpacetimeDB version check - prerequisites not met", 0);
            return;
        }
        
        // Use RunServerCommandAsync to run the spacetime --version command (mark as status check for silent mode)
        var result = await cmdProcessor.RunServerCommandAsync("spacetime --version", serverDirectory, true);
        
        if (string.IsNullOrEmpty(result.output))
        {
            if (debugMode) LogMessage("Failed to get SpacetimeDB version", -1);
            return;
        }
        //UnityEngine.Debug.Log($"SpacetimeDB version output: {result.output}");
        
        // Parse the version from output that looks like:
        // "spacetime Path: /home/mchat/.local/share/spacetime/bin/1.3.1/spacetimedb-cli
        // Commit: 
        // spacetimedb tool version 1.3.0; spacetimedb-lib version 1.3.0;"
        // Prefer the version from the path (1.3.1) over the tool version (1.3.0)
        string version = "";
        string toolversion = "";

        // First try to extract version from the path (preferred method)
        System.Text.RegularExpressions.Match pathMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"Path:\s+[^\r\n]*?/bin/([0-9]+\.[0-9]+\.[0-9]+)/");

        if (pathMatch.Success && pathMatch.Groups.Count > 1)
        {
            version = pathMatch.Groups[1].Value;
            if (debugMode) LogMessage($"Detected SpacetimeDB version from path: {version}", 1);
        }
        else
        {
            // Fallback to tool version if path version not found
            System.Text.RegularExpressions.Match fallbackToolMatch = 
                System.Text.RegularExpressions.Regex.Match(result.output, @"spacetimedb tool version ([0-9]+\.[0-9]+\.[0-9]+)");

            if (fallbackToolMatch.Success && fallbackToolMatch.Groups.Count > 1)
            {
                version = fallbackToolMatch.Groups[1].Value;
                if (debugMode) LogMessage($"Detected SpacetimeDB version from tool output: {version}", 1);
            }
        }

        // Also save the tool version for cargo.toml version update in Installer Window
        System.Text.RegularExpressions.Match toolMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"spacetimedb tool version ([0-9]+\.[0-9]+\.[0-9]+)");

        if (toolMatch.Success && toolMatch.Groups.Count > 1)
        {
            toolversion = toolMatch.Groups[1].Value;
            EditorPrefs.SetString(PrefsKeyPrefix + "SpacetimeDBVersionTool", toolversion);
            spacetimeDBCurrentVersionTool = toolversion;

            if (debugMode) LogMessage($"Detected SpacetimeDB tool version from output: {toolversion}", 1);
        }

        if (!string.IsNullOrEmpty(version))
        {
            // Save to EditorPrefs
            EditorPrefs.SetString(PrefsKeyPrefix + "SpacetimeDBVersion", version);

            spacetimeDBCurrentVersion = version;

            // Check if update is available by comparing with the latest version from ServerUpdateProcess
            spacetimeDBLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBLatestVersion", "");
            if (!string.IsNullOrEmpty(spacetimeDBLatestVersion) && version != spacetimeDBLatestVersion)
            {
                LogMessage($"SpacetimeDB update available for WSL! Click on the update button in Commands. Current version: {version} and latest version: {spacetimeDBLatestVersion}", 1);
                EditorPrefs.SetBool(PrefsKeyPrefix + "SpacetimeDBUpdateAvailable", true);
            }
        }
        else
        {
            if (debugMode) LogMessage("Could not parse SpacetimeDB version from output", -1);
        }
    }
    #endregion
    
    #region Rust Version
    public async Task CheckRustVersion() // Only runs in WSL once when WSL has started
    {
        if (debugMode) LogMessage("Checking Rust version...", 0);
        
        // Only proceed if prerequisites are met
        if (!hasAllPrerequisites || !hasRust)
        {
            if (debugMode) LogMessage("Skipping Rust version check - prerequisites not met or Rust not installed", 0);
            return;
        }
        
        // Use RunServerCommandAsync to run the rustup check command (mark as status check for silent mode)
        var result = await cmdProcessor.RunServerCommandAsync("rustup check", serverDirectory, true);
        
        if (string.IsNullOrEmpty(result.output))
        {
            if (debugMode) LogMessage("Failed to get Rust version information", -1);
            return;
        }
        
        // TODO: Remove this simulation line after testing
        //result.output = "stable-x86_64-unknown-linux-gnu - Update available : 1.89.0 (29483883e 2025-08-04) -> 1.90.0 (5bc8c42bb 2025-09-04)\nrustup - Up to date : 1.28.2";
        
        if (debugMode) LogMessage($"Rust check output: {result.output}", 0);
        
        // Parse the version from output that looks like:
        // "stable-x86_64-unknown-linux-gnu - Up to date : 1.89.0 (29483883e 2025-08-04)
        // rustup - Up to date : 1.28.2"
        // Or when updates are available:
        // "stable-x86_64-unknown-linux-gnu - Update available : 1.89.0 (29483883e 2025-08-04) -> 1.90.0 (5bc8c42bb 2025-09-04)
        // rustup - Update available : 1.28.2 -> 1.29.0"
        
        string rustStableVersion = "";
        string rustupCurrentVersion = "";
        bool rustUpdateAvailable = false;
        bool rustupUpdateAvailable = false;
        
        // Parse Rust stable version
        System.Text.RegularExpressions.Match rustMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"stable-x86_64-unknown-linux-gnu.*?:\s*([0-9]+\.[0-9]+\.[0-9]+)");
        
        if (rustMatch.Success && rustMatch.Groups.Count > 1)
        {
            rustStableVersion = rustMatch.Groups[1].Value;
            
            // Check if update is available for Rust and extract latest version
            if (result.output.Contains("stable-x86_64-unknown-linux-gnu - Update available"))
            {
                rustUpdateAvailable = true;
                
                // Try to extract the latest version from "1.89.0 -> 1.90.0" format
                System.Text.RegularExpressions.Match latestMatch = 
                    System.Text.RegularExpressions.Regex.Match(result.output, @"stable-x86_64-unknown-linux-gnu.*?->\s*([0-9]+\.[0-9]+\.[0-9]+)");
                
                if (latestMatch.Success && latestMatch.Groups.Count > 1)
                {
                    string latestVersion = latestMatch.Groups[1].Value;
                    EditorPrefs.SetString(PrefsKeyPrefix + "RustLatestVersion", latestVersion);
                    rustLatestVersion = latestVersion;
                    if (debugMode) LogMessage($"Rust update available from version: {rustStableVersion} to {latestVersion}", 1);
                }
                else
                {
                    if (debugMode) LogMessage($"Rust update available from version: {rustStableVersion}", 1);
                }
            }
            else if (result.output.Contains("stable-x86_64-unknown-linux-gnu - Up to date"))
            {
                // Clear the latest version when up to date
                EditorPrefs.SetString(PrefsKeyPrefix + "RustLatestVersion", "");
                rustLatestVersion = "";
                if (debugMode) LogMessage($"Rust is up to date at version: {rustStableVersion}", 1);
            }
        }
        
        // Parse rustup version
        System.Text.RegularExpressions.Match rustupMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"rustup.*?:\s*([0-9]+\.[0-9]+\.[0-9]+)");
        
        if (rustupMatch.Success && rustupMatch.Groups.Count > 1)
        {
            rustupCurrentVersion = rustupMatch.Groups[1].Value;
            
            // Check if update is available for rustup
            if (result.output.Contains("rustup - Update available"))
            {
                rustupUpdateAvailable = true;
                if (debugMode) LogMessage($"Rustup update available from version: {rustupCurrentVersion}", 1);
            }
            else if (result.output.Contains("rustup - Up to date"))
            {
                if (debugMode) LogMessage($"Rustup is up to date at version: {rustupCurrentVersion}", 1);
            }
        }
        
        // Save versions to EditorPrefs and local variables
        if (!string.IsNullOrEmpty(rustStableVersion))
        {
            EditorPrefs.SetString(PrefsKeyPrefix + "RustVersion", rustStableVersion);
            rustCurrentVersion = rustStableVersion;
            
            if (rustUpdateAvailable)
            {
                LogMessage($"Rust update available for WSL! Click on the Installer Window update button to install. Current version: {rustCurrentVersion} and latest version: {rustLatestVersion}", 1);
                EditorPrefs.SetBool(PrefsKeyPrefix + "RustUpdateAvailable", true);
            }
            else
            {
                EditorPrefs.SetBool(PrefsKeyPrefix + "RustUpdateAvailable", false);
            }
        }
        
        if (!string.IsNullOrEmpty(rustupCurrentVersion))
        {
            EditorPrefs.SetString(PrefsKeyPrefix + "RustupVersion", rustupCurrentVersion);
            rustupVersion = rustupCurrentVersion;
            
            if (rustupUpdateAvailable)
            {
                //LogMessage($"Rustup update available for WSL! Click on the Installer Window update button to install the latest version: {rustupCurrentVersion}", 1);
                EditorPrefs.SetBool(PrefsKeyPrefix + "RustupUpdateAvailable", true);
            }
            else
            {
                EditorPrefs.SetBool(PrefsKeyPrefix + "RustupUpdateAvailable", false);
            }
        }
        
        if (string.IsNullOrEmpty(rustStableVersion) && string.IsNullOrEmpty(rustupCurrentVersion))
        {
            if (debugMode) LogMessage("Could not parse Rust version information from output", -1);
        }
    }
    
    // Public method to update log read intervals in ServerLogProcess
    public void UpdateLogReadIntervals(double interval)
    {
        if (logProcessor != null)
        {
            logProcessor.UpdateLogReadIntervals(interval);
        }
    }
    
    // Public method to trigger log processing directly (for ServerOutputWindow)
    public void TriggerLogProcessing()
    {
        if (logProcessor != null && serverStarted && silentMode && hasEditorFocus)
        {
            double currentTime = EditorApplication.timeSinceStartup;
            
            if (serverMode == ServerMode.WslServer || serverMode == ServerMode.MaincloudServer)
            {
                logProcessor.CheckLogProcesses(currentTime);
            }
            else if (serverMode == ServerMode.CustomServer)
            {
                logProcessor.CheckSSHLogProcesses(currentTime);
            }
        }
    }
    
    // Public method to get current log read intervals
    public double GetCurrentLogReadInterval()
    {
        if (logProcessor != null)
        {
            // Return WSL interval as default, both should be the same
            return logProcessor.GetWSLLogReadInterval();
        }
        return 1.0; // Default fallback
    }
    #endregion
} // Class
} // Namespace