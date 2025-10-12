using UnityEditor;
using System.Diagnostics;
using System;
using System.Threading.Tasks;
using UnityEngine;
using NorthernRogue.CCCP.Editor.Settings;

// Runs the methods related to managing and controlling the WSL server and Maincloud ///

namespace NorthernRogue.CCCP.Editor {

public class ServerManager
{       
    // SessionState keys for domain reload persistence
    private const string SessionKeyServerStarted = "ServerManager_ServerStarted";
    private const string SessionKeyServerMode = "ServerManager_ServerMode";
    private const string SessionKeyIsStartingUp = "ServerManager_IsStartingUp";
    
    // Process Handlers
    private ServerWSLProcess wslProcessor;
    private ServerDockerProcess dockerProcessor;
    private ServerLogProcess logProcessor;
    private ServerVersionProcess versionProcessor;
    private ServerCustomProcess serverCustomProcess;
    private ServerDetectionProcess detectionProcess;
    private Process serverProcess;
    
    // Settings reference
    private CCCPSettings Settings => CCCPSettings.Instance;
    
    // Server mode
    public ServerMode serverMode 
    { 
        get => Settings.serverMode; 
        set => CCCPSettingsAdapter.SetServerMode(value); 
    }

    // Server process
    public bool serverStarted = false;
    private bool isStartingUp = false;
    private float startupTime = 0f;
    private const float serverStartupGracePeriod = 10f;
    
    // Server status
    private bool serverConfirmedRunning = false;
    private bool justStopped = false;
    private bool pingShowsOnline = true;
    private double stopInitiatedTime = 0;
    private bool publishing = false;
    private bool isStopping = false;
    
    // Server status resilience (prevent false positives during compilation/brief hiccups)
    private int consecutiveFailedChecks = 0;
    private const int maxConsecutiveFailuresBeforeStop = 3;

    // Core internal properties used throughout ServerManager
    // These cache frequently accessed settings for performance and convenience
    public string userName 
    { 
        get => Settings.userName; 
        set => CCCPSettingsAdapter.SetUserName(value); 
    }
    public string serverDirectory 
    { 
        get => Settings.serverDirectory; 
        set => CCCPSettingsAdapter.SetServerDirectory(value); 
    }
    public string moduleName 
    { 
        get => Settings.moduleName; 
        set => CCCPSettingsAdapter.SetModuleName(value); 
    }
    public bool debugMode 
    { 
        get => Settings.debugMode; 
        set => CCCPSettingsAdapter.SetDebugMode(value); 
    }
    public bool silentMode 
    { 
        get => Settings.silentMode; 
        set => CCCPSettingsAdapter.SetSilentMode(value); 
    }
    
    // Additional frequently used properties for server operations
    public bool detectServerChanges 
    { 
        get => Settings.detectServerChanges; 
        set => CCCPSettingsAdapter.SetDetectServerChanges(value); 
    }
    public bool serverChangesDetected 
    { 
        get => Settings.serverChangesDetected; 
        set => CCCPSettingsAdapter.SetServerChangesDetected(value); 
    }
    public bool autoPublishMode 
    { 
        get => Settings.autoPublishMode; 
        set => CCCPSettingsAdapter.SetAutoPublishMode(value); 
    }
    public bool publishAndGenerateMode 
    { 
        get => Settings.publishAndGenerateMode; 
        set => CCCPSettingsAdapter.SetPublishAndGenerateMode(value); 
    }
    public bool clearModuleLogAtStart 
    { 
        get => Settings.clearModuleLogAtStart; 
        set => CCCPSettingsAdapter.SetClearModuleLogAtStart(value); 
    }
    public bool clearDatabaseLogAtStart 
    { 
        get => Settings.clearDatabaseLogAtStart; 
        set => CCCPSettingsAdapter.SetClearDatabaseLogAtStart(value); 
    }
    public bool autoCloseWsl 
    { 
        get => Settings.autoCloseWsl; 
        set => CCCPSettingsAdapter.SetAutoCloseWsl(value); 
    }

    // Prerequisites state
    public bool hasWSL 
    { 
        get => Settings.hasWSL; 
        set => CCCPSettingsAdapter.SetHasWSL(value); 
    }
    public bool hasDebian 
    { 
        get => Settings.hasDebian; 
        set => CCCPSettingsAdapter.SetHasDebian(value); 
    }
    public bool hasDebianTrixie 
    { 
        get => Settings.hasDebianTrixie; 
        set => CCCPSettingsAdapter.SetHasDebianTrixie(value); 
    }
    public bool hasCurl 
    { 
        get => Settings.hasCurl; 
        set => CCCPSettingsAdapter.SetHasCurl(value); 
    }
    public bool hasSpacetimeDBServer 
    { 
        get => Settings.hasSpacetimeDBServer; 
        set => CCCPSettingsAdapter.SetHasSpacetimeDBServer(value); 
    }
    public bool hasSpacetimeDBPath 
    { 
        get => Settings.hasSpacetimeDBPath; 
        set => CCCPSettingsAdapter.SetHasSpacetimeDBPath(value); 
    }
    public bool hasSpacetimeDBService 
    { 
        get => Settings.hasSpacetimeDBService; 
        set => CCCPSettingsAdapter.SetHasSpacetimeDBService(value); 
    }
    public bool hasSpacetimeDBLogsService 
    { 
        get => Settings.hasSpacetimeDBLogsService; 
        set => CCCPSettingsAdapter.SetHasSpacetimeDBLogsService(value); 
    }
    public bool hasRust 
    { 
        get => Settings.hasRust; 
        set => CCCPSettingsAdapter.SetHasRust(value); 
    }
    public bool hasNETSDK 
    { 
        get => Settings.hasNETSDK; 
        set => CCCPSettingsAdapter.SetHasNETSDK(value); 
    }
    public bool hasBinaryen 
    { 
        get => Settings.hasBinaryen; 
        set => CCCPSettingsAdapter.SetHasBinaryen(value); 
    }
    public bool hasGit 
    { 
        get => Settings.hasGit; 
        set => CCCPSettingsAdapter.SetHasGit(value); 
    }
    public bool wslPrerequisitesChecked 
    { 
        get => Settings.wslPrerequisitesChecked; 
        set => CCCPSettingsAdapter.SetWslPrerequisitesChecked(value); 
    }
    public bool initializedFirstModule 
    { 
        get => Settings.initializedFirstModule; 
        set => CCCPSettingsAdapter.SetInitializedFirstModule(value); 
    }
    public bool publishFirstModule 
    { 
        get => Settings.publishFirstModule; 
        set => CCCPSettingsAdapter.SetPublishFirstModule(value); 
    }
    public bool hasAllPrerequisites 
    { 
        get => Settings.hasAllPrerequisites; 
        set => CCCPSettingsAdapter.SetHasAllPrerequisites(value); 
    }

    // Update SpacetimeDB
    public string spacetimeDBCurrentVersion 
    { 
        get => Settings.spacetimeDBCurrentVersion; 
        set => CCCPSettingsAdapter.SetSpacetimeDBCurrentVersion(value); 
    }
    public string spacetimeDBCurrentVersionCustom 
    { 
        get => Settings.spacetimeDBCurrentVersionCustom; 
        set => CCCPSettingsAdapter.SetSpacetimeDBCurrentVersionCustom(value); 
    }
    public string spacetimeDBCurrentVersionTool 
    { 
        get => Settings.spacetimeDBCurrentVersionTool; 
        set => CCCPSettingsAdapter.SetSpacetimeDBCurrentVersionTool(value); 
    }
    public string spacetimeDBLatestVersion 
    { 
        get => Settings.spacetimeDBLatestVersion; 
        set => CCCPSettingsAdapter.SetSpacetimeDBLatestVersion(value); 
    }

    // Update Rust
    public string rustCurrentVersion 
    { 
        get => Settings.rustCurrentVersion; 
        set => CCCPSettingsAdapter.SetRustCurrentVersion(value); 
    }
    public string rustLatestVersion 
    { 
        get => Settings.rustLatestVersion; 
        set => CCCPSettingsAdapter.SetRustLatestVersion(value); 
    }
    public string rustupVersion 
    { 
        get => Settings.rustupVersion; 
        set => CCCPSettingsAdapter.SetRustupVersion(value); 
    }

    // Server output window settings
    public bool echoToConsole 
    { 
        get => Settings.echoToConsole; 
        set => CCCPSettingsAdapter.SetEchoToConsole(value); 
    }
    
    // Compatibility properties (old naming convention) - these will be deprecated
    public string SSHUserName => Settings.sshUserName;
    public string CustomServerUrl => Settings.customServerUrl;
    public int CustomServerPort => Settings.customServerPort;
    public string CustomServerAuthToken => Settings.customServerAuthToken;
    public string SSHPrivateKeyPath => Settings.sshPrivateKeyPath;
    
