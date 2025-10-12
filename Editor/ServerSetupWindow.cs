using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using NorthernRogue.CCCP.Editor.Settings;

// Check and setup everything necessary to run SpacetimeDB with this window ///

namespace NorthernRogue.CCCP.Editor {

public class ServerSetupWindow : EditorWindow
{
    public static bool debugMode = false; // Set in ServerWindow

    internal List<InstallerItem> installerItems = new List<InstallerItem>();
    internal List<InstallerItem> customInstallerItems = new List<InstallerItem>();
    internal List<InstallerItem> dockerInstallerItems = new List<InstallerItem>();
    internal ServerWSLProcess wslProcess;
    internal ServerCustomProcess customProcess;
    internal ServerManager serverManager;

    // UI
    private Vector2 scrollPosition;
    internal string statusMessage = "Ready to install components.";
    private bool userNamePrompt = false;
    private bool showUpdateButton = false;
    internal Color statusColor = Color.grey;
    internal string statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
    private double lastRepaintTime = 0;
    private const double minRepaintInterval = 0.5; // Minimum time between repaints in seconds
    
    // Tab selection
    private int currentTab; // 0 = WSL Installer, 1 = Custom Debian Installer, 2 = Docker Setup
    private string[] tabNames = new string[] { "WSL Local Setup", "Custom Remote Setup", "Docker Setup" };
    private bool isAssetStoreBuild => ServerUpdateProcess.IsAssetStoreVersion();
    private bool isGithubBuild => ServerUpdateProcess.IsGithubVersion();
    
    // Tab indices - always 3 tabs
    private int dockerTabIndex => 0;
    private int wslTabIndex => 1;
    private int customTabIndex => 2;

    // Settings access - no longer store in private variables
    private CCCPSettings Settings => CCCPSettings.Instance;

    // Convenience properties for frequently accessed settings in this window
    internal string userName => Settings.userName;
    internal string sshUserName => Settings.sshUserName;
    internal string serverDirectory => Settings.serverDirectory;

    // Temporary fields for username input
    private string tempUserNameInput = "";
    internal string tempCreateUserNameInput = ""; // For creating new user on remote SSH server

    internal string spacetimeDBCurrentVersion = "";
    internal string spacetimeDBCurrentVersionCustom = "";
    internal string spacetimeDBCurrentVersionTool = "";
    internal string spacetimeDBLatestVersion = "";
    
    internal string rustCurrentVersion = "";
    internal string rustLatestVersion = "";
    internal string rustupVersion = "";
    
    internal bool rustUpdateAvailable = false;
    
    // SpacetimeDB SDK version tracking
    internal string spacetimeSDKCurrentVersion = "";
    internal string spacetimeSDKLatestVersion = "";
    internal bool spacetimeSDKUpdateAvailable = false;
    
    // Styles
    private GUIStyle titleStyle;
    private GUIStyle itemTitleStyle;
    private GUIStyle installedStyle;
    private GUIStyle installButtonStyle;
    private GUIStyle sectionHeaderStyle;
    private bool stylesInitialized = false;
    
    // WSL Installation states
    private bool isRefreshing = false;
    internal bool hasWSL = false;
    internal bool hasDebian = false;
    internal bool hasDebianTrixie = false;
    internal bool hasCurl = false;
    internal bool hasSpacetimeDBServer = false;
    internal bool hasSpacetimeDBPath = false;
    internal bool hasSpacetimeDBService = false;
    internal bool hasSpacetimeDBLogsService = false;
    internal bool hasRust = false;
    internal bool hasNETSDK = false;
    internal bool hasBinaryen = false;
    internal bool hasGit = false;
    internal bool hasSpacetimeDBUnitySDK = false;

    // Custom SSH installation states
    private bool isCustomRefreshing = false;
    internal bool hasCustomDebianUser = false;
    internal bool hasCustomDebianTrixie = false;
    internal bool hasCustomCurl = false;
    internal bool hasCustomSpacetimeDBServer = false;
    internal bool hasCustomSpacetimeDBPath = false;
    internal bool hasCustomSpacetimeDBService = false;
    internal bool hasCustomSpacetimeDBLogsService = false;

    private bool isConnectedSSH = false;
    
    // Docker installation states
    private bool isDockerRefreshing = false;
    internal bool hasDocker = false;
    internal bool hasDockerCompose = false;
    internal bool hasDockerImage = false;
    internal bool hasDockerContainerMountsConfigured = false;
    internal ServerDockerProcess dockerProcess;
    
    // WSL 1 requires unique install logic for Debian apps
    internal bool WSL1Installed;

    // Debug install process
    internal bool visibleInstallProcesses = true;
    internal bool keepWindowOpenForDebug = true;
    internal bool alwaysShowInstall = false;
    internal bool installIfAlreadyInstalled = false;
    internal bool forceInstall = false; // Will toggle both alwaysShowInstall and installIfAlreadyInstalled
    
    // Settings
    internal bool updateCargoToml = false;
    
    // Install process handler - only for non-Asset Store builds
    private ServerInstallProcess installProcess;

    [MenuItem("Window/SpacetimeDB Server Manager/2. Setup Window")]
    public static void ShowWindow()
    {
        ServerSetupWindow window = GetWindow<ServerSetupWindow>("Server Setup");
        window.minSize = new Vector2(500, 400);
        window.InitializeTabNames();
        window.currentTab = 0; // Default to first tab
        window.InitializeInstallerItems();
        window.CheckInstallationStatus();
    }
    
    public static void ShowCustomWindow()
    {
        ServerSetupWindow window = GetWindow<ServerSetupWindow>("Server Setup");
        window.minSize = new Vector2(500, 400);
        window.currentTab = 2; // Custom tab is always index 2
        window.InitializeInstallerItems();
        window.CheckCustomInstallationStatus();
    }
    
    public static void ShowDockerWindow()
    {
        ServerSetupWindow window = GetWindow<ServerSetupWindow>("Server Setup");
        window.minSize = new Vector2(500, 400);
        window.currentTab = 0; // Docker tab is always index 0
        window.InitializeInstallerItems();
        window.CheckDockerPrerequisites();
    }

    private void InitializeTabNames()
    {
        // Always show all three tabs
        tabNames = new string[] { "Docker Local Setup", "WSL Local Setup", "Custom Remote Setup" };
    }

    #region OnEnable
    private void OnEnable()
    {
        // Initialize both processes
        wslProcess = new ServerWSLProcess(LogMessage, false);
        customProcess = new ServerCustomProcess(LogMessage, false);
        dockerProcess = new ServerDockerProcess(LogMessage, false);
        serverManager = new ServerManager(LogMessage, () => Repaint());
        
        // Initialize install process (only for non-Asset Store builds)
        if (!isAssetStoreBuild)
        {
            installProcess = new ServerInstallProcess(this);
        }
        
        // Check if this is the first time the window is opened
        /*if (CCCPSettingsAdapter.GetFirstTimeOpenInstaller())
        {
            // Show first-time information dialog
            EditorApplication.delayCall += () => {
                bool continuePressed = EditorUtility.DisplayDialog(
                    "SpacetimeDB Setup Window",
                    "Welcome to the setup window that checks and installs everything needed for your Windows PC to run SpacetimeDB from the ground up.\n\n" +
                    "All named software in this window is official and publicly available software owned by their respective parties.\n" +
                    "By proceeding, you agree to the terms and licenses of each installed component. For detailed licensing information, see 'Third Party Notices.md' in your package folder.\n" +
                    "A manual installation process is in the documentation.",
                    "Continue", "Documentation");
                
                if (!continuePressed) {
                    Application.OpenURL(ServerWindow.Documentation);
                }

                CCCPSettingsAdapter.SetFirstTimeOpenInstaller(false);
            };
        }*/
        
        // Load WSL installation status from Settings
        hasWSL = CCCPSettingsAdapter.GetHasWSL();
        hasDebian = CCCPSettingsAdapter.GetHasDebian();
        hasDebianTrixie = CCCPSettingsAdapter.GetHasDebianTrixie();
        hasCurl = CCCPSettingsAdapter.GetHasCurl();
        hasSpacetimeDBServer = CCCPSettingsAdapter.GetHasSpacetimeDBServer();
        hasSpacetimeDBPath = CCCPSettingsAdapter.GetHasSpacetimeDBPath();
        hasSpacetimeDBService = CCCPSettingsAdapter.GetHasSpacetimeDBService();
        hasSpacetimeDBLogsService = CCCPSettingsAdapter.GetHasSpacetimeDBLogsService();
        hasRust = CCCPSettingsAdapter.GetHasRust();
        hasNETSDK = CCCPSettingsAdapter.GetHasNETSDK();
        hasBinaryen = CCCPSettingsAdapter.GetHasBinaryen();
        hasGit = CCCPSettingsAdapter.GetHasGit();
        hasSpacetimeDBUnitySDK = CCCPSettingsAdapter.GetHasSpacetimeDBUnitySDK();

        // Load Custom SSH installation status from Settings
        hasCustomDebianUser = CCCPSettingsAdapter.GetHasCustomDebianUser();
        hasCustomDebianTrixie = CCCPSettingsAdapter.GetHasCustomDebianTrixie();
        hasCustomCurl = CCCPSettingsAdapter.GetHasCustomCurl();
        hasCustomSpacetimeDBServer = CCCPSettingsAdapter.GetHasCustomSpacetimeDBServer();
        hasCustomSpacetimeDBPath = CCCPSettingsAdapter.GetHasCustomSpacetimeDBPath();
        hasCustomSpacetimeDBService = CCCPSettingsAdapter.GetHasCustomSpacetimeDBService();
        hasCustomSpacetimeDBLogsService = CCCPSettingsAdapter.GetHasCustomSpacetimeDBLogsService();

        // Load Docker installation status from Settings
        hasDocker = CCCPSettingsAdapter.GetHasDocker();
        hasDockerCompose = CCCPSettingsAdapter.GetHasDockerCompose();
        hasDockerImage = CCCPSettingsAdapter.GetHasDockerImage();

        // WSL 1 requires unique install logic for Debian apps
        WSL1Installed = CCCPSettingsAdapter.GetWSL1Installed();

        // Load install debug settings and other settings from Settings
        visibleInstallProcesses = CCCPSettingsAdapter.GetVisibleInstallProcesses();
        keepWindowOpenForDebug = CCCPSettingsAdapter.GetKeepWindowOpenForDebug();
        updateCargoToml = CCCPSettingsAdapter.GetUpdateCargoToml();

        // Cache the current username from Settings
        tempUserNameInput = CCCPSettingsAdapter.GetUserName(); // Initialize the temp input with the stored username for WSL
        tempCreateUserNameInput = ""; // Initialize empty for the "Create User" functionality
        
        // Load version info of SpacetimeDB
        spacetimeDBCurrentVersion = CCCPSettingsAdapter.GetSpacetimeDBCurrentVersion();
        spacetimeDBCurrentVersionCustom = CCCPSettingsAdapter.GetSpacetimeDBCurrentVersionCustom();
        spacetimeDBCurrentVersionTool = CCCPSettingsAdapter.GetSpacetimeDBCurrentVersionTool();
        spacetimeDBLatestVersion = CCCPSettingsAdapter.GetSpacetimeDBLatestVersion();

        // Load version info of Rust
        rustCurrentVersion = CCCPSettingsAdapter.GetRustCurrentVersion();
        rustLatestVersion = CCCPSettingsAdapter.GetRustLatestVersion();
        rustupVersion = CCCPSettingsAdapter.GetRustupVersion();
        rustUpdateAvailable = CCCPSettingsAdapter.GetRustUpdateAvailable();

        // Load version info of SpacetimeDB SDK
        spacetimeSDKCurrentVersion = ServerUpdateProcess.GetCurrentSpacetimeSDKVersion();
        spacetimeSDKLatestVersion = ServerUpdateProcess.SpacetimeSDKLatestVersion();
        spacetimeSDKUpdateAvailable = ServerUpdateProcess.IsSpacetimeSDKUpdateAvailable();
        
        // Initialize both installer item lists
        InitializeInstallerItems();
        
        // Reduce frequency of automatic repaints
        EditorApplication.update += OnEditorUpdate;

        // Update installer items status based on loaded prefs
        UpdateInstallerItemsStatus();
    }

