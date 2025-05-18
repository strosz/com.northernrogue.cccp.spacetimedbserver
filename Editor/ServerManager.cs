using UnityEditor;
using System.Diagnostics;
using System;
using System.Threading.Tasks;

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
    private const float serverStartupGracePeriod = 30f; // Give server 30 seconds to start
    
    // Server status
    private const double checkInterval = 5.0;
    private bool serverConfirmedRunning = false;
    private bool justStopped = false;
    private bool pingShowsOnline = true;
    private double stopInitiatedTime = 0;

    // Configuration properties - accessed directly from EditorPrefs
    private string userName;
    private string backupDirectory;
    private string serverDirectory;
    private string unityLang;
    private string clientDirectory;
    private string serverLang;
    private string moduleName;
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
    private bool hasRust;
    private bool wslPrerequisitesChecked;
    private bool initializedFirstModule;

    // Update SpacetimeDB
    public string spacetimeDBCurrentVersion;
    public string spacetimeDBCurrentVersionCustom;
    public string spacetimeDBLatestVersion;
    
    // Properties for external access
    public string UserName => userName;
    public string BackupDirectory => backupDirectory;
    public string ServerDirectory => serverDirectory;
    public string UnityLang => unityLang;
    public string ClientDirectory => clientDirectory;
    public string ServerLang => serverLang;
    public string ModuleName => moduleName;
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
    public ServerMode CurrentServerMode => serverMode;

    // Prerequisites properties
    public bool HasWSL => hasWSL;
    public bool HasDebian => hasDebian;
    public bool HasDebianTrixie => hasDebianTrixie;
    public bool HasCurl => hasCurl;
    public bool HasSpacetimeDBServer => hasSpacetimeDBServer;
    public bool HasSpacetimeDBPath => hasSpacetimeDBPath;
    public bool HasRust => hasRust;
    public bool WslPrerequisitesChecked => wslPrerequisitesChecked;
    public bool InitializedFirstModule => initializedFirstModule;

    // Callbacks
    public Action<string, int> LogCallback { get; set; }
    public Action RepaintCallback { get; set; }

    // Near the top of the class, add a new private field for WSL status
    private bool isWslRunning = false;
    private double lastWslCheckTime = 0;
    private const double wslCheckInterval = 5.0;

    // Add property for external access
    public bool IsWslRunning => isWslRunning;

    // Add these properties near the other connection status properties
    private bool isMaincloudConnected = false;
    private double lastMaincloudCheckTime = 0;
    private const double maincloudCheckInterval = 10.0; // Check less frequently to reduce resource usage

    // Add this property for external access
    public bool IsMaincloudConnected => isMaincloudConnected;

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
        hasRust = EditorPrefs.GetBool(PrefsKeyPrefix + "HasRust", false);
        
        // Load UX state
        initializedFirstModule = EditorPrefs.GetBool(PrefsKeyPrefix + "InitializedFirstModule", false);
        
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
        clearModuleLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearModuleLogAtStart", false);
        clearDatabaseLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearDatabaseLogAtStart", false);
        autoCloseWsl = EditorPrefs.GetBool(PrefsKeyPrefix + "AutoCloseWsl", true);

        spacetimeDBCurrentVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersion", "");
        spacetimeDBCurrentVersionCustom = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersionCustom", "");
        spacetimeDBLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBLatestVersion", "");
        
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
    public void SetServerDirectory(string value) { serverDirectory = value; EditorPrefs.SetString(PrefsKeyPrefix + "ServerDirectory", value); }
    public void SetUnityLang(string value) { unityLang = value; EditorPrefs.SetString(PrefsKeyPrefix + "UnityLang", value); }
    public void SetClientDirectory(string value) { clientDirectory = value; EditorPrefs.SetString(PrefsKeyPrefix + "ClientDirectory", value); }
    public void SetServerLang(string value) { serverLang = value; EditorPrefs.SetString(PrefsKeyPrefix + "ServerLang", value); }
    public void SetModuleName(string value) { moduleName = value; EditorPrefs.SetString(PrefsKeyPrefix + "ModuleName", value); }
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
    public void SetDetectServerChanges(bool value) { detectServerChanges = value; EditorPrefs.SetBool(PrefsKeyPrefix + "DetectServerChanges", value); }
    public void SetAutoPublishMode(bool value) { autoPublishMode = value; EditorPrefs.SetBool(PrefsKeyPrefix + "AutoPublishMode", value); }
    public void SetPublishAndGenerateMode(bool value) { publishAndGenerateMode = value; EditorPrefs.SetBool(PrefsKeyPrefix + "PublishAndGenerateMode", value); }
    public void SetSilentMode(bool value) { silentMode = value; EditorPrefs.SetBool(PrefsKeyPrefix + "SilentMode", value); }
    public void SetAutoCloseWsl(bool value) { autoCloseWsl = value; EditorPrefs.SetBool(PrefsKeyPrefix + "AutoCloseWsl", value); }
    public void SetClearModuleLogAtStart(bool value) { clearModuleLogAtStart = value; EditorPrefs.SetBool(PrefsKeyPrefix + "ClearModuleLogAtStart", value); }
    public void SetClearDatabaseLogAtStart(bool value) { clearDatabaseLogAtStart = value; EditorPrefs.SetBool(PrefsKeyPrefix + "ClearDatabaseLogAtStart", value); }
    
    public void SetHasWSL(bool value) { hasWSL = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasWSL", value); }
    public void SetHasDebian(bool value) { hasDebian = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebian", value); }
    public void SetHasDebianTrixie(bool value) { hasDebianTrixie = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebianTrixie", value); }
    public void SetHasCurl(bool value) { hasCurl = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasCurl", value); }
    public void SetHasSpacetimeDBServer(bool value) { hasSpacetimeDBServer = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", value); }
    public void SetHasSpacetimeDBPath(bool value) { hasSpacetimeDBPath = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", value); }
    public void SetHasRust(bool value) { hasRust = value; EditorPrefs.SetBool(PrefsKeyPrefix + "HasRust", value); }
    public void SetWslPrerequisitesChecked(bool value) { wslPrerequisitesChecked = value; EditorPrefs.SetBool(PrefsKeyPrefix + "wslPrerequisitesChecked", value); }
    public void SetInitializedFirstModule(bool value) { initializedFirstModule = value; EditorPrefs.SetBool(PrefsKeyPrefix + "InitializedFirstModule", value); }
    
    public void SetServerMode(ServerMode mode)
    {
        serverMode = mode;
        EditorPrefs.SetString(PrefsKeyPrefix + "ServerMode", mode.ToString());
    }

    public bool CLIAvailable()
    {
        // Check if the CLI is available
        if (hasWSL)
        {
            if (debugMode) LogMessage("SpacetimeDB CLI is available.", 1);
            return true;
        }
        else
        {
            if (debugMode) LogMessage("SpacetimeDB CLI is not available.", -2);
            return false;
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
                // Handle async method without changing method signature
                EditorApplication.delayCall += async () => {
                    await StartCustomServer();
                };
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
        if (!HasWSL || !HasDebian || !HasDebianTrixie || !HasSpacetimeDBServer)
        {
            LogMessage("Missing required installed items. Will attempt to start server.", -2);
        }
        if (string.IsNullOrEmpty(UserName))
        {
            LogMessage("Cannot start server. Debian username is not set.", -1);
            return;
        }
        
        LogMessage("Start sequence initiated for WSL server. Waiting for confirmation...", 0);
        
        try
        {
            // Configure log processor with current settings
            logProcessor.Configure(ModuleName, ServerDirectory, ClearModuleLogAtStart, ClearDatabaseLogAtStart, UserName);
            
            if (SilentMode)
            {
                if (DebugMode) LogMessage($"Starting Spacetime Server (Silent Mode, File Logging to {ServerLogProcess.WslCombinedLogPath})...", 0);
                
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
                serverProcess = cmdProcessor.StartVisibleServerProcess(ServerDirectory);
                if (serverProcess == null) throw new Exception("Failed to start visible server process");
            }

            LogMessage("Server Successfully Started!",1);
        
            // Mark server as starting up
            isStartingUp = true;
            startupTime = (float)EditorApplication.timeSinceStartup;
            serverStarted = true; // Assume starting, CheckServerStatus will verify
            
            // Update log processor state
            logProcessor.SetServerRunningState(true);
        }
        catch (Exception ex)
        {
            LogMessage($"Error during server start sequence: {ex.Message}", -1);
            serverStarted = false;
            isStartingUp = false;
            logProcessor.StopLogging();
            serverProcess = null; 
            
            // Update log processor state
            logProcessor.SetServerRunningState(false);
        }
        finally
        {
            RepaintCallback?.Invoke();
        }
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
            bool confirmed = PingServerStatus();
            if (!confirmed)
            {
                LogMessage("Custom server process started but not confirmed running. Please check the server status.", -1);
                return;
            }
            else
            {
                LogMessage("Custom server process started and confirmed running.", 1);
                // Mark as connected to the custom server
                serverStarted = true;
                serverConfirmedRunning = true;
                isStartingUp = false;
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
        isStartingUp = false; // Ensure startup flag is cleared
        serverConfirmedRunning = false; // Reset confirmed state

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
            StopWslServer();
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

    private void StopWslServer()
    {
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
            serverProcess = null; 
            justStopped = true; // Set flag indicating stop was just initiated
            stopInitiatedTime = EditorApplication.timeSinceStartup; // Record time

            LogMessage("Server Successfully Stopped.", 1);
            
            // Update log processor state
            logProcessor.SetServerRunningState(false);
            
            // WSL Shutdown Logic
            if (AutoCloseWsl)
            {
                cmdProcessor.ShutdownWsl();
            }

            RepaintCallback?.Invoke();
        }
    }

    private async Task StopCustomServer()
    {
        try
        {
            await serverCustomProcess.StopCustomServer();
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

            LogMessage("Custom Server Successfully Stopped.", 1);
            
            // Update log processor state
            logProcessor.SetServerRunningState(false);
            
            RepaintCallback?.Invoke();
        }
    }

    private void StopMaincloudLog()
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

    #region Check Server Status

    public async Task CheckServerStatus()
    {
        // --- Reset justStopped flag after 5 seconds if grace period expired ---
        const double stopGracePeriod = 5.0;
        if (justStopped && (EditorApplication.timeSinceStartup - stopInitiatedTime >= stopGracePeriod))
        {
            if (DebugMode) LogMessage("Stop grace period expired, allowing normal status checks to resume.", 0);
            justStopped = false;
        }

        // --- Startup Phase Check ---
        if (isStartingUp)
        {
            float elapsedTime = (float)(EditorApplication.timeSinceStartup - startupTime);
            bool isActuallyRunning = false;
            
            try {
                if (serverMode == ServerMode.CustomServer)
                {
                    isActuallyRunning = await serverCustomProcess.CheckServerRunning();
                }
                else // WSL and other modes
                {
                    isActuallyRunning = await cmdProcessor.CheckPortAsync(ServerPort);
                }

                // If running during startup phase, confirm immediately
                if (isActuallyRunning)
                {
                    if (DebugMode) LogMessage($"Startup confirmed: Server is now running.", 1);
                    isStartingUp = false;
                    serverStarted = true; // Explicitly confirm started state
                    serverConfirmedRunning = true;
                    justStopped = false; // Reset flag on successful start confirmation

                    // Update logProcessor state
                    logProcessor.SetServerRunningState(true);

                    RepaintCallback?.Invoke();

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
                        return; // Confirmed, skip further checks this cycle
                    }
                }
                // If grace period expires and still not running, assume failure
                else if (elapsedTime >= serverStartupGracePeriod)
                {
                    LogMessage($"Server failed to start within grace period.", -1);
                    isStartingUp = false;
                    serverStarted = false;
                    serverConfirmedRunning = false;
                    justStopped = false; // Reset flag on failed start

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

        // --- Standard Running Check (Only if not starting up) ---
        if (serverStarted)
        {
            bool isActuallyRunning = false;
            try {
                if (serverMode == ServerMode.CustomServer)
                {
                    isActuallyRunning = await serverCustomProcess.CheckServerRunning();
                }
                else // WSL and other modes
                {
                    isActuallyRunning = await cmdProcessor.CheckPortAsync(ServerPort);
                }

                // State Change Detection:
                if (serverConfirmedRunning != isActuallyRunning)
                {
                    serverConfirmedRunning = isActuallyRunning; // Update confirmed state
                    string msg = isActuallyRunning
                        ? $"Server running confirmed ({(serverMode == ServerMode.CustomServer ? "CustomServer remote check" : $"Port {ServerPort}: open")})"
                        : $"WSL SpacetimeDB Server appears to have stopped ({(serverMode == ServerMode.CustomServer ? "CustomServer remote check" : $"Port {ServerPort}: closed")})";
                    LogMessage(msg, isActuallyRunning ? 1 : -2);

                    // If state changed to NOT running, update the main serverStarted flag
                    if (!isActuallyRunning)
                    {
                        serverStarted = false;

                        // Update logProcessor state
                        logProcessor.SetServerRunningState(false);

                        if (DebugMode) LogMessage("Server state updated to stopped.", -1);
                    }
                    else
                    {
                        // If we confirmed it IS running again, clear the stop flag
                        justStopped = false;

                        // Update logProcessor state
                        logProcessor.SetServerRunningState(true);
                    }
                    RepaintCallback?.Invoke();
                }
            }
            catch (Exception ex) {
                if (DebugMode) LogMessage($"Error during server status check: {ex.Message}", -1);
            }
        }
        // --- Check for External Start/Recovery ---
        // Only check if not already started, not starting up, and server is running
        else if (!serverStarted && !isStartingUp)
        {
            bool isActuallyRunning = false;
            try {
                if (serverMode == ServerMode.CustomServer)
                {
                    isActuallyRunning = await serverCustomProcess.CheckServerRunning();
                }
                else // WSL and other modes
                {
                    isActuallyRunning = await cmdProcessor.CheckPortAsync(ServerPort);
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
                        bool confirmed = serverMode == ServerMode.CustomServer ? isActuallyRunning : PingServerStatus();
                        
                        if (confirmed)
                        {
                            // Detected server running, not recently stopped -> likely external start/recovery
                            LogMessage($"Detected WSL server running ({(serverMode == ServerMode.CustomServer ? "CustomServer remote check" : $"Port {ServerPort}" )}).", 1);
                            serverStarted = true;
                            serverConfirmedRunning = true;
                            isStartingUp = false;
                            justStopped = false; // Ensure flag is clear if we recover state

                            // Update logProcessor state
                            logProcessor.SetServerRunningState(true);
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
        try
        {
            // Run the command silently and capture the output
            LogMessage($"{description}...", 0);
            
            // Choose the right processor based on server mode
            if (serverMode == ServerMode.CustomServer)
            {
                // Use the custom server processor for SSH commands
                var result = await serverCustomProcess.RunSpacetimeDBCommandAsync(command);
                
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
                        if (detectionProcess != null && detectionProcess.IsDetectingChanges())
                        {
                            detectionProcess.ResetTrackingAfterPublish();
                            ServerChangesDetected = false;
                            if(DebugMode) LogMessage("Cleared file size tracking after successful publish.", 0);
                        }

                        // Auto-generate if publish was successful and mode is enabled
                        if (PublishAndGenerateMode) 
                        {
                            LogMessage("Publish successful, automatically generating Unity files...", 0);
                            string outDir = GetRelativeClientPath();
                            RunServerCommand($"spacetime generate --out-dir {outDir} --lang {UnityLang}", "Generating Unity files (auto)");
                        }
                    }
                    else if (isGenerateCommand && description == "Generating Unity files (auto)")
                    {
                        LogMessage("Publish and generate successful, requesting script compilation...", 1);
                        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                    }
                }
            }
            else
            {
                // Use the standard command processor for WSL mode
                // Execute the command through the command processor
                var result = await cmdProcessor.RunServerCommandAsync(command, ServerDirectory);
                
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
                        if (detectionProcess != null && detectionProcess.IsDetectingChanges())
                        {
                            detectionProcess.ResetTrackingAfterPublish();
                            ServerChangesDetected = false;
                            if(DebugMode) LogMessage("Cleared file size tracking after successful publish.", 0);
                        }

                        // Auto-generate if publish was successful and mode is enabled
                        if (PublishAndGenerateMode) 
                        {
                            LogMessage("Publish successful, automatically generating Unity files...", 0);
                            string outDir = GetRelativeClientPath();
                            RunServerCommand($"spacetime generate --out-dir {outDir} --lang {UnityLang}", "Generating Unity files (auto)");
                        }
                    }
                    else if (isGenerateCommand && description == "Generating Unity files (auto)")
                    {
                        LogMessage("Publish and generate successful, requesting script compilation...", 1);
                        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                    }
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
        
        if (resetDatabase)
        {
            RunServerCommand($"spacetime publish --server local {ModuleName} --delete-data -y", $"Publishing module '{ModuleName}' and resetting database");
        }
        else
        {
            if (serverMode == ServerMode.MaincloudServer)
            {
                RunServerCommand($"spacetime publish --server maincloud {ModuleName}", $"Publishing module '{ModuleName}' to Maincloud");
            }
            else
            {
                // Default to local server for WSL and CustomServer modes
                RunServerCommand($"spacetime publish --server local {ModuleName}", $"Publishing module '{ModuleName}' to Local");
            }
        }

        // Reset change detection after publishing
        if (detectionProcess != null && detectionProcess.IsDetectingChanges())
        {
            detectionProcess.ResetTrackingAfterPublish();
            // Update local UI state
            ServerChangesDetected = false;
        }

        // publishAndGenerateMode will run generate after publish has been run successfully in RunServerCommand().
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

    public void CheckPrerequisites(Action<bool, bool, bool, bool, bool, bool, bool> callback)
    {
        cmdProcessor.CheckPrerequisites((wsl, debian, trixie, curl, spacetime, spacetimePath, rust) => {
            // Save state in ServerManager
            SetHasWSL(wsl);
            SetHasDebian(debian);
            SetHasDebianTrixie(trixie);
            SetHasCurl(curl);
            SetHasSpacetimeDBServer(spacetime);
            SetHasSpacetimeDBPath(spacetimePath);
            SetHasRust(rust);
            SetWslPrerequisitesChecked(true);
            
            // Save state to EditorPrefs - moved here from ServerWindow
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasWSL", wsl);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebian", debian);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebianTrixie", trixie);
            EditorPrefs.SetBool(PrefsKeyPrefix + "wslPrerequisitesChecked", true);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasCurl", curl);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", spacetime);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", spacetimePath);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasRust", rust);
            
            // Read userName from EditorPrefs - moved here from ServerWindow
            string storedUserName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
            if (!string.IsNullOrEmpty(storedUserName) && string.IsNullOrEmpty(userName))
            {
                SetUserName(storedUserName);
            }
            
            // Then call the original callback
            callback(wsl, debian, trixie, curl, spacetime, spacetimePath, rust);
        });
    }

    public void ViewServerLogs()
    {
        if (SilentMode)
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

    public void AttemptTailRestartAfterReload()
    {
        if (DebugMode) UnityEngine.Debug.Log($"[ServerCommandManager] Attempting tail restart.");
        
        // Delegate to the logProcessor for tail restart
        if (IsServerStarted && SilentMode && cmdProcessor.IsPortInUse(ServerPort))
        {
            logProcessor.AttemptTailRestartAfterReload();
        }
        else
        {
            if (DebugMode) UnityEngine.Debug.LogWarning("[ServerCommandManager] Cannot restart tail process - server not running or not in silent mode");
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
        if (DebugMode) UnityEngine.Debug.Log("[ServerCommandManager] Checking database log process");
        
        // Delegate to the logProcessor
        if (IsServerStarted && SilentMode && cmdProcessor.IsPortInUse(ServerPort))
        {
            logProcessor.AttemptDatabaseLogRestartAfterReload();
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

    // Add WSL status check to the existing EditorUpdateHandler/CheckServerStatus cycle
    public async Task CheckAllStatus()
    {
        // Check appropriate status based on server mode
        if (serverMode == ServerMode.WslServer)
        {
            await CheckWslStatus();
            await CheckServerStatus();
        }
        else if (serverMode == ServerMode.CustomServer)
        {
            await CheckServerStatus();
        }
        else if (serverMode == ServerMode.MaincloudServer)
        {
            await CheckMaincloudConnectivity();
        }
    }

    // Add this method in the #region Utility Methods section
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

    public async Task CheckSpacetimeDBVersion() // Only runs in WSL once when WSL has started
    {
        if (debugMode) LogMessage("Checking SpacetimeDB version...", 0);
        
        // Use RunServerCommandAsync to run the spacetime --version command
        var result = await cmdProcessor.RunServerCommandAsync("spacetime --version", serverDirectory);
        
        if (string.IsNullOrEmpty(result.output))
        {
            if (debugMode) LogMessage("Failed to get SpacetimeDB version", -1);
            return;
        }
        //UnityEngine.Debug.Log($"SpacetimeDB version output: {result.output}");
        
        // Parse the version from output that looks like:
        // "spacetime Path: /home/mchat/.local/share/spacetime/bin/1.1.0/spacetimedb-cli
        // Commit: 
        // spacetimedb tool version 1.1.0; spacetimedb-lib version 1.1.0;"
        string version = "";
        
        // Try to find the version using regex pattern
        System.Text.RegularExpressions.Match match = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"spacetimedb tool version ([0-9]+\.[0-9]+\.[0-9]+)");

        if (match.Success && match.Groups.Count > 1)
        {
            version = match.Groups[1].Value;
            if (debugMode) LogMessage($"Detected SpacetimeDB version: {version}", 1);

            // Save to EditorPrefs
            EditorPrefs.SetString(PrefsKeyPrefix + "SpacetimeDBVersion", version);

            spacetimeDBCurrentVersion = version;

            // Check if update is available by comparing with the latest version from ServerUpdateProcess
            spacetimeDBLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBLatestVersion", "");
            if (!string.IsNullOrEmpty(spacetimeDBLatestVersion) && version != spacetimeDBLatestVersion)
            {
                LogMessage($"SpacetimeDB update available for WSL! Current version: {version} and latest version: {spacetimeDBLatestVersion}", 1);
                EditorPrefs.SetBool(PrefsKeyPrefix + "SpacetimeDBUpdateAvailable", true);
            }
        }
        else
        {
            if (debugMode) LogMessage("Could not parse SpacetimeDB version from output", -1);
        }
    }
    #endregion
} // Class
} // Namespace