    public bool DebugMode => debugMode;
    public bool HideWarnings => Settings.hideWarnings;
    public bool DetectServerChanges => detectServerChanges;
    public bool ServerChangesDetected { get => serverChangesDetected; set => serverChangesDetected = value; }
    public bool AutoPublishMode => autoPublishMode;
    public bool PublishAndGenerateMode => publishAndGenerateMode;
    public bool SilentMode => silentMode;
    public bool AutoCloseWsl => autoCloseWsl;
    public bool ClearModuleLogAtStart => clearModuleLogAtStart;
    public bool ClearDatabaseLogAtStart => clearDatabaseLogAtStart;

    public string MaincloudUrl => Settings.maincloudUrl;
    public string MaincloudAuthToken => Settings.maincloudAuthToken;
    
    // Additional compatibility properties
    public string UserName => userName;
    public string BackupDirectory => Settings.backupDirectory;
    public string ServerDirectory => serverDirectory;
    public string UnityLang => Settings.unityLang;
    public string ClientDirectory => Settings.clientDirectory;
    public string ServerLang => Settings.serverLang;
    public string ModuleName => moduleName;
    public string LocalCLIProvider => Settings.localCLIProvider;

    // WSL Server properties
    public string ServerUrl => Settings.serverUrl;
    public int ServerPort => Settings.serverPort;
    public string AuthToken => Settings.authToken;
    
    // Docker server properties
    public string ServerUrlDocker => Settings.serverUrlDocker;
    public int ServerPortDocker => Settings.serverPortDocker;
    public string AuthTokenDocker => Settings.authTokenDocker;

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
    public bool HasNETSDK => hasNETSDK;
    public bool HasBinaryen => hasBinaryen;
    public bool HasGit => hasGit;
    public bool WslPrerequisitesChecked => wslPrerequisitesChecked;
    public bool InitializedFirstModule => initializedFirstModule;
    public bool PublishFirstModule => publishFirstModule;
    public bool HasAllPrerequisites => hasAllPrerequisites;

    // Process getters
    public ServerWSLProcess GetWSLProcessor() => wslProcessor;

    // Callbacks
    public Action<string, int> LogCallback { get; set; }
    public Action RepaintCallback { get; set; }

    // WSL Connection Status
    public bool IsWslRunning => isWslRunning;
    private bool isWslRunning = false;
    
    public bool IsDockerRunning => isDockerRunning;
    private bool isDockerRunning = false;
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
        WSLServer,
        DockerServer,
        CustomServer,
        MaincloudServer,
    }

    public ServerManager(Action<string, int> logCallback, Action repaintCallback)
    {
        LogCallback = logCallback;
        RepaintCallback = repaintCallback;
        
        // Load settings (migration will happen automatically if needed)
        LoadSettings();
        
        // Restore server state from SessionState (domain reload persistence)
        RestoreServerStateFromSessionState();
        
        // Initialize the processors
        wslProcessor = new ServerWSLProcess(LogMessage, debugMode);
        dockerProcessor = new ServerDockerProcess(LogMessage, debugMode);
        serverCustomProcess = new ServerCustomProcess(LogMessage, debugMode);
        
        // Initialize LogProcessor with callbacks
        logProcessor = new ServerLogProcess(
            LogMessage,
            () => ServerOutputWindow.RefreshOpenWindow(), // Module log update callback
            () => ServerOutputWindow.RefreshDatabaseLogs(), // Database log update callback - uses high-priority refresh
            wslProcessor,
            dockerProcessor,
            serverCustomProcess,
            debugMode
        );
        
        // Initialize VersionProcessor
        versionProcessor = new ServerVersionProcess(wslProcessor, LogMessage, debugMode);
        
        // Initialize ServerDetectionProcess
        detectionProcess = new ServerDetectionProcess();
        if (!string.IsNullOrEmpty(serverDirectory))
        {
            detectionProcess.Configure(serverDirectory, detectServerChanges);
        }
        detectionProcess.OnServerChangesDetected += OnServerChangesDetected;
        
        // Configure the server control delegates
        versionProcessor.ConfigureServerControlDelegates(
            () => serverStarted, // IsServerRunning
            () => autoCloseWsl,  // GetAutoCloseWsl
            (value) => { autoCloseWsl = value; }, // SetAutoCloseWsl
            () => StartServer(),  // StartServer
            () => StopServer()    // StopServer
        );
        
        // Configure
        Configure();
        
        // Register for assembly reload events to save state before domain reload
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
    }

    // Migration from EditorPrefs is handled automatically in CCCPSettingsProvider.GetOrCreateSettings()
    public void LoadSettings()
    {
        var settings = CCCPSettings.Instance;
    }

    // Repaint method which safely queues repaints
    private void SafeRepaint()
    {
        if (RepaintCallback != null)
        {
            EditorApplication.delayCall += () => RepaintCallback();
        }
    }

    // Needed for the ServerDetectionProcess
    public void UpdateServerDetectionDirectory(string value) 
    { 
        serverDirectory = value;
        
        if (detectionProcess != null)
        {
            detectionProcess.Configure(value, detectServerChanges);
        }
    }

    public void UpdateMaincloudUrl(string value)
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
        string httpsUrl = "https://" + cleanedUrl;
        CCCPSettingsAdapter.SetMaincloudUrl(httpsUrl);
        