    private void OnFocus()
    {
        InitializeCustomInstallerWindow();
    }

    private void InitializeCustomInstallerWindow()
    {
        customProcess = new ServerCustomProcess(LogMessage, false);
        isConnectedSSH = customProcess.IsSessionActive();
        if (isConnectedSSH)
        {
            CheckCustomInstallationStatus();
        }
    }
    
    private void OnDisable()
    {
        // Clean up the update callback when the window is closed
        EditorApplication.update -= OnEditorUpdate;

        // Reload server manager editor preferences to ensure up to date versions and variables
        serverManager.LoadSettings();
        
        // Repaint the ServerWindow
        var serverWindow = GetWindow<ServerWindow>();
        if (serverWindow != null)
        {
            serverWindow.Repaint();
        }
    }

    private void OnEditorUpdate()
    {
        // Only trigger repaint if refreshing AND enough time has passed
        double currentTime = EditorApplication.timeSinceStartup;
        if (isRefreshing && (currentTime - lastRepaintTime) > minRepaintInterval)
        {
            lastRepaintTime = currentTime;
            RequestRepaint(); // Use a helper to request repaint
        }
    }
    #endregion
    
    #region Installer Items
    private void InitializeInstallerItems()
    {
        // Initialize WSL installer items
        installerItems = new List<InstallerItem>
        {
            new InstallerItem
            {
                title = "Install WSL with Debian",
                description = "Windows Subsystem for Linux with Debian distribution\n"+
                "Important: Will launch a checker tool that determines if your system supports WSL1 or WSL2\n"+
                "Note: May require a system restart. If it reports as failed, please restart and try again\n"+
                "Note: If you already have WSL installed, it will install Debian for your chosen WSL version",
                isInstalled = hasDebian,
                isEnabled = true, // Always enabled as it's the first prerequisite
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallWSLDebian()) : null,
                sectionHeader = "Required Local Software"
            },
            new InstallerItem
            {
                title = "Install Debian Trixie Update",
                description = "Debian Trixie Update (Debian Version 13)\n"+
                "Required to run the SpacetimeDB Server\n"+
                "Note: Is now included by default in the WSL with Debian installer\n"+
                "Note: May take some minutes to install",
                isInstalled = hasDebianTrixie,
                isEnabled = hasWSL && hasDebian && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallDebianTrixie()) : null
            },
            new InstallerItem
            {
                title = "Install cURL",
                description = "cURL is a command-line tool for transferring data with URLs\n"+
                "Required to install the SpacetimeDB Server",
                isInstalled = hasCurl,
                isEnabled = hasWSL && hasDebian && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallCurl()) : null
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Server",
                description = "SpacetimeDB Server Installation for Debian\n"+
                "Note: Only supports installing to the users home directory (SpacetimeDB default)",
                isInstalled = hasSpacetimeDBServer,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallSpacetimeDBServer()) : null
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB PATH",
                description = "Add SpacetimeDB to the PATH environment variable of your Debian user",
                isInstalled = hasSpacetimeDBPath,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallSpacetimeDBPath()) : null
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Service",
                description = "Install SpacetimeDB as a system service that automatically starts on server boot\n" +
                              "Note: Also creates a lightweight logs service to capture SpacetimeDB database logs",
                isInstalled = hasSpacetimeDBService,
                isEnabled = hasWSL && hasDebian && hasSpacetimeDBServer && !String.IsNullOrEmpty(userName),
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallSpacetimeDBService()) : null,
                expectedModuleName = CCCPSettingsAdapter.GetModuleName() // Load from prefs or use default
            },
            new InstallerItem
            {
                title = "Install Rust",
                description = "Rust is a programming language that can be 2x faster than C#\n"+
                "Note: Required to use the SpacetimeDB Server with Rust Language",
                isInstalled = hasRust,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallRust()) : null
            },
            new InstallerItem
            {
                title = "Install .NET SDK for C#",
                description = ".NET SDK 8.0 is Microsoft's software development kit for C#\n"+
                "Note: Required to use the SpacetimeDB Server with C# Language",
                isInstalled = hasNETSDK,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallNETSDK()) : null
            },
            new InstallerItem
            {
                title = "Install Web Assembly Optimizer Binaryen",
                description = "Binaryen is a compiler toolkit for WebAssembly\n"+
                "SpacetimeDB make use of wasm-opt optimizer for improving performance",
                isInstalled = hasBinaryen,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallBinaryen()) : null
            },
            new InstallerItem
            {
                title = "Install Git",
                description = "Git is a distributed version control system\n"+
                "SpacetimeDB may call git commands during the publish and generate process",
                isInstalled = hasGit,
                isEnabled = hasWSL && hasDebian && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallGit()) : null
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Unity SDK",
                description = "SpacetimeDB SDK contains essential scripts for SpacetimeDB development in Unity \n"+
                "Examples include a network manager that syncs the client state with the database",
                isInstalled = hasSpacetimeDBUnitySDK,
                isEnabled = true, // Always enabled as it doesn't depend on WSL
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallSpacetimeDBUnitySDK()) : null,
                sectionHeader = "Required Unity Plugin"
            }
        };

        // Initialize Custom SSH installer items (no WSL entry as we assume Debian is already installed)
        customInstallerItems = new List<InstallerItem>
        {   
            new InstallerItem
            {
                title = "Install User",
                description = "Creates a new user on the SSH Debian server with proper permissions\n"+
                "Will add your public SSH key to the user. Requires a manual SSH connection initially\n"+
                "Note: You will be prompted to set a password for the new user",
                isInstalled = hasCustomDebianUser,
                isEnabled = isConnectedSSH,
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallCustomUser()) : null,
                hasUsernameField = true,
                usernameLabel = "Create Username:",
                sectionHeader = "Required Remote Software"
            },
            new InstallerItem
            {
                title = "Install Debian Trixie Update",
                description = "Debian Trixie Update (Debian Version 13)\n"+
                "Required to run the SpacetimeDB Server\n"+
                "Note: May take some minutes to install",
                isInstalled = hasCustomDebianTrixie,
                isEnabled = customProcess.IsSessionActive() && !String.IsNullOrEmpty(sshUserName),
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallCustomDebianTrixie()) : null
            },
            new InstallerItem
            {
                title = "Install cURL",
                description = "cURL is a command-line tool for transferring data with URLs\n"+
                "Required to install the SpacetimeDB Server",
                isInstalled = hasCustomCurl,
                isEnabled = customProcess.IsSessionActive() && !String.IsNullOrEmpty(userName),
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallCustomCurl()) : null
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Server",
                description = "SpacetimeDB Server Installation for Debian\n"+
                "Note: Will install to the current SSH user session home directory (SpacetimedDB default)",
                isInstalled = hasCustomSpacetimeDBServer,
                isEnabled = customProcess.IsSessionActive() && hasCustomCurl && !String.IsNullOrEmpty(userName),
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallCustomSpacetimeDBServer()) : null
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB PATH",
                description = "Add SpacetimeDB to the PATH environment variable of your Debian user",
                isInstalled = hasCustomSpacetimeDBPath,
                isEnabled = customProcess.IsSessionActive() && hasCustomCurl && !String.IsNullOrEmpty(userName),
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallCustomSpacetimeDBPath()) : null
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Service",
                description = "Install SpacetimeDB as a system service that automatically starts on boot\n" +
                              "Creates a systemd service file to run SpacetimeDB in the background\n" +
                              "Note: Also creates a lightweight logs service to capture SpacetimeDB database logs",
                isInstalled = hasCustomSpacetimeDBService,
                isEnabled = customProcess.IsSessionActive() && hasCustomSpacetimeDBServer && !String.IsNullOrEmpty(userName),
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallCustomSpacetimeDBService()) : null,
                expectedModuleName = CCCPSettingsAdapter.GetModuleName() // Load from prefs or use default
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Unity SDK",
                description = "SpacetimeDB SDK contains essential scripts for SpacetimeDB development in Unity \n"+
                "Examples include a network manager that syncs the client state with the database",
                isInstalled = hasSpacetimeDBUnitySDK,
                isEnabled = true, // Always enabled as it doesn't depend on Custom SSH
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallSpacetimeDBUnitySDK()) : null,
                sectionHeader = "Required Unity Plugin"
            }
        };
        
        // Initialize Docker installer items
        dockerInstallerItems = new List<InstallerItem>
        {
            new InstallerItem
            {
                title = "Install Docker Desktop",
                description = "Docker Desktop provides containerization for running SpacetimeDB\n"+
                "Note: Available for Windows, macOS, and Linux\n"+
                "Note: Docker Desktop includes Docker Compose",
                isInstalled = hasDocker,
                isEnabled = true,
                installAction = InstallDocker,
                sectionHeader = "Required Docker Software"
            },
            new InstallerItem
            {
                title = "Pull SpacetimeDB Docker Image",
                description = "Downloads the official SpacetimeDB Docker image (clockworklabs/spacetime) from Docker Hub\n"+
                "The image will be pulled and ready to use with the configured port mapping\n"+
                "Note: This may take a few minutes depending on your internet connection",
                isInstalled = hasDockerImage,
                isEnabled = hasDocker && hasDockerCompose,
                installAction = PullSpacetimeDBImage
            },
            new InstallerItem
            {
                title = "Configure Docker Container Volume Mounts",
                description = "Ensures the Docker container has proper volume mounts for Unity file generation\n"+
                "Verifies that the Unity Assets directory is mounted at /unity inside the container\n"+
                "Note: Will recreate container if mounts are incorrect (server must be stopped)",
                isInstalled = hasDockerContainerMountsConfigured,
                isEnabled = hasDocker && hasDockerImage,
                installAction = ReconfigureDockerContainer,
                sectionHeader = "Docker Container Configuration"
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Unity SDK",
                description = "SpacetimeDB SDK contains essential scripts for SpacetimeDB development in Unity \n"+
                "Examples include a network manager that syncs the client state with the database",
                isInstalled = hasSpacetimeDBUnitySDK,
                isEnabled = true, // Always enabled as it doesn't depend on Docker
                installAction = !isAssetStoreBuild ? (Action)(() => installProcess?.InstallSpacetimeDBUnitySDK()) : null,
                sectionHeader = "Required Unity Plugin"
            }
        };
    }
    #endregion

    #region Installer Item Status
    internal void UpdateInstallerItemsStatus()
    {
        bool repaintNeeded = false;

        // Update the correct list based on current tab
        List<InstallerItem> itemsToUpdate;
        if (currentTab == wslTabIndex) {
            itemsToUpdate = installerItems;
        } else if (currentTab == customTabIndex) {
            itemsToUpdate = customInstallerItems;
        } else if (currentTab == dockerTabIndex) {
            itemsToUpdate = dockerInstallerItems;
        } else {
            return; // Invalid tab
        }

        // Reload version information from Settings to ensure we have the latest data
        spacetimeDBCurrentVersion = CCCPSettingsAdapter.GetSpacetimeDBCurrentVersion();
        spacetimeDBCurrentVersionCustom = CCCPSettingsAdapter.GetSpacetimeDBCurrentVersionCustom();
        spacetimeDBLatestVersion = CCCPSettingsAdapter.GetSpacetimeDBLatestVersion();
        rustCurrentVersion = CCCPSettingsAdapter.GetRustCurrentVersion();
        rustLatestVersion = CCCPSettingsAdapter.GetRustLatestVersion();
        rustupVersion = CCCPSettingsAdapter.GetRustupVersion();
        rustUpdateAvailable = CCCPSettingsAdapter.GetRustUpdateAvailable();

        // Reload SpacetimeDB SDK version information
        spacetimeSDKCurrentVersion = ServerUpdateProcess.GetCurrentSpacetimeSDKVersion();
        spacetimeSDKLatestVersion = ServerUpdateProcess.SpacetimeSDKLatestVersion();
        spacetimeSDKUpdateAvailable = ServerUpdateProcess.IsSpacetimeSDKUpdateAvailable();
        
        // For WSL installer items
        if (currentTab == 1) {
            foreach (var item in itemsToUpdate
            )
            {
                bool previousState = item.isInstalled;
                bool previousEnabledState = item.isEnabled;
                bool newState = previousState; // Default to no change
                bool newEnabledState = previousEnabledState;
                
                if (item.title.Contains("WSL"))
                {
                    newState = hasWSL && hasDebian;
                    newEnabledState = true; // Always enabled
                }
                else if (item.title.Contains("Debian Trixie"))
                {
                    newState = hasDebianTrixie;
                    newEnabledState = hasWSL && hasDebian && !String.IsNullOrEmpty(userName);
                }
                else if (item.title.Contains("cURL"))
                {
                    newState = hasCurl;
                    newEnabledState = hasWSL && hasDebian && !String.IsNullOrEmpty(userName);
                }
                else if (item.title.Contains("SpacetimeDB Server"))
                {
                    newState = hasSpacetimeDBServer;
                    newEnabledState = hasWSL && hasDebian && hasDebianTrixie && hasCurl && !String.IsNullOrEmpty(userName);
                }
                else if (item.title.Contains("SpacetimeDB PATH"))
                {
                    newState = hasSpacetimeDBPath;
                    newEnabledState = hasWSL && hasDebian && hasDebianTrixie && hasCurl && !String.IsNullOrEmpty(userName);
                }
                else if (item.title.Contains("SpacetimeDB Service"))
                {
                    newState = hasSpacetimeDBService;
                    newEnabledState = hasWSL && hasDebian && hasSpacetimeDBServer && !String.IsNullOrEmpty(userName);
                }
                else if (item.title.Contains("Rust"))
                {
                    newState = hasRust;
                    newEnabledState = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName);
                }
                else if (item.title.Contains(".NET SDK"))
                {
                    newState = hasNETSDK;
                    newEnabledState = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName);
                }
                else if (item.title.Contains("Web Assembly Optimizer Binaryen"))
                {
                    newState = hasBinaryen;
                    newEnabledState = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName);
                }
                else if (item.title.Contains("Git"))
                {
                    newState = hasGit;
                    newEnabledState = hasWSL && hasDebian && !String.IsNullOrEmpty(userName);
                }
                else if (item.title.Contains("SpacetimeDB Unity SDK"))
                {
                    newState = hasSpacetimeDBUnitySDK;
                    newEnabledState = true; // Always enabled
                }
                
                if (newState != previousState || newEnabledState != previousEnabledState)
                {
                    item.isInstalled = newState;
                    item.isEnabled = newEnabledState;
                    repaintNeeded = true; // Mark that a repaint is needed because item state changed
                }
            }
        }
        // For Custom SSH installer items
        else if (currentTab == 2) {
            foreach (var item in itemsToUpdate)
            {
                bool previousState = item.isInstalled;
                bool previousEnabledState = item.isEnabled;
                bool newState = previousState; // Default to no change
                bool newEnabledState = previousEnabledState;
                
                bool isSessionActive = customProcess.IsSessionActive();

                if (item.title.Contains("Install User"))
                {
                    newState = hasCustomDebianUser;
                    newEnabledState = isSessionActive && !String.IsNullOrEmpty(sshUserName);
                }
                else if (item.title.Contains("Debian Trixie"))
                {
                    newState = hasCustomDebianTrixie;
                    newEnabledState = isSessionActive && !String.IsNullOrEmpty(sshUserName);
                }
                else if (item.title.Contains("cURL"))
                {
                    newState = hasCustomCurl;
                    newEnabledState = isSessionActive && !String.IsNullOrEmpty(sshUserName);
                }
                else if (item.title.Contains("SpacetimeDB Server"))
                {
                    newState = hasCustomSpacetimeDBServer;
                    newEnabledState = isSessionActive && hasCustomCurl && !String.IsNullOrEmpty(sshUserName);
                }
                else if (item.title.Contains("SpacetimeDB PATH"))
                {
                    newState = hasCustomSpacetimeDBPath;
                    newEnabledState = isSessionActive && hasCustomCurl && !String.IsNullOrEmpty(sshUserName);
                }
                else if (item.title.Contains("SpacetimeDB Service"))
                {
                    newState = hasCustomSpacetimeDBService;
                    newEnabledState = isSessionActive && hasCustomSpacetimeDBServer && !String.IsNullOrEmpty(sshUserName);
                }
                else if (item.title.Contains("SpacetimeDB Unity SDK"))
                {
                    newState = hasSpacetimeDBUnitySDK;
                    newEnabledState = true; // Always enabled
                }
                
                if (newState != previousState || newEnabledState != previousEnabledState)
                {
                    item.isInstalled = newState;
                    item.isEnabled = newEnabledState;
                    repaintNeeded = true; // Mark that a repaint is needed because item state changed
                }
            }
        }
        // For Docker installer items
        else if (currentTab == 0) {
            foreach (var item in itemsToUpdate)
            {
                bool previousState = item.isInstalled;
                bool previousEnabledState = item.isEnabled;
                bool newState = previousState; // Default to no change
                bool newEnabledState = previousEnabledState;
                
                if (item.title.Contains("Docker Desktop"))
                {
                    newState = hasDocker && hasDockerCompose;
                    newEnabledState = true; // Always enabled
                }
                else if (item.title.Contains("Pull SpacetimeDB Docker Image"))
                {
                    newState = hasDockerImage;
                    newEnabledState = hasDocker && hasDockerCompose;
                }
                else if (item.title.Contains("Configure Docker Container Volume Mounts"))
                {
                    newState = hasDockerContainerMountsConfigured;
                    newEnabledState = hasDocker && hasDockerCompose;
                }
                else if (item.title.Contains("SpacetimeDB Unity SDK"))
                {
                    newState = hasSpacetimeDBUnitySDK;
                    newEnabledState = true; // Always enabled
                }
                
                if (newState != previousState || newEnabledState != previousEnabledState)
                {
                    item.isInstalled = newState;
                    item.isEnabled = newEnabledState;
                    repaintNeeded = true; // Mark that a repaint is needed because item state changed
                }
            }
        }
        
        if (repaintNeeded)
        {
            RequestRepaint(); // Request a repaint only if an item's state changed
        }
    }
    #endregion

    #region UI Methods
    private void OnGUI()
    {
        if (!stylesInitialized)
        {
            InitializeStyles();
        }
        
        EditorGUILayout.BeginVertical();
        
        DrawToolbar();

        // Draw the tab bar
        int newTab = GUILayout.Toolbar(currentTab, tabNames);
        if (newTab != currentTab)
        {
            currentTab = newTab;
            // When switching tabs, check the appropriate installation status
    
    
    
            
            if (currentTab == wslTabIndex) {
                CheckInstallationStatus();
            } else if (currentTab == customTabIndex) {
                CheckCustomInstallationStatus();
            } else if (currentTab == dockerTabIndex) {
                CheckDockerPrerequisites();
            }
            UpdateInstallerItemsStatus();
        }

        EditorGUILayout.Space(5);
        
        DrawInstallerItemsList();
        EditorGUILayout.Space(5);
        
        DrawStatusMessage();
        
        EditorGUILayout.EndVertical();
    }

    private void InitializeStyles()
    {
        // Title style
        titleStyle = new GUIStyle(EditorStyles.whiteLargeLabel);
        titleStyle.fontSize = 15;
        titleStyle.alignment = TextAnchor.MiddleCenter;

        // Item title style
        itemTitleStyle = new GUIStyle(EditorStyles.largeLabel);
        itemTitleStyle.fontSize = 14;
        
        // Installed style (green text with checkmark)
        installedStyle = new GUIStyle(EditorStyles.label);
        installedStyle.normal.textColor = new Color(0.0f, 0.75f, 0.0f);
        installedStyle.fontSize = 12;
        
        // Install button style
        installButtonStyle = new GUIStyle(GUI.skin.button);
        installButtonStyle.fontSize = 10;
        installButtonStyle.normal.textColor = new Color(0.2f, 0.7f, 0.2f);
        installButtonStyle.hover.textColor = new Color(0.3f, 0.8f, 0.3f);
        installButtonStyle.fontStyle = FontStyle.Bold;
        
        // Section header style
        sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel);
        sectionHeaderStyle.fontSize = 12;
        sectionHeaderStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
        sectionHeaderStyle.margin = new RectOffset(0, 0, 10, 5);
        
        stylesInitialized = true;
    }
    
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        // Determine which tab we're on



        
        // Refresh Button
        bool isRefreshingCurrentTab = (currentTab == wslTabIndex && isRefreshing) || 
                                       (currentTab == customTabIndex && isCustomRefreshing) ||
                                       (currentTab == dockerTabIndex && isDockerRefreshing);
        EditorGUI.BeginDisabledGroup(isRefreshingCurrentTab);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            if (currentTab == wslTabIndex) {
                CheckInstallationStatus();
            } else if (currentTab == customTabIndex) {
                CheckCustomInstallationStatus();
            } else if (currentTab == dockerTabIndex) {
                CheckDockerPrerequisites();
            }
            UpdateInstallerItemsStatus();
        }
        EditorGUI.EndDisabledGroup();
               
        // Debug Dropdown
        GUIContent debugContent = new GUIContent("Debug");
        if (EditorGUILayout.DropdownButton(debugContent, FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(60)))
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Show Install Windows"), visibleInstallProcesses, () => {
                visibleInstallProcesses = !visibleInstallProcesses;
                CCCPSettingsAdapter.SetVisibleInstallProcesses(visibleInstallProcesses);
            });

            menu.AddItem(new GUIContent("Keep Windows Open"), keepWindowOpenForDebug, () => {
                keepWindowOpenForDebug = !keepWindowOpenForDebug;
                CCCPSettingsAdapter.SetKeepWindowOpenForDebug(keepWindowOpenForDebug);
            });
            
            menu.AddItem(new GUIContent("Force Install"), forceInstall, () => {
                forceInstall = !forceInstall;
                // Update dependent flags when forceInstall changes
                alwaysShowInstall = forceInstall;
                installIfAlreadyInstalled = forceInstall;
                // No Settings for forceInstall as it's a transient state for the session
            });

            menu.ShowAsContext();
        }
        
        // Settings Dropdown
        GUIContent settingsContent = new GUIContent("Settings");
        if (EditorGUILayout.DropdownButton(settingsContent, FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(70)))
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Update Cargo.toml automatically"), updateCargoToml, () => {
                updateCargoToml = !updateCargoToml;
                CCCPSettingsAdapter.SetUpdateCargoToml(updateCargoToml);
            });

            menu.ShowAsContext();
        }

        // Add space between elements
        GUILayout.FlexibleSpace();
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawInstallerItemsList()
    {
        // Use GUILayout group to reduce layout recalculations
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));

        // Determine which tab we're on



        
        string title;
        string description;
        
        if (currentTab == wslTabIndex) {
            title = "SpacetimeDB Local WSL Server Installer";
            description = "Setup all the required software to run your local SpacetimeDB Server in WSL.\n" +
                         "You get a local WSL CLI for spacetime commands.\n" +
                         "Required for all server modes to be able to publish your server.";
        } else if (currentTab == customTabIndex) {
            title = "SpacetimeDB Remote Custom Server Installer";
            description = "Setup all the required software to run SpacetimeDB Server on a remote Debian server via SSH.\n" +
                         "Works on any fresh Debian 12 or 13 server from the ground up.\n" +
                         "Note: The Local WSL or Docker setup is required to be able to publish to your remote server.";
        } else if (currentTab == dockerTabIndex) {
            title = "SpacetimeDB Docker Setup";
            description = "Setup Docker to run SpacetimeDB in containers.\n" +
                         "Works on Windows, macOS, and Linux.\n" +
                         "Includes tools to generate docker-compose.yml with your configuration.";
        } else {
            title = "Unknown Tab";
            description = "";
        }

        GUILayout.Label(title, titleStyle);
        
        EditorGUILayout.LabelField(description,
            EditorStyles.centeredGreyMiniLabel, GUILayout.Height(43));

        // Show usernameprompt for clarity before SpacetimeDB install (WSL and Custom only, not Docker)
        bool showUsernamePrompt = String.IsNullOrEmpty(userName) && 
            ((currentTab == wslTabIndex && hasWSL && hasDebian) || 
             (currentTab == customTabIndex && customProcess.IsSessionActive()));
        
        if (showUsernamePrompt)
        {
            List<InstallerItem> itemsToUpdate;
            if (currentTab == wslTabIndex) {
                itemsToUpdate = installerItems;
            } else if (currentTab == customTabIndex) {
                itemsToUpdate = customInstallerItems;
            } else {
                itemsToUpdate = new List<InstallerItem>(); // Fallback
            }
            
            foreach (var item in itemsToUpdate) item.isEnabled = false;
            userNamePrompt = true;

            EditorGUILayout.Space(10);

            // Center the helpbox by using FlexibleSpace before and after
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(330));
            
            EditorGUILayout.LabelField("Please enter your Debian username to continue", itemTitleStyle);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Debian Username:", GUILayout.Width(120));
            
            // Store the control ID before TextField to detect when Enter is pressed
            GUI.SetNextControlName("UsernameField");
            // Use the temporary input field instead of directly modifying userName
            tempUserNameInput = EditorGUILayout.TextField(tempUserNameInput, GUILayout.Width(150));
            
            // Handle Enter key press - must use current event and keycode
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return && 
                GUI.GetNameOfFocusedControl() == "UsernameField" && !string.IsNullOrEmpty(tempUserNameInput))
            {
                // Submit the username only on Enter
                CCCPSettingsAdapter.SetUserName(tempUserNameInput);
                foreach (var item in itemsToUpdate) item.isEnabled = true;
                userNamePrompt = false;
                UnityEngine.Debug.Log("Username submitted via Enter: " + tempUserNameInput);
                
                // Use the current event to prevent it from propagating
                e.Use();

                if (currentTab == wslTabIndex) {
                    CheckInstallationStatus();
                } else if (currentTab == customTabIndex) {
                    CheckCustomInstallationStatus();
                }
            }
            
            // Add a submit button for clarity
            if (GUILayout.Button("Set", GUILayout.Width(50)) && !string.IsNullOrEmpty(tempUserNameInput))
            {
                // Submit the username only on button click
                CCCPSettingsAdapter.SetUserName(tempUserNameInput);
                foreach (var item in itemsToUpdate) item.isEnabled = true;
                userNamePrompt = false;

                if (currentTab == wslTabIndex) {
                    CheckInstallationStatus();
                } else if (currentTab == customTabIndex) {
                    CheckCustomInstallationStatus();
                }
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
        }
        
        // Begin the scrollview for the installer items
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        // Get the appropriate list based on current tab
        List<InstallerItem> displayItems;
        if (currentTab == wslTabIndex) {
            displayItems = installerItems;
        } else if (currentTab == customTabIndex) {
            displayItems = customInstallerItems;
        } else if (currentTab == dockerTabIndex) {
            displayItems = dockerInstallerItems;
        } else {
            displayItems = new List<InstallerItem>(); // Fallback
        }
        
        if (displayItems.Count == 0)
        {
            EditorGUILayout.HelpBox("No installer items found.", MessageType.Info);
        }
        else
        {
            // Cache to reduce GC and memory allocations
            for (int i = 0; i < displayItems.Count; i++)
            {
                DrawInstallerItem(displayItems[i], i);
            }
        }
        
        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();
    }
    
    // Optimized to reduce GC allocations
    private void DrawInstallerItem(InstallerItem item, int index)
    {
        // Display section header if this item has one
        if (!string.IsNullOrEmpty(item.sectionHeader))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(item.sectionHeader, sectionHeaderStyle);
        }
        
        // Container box for each installer item
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        // Determine if the item should be greyed out
        bool isDisabled = !item.isEnabled;
        Color originalColor = GUI.color;
        if (isDisabled)
        {
            GUI.color = new Color(originalColor.r, originalColor.g, originalColor.b, 0.6f); // Make it semi-transparent
        }
        
        // Header with name and install button
        EditorGUILayout.BeginHorizontal();
        
        // Title - reuse cached content when possible
        EditorGUILayout.LabelField(item.title, itemTitleStyle, GUILayout.ExpandWidth(true));        

        if (currentTab == 0) {
            showUpdateButton = (item.title.Contains("SpacetimeDB Server") && 
                                    !string.IsNullOrEmpty(spacetimeDBCurrentVersion) && 
                                    !string.IsNullOrEmpty(spacetimeDBLatestVersion) && 
                                    spacetimeDBCurrentVersion != spacetimeDBLatestVersion) ||
                              (item.title.Contains("Install Rust") && 
                                    rustUpdateAvailable && 
                                    !string.IsNullOrEmpty(rustLatestVersion)) ||
                              (item.title.Contains("SpacetimeDB Unity SDK") && 
                                    spacetimeSDKUpdateAvailable && 
                                    !string.IsNullOrEmpty(spacetimeSDKLatestVersion));
        } else if (currentTab == 1) {
            showUpdateButton = item.title.Contains("SpacetimeDB Server") && 
                                    !string.IsNullOrEmpty(spacetimeDBCurrentVersionCustom) && 
                                    !string.IsNullOrEmpty(spacetimeDBLatestVersion) && 
                                    spacetimeDBCurrentVersionCustom != spacetimeDBLatestVersion;
        }
        
        // Status (installed or install button)
        if (showUpdateButton)
        {
            EditorGUILayout.Space(2);
            EditorGUI.BeginDisabledGroup(isDisabled);
            
            string updateButtonText;
            if (item.title.Contains("Install Rust"))
            {
                updateButtonText = "Update to v" + rustLatestVersion;
            }
            else if (item.title.Contains("SpacetimeDB Server"))
            {
                updateButtonText = "Update to v" + spacetimeDBLatestVersion;
            }
            else if (item.title.Contains("SpacetimeDB Unity SDK"))
            {
                updateButtonText = "Update to v" + spacetimeSDKLatestVersion;
            }
            else
            {
                updateButtonText = "Update";
            }
            
            if (GUILayout.Button(updateButtonText, installButtonStyle, GUILayout.Width(100), GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () => {
                    item.installAction?.Invoke();
                };
            }
            EditorGUI.EndDisabledGroup();
        }
        else if (item.isInstalled && !alwaysShowInstall)
        {
            if (item.title.Contains("SpacetimeDB Server") && !string.IsNullOrEmpty(spacetimeDBCurrentVersion))
            {
                if (currentTab == 0 && !string.IsNullOrEmpty(spacetimeDBCurrentVersion))
                {
                    EditorGUILayout.LabelField("✓ Installed v " + spacetimeDBCurrentVersion, installedStyle, GUILayout.Width(110));
                }
                else if (currentTab == 1 && !string.IsNullOrEmpty(spacetimeDBCurrentVersionCustom))
                {
                    EditorGUILayout.LabelField("✓ Installed v " + spacetimeDBCurrentVersionCustom, installedStyle, GUILayout.Width(110));
                }
            }
            else if (item.title.Contains("Install Rust") && !string.IsNullOrEmpty(rustCurrentVersion))
            {
                EditorGUILayout.LabelField("✓ Installed v " + rustCurrentVersion, installedStyle, GUILayout.Width(110));
            }
            else if (item.title.Contains("SpacetimeDB Unity SDK") && !string.IsNullOrEmpty(spacetimeSDKCurrentVersion))
            {
                EditorGUILayout.LabelField("✓ Installed v " + spacetimeSDKCurrentVersion, installedStyle, GUILayout.Width(110));
            }
            else
            {
                EditorGUILayout.LabelField("✓ Installed", installedStyle, GUILayout.Width(110));
            }
        }
        else
        {
            EditorGUILayout.Space(2);
            
            // Install button
            EditorGUI.BeginDisabledGroup(isDisabled);
            if (GUILayout.Button("Install", installButtonStyle, GUILayout.Width(100), GUILayout.Height(30)))
            {
                // Use delayCall to avoid issues with GUI during install action
                EditorApplication.delayCall += () => {
                    item.installAction?.Invoke();
                };
            }
            EditorGUI.EndDisabledGroup();
        }
        
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField(item.description, EditorStyles.wordWrappedMiniLabel);
        
        // Add username field for installers that need it
        if (item.hasUsernameField || item.title.Contains("SpacetimeDB Server"))
        {
            // Determine which tab we're on
    
    
    
            
            EditorGUILayout.BeginHorizontal();
            string labelText = item.hasUsernameField ? item.usernameLabel : "Install as Username:";
            EditorGUILayout.LabelField(labelText, GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            
            if (currentTab == wslTabIndex) // For WSL mode, use the regular username
            {
                string newUserName = EditorGUILayout.TextField(userName, GUILayout.Width(150));
                if (EditorGUI.EndChangeCheck() && newUserName != userName)
                {
                    CCCPSettingsAdapter.SetUserName(newUserName);
                }
            }
            else if (currentTab == customTabIndex)
            {
                if (item.title == "Install User") // Use temp name for Install User
                {
                    // For user creation, use the special temporary field
                    string newUserName = EditorGUILayout.TextField(tempCreateUserNameInput, GUILayout.Width(150));
                    if (EditorGUI.EndChangeCheck() && newUserName != tempCreateUserNameInput)
                    {
                        tempCreateUserNameInput = newUserName;
                    }
                }
                else // For other installers in SSH mode (like Install SpacetimeDB Server) use sshUserName
                {
                    EditorGUI.BeginDisabledGroup(true);
                    string newUserName = EditorGUILayout.TextField(sshUserName, GUILayout.Width(150));
                    if (EditorGUI.EndChangeCheck() && newUserName != sshUserName)
                    {
                        CCCPSettingsAdapter.SetSSHUserName(newUserName);
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }
        
        // Add module name field for SpacetimeDB Service installer
        if (item.title == "Install SpacetimeDB Service")
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Module Name:", GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            EditorGUI.BeginDisabledGroup(true);
            string currentModuleName = string.IsNullOrEmpty(item.expectedModuleName) ? 
                CCCPSettingsAdapter.GetModuleName() : 
                item.expectedModuleName;
            
            // Display "No module found." if currentModuleName is empty
            string displayText = string.IsNullOrEmpty(currentModuleName) ? "No module found" : currentModuleName;
            
            string newModuleName = EditorGUILayout.TextField(displayText, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck() && newModuleName != item.expectedModuleName)
            {
                item.expectedModuleName = newModuleName;
                // Save to Settings for persistence
                CCCPSettingsAdapter.SetModuleName(item.expectedModuleName);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }
        
        // If disabled, add a note about prerequisites
        if (isDisabled && !userNamePrompt)
        {
            GUIStyle prereqStyle = new GUIStyle(EditorStyles.miniLabel);
            prereqStyle.normal.textColor = new Color(0.7f, 0.5f, 0.3f); // Orange
            if (!hasDebianTrixie)
            EditorGUILayout.LabelField("Requires WSL with Debian Trixie to be installed first", prereqStyle);
            else if (!hasCurl)
            EditorGUILayout.LabelField("Requires cURL to be installed first", prereqStyle);
        }
        
        // Restore original color
        GUI.color = originalColor;
        
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }
    
    private void DrawStatusMessage()
    {
        // Match ServerReducerWindow's status message style
        EditorGUILayout.BeginHorizontal();
        
        // Timestamp section with light grey color
        GUIStyle timeStyle = new GUIStyle(EditorStyles.label);
        timeStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f); // Light grey
        timeStyle.alignment = TextAnchor.MiddleLeft;
        timeStyle.fontStyle = FontStyle.Italic;
        EditorGUILayout.LabelField(statusTimestamp, timeStyle, GUILayout.Width(60), GUILayout.Height(16));
        
        // Message section with status color
        GUIStyle msgStyle = new GUIStyle(EditorStyles.label);
        msgStyle.normal.textColor = statusColor;
        msgStyle.alignment = TextAnchor.MiddleLeft;
        EditorGUILayout.LabelField(statusMessage, msgStyle, GUILayout.Height(16));
        
        EditorGUILayout.EndHorizontal();
    }
    #endregion
    
    #region Check Installation Status
    internal async void CheckInstallationStatus()
    {
        if (isRefreshing) return; // Don't start a new refresh if one is already running
        
        isRefreshing = true;
        SetStatus("Checking WSL installation status...", Color.yellow);
        
        // Check for SpacetimeDB Unity SDK separately
        ServerSpacetimeSDKInstaller.IsSDKInstalled((isSDKInstalled) => {
            hasSpacetimeDBUnitySDK = isSDKInstalled;
            CCCPSettingsAdapter.SetHasSpacetimeDBUnitySDK(hasSpacetimeDBUnitySDK);
            UpdateInstallerItemsStatus();
        });
        
        wslProcess.CheckPrerequisites((wsl, debian, trixie, curl, spacetime, spacetimePath, rust, spacetimeService, spacetimeLogsService, binaryen, git, netsdk) => {
            hasWSL = wsl;
            hasDebian = debian;
            hasDebianTrixie = trixie;
            hasCurl = curl;
            hasSpacetimeDBServer = spacetime;
            hasSpacetimeDBPath = spacetimePath;
            hasRust = rust;
            hasSpacetimeDBService = spacetimeService;
            hasSpacetimeDBLogsService = spacetimeLogsService;
            hasBinaryen = binaryen;
            hasGit = git;
            hasNETSDK = netsdk;
            
            // Save state to Settings
            CCCPSettingsAdapter.SetHasWSL(hasWSL);
            CCCPSettingsAdapter.SetHasDebian(hasDebian);
            CCCPSettingsAdapter.SetHasDebianTrixie(hasDebianTrixie);
            CCCPSettingsAdapter.SetHasCurl(hasCurl);
            CCCPSettingsAdapter.SetHasSpacetimeDBServer(hasSpacetimeDBServer);
            CCCPSettingsAdapter.SetHasSpacetimeDBPath(hasSpacetimeDBPath);
            CCCPSettingsAdapter.SetHasSpacetimeDBService(hasSpacetimeDBService);
            CCCPSettingsAdapter.SetHasSpacetimeDBLogsService(hasSpacetimeDBLogsService);
            CCCPSettingsAdapter.SetHasRust(hasRust);
            CCCPSettingsAdapter.SetHasNETSDK(hasNETSDK);
            CCCPSettingsAdapter.SetHasBinaryen(hasBinaryen);
            CCCPSettingsAdapter.SetHasGit(hasGit);
            CCCPSettingsAdapter.SetVisibleInstallProcesses(visibleInstallProcesses);
        });

        // Check SpacetimeDB version to update it if it was updated in the installer
        await serverManager.CheckSpacetimeDBVersion();

        // Check Rust version to update it if it was updated in the installer
        await serverManager.CheckRustVersion();

        // Update UI
        UpdateInstallerItemsStatus();

        // Update WSL1 status
        WSL1Installed = CCCPSettingsAdapter.GetWSL1Installed();

        isRefreshing = false;
        SetStatus("WSL installation status updated.", Color.green); // This might request repaint (throttled)
    }
    
    internal async void CheckCustomInstallationStatus()
    {
        await customProcess.StartSession();
        // Don't check if the SSH session isn't active
        if (!customProcess.IsSessionActive())
        {
            // Reset installation status
            hasCustomDebianUser = false;
            hasCustomDebianTrixie = false;
            hasCustomCurl = false;
            hasCustomSpacetimeDBServer = false;
            hasCustomSpacetimeDBPath = false;
            hasCustomSpacetimeDBService = false;
            hasCustomSpacetimeDBLogsService = false;
            
            // Update UI
            UpdateInstallerItemsStatus();
            SetStatus("SSH session is not active. Please connect to your server.", Color.yellow);
            return;
        }
        
        isCustomRefreshing = true;
        SetStatus("Checking remote installation status...", Color.yellow);
        
        // Check if a username is set for SSH operations
        if (string.IsNullOrEmpty(sshUserName))
        {
            // If no username is set, disable custom installer items
            hasCustomDebianUser = false;
            hasCustomDebianTrixie = false;
            hasCustomCurl = false;
            hasCustomSpacetimeDBServer = false;
            hasCustomSpacetimeDBPath = false;
            hasCustomSpacetimeDBService = false;
            hasCustomSpacetimeDBLogsService = false;
            
            UpdateInstallerItemsStatus();
            isCustomRefreshing = false;
            SetStatus("Please enter your Debian username to check installation status.", Color.yellow);
            return;
        }

        // Check Debian User
        SetStatus("Checking Debian User status...", Color.yellow);
        var userResult = await customProcess.RunCustomCommandAsync($"getent group sudo | cut -d: -f4");
        hasCustomDebianUser = userResult.success && !string.IsNullOrEmpty(userResult.output);
        await Task.Delay(100); // Small delay between checks
        
        // Check Debian Trixie
        SetStatus("Checking Debian Trixie status...", Color.yellow);
        var debianResult = await customProcess.RunCustomCommandAsync("cat /etc/debian_version");
        hasCustomDebianTrixie = debianResult.success && (debianResult.output.Contains("trixie") || debianResult.output.Contains("13"));
        await Task.Delay(100); // Small delay between checks
        
        // Check cURL
        SetStatus("Checking cURL status...", Color.yellow);
        var curlResult = await customProcess.RunCustomCommandAsync("which curl");
        hasCustomCurl = curlResult.success && curlResult.output.Contains("curl");
        await Task.Delay(100);
        
        // Check SpacetimeDB Server
        SetStatus("Checking SpacetimeDB Server status...", Color.yellow);
        string spacetimeDBServerExecutablePath = $"/home/{sshUserName}/.local/bin/spacetime";
        var spacetimeDBResult = await customProcess.RunCustomCommandAsync($"test -x {spacetimeDBServerExecutablePath} && echo 'executable' || echo 'not_executable_or_not_found'");
        hasCustomSpacetimeDBServer = spacetimeDBResult.success && spacetimeDBResult.output.Trim() == "executable";
        await Task.Delay(100);

        // Check SpacetimeDB PATH - Use which command to verify it's actually in PATH
        SetStatus("Checking SpacetimeDB PATH status...", Color.yellow);
        var pathResult = await customProcess.RunCustomCommandAsync($"bash -l -c 'which spacetime' 2>/dev/null");
        hasCustomSpacetimeDBPath = pathResult.success && !string.IsNullOrEmpty(pathResult.output) && pathResult.output.Contains("spacetime");
        await Task.Delay(100);

        // Check if SpacetimeDB is installed as a service
        SetStatus("Checking SpacetimeDB Service status...", Color.yellow);
        var serviceResult = await customProcess.RunCustomCommandAsync("systemctl is-enabled spacetimedb.service 2>/dev/null || echo 'not-found'");
        hasCustomSpacetimeDBService = serviceResult.success && serviceResult.output.Trim() == "enabled";
        await Task.Delay(100);

        // Check if SpacetimeDB Database Logs service exists (it may not be enabled until a module is deployed)
        SetStatus("Checking SpacetimeDB Database Logs Service status...", Color.yellow);
        var logsServiceResult = await customProcess.RunCustomCommandAsync("systemctl status spacetimedb-logs.service 2>/dev/null | head -n 1");
        hasCustomSpacetimeDBLogsService = logsServiceResult.success && logsServiceResult.output.Contains("spacetimedb-logs.service");

        // Save installation status to Settings
        CCCPSettingsAdapter.SetHasCustomDebianUser(hasCustomDebianUser);
        CCCPSettingsAdapter.SetHasCustomDebianTrixie(hasCustomDebianTrixie);
        CCCPSettingsAdapter.SetHasCustomCurl(hasCustomCurl);
        CCCPSettingsAdapter.SetHasCustomSpacetimeDBServer(hasCustomSpacetimeDBServer);
        CCCPSettingsAdapter.SetHasCustomSpacetimeDBPath(hasCustomSpacetimeDBPath);
        CCCPSettingsAdapter.SetHasCustomSpacetimeDBService(hasCustomSpacetimeDBService);
        CCCPSettingsAdapter.SetHasCustomSpacetimeDBLogsService(hasCustomSpacetimeDBLogsService);

        // Update SpacetimeDB version for custom installation
        await customProcess.CheckSpacetimeDBVersionCustom();

        // Update UI
        UpdateInstallerItemsStatus();
        Repaint(); // Ensure UI is updated after async checks
        
        isCustomRefreshing = false;
        SetStatus("Remote installation status check complete.", Color.green);
    }
    
    private void CheckDockerPrerequisites()
    {
        if (isDockerRefreshing) return; // Don't start a new refresh if one is already running
        
        isDockerRefreshing = true;
        SetStatus("Checking Docker prerequisites...", Color.yellow);
        
        // Check Docker prerequisites asynchronously
        dockerProcess.CheckPrerequisites((docker, compose, image) =>
        {
            hasDocker = docker;
            hasDockerCompose = compose;
            hasDockerImage = image;
            
            // Check container mounts if Docker and image are available
            if (hasDocker && hasDockerImage)
            {
                hasDockerContainerMountsConfigured = CheckDockerContainerMounts();
            }
            else
            {
                hasDockerContainerMountsConfigured = false;
            }
            
            // Save to settings
            CCCPSettingsAdapter.SetHasDocker(hasDocker);
            CCCPSettingsAdapter.SetHasDockerCompose(hasDockerCompose);
            CCCPSettingsAdapter.SetHasDockerImage(hasDockerImage);
            
            UpdateInstallerItemsStatus();
            Repaint();
            
            isDockerRefreshing = false;
            
            // Provide more detailed status messages
            if (hasDocker && hasDockerCompose && hasDockerImage && hasDockerContainerMountsConfigured)
            {
                SetStatus("Docker prerequisites check complete. All components ready!", Color.green);
            }
            else if (hasDocker && hasDockerCompose && hasDockerImage && !hasDockerContainerMountsConfigured)
            {
                SetStatus("Docker ready but container needs volume mount configuration.", Color.yellow);
            }
            else if (hasDocker && hasDockerCompose)
            {
                SetStatus("Docker is installed. SpacetimeDB image will be pulled when needed.", Color.green);
            }
            else if (hasDocker && !hasDockerCompose)
            {
                SetStatus("Docker is installed but Docker Compose is not available. Please update Docker Desktop.", Color.yellow);
            }
            else if (!hasDocker)
            {
                SetStatus("Docker is not installed or not in system PATH. Please install Docker Desktop.", Color.yellow);
            }
            else
            {
                SetStatus("Docker prerequisites check complete.", Color.yellow);
            }
        });
    }
    #endregion
    
    #region Cargo.toml Update
    internal void UpdateCargoSpacetimeDBVersion()
    {
        // LogMessages are behind debugMode because this method is more of an extra check
        try
        {
            if (string.IsNullOrEmpty(serverDirectory))
            {
                if (debugMode) LogMessage("Server directory not set. Cannot update Cargo.toml.", 0);
                return;
            }
            
            if (string.IsNullOrEmpty(spacetimeDBCurrentVersionTool))
            {
                if (debugMode) LogMessage("SpacetimeDB tool version not available. Cannot update Cargo.toml.", 0);
                return;
            }
            
            // Convert WSL path to Windows path if needed
            string cargoTomlPath;
            if (serverDirectory.StartsWith("/home/") || serverDirectory.StartsWith("/mnt/"))
            {
                // This is a WSL path, convert to Windows path
                // For WSL paths like /home/username/server, we need to access via \\wsl$\Debian\home\username\server
                string wslPath = serverDirectory.Replace("/", "\\");
                cargoTomlPath = $"\\\\wsl$\\Debian{wslPath}\\Cargo.toml";
            }
            else
            {
                // Assume it's already a Windows path
                cargoTomlPath = Path.Combine(serverDirectory, "Cargo.toml");
            }
            
            // Check if Cargo.toml exists
            if (!File.Exists(cargoTomlPath))
            {
                if (debugMode) LogMessage($"Cargo.toml not found at {cargoTomlPath}. No update needed.", 0);
                return;
            }
            
            // Read the file content
            string content = File.ReadAllText(cargoTomlPath);
            
            // Pattern to match spacetimedb = "x.x.x" (allowing for different quote styles and spacing)
            string pattern = @"spacetimedb\s*=\s*[""']([^""']+)[""']";
            Match match = Regex.Match(content, pattern);
            
            if (match.Success)
            {
                string currentVersion = match.Groups[1].Value.Trim();
                
                if (!string.IsNullOrEmpty(currentVersion) && currentVersion != spacetimeDBCurrentVersionTool)
                {
                    // Replace the version, preserving the original quote style
                    string originalMatch = match.Value;
                    string quoteChar = originalMatch.Contains("\"") ? "\"" : "'";
                    string replacement = $"spacetimedb = {quoteChar}{spacetimeDBCurrentVersionTool}{quoteChar}";
                    string newContent = content.Replace(originalMatch, replacement);
                    
                    // Write back to file
                    File.WriteAllText(cargoTomlPath, newContent);
                    
                    LogMessage($"Updated Cargo.toml spacetimedb version from {currentVersion} to {spacetimeDBCurrentVersionTool}", 1);
                }
                else if (currentVersion == spacetimeDBCurrentVersionTool)
                {
                    if (debugMode) LogMessage($"Cargo.toml spacetimedb version is already up to date ({currentVersion})", 1);
                }
                else
                {
                    if (debugMode) LogMessage("Found spacetimedb dependency but version string is empty", 0);
                }
            }
            else
            {
                if (debugMode) LogMessage("spacetimedb dependency not found in Cargo.toml", 0);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) LogMessage($"Error updating Cargo.toml: {ex.Message}", -1);
        }
    }
    #endregion
    
    #region Docker Installation Methods
    
    private void InstallDocker()
    {
        if (isAssetStoreBuild)
        {
            CheckDocker(); // Asset Store builds only check, provide link to docs
            return;
        }

        // For non-Asset Store builds, call the actual installation process
        InstallDockerInternal();
    }

    private void InstallDockerInternal()
    {
        SetStatus("Opening Docker Desktop download page...", Color.yellow);
        
        // Open Docker Desktop download page based on platform
        string dockerUrl = "https://www.docker.com/products/docker-desktop/";
        
        EditorUtility.DisplayDialog("Install Docker Desktop",
            "Docker Desktop provides containerization for running SpacetimeDB.\n\n" +
            "You will be redirected to the Docker Desktop download page.\n\n" +
            "After installation, please restart this window and click Refresh to verify the installation.",
            "OK");
        
        Application.OpenURL(dockerUrl);
        
        SetStatus("Docker Desktop download page opened. Please install and restart.", Color.green);
    }
    
    private async void PullSpacetimeDBImage()
    {
        if (isAssetStoreBuild)
        {
            CheckDockerImage(); // Asset Store builds only check, provide link to docs
            return;
        }

        // For non-Asset Store builds, call the actual installation process
        await PullSpacetimeDBImageInternal();
    }

    private async Task PullSpacetimeDBImageInternal()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Pulling SpacetimeDB Image", "Starting Docker pull...", 0.1f);
            SetStatus("Pulling SpacetimeDB Docker image...", Color.yellow);
            
            // Use simpler docker pull command instead of docker run
            string imageName = "clockworklabs/spacetime";
            
            if (debugMode)
            {
                LogMessage($"Pulling Docker image: {imageName}", 0);
            }
            
            // Pull the image
            EditorUtility.DisplayProgressBar("Pulling SpacetimeDB Image", "Downloading image (this may take a few minutes)...", 0.3f);
            
            bool pullResult = await Task.Run(() =>
            {
                try
                {
                    using (System.Diagnostics.Process process = new System.Diagnostics.Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = $"pull {imageName}";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = !visibleInstallProcesses;
                        
                        if (debugMode)
                        {
                            UnityEngine.Debug.Log($"[ServerSetupWindow] Executing: docker pull {imageName}");
                        }
                        
                        process.Start();
                        
                        // Read output for progress updates
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        
                        process.WaitForExit();
                        
                        if (debugMode)
                        {
                            if (!string.IsNullOrEmpty(output))
                            {
                                UnityEngine.Debug.Log($"[ServerSetupWindow] Pull output: {output}");
                            }
                            if (!string.IsNullOrEmpty(error))
                            {
                                UnityEngine.Debug.Log($"[ServerSetupWindow] Pull stderr: {error}");
                            }
                        }
                        
                        return process.ExitCode == 0;
                    }
                }
                catch (Exception ex)
                {
                    if (debugMode)
                    {
                        UnityEngine.Debug.LogError($"[ServerSetupWindow] Error during pull: {ex.Message}");
                    }
                    return false;
                }
            });
            
            if (pullResult)
            {
                EditorUtility.DisplayProgressBar("Pulling SpacetimeDB Image", "Verifying image...", 0.8f);
                SetStatus("Verifying image availability...", Color.yellow);
                await Task.Delay(1000); // Brief delay for Docker to update
                
                // Verify the image is actually available
                bool imageExists = await Task.Run(() =>
                {
                    try
                    {
                        using (System.Diagnostics.Process checkProcess = new System.Diagnostics.Process())
                        {
                            checkProcess.StartInfo.FileName = "docker";
                            checkProcess.StartInfo.Arguments = $"images -q {imageName}";
                            checkProcess.StartInfo.RedirectStandardOutput = true;
                            checkProcess.StartInfo.RedirectStandardError = true;
                            checkProcess.StartInfo.UseShellExecute = false;
                            checkProcess.StartInfo.CreateNoWindow = true;
                            
                            checkProcess.Start();
                            string output = checkProcess.StandardOutput.ReadToEnd();
                            string error = checkProcess.StandardError.ReadToEnd();
                            checkProcess.WaitForExit();
                            
                            bool exists = !string.IsNullOrWhiteSpace(output);
                            
                            if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerSetupWindow] Image verification - Exists: {exists}");
                                if (exists)
                                {
                                    UnityEngine.Debug.Log($"[ServerSetupWindow] Image ID: {output.Trim()}");
                                }
                                if (!string.IsNullOrEmpty(error))
                                {
                                    UnityEngine.Debug.Log($"[ServerSetupWindow] Verification stderr: {error}");
                                }
                            }
                            
                            return exists;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (debugMode)
                        {
                            UnityEngine.Debug.LogError($"[ServerSetupWindow] Error checking image: {ex.Message}");
                        }
                        return false;
                    }
                });
                
                EditorUtility.DisplayProgressBar("Pulling SpacetimeDB Image", "Complete!", 1.0f);
                
                if (imageExists)
                {
                    SetStatus("SpacetimeDB Docker image pulled and verified successfully!", Color.green);
                    hasDockerImage = true;
                    CCCPSettingsAdapter.SetHasDockerImage(true);
                }
                else
                {
                    SetStatus("Image pull completed but verification failed. Please click Refresh to check again.", Color.yellow);
                }
            }
            else
            {
                SetStatus("Failed to pull SpacetimeDB Docker image. Check Docker Desktop is running.", Color.red);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error pulling image: {ex.Message}", Color.red);
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerSetupWindow] Pull error: {ex}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            UpdateInstallerItemsStatus();
            Repaint();
        }
    }
    
    /// <summary>
    /// Checks if the Docker container has correct volume mounts for Unity file generation
    /// </summary>
    private bool CheckDockerContainerMounts()
    {
        try
        {
            // Check if container exists
            var (exists, isRunning) = CheckContainerExistsAndRunning();
            
            if (!exists)
            {
                // Container doesn't exist yet - that's okay, it will be created with correct mounts
                return true;
            }
            
            // Inspect the container to check mounts
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "docker";
            process.StartInfo.Arguments = $"inspect {ServerDockerProcess.ContainerName}";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode != 0)
            {
                return false;
            }
            
            // Check if /unity and /app mounts exist in the output
            bool hasUnityMount = output.Contains("/unity");
            bool hasAppMount = output.Contains("/app");
            
            // For proper configuration, we need at least the Unity mount
            // The /app mount is also desirable but not strictly required for all operations
            bool hasProperMounts = hasUnityMount;
            
            if (debugMode)
            {
                UnityEngine.Debug.Log($"[ServerSetupWindow] Container mount check - Unity: {hasUnityMount}, App: {hasAppMount}, Proper: {hasProperMounts}");
            }
            
            return hasProperMounts;
        }
        catch (Exception ex)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerSetupWindow] Error checking container mounts: {ex.Message}");
            }
            return false;
        }
    }
    
    /// <summary>
    /// Helper method to check if container exists and is running
    /// </summary>
    private (bool exists, bool isRunning) CheckContainerExistsAndRunning()
    {
        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "docker";
            process.StartInfo.Arguments = $"ps -a --filter name={ServerDockerProcess.ContainerName} --format \"{{{{.Names}}}}|{{{{.Status}}}}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            
            if (string.IsNullOrEmpty(output))
            {
                return (false, false);
            }
            
            bool isRunning = output.Contains("Up");
            return (true, isRunning);
        }
        catch
        {
            return (false, false);
        }
    }
    
    /// <summary>
    /// Recreates the Docker container with correct volume mounts
    /// </summary>
    private async void ReconfigureDockerContainer()
    {
        try
        {
            SetStatusInternal("Reconfiguring Docker container...", Color.yellow);
            
            // Check if container is running
            var (exists, isRunning) = CheckContainerExistsAndRunning();
            
            if (isRunning)
            {
                bool userConfirmed = EditorUtility.DisplayDialog(
                    "Server Running",
                    "The Docker server is currently running and must be stopped to reconfigure volume mounts.\n\n" +
                    "The container will be removed and recreated with correct volume mounts. " +
                    "No data will be lost as your server files and Unity project are on your Windows filesystem.\n\n" +
                    "Stop server and reconfigure?",
                    "Yes, Reconfigure",
                    "Cancel"
                );
                
                if (!userConfirmed)
                {
                    SetStatusInternal("Container reconfiguration cancelled.", Color.grey);
                    return;
                }
                
                // Stop the server through server manager
                if (serverManager != null)
                {
                    SetStatusInternal("Stopping server...", Color.yellow);
                    serverManager.StopServer();
                    await Task.Delay(2000); // Wait for graceful shutdown
                }
            }
            
            if (exists)
            {
                // Remove the existing container
                SetStatusInternal("Removing old container...", Color.yellow);
                EditorUtility.DisplayProgressBar("Reconfiguring Docker Container", "Removing old container...", 0.3f);
                
                var removeProcess = new System.Diagnostics.Process();
                removeProcess.StartInfo.FileName = "docker";
                removeProcess.StartInfo.Arguments = $"rm -f {ServerDockerProcess.ContainerName}";
                removeProcess.StartInfo.UseShellExecute = false;
                removeProcess.StartInfo.RedirectStandardOutput = true;
                removeProcess.StartInfo.RedirectStandardError = true;
                removeProcess.StartInfo.CreateNoWindow = true;
                
                removeProcess.Start();
                string removeOutput = removeProcess.StandardOutput.ReadToEnd();
                string removeError = removeProcess.StandardError.ReadToEnd();
                removeProcess.WaitForExit();
                
                if (removeProcess.ExitCode != 0)
                {
                    SetStatusInternal($"Failed to remove container: {removeError}", Color.red);
                    EditorUtility.ClearProgressBar();
                    return;
                }
                
                LogMessageInternal("Old container removed successfully", 1);
            }
            
            EditorUtility.DisplayProgressBar("Reconfiguring Docker Container", "Configuration updated", 1.0f);
            
            SetStatusInternal("Container reconfigured! Volume mounts will be applied on next server start.", Color.green);
            LogMessageInternal("Container reconfigured successfully. Volume mounts will be correct on next server start.", 1);
            
            // Update status
            hasDockerContainerMountsConfigured = true;
            UpdateInstallerItemsStatus();
        }
        catch (Exception ex)
        {
            SetStatusInternal($"Error reconfiguring container: {ex.Message}", Color.red);
            LogMessageInternal($"Container reconfiguration error: {ex.Message}", -1);
            
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerSetupWindow] Reconfiguration error: {ex}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }
    
    #endregion

    #region Log Messages
    internal void LogMessageInternal(string message, int type)
    {
        switch (type)
        {
            case -1:
                SetStatusInternal(message, Color.red);
                break;
            case 0:
                SetStatusInternal(message, Color.yellow);
                break;
            case 1:
                SetStatusInternal(message, Color.green);
                break;
            default:
                SetStatusInternal(message, Color.grey);
                break;
        }
        RequestRepaint(); // Always request repaint on status change, but throttled
    }
    
    internal void SetStatusInternal(string message, Color color)
    {
        statusMessage = message;
        statusColor = color;
        statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
        RequestRepaint(); // Always request repaint on status change, but throttled
    }

    private void RequestRepaint()
    {
        double currentTime = EditorApplication.timeSinceStartup;
        if ((currentTime - lastRepaintTime) > minRepaintInterval)
        {
            lastRepaintTime = currentTime;
            Repaint();
        }
    }
    
    // Public methods that can be called by external code
    private void LogMessage(string message, int type)
    {
        LogMessageInternal(message, type);
    }
    
    private void SetStatus(string message, Color color)
    {
        SetStatusInternal(message, color);
    }
    #endregion
    
    #region Asset Store Check-Only Methods
    // These methods are used in Asset Store builds where installation code is not allowed
    // They only check status and provide links to official documentation
    
    private void CheckDocker()
    {
        SetStatus("Docker Desktop required. Please visit: https://spacetimedb.com/install", Color.yellow);
        
        if (EditorUtility.DisplayDialog("Docker Desktop Required",
            "Docker Desktop needs to be installed to run SpacetimeDB in Docker mode.\n\n" +
            "Please visit the official SpacetimeDB installation guide for instructions.\n\n" +
            "After installation, click Refresh to verify.",
            "Open Installation Guide", "Cancel"))
        {
            Application.OpenURL("https://spacetimedb.com/install#docker");
        }
    }
    
    private void CheckDockerImage()
    {
        SetStatus("SpacetimeDB Docker image required. Please visit: https://spacetimedb.com/install", Color.yellow);
        
        if (EditorUtility.DisplayDialog("SpacetimeDB Docker Image Required",
            "The SpacetimeDB Docker image needs to be pulled manually.\n\n" +
            "Please visit the official SpacetimeDB installation guide for instructions.\n\n" +
            "After pulling the image, click Refresh to verify.",
            "Open Installation Guide", "Cancel"))
        {
            Application.OpenURL("https://spacetimedb.com/install#docker");
        }
    }
    #endregion
    
    #region Data Classes
    [Serializable]
    public class InstallerItem
    {
        public string title;
        public string description;
        public bool isInstalled;
        public bool isEnabled = true; // Whether the item is enabled or greyed out
        public Action installAction;
        public bool hasUsernameField = false; // Whether to show a username input field
        public string usernameLabel = "Debian Username:"; // Default label for the username field
        public string expectedModuleName = ""; // Expected module name for database logs service
        public string sectionHeader = ""; // Optional section header to display before this item
    }
    #endregion
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