        if (debugMode) LogMessage($"Maincloud URL set to: {httpsUrl}", 0);
    }
    
    public void UpdateDetectServerChanges(bool value) 
    { 
        detectServerChanges = value;
        
        // Update ServerDetectionProcess if it exists
        if (detectionProcess != null)
        {
            detectionProcess.SetDetectChanges(value);
        }
    }
    
    // Set editor focus state to prevent background log processing accumulation
    public void SetEditorFocus(bool hasFocus) { hasEditorFocus = hasFocus; }
    
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
        if (logProcessor != null && Settings.serverMode == ServerMode.WSLServer)
        {
            logProcessor.ForceWSLLogRefresh();
        }
    }
    
    // Force SSH log refresh - triggers new journalctl commands for custom server
    public void ForceSSHLogRefresh()
    {
        if (logProcessor != null && Settings.serverMode == ServerMode.CustomServer)
        {
            logProcessor.ForceSSHLogRefresh();
        }
    }
    
    // Force SSH log continuation after compilation - preserves timestamps to maintain log continuity
    public void ForceSSHLogContinuation()
    {
        if (logProcessor != null && Settings.serverMode == ServerMode.CustomServer)
        {
            logProcessor.ForceSSHLogContinuation();
        }
    }
    
    public void SetServerMode(ServerMode mode)
    {
        serverMode = mode;
        CCCPSettingsAdapter.SetServerMode(mode);
        
        // Clear status cache when changing server modes
        if (wslProcessor != null)
        {
            wslProcessor.ClearStatusCache();
        }
    }

    public void Configure()
    {
        // Configure the log processor
        logProcessor.Configure(moduleName, serverDirectory, clearModuleLogAtStart, clearDatabaseLogAtStart, userName);
        logProcessor.SetServerRunningState(serverStarted);
        
        // Initialize the detection processor
        detectionProcess = new ServerDetectionProcess();
        detectionProcess.Configure(serverDirectory, detectServerChanges);
        detectionProcess.OnServerChangesDetected += OnServerChangesDetected;
    }

    /// <summary>
    /// Restores server state from SessionState after domain reload
    /// This ensures serverStarted persists across Unity compilation
    /// </summary>
    private void RestoreServerStateFromSessionState()
    {
        // Restore server started state
        bool sessionServerStarted = SessionState.GetBool(SessionKeyServerStarted, false);
        bool sessionIsStartingUp = SessionState.GetBool(SessionKeyIsStartingUp, false);
        int sessionServerMode = SessionState.GetInt(SessionKeyServerMode, (int)ServerMode.WSLServer);
        
        if (sessionServerStarted || sessionIsStartingUp)
        {
            if (debugMode)
                LogMessage($"[ServerManager] Restoring server state from SessionState: serverStarted={sessionServerStarted}, isStartingUp={sessionIsStartingUp}, serverMode={(ServerMode)sessionServerMode}", 0);
            
            // Restore the state
            serverStarted = sessionServerStarted;
            isStartingUp = sessionIsStartingUp;
            serverMode = (ServerMode)sessionServerMode;
            
            // If server was marked as started, verify it's actually still running
            if (serverStarted)
            {
                EditorApplication.delayCall += async () =>
                {
                    try
                    {
                        bool isActuallyRunning = await PingServerStatusAsync();
                        if (!isActuallyRunning)
                        {
                            if (debugMode)
                                LogMessage("[ServerManager] Server state was restored but ping failed - server appears to be offline. Resetting state.", 1);
                            
                            serverStarted = false;
                            isStartingUp = false;
                            SaveServerStateToSessionState();
                        }
                        else
                        {
                            if (debugMode)
                                LogMessage("[ServerManager] Server state restored successfully - server is confirmed running.", 0);
                            
                            // Restart logging after compilation when server state is confirmed
                            if (logProcessor != null)
                            {
                                // Reconfigure log processor for the restored server mode
                                if (serverMode == ServerMode.CustomServer)
                                {
                                    // Extract SSH details like in normal startup
                                    string sshHost = ServerUtilityProvider.ExtractHostname(CustomServerUrl);
                                    
                                    logProcessor.ConfigureSSH(SSHUserName, sshHost, SSHPrivateKeyPath, true);
                                    if (debugMode)
                                        LogMessage($"[ServerManager] Reconfigured SSH log processor: {SSHUserName}@{sshHost}", 1);
                                }
                                else if (serverMode == ServerMode.DockerServer)
                                {
                                    logProcessor.ConfigureDocker(true);
                                    if (debugMode)
                                        LogMessage($"[ServerManager] Reconfigured Docker log processor", 1);
                                }
                                else if (serverMode == ServerMode.WSLServer)
                                {
                                    logProcessor.ConfigureWSL(true);
                                    if (debugMode)
                                        LogMessage($"[ServerManager] Reconfigured WSL log processor", 1);
                                }
                                
                                logProcessor.SetServerRunningState(true);
                                logProcessor.StartLogging();
                                if (debugMode)
                                    LogMessage("[ServerManager] Restarted log processor after compilation - server confirmed running.", 1);
                            }
                        }
                        
                        SafeRepaint();
                    }
                    catch (Exception ex)
                    {
                        if (debugMode)
                            LogMessage($"[ServerManager] Error verifying restored server state: {ex.Message}", 2);
                    }
                };
            }
        }
        else if (debugMode)
        {
            LogMessage("[ServerManager] No server state to restore from SessionState", 0);
        }
    }

    /// <summary>
    /// Saves current server state to SessionState for domain reload persistence
    /// </summary>
    public void SaveServerStateToSessionState()
    {
        SessionState.SetBool(SessionKeyServerStarted, serverStarted);
        SessionState.SetBool(SessionKeyIsStartingUp, isStartingUp);
        SessionState.SetInt(SessionKeyServerMode, (int)serverMode);
        
        if (debugMode)
            LogMessage($"[ServerManager] Saved server state to SessionState: serverStarted={serverStarted}, isStartingUp={isStartingUp}, serverMode={serverMode}", 0);
    }

    /// <summary>
    /// Called before assembly reload to save server state
    /// </summary>
    private void OnBeforeAssemblyReload()
    {
        if (debugMode)
            LogMessage("[ServerManager] Assembly reload detected - saving server state...", 0);
        
        SaveServerStateToSessionState();
        
        // Unregister the event to prevent memory leaks
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
    }

    private void LogMessage(string message, int style)
    {
        LogCallback?.Invoke(message, style);
    }

    private void OnServerChangesDetected(bool changesDetected)
    {
        // Update UI when changes are detected
        serverChangesDetected = changesDetected;
        SafeRepaint();
        
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

        switch (Settings.serverMode)
        {
            case ServerMode.WSLServer:
                StartWSLServer();
                break;
            case ServerMode.DockerServer:
                StartDockerServer();
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

    private void StartWSLServer()
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
        if (wslProcessor != null)
        {
            wslProcessor.ClearStatusCache();
        }
        
        EditorApplication.delayCall += async () => {
            try
            {
                // Configure log processor with current settings
                logProcessor.Configure(ModuleName, ServerDirectory, ClearModuleLogAtStart, ClearDatabaseLogAtStart, UserName);
                
                // Start SpacetimeDB services using systemctl
                if (DebugMode) LogMessage("Starting SpacetimeDB service...", 0);
                bool serviceStarted = await wslProcessor.StartSpacetimeDBServices();
                if (!serviceStarted)
                {
                    throw new Exception("Failed to start SpacetimeDB services");
                }

                LogMessage("Server Successfully Started!", 1);
                
                bool serviceRunning = await wslProcessor.CheckServerRunning(instantCheck: true);
                
                if (DebugMode) LogMessage($"Immediate startup verification - Service: {(serviceRunning ? "active" : "inactive")}", 0);
                
                if (serviceRunning)
                {
                    if (DebugMode) LogMessage("Server service confirmed running immediately!", 1);
                    serverStarted = true;
                    SaveServerStateToSessionState(); // Persist state across domain reloads
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
                    SaveServerStateToSessionState(); // Persist state across domain reloads
                    
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
                SaveServerStateToSessionState(); // Persist state across domain reloads
                logProcessor.StopLogging();
                
                // Update log processor state
                logProcessor.SetServerRunningState(false);
            }
            finally
            {
                SafeRepaint();
            }
        };
    }

    private void StartDockerServer()
    {
        LogMessage("Start sequence initiated for Docker server. Waiting for confirmation...", 0);
        
        EditorApplication.delayCall += async () => {
            try
            {
                // Configure log processor with current settings
                logProcessor.Configure(ModuleName, ServerDirectory, ClearModuleLogAtStart, ClearDatabaseLogAtStart, "docker");
                
                // Check if Docker service is running first
                bool dockerServiceRunning = await dockerProcessor.IsDockerServiceRunning();
                if (!dockerServiceRunning)
                {
                    LogMessage("Docker service is not running. Attempting to start Docker Desktop...", 0);
                    bool started = await dockerProcessor.StartDockerService();
                    if (!started)
                    {
                        throw new Exception("Failed to start Docker service. Please start Docker Desktop manually.");
                    }
                    
                    // Wait a moment for Docker service to fully initialize
                    await Task.Delay(3000);
                }

                // Start Docker container
                if (DebugMode) LogMessage("Starting SpacetimeDB Docker container...", 0);
                Process containerProcess;
                if (silentMode)
                {
                    containerProcess = dockerProcessor.StartSilentServerProcess(ServerDirectory, ClientDirectory);
                }
                else // Should be removed
                {
                    containerProcess = dockerProcessor.StartVisibleServerProcess(ServerDirectory, ClientDirectory);
                }

                if (containerProcess == null)
                {
                    throw new Exception("Failed to start Docker container");
                }

                LogMessage("Docker Container Started Successfully!", 1);
                
                // Wait a moment for container to initialize
                await Task.Delay(2000);
                
                bool containerRunning = await dockerProcessor.CheckDockerProcessAsync(true);
                
                if (DebugMode) LogMessage($"Immediate startup verification - Container: {(containerRunning ? "running" : "not running")}", 0);
                
                if (containerRunning)
                {
                    if (DebugMode) LogMessage("Docker container confirmed running immediately!", 1);
                    serverStarted = true;
                    SaveServerStateToSessionState(); // Persist state across domain reloads
                    serverConfirmedRunning = true;
                    isStartingUp = false; // Skip the startup grace period since we confirmed container is running
                    
                    // Configure Docker log processor
                    logProcessor.ConfigureDocker(true);
                    logProcessor.SetServerRunningState(true);
                    
                    if (silentMode)
                    {
                        logProcessor.StartLogging();
                        if (debugMode) LogMessage("Docker log processors started successfully.", 1);
                    }
                    
                    // Check ping in background for additional confirmation
                    _ = Task.Run(async () => {
                        await Task.Delay(3000); // Give HTTP endpoint time to initialize
                        bool pingResponding = await PingServerStatusAsync();
                        if (DebugMode)
                        {
                            LogMessage($"Background ping check result: {(pingResponding ? "responding" : "not responding")}", 0);
                        }
                    });
                }
                else
                {
                    LogMessage("Container started, waiting for server to become ready...", 0);
                    // Mark server as starting up for grace period monitoring
                    isStartingUp = true;
                    startupTime = (float)EditorApplication.timeSinceStartup;
                    serverStarted = true; // Assume starting, CheckServerStatus will verify
                    SaveServerStateToSessionState(); // Persist state across domain reloads
                    
                    // Configure Docker log processor
                    logProcessor.ConfigureDocker(true);
                    logProcessor.SetServerRunningState(true);
                    
                    // Start logging if in silent mode
                    if (silentMode)
                    {
                        logProcessor.StartLogging();
                        if (debugMode) LogMessage("Docker log processors started successfully.", 1);
                    }
                }
            }
            catch (Exception ex)
            {
                if (debugMode) LogMessage($"Error during Docker server start sequence: {ex.Message}", -1);
                serverStarted = false;
                isStartingUp = false;
                SaveServerStateToSessionState(); // Persist state across domain reloads
                
                // Update log processor state
                logProcessor.SetServerRunningState(false);
            }
            finally
            {
                SafeRepaint();
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
            SaveServerStateToSessionState(); // Persist state across domain reloads
            isStartingUp = true;
            serverConfirmedRunning = false;
            startupTime = (float)EditorApplication.timeSinceStartup;

            // Configure log processor for custom server if in silent mode
            if (silentMode && logProcessor != null)
            {
                // Extract hostname from CustomServerUrl
                string sshHost = ServerUtilityProvider.ExtractHostname(CustomServerUrl);

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
        SafeRepaint();
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
        SaveServerStateToSessionState(); // Persist state across domain reloads
        isStartingUp = false;
        serverConfirmedRunning = true;
        
        // Initialize the log processor if it's null
        if (logProcessor == null)
        {
            logProcessor = new ServerLogProcess(
                LogMessage,
                () => { SafeRepaint(); },
                () => { SafeRepaint(); },
                wslProcessor,
                dockerProcessor,
                serverCustomProcess,
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
        SafeRepaint();
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

        if (Settings.serverMode == ServerMode.CustomServer)
        {
            EditorApplication.delayCall += async () => {
                await StopCustomServer();
            };
            SafeRepaint();
            return;
        }
        else if (Settings.serverMode == ServerMode.WSLServer)
        {
            EditorApplication.delayCall += async () => {
                await StopWSLServer();
            };
            SafeRepaint();
            return;
        }
        else if (Settings.serverMode == ServerMode.DockerServer)
        {
            EditorApplication.delayCall += async () => {
                await StopDockerServer();
            };
            SafeRepaint();
            return;
        }
        else if (Settings.serverMode == ServerMode.MaincloudServer)
        {
            StopMaincloudLog();
            SafeRepaint();
            return;
        }
    }    

    private async Task StopWSLServer()
    {
        // Set the stopping flag to prevent concurrent stops
        isStopping = true;
        
        try
        {
            // Clear the status cache since we're stopping the server
            if (wslProcessor != null)
            {
                wslProcessor.ClearStatusCache();
            }
            
            LogMessage("Stopping SpacetimeDB services and processes...", 0);
            
            // Use the wslProcessor to stop the services
            bool stopSuccessful = await wslProcessor.StopSpacetimeDBServices();
            
            if (stopSuccessful)
            {
                if (debugMode) LogMessage("Stop commands completed. Verifying server is fully stopped...", 0);
                
                // Clear status cache again and force immediate status check
                wslProcessor.ClearStatusCache();
                
                // Check if server is actually stopped (with instant check to bypass cache)
                bool stillRunning = await wslProcessor.CheckServerRunning(instantCheck: true);
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
                    if (stillRunning && debugMode)
                        LogMessage("Warning: Some SpacetimeDB processes may still be running.", 0);
                    if (pingStillResponding && debugMode)
                        LogMessage("Warning: Server is still responding to ping requests.", 0);
                        
                    // Still mark as stopped since we did our best
                    serverStarted = false;
                    isStartingUp = false;
                    serverConfirmedRunning = false;
                    serverProcess = null;
                    justStopped = true;
                    stopInitiatedTime = EditorApplication.timeSinceStartup;
                    consecutiveFailedChecks = 0;

                    LogMessage("Stop sequence completed. Check server status manually if needed.", 1);
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
            SafeRepaint();
        }
    }

    private async Task StopDockerServer()
    {
        // Set the stopping flag to prevent concurrent stops
        isStopping = true;
        
        try
        {
            LogMessage("Stopping SpacetimeDB Docker container...", 0);
            
            // Use the dockerProcessor to stop the container
            bool stopSuccessful = await dockerProcessor.StopServer();
            
            if (stopSuccessful)
            {
                if (debugMode) LogMessage("Stop commands completed. Verifying container is fully stopped...", 0);
                
                // Wait a moment for container to fully stop
                await Task.Delay(2000);
                
                // Verify container is stopped and ping is not responding
                bool stillRunning = await dockerProcessor.CheckDockerProcessAsync(await dockerProcessor.IsDockerServiceRunning());
                bool pingStillResponding = await PingServerStatusAsync();
                
                if (!stillRunning && !pingStillResponding)
                {
                    // Clean shutdown confirmed
                    serverStarted = false;
                    isStartingUp = false;
                    serverConfirmedRunning = false;
                    serverProcess = null;
                    justStopped = true;
                    stopInitiatedTime = EditorApplication.timeSinceStartup;
                    consecutiveFailedChecks = 0;

                    LogMessage("Docker SpacetimeDB Server Successfully Stopped!", 1);
                    logProcessor.StopLogging();
                    logProcessor.SetServerRunningState(false);
                }
                else
                {
                    if (stillRunning && debugMode)
                        LogMessage("Warning: Docker container may still be running.", 0);
                    if (pingStillResponding && debugMode)
                        LogMessage("Warning: Server is still responding to ping requests.", 0);

                    // Still mark as stopped since we did our best
                    serverStarted = false;
                    isStartingUp = false;
                    serverConfirmedRunning = false;
                    serverProcess = null;
                    justStopped = true;
                    stopInitiatedTime = EditorApplication.timeSinceStartup;
                    consecutiveFailedChecks = 0;

                    LogMessage("Stop sequence completed. Check Docker container status manually if needed.", 1);
                    logProcessor.StopLogging();
                    logProcessor.SetServerRunningState(false);
                }
            }
            else
            {
                LogMessage("Docker stop commands failed or timed out. Container may still be running.", -1);
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Error during Docker server stop sequence: {ex.Message}", -1);
        }
        finally
        {
            // Always clear the stopping flag
            isStopping = false;
            SafeRepaint();
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
            SaveServerStateToSessionState(); // Persist state across domain reloads
            isStartingUp = false;
            serverConfirmedRunning = false;
            serverProcess = null; 
            justStopped = true; // Set flag indicating stop was just initiated
            stopInitiatedTime = EditorApplication.timeSinceStartup; // Record time

            LogMessage("Custom remote SpacetimeDB Successfully Stopped!", 1);
            
            // Update log processor state
            logProcessor.SetServerRunningState(false);
            
            SafeRepaint();
        }
    }

    public void StopMaincloudLog()
    {
        // Stop the log processors
        logProcessor.StopLogging();
        
        // Force state update
        serverStarted = false;
        SaveServerStateToSessionState(); // Persist state across domain reloads
        isStartingUp = false;
        serverConfirmedRunning = false;
        justStopped = true; // Set flag indicating stop was just initiated
        stopInitiatedTime = EditorApplication.timeSinceStartup; // Record time

        LogMessage("Maincloud Server Successfully Stopped.", 1);
        
        // Update log processor state
        logProcessor.SetServerRunningState(false);
        
        SafeRepaint();
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
                if (Settings.serverMode == ServerMode.CustomServer)
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
                else if (Settings.serverMode == ServerMode.DockerServer)
                {
                    // Docker mode - check Docker container status
                    bool dockerServiceRunning = await dockerProcessor.IsDockerServiceRunning();
                    bool containerRunning = false;
                    
                    if (dockerServiceRunning)
                    {
                        containerRunning = await dockerProcessor.CheckDockerProcessAsync(dockerServiceRunning);
                    }
                    
                    // During startup phase, prioritize container status
                    if (containerRunning)
                    {
                        isActuallyRunning = true;
                        
                        // Optionally check ping for additional confirmation
                        if (elapsedTime > 3.0f)
                        {
                            bool pingResponding = await PingServerStatusAsync();
                            if (DebugMode) 
                            {
                                LogMessage($"Docker startup check - Container: running, Ping: {(pingResponding ? "responding" : "not responding")}, Elapsed: {elapsedTime:F1}s", 0);
                            }
                        }
                        else
                        {
                            if (DebugMode) 
                            {
                                LogMessage($"Docker startup check - Container: running, Elapsed: {elapsedTime:F1}s (early startup, ping skipped)", 0);
                            }
                        }
                    }
                    else
                    {
                        isActuallyRunning = false;
                        if (DebugMode) 
                        {
                            LogMessage($"Docker startup check - Container: not running, Elapsed: {elapsedTime:F1}s", 0);
                        }
                    }
                }
                else // WSL mode
                {
                    // Use instantCheck=true to bypass cache during startup for immediate status verification
                    bool serviceRunning = await wslProcessor.CheckServerRunning(instantCheck: true);
                    
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
                }
                // If running during startup phase, confirm immediately
                if (isActuallyRunning)
                {
                    if (DebugMode) LogMessage($"Startup confirmed: Server service is active and running.", 1);
                    LogMessage("Server confirmed running!", 1);
                    isStartingUp = false;
                    serverStarted = true; // Explicitly confirm started state
                    SaveServerStateToSessionState(); // Persist state across domain reloads
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
                    SafeRepaint();
                    return;                }                // If grace period expires and still not running, assume failure
                else if (elapsedTime >= serverStartupGracePeriod)
                {
                    LogMessage($"Server failed to start within grace period ({serverStartupGracePeriod} seconds).", -1);
                    
                    // Before giving up, do one final service check to be absolutely sure
                    try
                    {
                        bool finalServiceCheck = await wslProcessor.CheckServerRunning(instantCheck: true);
                        if (finalServiceCheck)
                        {
                            LogMessage("Final service check shows server is actually running - recovering!", 1);
                            isStartingUp = false;
                            serverStarted = true;
                            SaveServerStateToSessionState(); // Persist state across domain reloads
                            serverConfirmedRunning = true;
                            justStopped = false;
                            consecutiveFailedChecks = 0;
                            logProcessor.SetServerRunningState(true);
                            SafeRepaint();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (DebugMode) LogMessage($"Final service check failed: {ex.Message}", -1);
                    }
                    
                    isStartingUp = false;
                    serverStarted = false;
                    SaveServerStateToSessionState(); // Persist state across domain reloads
                    serverConfirmedRunning = false;
                    justStopped = false; // Reset flag on failed start
                    consecutiveFailedChecks = 0; // Reset failure counter on failed start

                    // Update logProcessor state
                    logProcessor.SetServerRunningState(false);

                    if (serverProcess != null && !serverProcess.HasExited) { try { serverProcess.Kill(); } catch {} }
                    serverProcess = null;
                    
                    SafeRepaint();
                    return; // Failed, skip further checks
                }
                else
                {
                    // Still starting up, update UI and wait
                    if (DebugMode && elapsedTime % 2.0f < 0.1f) // Log every 2 seconds during startup
                    {
                        LogMessage($"Startup in progress... elapsed: {elapsedTime:F1}s / {serverStartupGracePeriod}s", 0);
                    }
                    SafeRepaint();
                    return;
                }
            }
            catch (Exception ex) {
                if (DebugMode) LogMessage($"Error during server status check: {ex.Message}", -1);
                SafeRepaint();
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
                if (Settings.serverMode == ServerMode.CustomServer)
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
                else if (Settings.serverMode == ServerMode.DockerServer)
                {
                    // Docker mode - check container and ping status
                    bool dockerServiceRunning = await dockerProcessor.IsDockerServiceRunning();
                    bool containerRunning = false;
                    bool pingResponding = false;
                    
                    if (dockerServiceRunning)
                    {
                        containerRunning = await dockerProcessor.CheckDockerProcessAsync(dockerServiceRunning);
                        if (containerRunning)
                        {
                            pingResponding = await PingServerStatusAsync();
                        }
                    }
                    
                    // Special handling for recently stopped servers
                    double timeSinceStop = EditorApplication.timeSinceStartup - stopInitiatedTime;
                    bool recentlyStopped = justStopped && timeSinceStop < 10.0;
                    
                    if (recentlyStopped)
                    {
                        // Require both container AND ping to be active for recently stopped servers
                        isActuallyRunning = containerRunning && pingResponding;
                        if (DebugMode)
                        {
                            LogMessage($"Docker status (recently stopped) - Container: {(containerRunning ? "running" : "not running")}, Ping: {(pingResponding ? "responding" : "not responding")}, Result: {(isActuallyRunning ? "running" : "stopped")}", 0);
                        }
                    }
                    else
                    {
                        // Normal operation: Server running if both container exists AND ping responds
                        isActuallyRunning = containerRunning && pingResponding;
                        if (DebugMode)
                        {
                            //LogMessage($"Docker status - Container: {(containerRunning ? "running" : "not running")}, Ping: {(pingResponding ? "responding" : "not responding")}, Result: {(isActuallyRunning ? "running" : "stopped")}", 0);
                        }
                    }
                }
                else // WSL mode
                {
                    // Use a combination of service check and HTTP ping for better reliability
                    bool serviceRunning = await wslProcessor.CheckServerRunning();
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
                        
                        string serverTypeDesc = Settings.serverMode == ServerMode.CustomServer ? "Custom Remote Server" : 
                                               Settings.serverMode == ServerMode.DockerServer ? "Docker Server" : "WSL Server";
                        if (DebugMode) LogMessage($"Server running confirmed ({serverTypeDesc})", 1);

                        // Update logProcessor state
                        logProcessor.SetServerRunningState(true);
                        
                        SafeRepaint();
                    }
                    else
                    {
                        // Server appears to have stopped - increment failure counter
                        consecutiveFailedChecks++;
                        
                        string serverTypeDesc = Settings.serverMode == ServerMode.CustomServer ? "Custom Remote Server" : 
                                               Settings.serverMode == ServerMode.DockerServer ? "Docker Server" : "WSL Server";
                        if (DebugMode) LogMessage($"Server check failed ({consecutiveFailedChecks}/{maxConsecutiveFailuresBeforeStop}) - {serverTypeDesc}", 0);
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
                                SafeRepaint();
                                return;
                            }
                            
                            string serverType = Settings.serverMode == ServerMode.CustomServer ? "Custom Remote Server" : 
                                                   Settings.serverMode == ServerMode.DockerServer ? "Docker Server" : "WSL Server";
                            string msg = $"SpacetimeDB Server confirmed stopped after {maxConsecutiveFailuresBeforeStop} consecutive failed checks and final ping verification ({serverType})";
                            if (DebugMode) LogMessage(msg, -2);
                            
                            // Update state
                            serverConfirmedRunning = false;
                            serverStarted = false;
                            SaveServerStateToSessionState(); // Persist state across domain reloads

                            // Update logProcessor state
                            logProcessor.SetServerRunningState(false);

                            if (DebugMode) LogMessage("Server state updated to stopped.", -1);
                            SafeRepaint();
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
                if (Settings.serverMode == ServerMode.CustomServer)
                {
                    await serverCustomProcess.CheckServerRunning(true);
                    isActuallyRunning = serverCustomProcess.cachedServerRunningStatus;
                }                
                else // WSL, Docker and other modes
                {
                    if (Settings.serverMode == ServerMode.DockerServer)
                    {
                        // Use Docker process check
                        bool dockerServiceRunning = await dockerProcessor.IsDockerServiceRunning();
                        bool processRunning = await dockerProcessor.CheckDockerProcessAsync(dockerServiceRunning);
                        bool pingResponding = false;
                        if (processRunning)
                        {
                            // If Docker container is running, verify with ping for complete confirmation
                            pingResponding = await PingServerStatusAsync();
                            isActuallyRunning = pingResponding; // Only consider it running if both container exists AND ping responds
                            
                            if (DebugMode)
                            {
                                LogMessage($"External recovery check - Docker Container: {(processRunning ? "running" : "not running")}, Ping: {(pingResponding ? "responding" : "not responding")}", 0);
                            }
                        }
                        else
                        {
                            isActuallyRunning = false;
                            if (DebugMode) LogMessage("External recovery check - No spacetimedb Docker container found", 0);
                        }
                    }
                    else // WSL mode
                    {
                        // Use both process check and ping for external recovery detection
                        bool processRunning = await wslProcessor.CheckWslProcessAsync(IsWslRunning);
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
                        bool confirmed = Settings.serverMode == ServerMode.CustomServer ? isActuallyRunning : isActuallyRunning;
                        
                        if (confirmed)
                        {                            
                            // Detected server running, probably it was already running when Unity started
                            string serverTypeDesc = Settings.serverMode == ServerMode.CustomServer ? "Custom Remote Server" : 
                                                   Settings.serverMode == ServerMode.DockerServer ? "Docker Server" : "WSL Server";
                            if (debugMode) LogMessage($"Detected SpacetimeDB running ({serverTypeDesc})", 1);
                            serverStarted = true;
                            SaveServerStateToSessionState(); // Persist state across domain reloads
                            serverConfirmedRunning = true;
                            isStartingUp = false;
                            justStopped = false; // Ensure flag is clear if we recover state
                            consecutiveFailedChecks = 0; // Reset failure counter on external recovery

                            // Update logProcessor state
                            logProcessor.SetServerRunningState(true);
                            
                            // If we detected a custom server is running and we are in silent mode,
                            // start the SSH log processors
                            if (Settings.serverMode == ServerMode.CustomServer && isActuallyRunning && silentMode && logProcessor != null)
                            {
                                // Extract hostname from CustomServerUrl
                                string sshHost = ServerUtilityProvider.ExtractHostname(CustomServerUrl);
                                
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

                        SafeRepaint();
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

            // Publish and Generate uses the local CLI and is not run on SSH, so this is for all other SSH commands
            // Also exclude "spacetime server set-default" commands as these should always run locally
            if (Settings.serverMode == ServerMode.CustomServer && !command.Contains("spacetime publish") && !command.Contains("spacetime generate") && !command.Contains("spacetime server set-default"))
            {
                var result = await serverCustomProcess.RunSpacetimeDBCommandAsync(command); // SSH command
                
                // Display the results in the output log
                if (!string.IsNullOrEmpty(result.output))
                {
                    // Check if this is login info output and apply color formatting
                    string formattedOutput = ServerUtilityProvider.FormatLoginInfoOutput(result.output);
                    LogMessage(formattedOutput, 0);
                }
                
                // Most results will be output here
                if (!string.IsNullOrEmpty(result.error))
                {
                    // To be added
                }
                
                if (string.IsNullOrEmpty(result.output) && string.IsNullOrEmpty(result.error))
                {
                    if (debugMode) LogMessage("Command completed with no output.", 0);
                    // Don't reset publishing flag for custom server SSH commands (these are not publish/generate operations)
                }
                
                // Marked as success if the command was run, but may yet contain errors
                if (result.success)
                {
                    // To be added
                } 
                else // if !result.success
                {
                    if (debugMode) LogMessage("Custom Remote mode command failed to execute.", -2);
                }
            }
            else // WSL local commands, Docker commands, Custom Remote publish or Maincloud commands
            {
                // Errors which causes the command to fail may be needed to be procesed directly in RunServerCommandAsync
                var result = Settings.serverMode == ServerMode.DockerServer 
                    ? await dockerProcessor.RunServerCommandAsync(command, ServerDirectory)
                    : await wslProcessor.RunServerCommandAsync(command, ServerDirectory);
                
                if (!string.IsNullOrEmpty(result.output))
                {
                    // Check if this is login info output and apply color formatting
                    string formattedOutput = ServerUtilityProvider.FormatLoginInfoOutput(result.output);
                    LogMessage(formattedOutput, 0);
                    if (debugMode) UnityEngine.Debug.Log("Command output: " + formattedOutput);
                }
                
                if (!string.IsNullOrEmpty(result.error))
                {
                    // Filter out formatting errors for generate commands that don't affect functionality
                    bool isGenerateCmd = command.Contains("spacetime generate");
                    string filteredError = isGenerateCmd ? ServerUtilityProvider.FilterGenerateErrors(result.error) : result.error;
                    
                    if (!string.IsNullOrEmpty(filteredError))
                    {
                        LogMessage(filteredError, -2);
                        // Only reset publishing flag for publish/generate command errors
                        bool isPublishOrGenerateCmd = command.Contains("spacetime publish") || command.Contains("spacetime generate");
                        if (isPublishOrGenerateCmd)
                        {
                            publishing = false;
                        }
                    }
                }
                
                if (string.IsNullOrEmpty(result.output) && string.IsNullOrEmpty(result.error))
                {
                    if (debugMode) LogMessage("Command completed with no output.", 0);
                    // Only reset publishing flag for publish/generate commands with no output (likely an error)
                    bool isPublishOrGenerateCmd = command.Contains("spacetime publish") || command.Contains("spacetime generate");
                    if (isPublishOrGenerateCmd)
                    {
                        publishing = false;
                    }
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
                    else if (isGenerateCommand && description == "Generating Unity files")
                    {
                        GenerateResult(result.output, result.error, true);
                    }
                    else if (isGenerateCommand && description == "Generating Unity files (Publish failed)")
                    {
                        GenerateResult(result.output, result.error, false);
                    }
                }
                else // if !result.success
                {
                    if (isPublishCommand)
                    {
                        LogMessage("Publishing failed!", -1);
                        publishing = false;
                    }
                    else if (isGenerateCommand)
                    {
                        LogMessage("Generating Unity files failed!", -1);
                        publishing = false;
                    }
                    else
                    {
                        LogMessage("Command failed to execute.", -2);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Error running command: {ex.Message}", -1);
            // Only reset publishing flag for publish/generate command exceptions
            bool isPublishOrGenerateCmd = command.Contains("spacetime publish") || command.Contains("spacetime generate");
            if (isPublishOrGenerateCmd)
            {
                publishing = false;
            }
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

        // Set publishing flag to true at the start of the publish process
        publishing = true;

        // Always trim trailing slashes from CustomServerUrl for all usages
        string customServerUrl = !string.IsNullOrEmpty(CustomServerUrl) ? CustomServerUrl.TrimEnd('/') : "";
        
        if (resetDatabase)
        {
            if (Settings.serverMode == ServerMode.MaincloudServer)
                RunServerCommand($"spacetime publish --server maincloud {ModuleName} --delete-data -y", $"Publishing module '{ModuleName}' and resetting database");
            else if (Settings.serverMode == ServerMode.CustomServer)
                RunServerCommand($"spacetime publish --server {customServerUrl} {ModuleName} --delete-data -y", $"Publishing module '{ModuleName}' and resetting database");
            else if (Settings.serverMode == ServerMode.WSLServer || Settings.serverMode == ServerMode.DockerServer)
                RunServerCommand($"spacetime publish --server local {ModuleName} --delete-data -y", $"Publishing module '{ModuleName}' and resetting database");
        }
        else
        {
            if (Settings.serverMode == ServerMode.MaincloudServer)
                RunServerCommand($"spacetime publish --server maincloud {ModuleName} -y", $"Publishing module '{ModuleName}' to Maincloud");
            else if (Settings.serverMode == ServerMode.CustomServer)
                RunServerCommand($"spacetime publish --server {customServerUrl} {ModuleName} -y", $"Publishing module '{ModuleName}' to Custom Server at '{customServerUrl}'");
            else if (Settings.serverMode == ServerMode.WSLServer || Settings.serverMode == ServerMode.DockerServer)
                RunServerCommand($"spacetime publish --server local {ModuleName} -y", $"Publishing module '{ModuleName}' to Local");
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

            successfulPublish = false;
        }

        if (error.Contains("Identity") && error.Contains("is not valid"))
        {
            if (serverMode == ServerMode.WSLServer || serverMode == ServerMode.DockerServer)
            EditorUtility.DisplayDialog("Invalid Identity", 
            "Please try to Logout and then Login again on your local mode and then copy and paste the new auth token into your Pre-Requisites."
            ,"OK");
            else if (serverMode == ServerMode.CustomServer)
            EditorUtility.DisplayDialog("Invalid Identity",
            "Please try to Logout and then Login again on your both your local mode and Custom Server mode and then copy and paste each new auth token into their respective fields in your Pre-Requisites."
            ,"OK");
            successfulPublish = false;
        }

        if (error.Contains("Permission denied"))
        {
            EditorUtility.DisplayDialog("Permission Denied", 
            "Your currently logged in user does not have permission to publish this module to this server.\n" + 
            "Either log out and login with the user that originally created the module OR\n" +
            "Run the Clear Server Data command which clears the database for this module and allows you to republish with the currently logged in user."
            ,"OK");
            successfulPublish = false;
        }

        if (error.Contains("error sending request for url"))
        {
            EditorUtility.DisplayDialog("Server Not Found", 
            "Could not find a server running at the specified URL.\n" + 
            "Please check that the URL is correct and that your server is running and accessible from your network."
            ,"OK");
            successfulPublish = false;
        }

        // If the output contains the word error and isn't compiling the publish probably has failed. Excluded initial downloading since some packages may contain the word error.
        if (!string.IsNullOrEmpty(error) && error.Contains("error", StringComparison.OrdinalIgnoreCase) && !error.Contains("downloaded", StringComparison.OrdinalIgnoreCase) && !error.Contains("compiling", StringComparison.OrdinalIgnoreCase))
        {
            successfulPublish = false;
        }

        // Go on to Auto-Generate if mode is enabled (continues even if unsuccessful to get all logs)
        if (PublishAndGenerateMode)
        {
            if (successfulPublish)
            {
                LogMessage("Publish successful, automatically generating Unity files...", 0);
                string outDir = ServerUtilityProvider.GetRelativeClientPath(ClientDirectory, CurrentServerMode.ToString());
                RunServerCommand($"spacetime generate --out-dir {outDir} --lang {UnityLang} -y", "Generating Unity files");
            }
            else // If unsuccessful Publish
            {
                LogMessage("Publish failed, automatically generating Unity files to capture all logs...", 0);
                string outDir = ServerUtilityProvider.GetRelativeClientPath(ClientDirectory, CurrentServerMode.ToString());
                RunServerCommand($"spacetime generate --out-dir {outDir} --lang {UnityLang} -y", "Generating Unity files (Publish failed)");
            }
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

    private async void GenerateResult(string output, string error, bool publishSuccessful)
    {
        LogMessage("Waiting for generated files to be fully written...", 0);
        await Task.Delay(3000); // Wait 3 seconds for files to be fully generated
        if (!string.IsNullOrEmpty(error) && error.Contains("Error") || !publishSuccessful)
        {
            LogMessage("Publish and Generate failed! Attempted to anyhow generate the client files to capture all the error logs.", -1);
        }
        else
        {
            LogMessage("Requesting script compilation...", 0);
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
            LogMessage("Publish and Generate successful!", 1);
        }
        
        // Reset publishing flag when generation is complete
        publishing = false;
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
        else if (Settings.serverMode == ServerMode.CustomServer)
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

        if (Settings.serverMode == ServerMode.DockerServer)
        {
            // For Docker, the directory is mounted into the container, use the mount path directly
            string command = $"cd /app && spacetime init --lang {ServerLang} .";
            dockerProcessor.RunDockerCommandSilent($"exec {ServerDockerProcess.ContainerName} bash -c \"{command}\"");
            LogMessage("New module initialized", 1);
        }
        else // WSL mode
        {
            string wslPath = wslProcessor.GetWslPath(ServerDirectory);
            // Combine cd and init command
            string command = $"cd \"{wslPath}\" && spacetime init --lang {ServerLang} .";
            wslProcessor.RunWslCommandSilent(command);
            LogMessage("New module initialized", 1);
        }
        
        // Reset the detection process tracking
        if (detectionProcess != null)
        {
            detectionProcess.ResetTracking();
            ServerChangesDetected = false;
        }
    }

    public void ViewServerLogs()
    {
        if (SilentMode || Settings.serverMode == ServerMode.CustomServer)
        {
            if (DebugMode) LogMessage("Opening/focusing silent server output window...", 0);
            if (Settings.serverMode == ServerMode.MaincloudServer)
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
                string wslPath = wslProcessor.GetWslPath(ServerDirectory);
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
        wslProcessor.OpenDebianWindow(userNameReq);
    }

    public void OpenDockerWindow()
    {
        dockerProcessor.OpenDockerWindow();
    }

    public bool PingServerStatus()
    {
        PingServer(false);
        return pingShowsOnline;
    }
    
    public async Task<bool> PingServerStatusAsync() // For the status checks
    {
        string url;
        if (Settings.serverMode == ServerMode.CustomServer)
        {
            url = !string.IsNullOrEmpty(CustomServerUrl) ? CustomServerUrl : "";
        }
        else if (Settings.serverMode == ServerMode.MaincloudServer)
        {
            url = !string.IsNullOrEmpty(Settings.maincloudUrl) ? Settings.maincloudUrl : "https://maincloud.spacetimedb.com/";
        }
        else // Docker or WSL local server // We don't use ServerUrlDocker since the server pings itself at port 3000 regardless of the external mapping
        {
            url = !string.IsNullOrEmpty(ServerUrl) ? ServerUrl : "http://127.0.0.1:3000";
        }

        if (url.EndsWith("/"))
        {
            url = url.TrimEnd('/');
        }

        try
        {
            // Use the appropriate processor based on LocalCLIProvider
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            if (LocalCLIProvider == "Docker")
            {
                dockerProcessor.PingServer(url, (isOnline, message) => {
                    tcs.TrySetResult(isOnline);
                });
            }
            else
            {
                wslProcessor.PingServer(url, (isOnline, message) => {
                    tcs.TrySetResult(isOnline);
                });
            }
            
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

    public void PingServer(bool showLog) // For manual ping
    {
        string url;
        if (Settings.serverMode == ServerMode.CustomServer)
        {
            url = !string.IsNullOrEmpty(CustomServerUrl) ? CustomServerUrl : "";
        }
        else if (Settings.serverMode == ServerMode.MaincloudServer)
        {
            url = !string.IsNullOrEmpty(Settings.maincloudUrl) ? Settings.maincloudUrl : "https://maincloud.spacetimedb.com/";
        }
        else // Docker or WSL local server // We don't use ServerUrlDocker since the server pings itself at port 3000 regardless of the external mapping
        {
            url = !string.IsNullOrEmpty(ServerUrl) ? ServerUrl : "http://127.0.0.1:3000";
        }

        if (url.EndsWith("/"))
        {
            url = url.TrimEnd('/');
        }
        if (DebugMode) LogMessage($"Pinging server at {url}...", 0);

        if (LocalCLIProvider == "Docker"){
            dockerProcessor.PingServer(url, (isOnline, message) => {
                EditorApplication.delayCall += () => {
                    if (isOnline && Settings.serverMode == ServerMode.DockerServer)
                    {
                        if (showLog) LogMessage($"Server is online: {url} \n External mapping set to {ServerUrlDocker}", 1);
                        pingShowsOnline = true;
                    }
                    else if (isOnline && Settings.serverMode == ServerMode.CustomServer)
                    {
                        if (showLog) LogMessage($"Server is online: {url}", 1);
                        pingShowsOnline = true;
                    }
                    else
                    {
                        if (showLog) LogMessage($"Server is offline: {message} \n External mapping set to {ServerUrlDocker}", -1);
                        pingShowsOnline = false;
                    }
                    
                    SafeRepaint();
                };
            });
        }
        else if (LocalCLIProvider == "WSL"){
            wslProcessor.PingServer(url, (isOnline, message) => {
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
                    
                    SafeRepaint();
                };
            });
        }
    }

    public void BackupServerData()
    {
        versionProcessor.BackupServerData(BackupDirectory, UserName);
    }

    public void ClearServerData()
    {
        versionProcessor.ClearServerData(UserName);
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
            if (Settings.serverMode == ServerMode.WSLServer || Settings.serverMode == ServerMode.DockerServer)
            {
                // Configure WSL/Docker and start logging for journalctl-based approach
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
                    await CheckSpacetimeSDKVersion();
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

    public async Task CheckDockerStatus()
    {
        // Only check periodically to avoid excessive checks
        double currentTime = EditorApplication.timeSinceStartup;
        if (currentTime - lastWslCheckTime < wslCheckInterval) // Reuse the same interval
            return;
        
        lastWslCheckTime = currentTime; // Update the cache time
        
        try
        {
            if (dockerProcessor == null)
            {
                isDockerRunning = false;
                return;
            }
            
            // Check if Docker service is running
            bool dockerServiceRunning = await dockerProcessor.IsDockerServiceRunning();
            
            // Update running status
            if (isDockerRunning != dockerServiceRunning)
            {
                isDockerRunning = dockerServiceRunning;
                if (debugMode) LogMessage($"Docker status updated to: {(isDockerRunning ? "Running" : "Stopped")}", 0);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) LogMessage($"Exception in CheckDockerStatus: {ex.Message}", -1);
            isDockerRunning = false;
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
                        SafeRepaint();
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
                            SafeRepaint();
                        };
                    }
                    else
                    {
                        EditorApplication.delayCall += () =>
                        {
                            cachedSSHConnectionStatus = false;
                            SafeRepaint();
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
                SafeRepaint();
                return;
            }

            string url = Settings.maincloudUrl;
            if (!url.EndsWith("/")) url += "/";
            url += $"v1/database/{moduleName}/schema?version=9";
            
            if (DebugMode) LogMessage($"Checking Maincloud connectivity at {url}...", 0);
            
            // Create a HttpClient instance with timeout
            using (var httpClient = new System.Net.Http.HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                // Add authorization header if token exists
                if (!string.IsNullOrEmpty(Settings.maincloudAuthToken))
                {
                    httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Settings.maincloudAuthToken);
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
                    SafeRepaint();
                }
            }
        }
        catch (Exception ex)
        {
            if (DebugMode) LogMessage($"Exception in CheckMaincloudConnectivity: {ex.Message}", -1);
            isMaincloudConnected = false;
            SafeRepaint();
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

    public void ResetServerStatusOnModeChange()
    {
        // Reset server status flags to prevent carrying over status from previous mode
        serverConfirmedRunning = false;
        serverStarted = false;
        isStartingUp = false;
        justStopped = false;
        pingShowsOnline = false;
        consecutiveFailedChecks = 0;
        
        // Update logProcessor state
        if (logProcessor != null)
        {
            logProcessor.SetServerRunningState(false);
        }
        
        // Save the reset state
        SaveServerStateToSessionState();
        
        if (DebugMode) LogMessage("Server status reset for server mode change", 0);
    }

    public void OpenSSHWindow()
    {
        // Extract just the IP/hostname from the CustomServerUrl (remove protocol, port, and path)
        string serverHost = ServerUtilityProvider.ExtractHostname(CustomServerUrl);
        
        // Validate that we have all required values
        if (string.IsNullOrEmpty(serverHost) || string.IsNullOrEmpty(SSHUserName))
        {
            LogMessage("Cannot open SSH window: Missing server host or username", -1);
            return;
        }
        
        // Construct the SSH command with proper escaping
        string sshCommand = $"ssh {SSHUserName}@{serverHost}";
        
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
        if (Settings.serverMode == ServerMode.WSLServer)
        {
            await CheckWslStatus();
            await CheckServerStatus();
            
            // Check WSL journalctl log processes only if editor has focus
            if (serverStarted && silentMode && logProcessor != null && hasEditorFocus)
            {
                logProcessor.CheckLogProcesses(EditorApplication.timeSinceStartup);
            }
        }
        else if (Settings.serverMode == ServerMode.DockerServer)
        {
            await CheckDockerStatus();
            await CheckServerStatus();
            
            // Check Docker log processes only if editor has focus
            if (serverStarted && silentMode && logProcessor != null && hasEditorFocus)
            {
                logProcessor.CheckLogProcesses(EditorApplication.timeSinceStartup);
            }
        }
        else if (Settings.serverMode == ServerMode.CustomServer)
        {
            await CheckServerStatus();
            
            // Check SSH log processes for custom server mode only if editor has focus
            if (serverStarted && silentMode && logProcessor != null && hasEditorFocus)
            {
                logProcessor.CheckSSHLogProcesses(EditorApplication.timeSinceStartup);
            }
        }
        else if (Settings.serverMode == ServerMode.MaincloudServer)
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
            CCCPSettingsAdapter.SetEchoToConsole(echoToConsole);
        }
    }
    #endregion

    #region Spacetime Version

    public async Task CheckSpacetimeDBVersion() // Only runs in WSL once when WSL has started
    {
        if (debugMode) LogMessage("Checking SpacetimeDB version...", 0);
        
        // Only proceed if enough prerequisites are met
        if (!hasWSL || !hasDebianTrixie || !hasSpacetimeDBServer || !hasSpacetimeDBPath)
        {
            if (debugMode) LogMessage("Skipping SpacetimeDB version check - prerequisites not met", 0);
            return;
        }
        
        // Use RunServerCommandAsync to run the spacetime --version command (mark as status check for silent mode)
        var result = await wslProcessor.RunServerCommandAsync("spacetime --version", serverDirectory, true);
        
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

        // Also save the tool version for cargo.toml version update in Setup Window
        System.Text.RegularExpressions.Match toolMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"spacetimedb tool version ([0-9]+\.[0-9]+\.[0-9]+)");

        if (toolMatch.Success && toolMatch.Groups.Count > 1)
        {
            toolversion = toolMatch.Groups[1].Value;
            CCCPSettingsAdapter.SetSpacetimeDBCurrentVersionTool(toolversion);
            spacetimeDBCurrentVersionTool = toolversion;

            if (debugMode) LogMessage($"Detected SpacetimeDB tool version from output: {toolversion}", 1);
        }

        if (!string.IsNullOrEmpty(version))
        {
            CCCPSettingsAdapter.SetSpacetimeDBCurrentVersion(version);

            spacetimeDBCurrentVersion = version;

            // Check if update is available by comparing with the latest version from ServerUpdateProcess
            spacetimeDBLatestVersion = CCCPSettingsAdapter.GetSpacetimeDBLatestVersion();
            if (!string.IsNullOrEmpty(spacetimeDBLatestVersion) && version != spacetimeDBLatestVersion)
            {
                // Only show the update message once per editor session (persists across script recompilations)
                if (!SessionState.GetBool("SpacetimeDBWSLUpdateMessageShown", false))
                {
                    LogMessage($"SpacetimeDB update available for WSL! Click on the Setup Window update button to install. Current version: {version} and latest version: {spacetimeDBLatestVersion}", 1);
                    SessionState.SetBool("SpacetimeDBWSLUpdateMessageShown", true);
                }
                CCCPSettingsAdapter.SetSpacetimeDBUpdateAvailable(true);
            }
        }
        else
        {
            if (debugMode) LogMessage("Could not parse SpacetimeDB version from output", -1);
        }
    }
    
    public async Task CheckSpacetimeSDKVersion() // Check for SDK updates when WSL starts
    {
        if (debugMode) LogMessage("Checking SpacetimeDB SDK version...", 0);
        
        // First check if SDK is installed using the installer
        bool isSDKInstalled = false;
        var taskCompletionSource = new TaskCompletionSource<bool>();
        
        ServerSpacetimeSDKInstaller.IsSDKInstalled((isInstalled) =>
        {
            isSDKInstalled = isInstalled;
            taskCompletionSource.SetResult(isInstalled);
        });
        
        // Wait for the callback to complete
        await taskCompletionSource.Task;
        
        if (!isSDKInstalled)
        {
            if (debugMode) LogMessage("SpacetimeDB SDK is not installed - skipping version check", 0);
            return;
        }
        
        if (debugMode) LogMessage("SpacetimeDB SDK is installed, checking for updates...", 0);
        
        // Get current and latest versions from ServerUpdateProcess
        string currentSDKVersion = ServerUpdateProcess.GetCurrentSpacetimeSDKVersion();
        string latestSDKVersion = ServerUpdateProcess.SpacetimeSDKLatestVersion();
        bool updateAvailable = ServerUpdateProcess.IsSpacetimeSDKUpdateAvailable();
        
        if (!string.IsNullOrEmpty(currentSDKVersion))
        {
            if (debugMode) LogMessage($"Current SpacetimeDB SDK version: {currentSDKVersion}", 1);
            
            if (updateAvailable && !string.IsNullOrEmpty(latestSDKVersion) && currentSDKVersion != latestSDKVersion)
            {
                LogMessage($"SpacetimeDB SDK update available for Unity! Click on the Setup Window update button to install. Current version: {currentSDKVersion} and latest version: {latestSDKVersion}", 1);
            }
            else if (!string.IsNullOrEmpty(latestSDKVersion))
            {
                if (debugMode) LogMessage($"SpacetimeDB SDK is up to date (version {currentSDKVersion})", 1);
            }
        }
        else
        {
            if (debugMode) LogMessage("Could not determine current SpacetimeDB SDK version", -1);
        }
    }
    #endregion
    
    #region Rust Version
    public async Task CheckRustVersion() // Only runs in WSL once when WSL has started
    {
        if (debugMode) LogMessage("Checking Rust version...", 0);
        
        // Only proceed if enough prerequisites are met
        if (!hasWSL || !hasDebianTrixie || !hasRust)
        {
            if (debugMode) LogMessage("Skipping Rust version check - prerequisites not met or Rust not installed", 0);
            return;
        }
        
        // Use RunServerCommandAsync to run the rustup check command (mark as status check for silent mode)
        var result = await wslProcessor.RunServerCommandAsync("rustup check", serverDirectory, true);
        
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
                    CCCPSettingsAdapter.SetRustLatestVersion(latestVersion);
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
                CCCPSettingsAdapter.SetRustLatestVersion("");
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
        
        // Save versions to Settings and local variables
        if (!string.IsNullOrEmpty(rustStableVersion))
        {
            CCCPSettingsAdapter.SetRustCurrentVersion(rustStableVersion);
            rustCurrentVersion = rustStableVersion;
            
            if (rustUpdateAvailable)
            {
                LogMessage($"Rust update available for WSL! Click on the Setup Window update button to install. Current version: {rustCurrentVersion} and latest version: {rustLatestVersion}", 1);
                CCCPSettingsAdapter.SetRustUpdateAvailable(true);
            }
            else
            {
                CCCPSettingsAdapter.SetRustUpdateAvailable(false);
            }
        }
        
        if (!string.IsNullOrEmpty(rustupCurrentVersion))
        {
            CCCPSettingsAdapter.SetRustupVersion(rustupCurrentVersion);
            rustupVersion = rustupCurrentVersion;
            
            if (rustupUpdateAvailable)
            {
                //LogMessage($"Rustup update available for WSL! Click on the Setup Window update button to install the latest version: {rustupCurrentVersion}", 1);
                CCCPSettingsAdapter.SetRustupUpdateAvailable(true);
            }
            else
            {
                CCCPSettingsAdapter.SetRustupUpdateAvailable(false);
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
            
            if (Settings.serverMode == ServerMode.WSLServer || Settings.serverMode == ServerMode.MaincloudServer)
            {
                logProcessor.CheckLogProcesses(currentTime);
            }
            else if (Settings.serverMode == ServerMode.CustomServer)
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
