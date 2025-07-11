using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Check and install everything necessary to run SpacetimeDB with this window ///

namespace NorthernRogue.CCCP.Editor {

public class ServerInstallerWindow : EditorWindow
{
    private List<InstallerItem> installerItems = new List<InstallerItem>();
    private List<InstallerItem> customInstallerItems = new List<InstallerItem>();
    private ServerCMDProcess cmdProcess;
    private ServerCustomProcess customProcess;
    private ServerManager serverManager;

    // UI
    private Vector2 scrollPosition;
    private string statusMessage = "Ready to install components.";
    private bool userNamePrompt = false;
    private bool showUpdateButton = false;
    private Color statusColor = Color.grey;
    private string statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
    private double lastRepaintTime = 0;
    private const double minRepaintInterval = 0.5; // Minimum time between repaints in seconds
    
    // Tab selection
    private int currentTab; // 0 = WSL Installer, 1 = Custom Debian Installer
    private readonly string[] tabNames = { "WSL Server Installer", "Custom Server Installer" };

    // EditorPrefs
    private string userName = ""; // For WSL mode
    private string sshUserName = ""; // For SSH remote server mode

    // Temporary fields for username input
    private string tempUserNameInput = "";
    private string tempCreateUserNameInput = ""; // For creating new user on remote SSH server

    private string spacetimeDBCurrentVersion = "";
    private string spacetimeDBCurrentVersionCustom = "";
    private string spacetimeDBLatestVersion = "";
    
    // Styles
    private GUIStyle titleStyle;
    private GUIStyle itemTitleStyle;
    private GUIStyle installedStyle;
    private GUIStyle installButtonStyle;
    private bool stylesInitialized = false;
    
    // WSL Installation states
    private bool isRefreshing = false;
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
    private bool hasSpacetimeDBUnitySDK = false;

    // Custom SSH installation states
    private bool isCustomRefreshing = false;
    private bool hasCustomDebianUser = false;
    private bool hasCustomDebianTrixie = false;
    private bool hasCustomCurl = false;
    private bool hasCustomSpacetimeDBServer = false;
    private bool hasCustomSpacetimeDBPath = false;
    private bool hasCustomRust = false;
    private bool hasCustomBinaryen = false;
    private bool hasCustomSpacetimeDBService = false;
    private bool hasCustomSpacetimeDBLogsService = false;

    private bool isConnectedSSH = false;
    
    // WSL 1 requires unique install logic for Debian apps
    private bool WSL1Installed;

    // Debug
    private bool visibleInstallProcesses = true;
    private bool keepWindowOpenForDebug = true;

    // Debug install process
    private bool alwaysShowInstall = false;
    private bool installIfAlreadyInstalled = false;
    private bool forceInstall = false; // Will toggle both alwaysShowInstall and installIfAlreadyInstalled
    
    // Settings
    private const string PrefsKeyPrefix = "CCCP_"; // Use the same prefix as ServerWindow
    private const string FirstTimeOpenKey = "FirstTimeOpen";    

    [MenuItem("SpacetimeDB/Server Installer", priority = -10001)]
    public static void ShowWindow()
    {
        ServerInstallerWindow window = GetWindow<ServerInstallerWindow>("Server Installer");
        window.minSize = new Vector2(500, 400);
        window.currentTab = 0; // Default to WSL tab
        window.InitializeInstallerItems();
        window.CheckInstallationStatus();
    }
    
    public static void ShowCustomWindow()
    {
        ServerInstallerWindow window = GetWindow<ServerInstallerWindow>("Server Installer");
        window.minSize = new Vector2(500, 400);
        window.currentTab = 1; // Set to Custom SSH tab
        window.InitializeInstallerItems();
        window.CheckCustomInstallationStatus();
    }

    #region OnEnable
    private void OnEnable()
    {
        // Initialize both processes
        cmdProcess = new ServerCMDProcess(LogMessage, false);
        customProcess = new ServerCustomProcess(LogMessage, false);
        serverManager = new ServerManager(LogMessage, () => Repaint());
        
        // Check if this is the first time the window is opened
        bool isFirstTime = !EditorPrefs.HasKey(PrefsKeyPrefix+FirstTimeOpenKey);
        if (isFirstTime)
        {
            // Show first-time information dialog
            EditorApplication.delayCall += () => {
                bool continuePressed = EditorUtility.DisplayDialog(
                    "SpacetimeDB Automatic Installer",
                    "Welcome to the automatic installer window that can check and install everything needed for your Windows PC to run SpacetimeDB from the ground up.\n" +
                    "Start from the top and click the install commands. The installer will gray out the steps that you can't yet complete, before the previous steps are installed.\n\n" +
                    "All named software in this window is official and publicly available software that belongs to the respective parties and is provided by them for free.\n" +
                    "The installer window works by entering the same commands as in the manual installation process for the purpose of ease of use.",
                    "Continue", "Documentation");
                
                if (!continuePressed) {
                    Application.OpenURL(ServerWindow.Documentation);
                }

                EditorPrefs.SetBool(PrefsKeyPrefix+FirstTimeOpenKey, true);
            };
        }
        
        // Load WSL installation status from EditorPrefs
        hasWSL = EditorPrefs.GetBool(PrefsKeyPrefix + "HasWSL", false);
        hasDebian = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebian", false);
        hasDebianTrixie = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebianTrixie", false);
        hasCurl = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCurl", false);
        hasSpacetimeDBServer = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", false);
        hasSpacetimeDBPath = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", false);
        hasSpacetimeDBService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBService", false);
        hasSpacetimeDBLogsService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBLogsService", false);
        hasRust = EditorPrefs.GetBool(PrefsKeyPrefix + "HasRust", false);
        hasSpacetimeDBUnitySDK = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBUnitySDK", false);

        // Load Custom SSH installation status from EditorPrefs
        hasCustomDebianUser = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomDebianUser", false);
        hasCustomDebianTrixie = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomDebianTrixie", false);
        hasCustomCurl = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomCurl", false);
        hasCustomSpacetimeDBServer = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBServer", false);
        hasCustomSpacetimeDBPath = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBPath", false);
        hasCustomRust = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomRust", false);
        hasCustomBinaryen = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomBinaryen", false);
        hasCustomSpacetimeDBService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBService", false);
        hasCustomSpacetimeDBLogsService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBLogsService", false);

        // WSL 1 requires unique install logic for Debian apps
        WSL1Installed = EditorPrefs.GetBool(PrefsKeyPrefix + "WSL1Installed", false);
        
        // Load install debug settings from EditorPrefs
        visibleInstallProcesses = EditorPrefs.GetBool(PrefsKeyPrefix + "VisibleInstallProcesses", true);
        keepWindowOpenForDebug = EditorPrefs.GetBool(PrefsKeyPrefix + "KeepWindowOpenForDebug", true);
        
        // Cache the current username
        userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        sshUserName = EditorPrefs.GetString(PrefsKeyPrefix + "SSHUserName", "");
        tempUserNameInput = userName; // Initialize the temp input with the stored username for WSL
        tempCreateUserNameInput = ""; // Initialize empty for the "Create User" functionality
        
        // Load version info of SpacetimeDB
        spacetimeDBCurrentVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersion", "");
        spacetimeDBCurrentVersionCustom = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersionCustom", "");
        spacetimeDBLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBLatestVersion", "");
        
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
            sshUserName = EditorPrefs.GetString(PrefsKeyPrefix + "SSHUserName", "");
        }
    }
    
    private void OnDisable()
    {
        // Clean up the update callback when the window is closed
        EditorApplication.update -= OnEditorUpdate;
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
                installAction = InstallWSLDebian
            },
            new InstallerItem
            {
                title = "Install Debian Trixie Update",
                description = "Debian Trixie Update (Debian Version 13)\n"+
                "Required to run the SpacetimeDB Server\n"+
                "Note: If it reports as failed, you may have to restart your PC since the first WSL step\n"+
                "Note: May take some minutes to install",
                isInstalled = hasDebianTrixie,
                isEnabled = hasWSL && hasDebian && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = InstallDebianTrixie
            },
            new InstallerItem
            {
                title = "Install cURL",
                description = "cURL is a command-line tool for transferring data with URLs\n"+
                "Required to install the SpacetimeDB Server",
                isInstalled = hasCurl,
                isEnabled = hasWSL && hasDebian && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = InstallCurl
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Server",
                description = "SpacetimeDB Server Installation for Debian\n"+
                "Note: Only supports installing to the users home directory (SpacetimeDB default)",
                isInstalled = hasSpacetimeDBServer,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = InstallSpacetimeDBServer
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB PATH",
                description = "Add SpacetimeDB to the PATH environment variable of your Debian user",
                isInstalled = hasSpacetimeDBPath,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = InstallSpacetimeDBPath
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Service",
                description = "Install SpacetimeDB as a system service that automatically starts on server boot\n" +
                              "Note: Also creates a lightweight logs service to capture SpacetimeDB database logs",
                isInstalled = hasSpacetimeDBService,
                isEnabled = hasWSL && hasDebian && hasSpacetimeDBServer && !String.IsNullOrEmpty(userName),
                installAction = InstallSpacetimeDBService,
                expectedModuleName = EditorPrefs.GetString(PrefsKeyPrefix + "ModuleName", "") // Load from prefs or use default
            },
            new InstallerItem
            {
                title = "Install Rust",
                description = "Rust is a programming language that runs 2x faster than C#\n"+
                "Note: Required to use the SpacetimeDB Server with Rust Language",
                isInstalled = hasRust,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = InstallRust
            },
            new InstallerItem
            {
                title = "Install Web Assembly Optimizer Binaryen",
                description = "Binaryen is a compiler toolkit for WebAssembly\n"+
                "SpacetimeDB can use wasm-opt optimizer for WebAssembly modules improving performance",
                isInstalled = hasBinaryen,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = InstallBinaryen
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Unity SDK",
                description = "SpacetimeDB SDK contains essential scripts for SpacetimeDB development in Unity \n"+
                "Examples include a network manager that syncs the client state with the database",
                isInstalled = hasSpacetimeDBUnitySDK,
                isEnabled = true, // Always enabled as it doesn't depend on WSL
                installAction = InstallSpacetimeDBUnitySDK
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
                installAction = InstallCustomUser,
                hasUsernameField = true,
                usernameLabel = "Create Username:"
            },
            new InstallerItem
            {
                title = "Install Debian Trixie Update",
                description = "Debian Trixie Update (Debian Version 13)\n"+
                "Required to run the SpacetimeDB Server\n"+
                "Note: May take some minutes to install",
                isInstalled = hasCustomDebianTrixie,
                isEnabled = customProcess.IsSessionActive() && !String.IsNullOrEmpty(sshUserName),
                installAction = InstallCustomDebianTrixie
            },
            new InstallerItem
            {
                title = "Install cURL",
                description = "cURL is a command-line tool for transferring data with URLs\n"+
                "Required to install the SpacetimeDB Server",
                isInstalled = hasCustomCurl,
                isEnabled = customProcess.IsSessionActive() && !String.IsNullOrEmpty(userName),
                installAction = InstallCustomCurl
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Server",
                description = "SpacetimeDB Server Installation for Debian\n"+
                "Note: Will install to the current SSH user session home directory (SpacetimedDB default)",
                isInstalled = hasCustomSpacetimeDBServer,
                isEnabled = customProcess.IsSessionActive() && hasCustomCurl && !String.IsNullOrEmpty(userName),
                installAction = InstallCustomSpacetimeDBServer
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB PATH",
                description = "Add SpacetimeDB to the PATH environment variable of your Debian user",
                isInstalled = hasCustomSpacetimeDBPath,
                isEnabled = customProcess.IsSessionActive() && hasCustomCurl && !String.IsNullOrEmpty(userName),
                installAction = InstallCustomSpacetimeDBPath
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Service",
                description = "Install SpacetimeDB as a system service that automatically starts on boot\n" +
                              "Creates a systemd service file to run SpacetimeDB in the background\n" +
                              "Note: Also creates a lightweight logs service to capture SpacetimeDB database logs",
                isInstalled = hasCustomSpacetimeDBService,
                isEnabled = customProcess.IsSessionActive() && hasCustomSpacetimeDBServer && !String.IsNullOrEmpty(userName),
                installAction = InstallCustomSpacetimeDBService,
                expectedModuleName = EditorPrefs.GetString(PrefsKeyPrefix + "ModuleName", "") // Load from prefs or use default
            },
            new InstallerItem
            {
                title = "Install Rust",
                description = "Rust is a programming language that runs 2x faster than C#\n"+
                "Note: Required to use the SpacetimeDB Server with Rust Language",
                isInstalled = hasCustomRust,
                isEnabled = customProcess.IsSessionActive() && hasCustomCurl && !String.IsNullOrEmpty(userName),
                installAction = InstallCustomRust
            },
            new InstallerItem
            {
                title = "Install Web Assembly Optimizer Binaryen",
                description = "Binaryen is a compiler toolkit for WebAssembly\n"+
                "SpacetimeDB can use wasm-opt optimizer for WebAssembly modules improving performance",
                isInstalled = hasCustomBinaryen,
                isEnabled = customProcess.IsSessionActive() && hasCustomCurl && !String.IsNullOrEmpty(userName),
                installAction = InstallCustomBinaryen
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Unity SDK",
                description = "SpacetimeDB SDK contains essential scripts for SpacetimeDB development in Unity \n"+
                "Examples include a network manager that syncs the client state with the database",
                isInstalled = hasSpacetimeDBUnitySDK,
                isEnabled = true, // Always enabled as it doesn't depend on Custom SSH
                installAction = InstallSpacetimeDBUnitySDK
            }
        };
    }

    private void UpdateInstallerItemsStatus()
    {
        bool repaintNeeded = false;

        // Update the correct list based on current tab
        List<InstallerItem> itemsToUpdate = currentTab == 0 ? installerItems : customInstallerItems;
        
        // For WSL installer items
        if (currentTab == 0) {
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
                else if (item.title.Contains("Web Assembly Optimizer Binaryen"))
                {
                    newState = hasBinaryen;
                    newEnabledState = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName);
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
        else {
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
                else if (item.title.Contains("Rust"))
                {
                    newState = hasCustomRust;
                    newEnabledState = isSessionActive && hasCustomCurl && !String.IsNullOrEmpty(sshUserName);
                }
                else if (item.title.Contains("Web Assembly Optimizer Binaryen"))
                {
                    newState = hasCustomBinaryen;
                    newEnabledState = isSessionActive && hasCustomCurl && !String.IsNullOrEmpty(sshUserName);
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
            if (currentTab == 0) {
                CheckInstallationStatus();
            } else {
                CheckCustomInstallationStatus();
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
        
        stylesInitialized = true;
    }
    
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        // Refresh Button
        EditorGUI.BeginDisabledGroup(currentTab == 0 ? isRefreshing : isCustomRefreshing);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            if (currentTab == 0) {
                CheckInstallationStatus();
            } else {
                CheckCustomInstallationStatus();
                sshUserName = EditorPrefs.GetString(PrefsKeyPrefix + "SSHUserName", "");
            }
        }
        EditorGUI.EndDisabledGroup();
               
        // Debug Dropdown
        GUIContent debugContent = new GUIContent("Debug");
        if (EditorGUILayout.DropdownButton(debugContent, FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(60)))
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Show Install Windows"), visibleInstallProcesses, () => {
                visibleInstallProcesses = !visibleInstallProcesses;
                EditorPrefs.SetBool(PrefsKeyPrefix + "VisibleInstallProcesses", visibleInstallProcesses);
            });

            menu.AddItem(new GUIContent("Keep Windows Open"), keepWindowOpenForDebug, () => {
                keepWindowOpenForDebug = !keepWindowOpenForDebug;
                EditorPrefs.SetBool(PrefsKeyPrefix + "KeepWindowOpenForDebug", keepWindowOpenForDebug);
            });
            
            menu.AddItem(new GUIContent("Force Install"), forceInstall, () => {
                forceInstall = !forceInstall;
                // Update dependent flags when forceInstall changes
                alwaysShowInstall = forceInstall;
                installIfAlreadyInstalled = forceInstall;
                // No EditorPrefs for forceInstall as it's a transient state for the session
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

        GUILayout.Label(currentTab == 0 ? "SpacetimeDB Local WSL Server Installer" : "SpacetimeDB Remote Custom Server Installer", titleStyle);
        
        string description = currentTab == 0 ? 
            "Install all the required software to run your local SpacetimeDB Server in WSL from the ground up.\n" +
            "This will give you a local CLI for spacetime commands. Required for all modes to be able to publish." :
            "Install all the required software to run SpacetimeDB Server on a remote Debian machine via SSH.\n" +
            "This has been tested to work on a fresh Debian 12 server, VM or VPS instance from the ground up.";

        EditorGUILayout.LabelField(description,
            EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30));

        // Show usernameprompt for clarity before SpacetimeDB install
        bool showUsernamePrompt = String.IsNullOrEmpty(userName) && 
            (currentTab == 0 ? (hasWSL && hasDebian) : customProcess.IsSessionActive());
        
        if (showUsernamePrompt)
        {
            List<InstallerItem> itemsToUpdate = currentTab == 0 ? installerItems : customInstallerItems;
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
                userName = tempUserNameInput;
                EditorPrefs.SetString(PrefsKeyPrefix + "UserName", userName);
                foreach (var item in itemsToUpdate) item.isEnabled = true;
                userNamePrompt = false;
                UnityEngine.Debug.Log("Username submitted via Enter: " + userName);
                
                // Use the current event to prevent it from propagating
                e.Use();

                if (currentTab == 0) {
                    CheckInstallationStatus();
                } else {
                    CheckCustomInstallationStatus();
                }
            }
            
            // Add a submit button for clarity
            if (GUILayout.Button("Set", GUILayout.Width(50)) && !string.IsNullOrEmpty(tempUserNameInput))
            {
                // Submit the username only on button click
                userName = tempUserNameInput;
                EditorPrefs.SetString(PrefsKeyPrefix + "UserName", userName);
                foreach (var item in itemsToUpdate) item.isEnabled = true;
                userNamePrompt = false;
                UnityEngine.Debug.Log("Username submitted via button: " + userName);

                if (currentTab == 0) {
                    CheckInstallationStatus();
                } else {
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
        List<InstallerItem> displayItems = currentTab == 0 ? installerItems : customInstallerItems;
        
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
            showUpdateButton = item.title.Contains("SpacetimeDB Server") && 
                                    !string.IsNullOrEmpty(spacetimeDBCurrentVersion) && 
                                    !string.IsNullOrEmpty(spacetimeDBLatestVersion) && 
                                    spacetimeDBCurrentVersion != spacetimeDBLatestVersion;
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
            if (GUILayout.Button("Update to v"+spacetimeDBLatestVersion, installButtonStyle, GUILayout.Width(100), GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () => {
                    item.installAction?.Invoke();
                };
            }
            EditorGUI.EndDisabledGroup();
        }
        else if (item.isInstalled && !alwaysShowInstall)
        {
            EditorGUILayout.LabelField("✓ Installed", installedStyle, GUILayout.Width(100));
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
            EditorGUILayout.BeginHorizontal();
            string labelText = item.hasUsernameField ? item.usernameLabel : "Install as Username:";
            EditorGUILayout.LabelField(labelText, GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            
            if (currentTab == 0) // For WSL mode, use the regular username
            {
                string newUserName = EditorGUILayout.TextField(userName, GUILayout.Width(150));
                if (EditorGUI.EndChangeCheck() && newUserName != userName)
                {
                    userName = newUserName;
                    EditorPrefs.SetString(PrefsKeyPrefix + "UserName", userName);
                }
            }
            else if (currentTab == 1)
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
                        sshUserName = newUserName;
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }
        
        // Add module name field for SpacetimeDB Service installer
        if (item.title == "Install SpacetimeDB Service" && !string.IsNullOrEmpty(item.expectedModuleName))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Module Name:", GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            EditorGUI.BeginDisabledGroup(true);
            string newModuleName = EditorGUILayout.TextField(item.expectedModuleName, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck() && newModuleName != item.expectedModuleName)
            {
                item.expectedModuleName = newModuleName;
                // Save to EditorPrefs for persistence
                EditorPrefs.SetString(PrefsKeyPrefix + "ModuleName", item.expectedModuleName);
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
    private async void CheckInstallationStatus()
    {
        if (isRefreshing) return; // Don't start a new refresh if one is already running
        
        isRefreshing = true;
        SetStatus("Checking WSL installation status...", Color.yellow);
        
        // Check for SpacetimeDB Unity SDK separately
        ServerSpacetimeSDKInstaller.IsSDKInstalled((isSDKInstalled) => {
            hasSpacetimeDBUnitySDK = isSDKInstalled;
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBUnitySDK", hasSpacetimeDBUnitySDK);
            UpdateInstallerItemsStatus();
        });
        
        cmdProcess.CheckPrerequisites((wsl, debian, trixie, curl, spacetime, spacetimePath, rust, spacetimeService, spacetimeLogsService, binaryen) => {
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
            
            // Save state to EditorPrefs
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasWSL", hasWSL);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebian", hasDebian);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebianTrixie", hasDebianTrixie);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasCurl", hasCurl);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", hasSpacetimeDBServer);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", hasSpacetimeDBPath);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBService", hasSpacetimeDBService);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBLogsService", hasSpacetimeDBLogsService);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasRust", hasRust);
            EditorPrefs.SetBool(PrefsKeyPrefix + "VisibleInstallProcesses", visibleInstallProcesses);
        });

        // Check SpacetimeDB version to update it if it was updated in the installer
        await serverManager.CheckSpacetimeDBVersion();

        // Update UI
        UpdateInstallerItemsStatus();
        
        isRefreshing = false;
        SetStatus("WSL installation status updated.", Color.green); // This might request repaint (throttled)
    }
    
    private async void CheckCustomInstallationStatus()
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
            hasCustomRust = false;
            hasCustomBinaryen = false;
            
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
            hasCustomRust = false;
            hasCustomBinaryen = false;
            
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
        
        // Check Rust - Verify rustc and cargo are actually installed and working
        SetStatus("Checking Rust status...", Color.yellow);
        var rustResult = await customProcess.RunCustomCommandAsync($"bash -l -c 'which rustc && which cargo' 2>/dev/null");
        hasCustomRust = rustResult.success && !string.IsNullOrEmpty(rustResult.output) && rustResult.output.Contains("rustc") && rustResult.output.Contains("cargo");
        await Task.Delay(100);

        // Check Binaryen - Check if wasm-opt is installed
        SetStatus("Checking Binaryen status...", Color.yellow);
        var binaryenResult = await customProcess.RunCustomCommandAsync("test -f '/usr/local/bin/wasm-opt' && echo 'found' || echo 'not_found'");
        hasCustomBinaryen = binaryenResult.success && binaryenResult.output.Trim() == "found";
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
        
        // Save installation status to EditorPrefs
        EditorPrefs.SetBool(PrefsKeyPrefix + "HasCustomDebianUser", hasCustomDebianUser);
        EditorPrefs.SetBool(PrefsKeyPrefix + "HasCustomDebianTrixie", hasCustomDebianTrixie);
        EditorPrefs.SetBool(PrefsKeyPrefix + "HasCustomCurl", hasCustomCurl);
        EditorPrefs.SetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBServer", hasCustomSpacetimeDBServer);
        EditorPrefs.SetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBPath", hasCustomSpacetimeDBPath);
        EditorPrefs.SetBool(PrefsKeyPrefix + "HasCustomRust", hasCustomRust);
        EditorPrefs.SetBool(PrefsKeyPrefix + "HasCustomBinaryen", hasCustomBinaryen);
        EditorPrefs.SetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBService", hasCustomSpacetimeDBService);
        EditorPrefs.SetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBLogsService", hasCustomSpacetimeDBLogsService);
        
        // Update SpacetimeDB version for custom installation
        await customProcess.CheckSpacetimeDBVersionCustom();

        // Update UI
        UpdateInstallerItemsStatus();
        
        isCustomRefreshing = false;
        SetStatus("Remote installation status check complete.", Color.green);
    }
    #endregion
    
    #region Installation Methods
    private async void InstallWSLDebian()
    {
        CheckInstallationStatus();
        if (hasWSL && hasDebian && !installIfAlreadyInstalled)
        {
            SetStatus("WSL2 with Debian is already installed.", Color.green);
            return;
        }

        EditorUtility.DisplayDialog("About WSL1 and WSL2", 
        "WSL1 and WSL2 allows you to run a Linux distribution within Windows. This allows SpacetimeDB to run silently and be more easily controlled when running locally.\n\n" +
        "WSL2 is the latest and recommended version.\n" +
        "WSL1 has better compability with some systems.\n\n" +
        "Cosmos Cove Control Panel will now run a compability test to determine if your PC supports Virtualization and Hyper-V which is necessary for WSL2.\n\n"
        , "OK");

        bool installedSuccessfully = false;

        // Define installation actions for WSL1 and WSL2
        Action installWSL1 = async () => 
        {
            SetStatus("Installing WSL1 with Debian...", Color.green);
            
            if (EditorUtility.DisplayDialog("Install WSL1 with Debian", "This will install WSL1 with Debian. You may have to press keys during the install process. Do you want to continue?", "Yes", "No"))
            {
                string dismCommand = "powershell.exe -Command \"Start-Process powershell -Verb RunAs -ArgumentList '-Command dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart'\"";
                string wsl1SetupCommand = "cmd.exe /c \"wsl --update & wsl --set-default-version 1 && wsl --install -d Debian\"";

                bool dismSuccess = await cmdProcess.RunPowerShellInstallCommand(dismCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug, true);
                if (dismSuccess)
                {
                    SetStatus("DISM successful. Proceeding with WSL1 setup...", Color.yellow);
                    installedSuccessfully = await cmdProcess.RunPowerShellInstallCommand(wsl1SetupCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug, true);
                }
                else
                {
                    installedSuccessfully = false;
                    SetStatus("DISM command failed for WSL1 setup. Please check console output.", Color.red);
                }

                if (installedSuccessfully)
                {
                    CheckInstallationStatus();
                    await Task.Delay(1000);
                    if (hasWSL && hasDebian)
                    {
                        SetStatus("WSL1 with Debian installed successfully.", Color.green);

                        WSL1Installed = true; // To handle WSL1 Debian installs uniquely
                        EditorPrefs.SetBool(PrefsKeyPrefix + "WSL1Installed", WSL1Installed);

                        UpdateInstallerItemsStatus();

                        // Display dialog informing user about Debian first-time setup
                        EditorUtility.DisplayDialog(
                            "Debian First-Time Setup",
                            "Starting Debian for the first time so you can create your user credentials. You can close the Debian window afterwards.",
                            "OK"
                        );
                        // Launch visible Debian window without username required
                        bool userNameReq = false;
                        cmdProcess.OpenDebianWindow(userNameReq);
                    }
                    else
                    {
                        SetStatus("WSL1 with Debian installation failed or requires a restart. Please check console output and restart if prompted.", Color.red);
                    }
                } else {
                    if(dismSuccess) // Only wsl1SetupCommand failed - common if DISM requires a restart
                    {
                        SetStatus("Please restart your PC and try to install WSL1 again.", Color.yellow);
                        EditorUtility.DisplayDialog("Restart Needed","Please restart your PC and Unity and try to install WSL1 again.", "OK");
                    }
                }
            } else {
                SetStatus("WSL1 with Debian installation cancelled.", Color.yellow);
            }
        };
        
        Action installWSL2 = async () => 
        {
            SetStatus("Installing WSL2 with Debian...", Color.green);
            
            if (EditorUtility.DisplayDialog("Install WSL2 with Debian", "This will install WSL2 with Debian. Do you want to continue?", "Yes", "No"))
            {
                string wsl2InstallCommand = "cmd.exe /c \"wsl --update & wsl --set-default-version 2 && wsl --install -d Debian\"";
                installedSuccessfully = await cmdProcess.RunPowerShellInstallCommand(wsl2InstallCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug, true);
                if (installedSuccessfully)
                {
                    await Task.Delay(5000);
                    CheckInstallationStatus();
                    if (hasWSL && hasDebian)
                    {
                        SetStatus("WSL2 with Debian installed successfully.", Color.green);

                        // Display dialog informing user about Debian first-time setup
                        EditorUtility.DisplayDialog(
                            "Debian First-Time Setup",
                            "Starting Debian for the first time so you can create your user credentials. You can close the Debian window afterwards.",
                            "OK"
                        );
                        // Launch visible Debian window without username required
                        bool userNameReq = false;
                        cmdProcess.OpenDebianWindow(userNameReq);
                    } else {
                        EditorUtility.DisplayDialog(
                            "Restart and Try Again",
                            "Sometimes the WSL install process requires a system restart.\n\n" +
                            "Please restart your PC and Unity and try to install WSL2 with Debian again.",
                            "OK"
                        );
                        SetStatus("WSL2 with Debian installation failed or requires a restart.", Color.red);
                    }
                } else {
                    EditorUtility.DisplayDialog(
                        "Restart and Try Again",
                        "Sometimes the WSL install process requires a system restart.\n\n" +
                        "Please restart your PC and Unity and try to install WSL2 with Debian again.",
                        "OK"
                    );
                    SetStatus("WSL2 with Debian installation failed or requires a restart.", Color.red);
                }
            } else {
                SetStatus("WSL2 with Debian installation cancelled.", Color.yellow);
            }
        };

        // Call CheckWSL2Support and wait for the user to make a choice in the dialog
        // The actual installation will happen when the user clicks one of the buttons in the dialog,
        // which will invoke either installWSL1 or installWSL2
        await ServerCompabilityReport.CheckWSL2Support(true, installWSL1, installWSL2);
    }

    private async void InstallDebianTrixie()
    {
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (hasDebianTrixie && !installIfAlreadyInstalled)
        {
            SetStatus("Debian Trixie Update is already installed.", Color.green);
            return;
        }
        
        SetStatus("Installing Debian Trixie Update - Step 1: apt update", Color.yellow);
        
        // Step 1: Update
        string updateCommand = "wsl -d Debian -u root bash -c \"sudo apt update\"";
        bool updateSuccess = await cmdProcess.RunPowerShellInstallCommand(updateCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!updateSuccess)
        {
            SetStatus("Failed to update Debian. Trixie installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000); // Wait longer to ensure command completes
        
        // Step 2: Upgrade
        SetStatus("Installing Debian Trixie Update - Step 2: apt upgrade", Color.yellow);
        string upgradeCommand = "wsl -d Debian -u root bash -c \"sudo apt upgrade -y\"";
        bool upgradeSuccess = await cmdProcess.RunPowerShellInstallCommand(upgradeCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!upgradeSuccess)
        {
            // It's common to get a failed install here, but the install process will work anyway
            SetStatus("Failed to upgrade for Trixie install. Attempting to continue.", Color.green);
        }
        await Task.Delay(2000);
        
        // Step 3: Install update-manager-core
        SetStatus("Installing Debian Trixie Update - Step 3: install update-manager-core", Color.yellow);
        string coreCommand = "wsl -d Debian -u root bash -c \"sudo apt install -y update-manager-core\"";
        bool coreSuccess = await cmdProcess.RunPowerShellInstallCommand(coreCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!coreSuccess)
        {
            // It's common to get a failed install here, but the install process will work anyway
            SetStatus("Failed to install update-manager-core. Attempting to continue.", Color.green);
        }
        await Task.Delay(2000);
        
        // Step 4: Change sources.list to trixie
        SetStatus("Installing Debian Trixie Update - Step 4: update sources to Trixie", Color.yellow);
        string sourcesCommand = "wsl -d Debian -u root bash -c \"sudo sed -i 's/bookworm/trixie/g' /etc/apt/sources.list\"";
        bool sourcesSuccess = await cmdProcess.RunPowerShellInstallCommand(sourcesCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!sourcesSuccess)
        {
            SetStatus("Failed to update sources.list. Trixie installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Step 5: Update again for Trixie
        SetStatus("Installing Debian Trixie Update - Step 5: update package lists for Trixie", Color.yellow);
        string updateTrixieCommand = "wsl -d Debian -u root bash -c \"sudo apt update\"";
        bool updateTrixieSuccess = await cmdProcess.RunPowerShellInstallCommand(updateTrixieCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!updateTrixieSuccess)
        {
            SetStatus("Failed to update package lists for Trixie. Installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Step 6: Full upgrade
        SetStatus("Installing Debian Trixie Update - Step 6: performing full upgrade to Trixie", Color.yellow);
        string fullUpgradeCommand = "wsl -d Debian -u root bash -c \"sudo apt full-upgrade -y\"";
        bool fullUpgradeSuccess = await cmdProcess.RunPowerShellInstallCommand(fullUpgradeCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!fullUpgradeSuccess)
        {
            CheckInstallationStatus();
            await Task.Delay(1000);
            if (WSL1Installed && hasDebianTrixie)
            SetStatus("Debian Trixie Update installed successfully. (WSL1)", Color.green);
            else
            SetStatus("Failed to perform full upgrade to Trixie.", Color.red);

            return;
        }
        await Task.Delay(2000);
        
        SetStatus("Debian Trixie Update installed. Shutting down WSL...", Color.green);

        // WSL Shutdown
        cmdProcess.ShutdownWsl();
        await Task.Delay(3000); // Longer wait for shutdown

        // Restart WSL
        cmdProcess.StartWsl();
        SetStatus("WSL restarted. Checking installation status...", Color.green);
        await Task.Delay(5000); // Longer wait for startup
        CheckInstallationStatus();
        await Task.Delay(1000);
        if (hasDebianTrixie)
        {
            SetStatus("Debian Trixie Update installed successfully.", Color.green);
        }
        else
        {
            SetStatus("Debian Trixie Update installation failed. Please check logs.", Color.red);
        }
    }
    
    private async void InstallCurl()
    {
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (hasCurl && !installIfAlreadyInstalled)
        {
            SetStatus("curl is already installed.", Color.green);
            return;
        }
                
        SetStatus("Installing curl...", Color.green);
        
        string updateCommand = $"wsl -d Debian -u root bash -c \"echo 'Updating package lists...' && sudo apt update\"";
        SetStatus("Running: apt update", Color.yellow);
        bool updateSuccess = await cmdProcess.RunPowerShellInstallCommand(
            updateCommand, 
            LogMessage, 
            visibleInstallProcesses, 
            keepWindowOpenForDebug
        );
        if (!updateSuccess)
        {
            SetStatus("Failed to update package list. Curl installation aborted.", Color.red);
            return;
        }
        
        // Delay between commands to ensure UI updates
        await Task.Delay(1000);
        
        // Now install curl
        string installCommand = $"wsl -d Debian -u root bash -c \"echo 'Installing curl...' && sudo apt install -y curl\"";
        SetStatus("Running: apt install -y curl", Color.yellow);
        bool installSuccess = await cmdProcess.RunPowerShellInstallCommand(
            installCommand, 
            LogMessage, 
            visibleInstallProcesses, 
            keepWindowOpenForDebug
        );
        if (!installSuccess)
        {
            CheckInstallationStatus();
            await Task.Delay(2000);
            if (WSL1Installed && hasCurl)
            SetStatus("cURL installed successfully. (WSL1)", Color.green);
            else
            SetStatus("Failed to install cURL. Installation aborted.", Color.red);
            
            return;
        }
        
        // Check installation status
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (hasCurl)
        {
            SetStatus("cURL installed successfully.", Color.green);
        }
        else
        {
            SetStatus("cURL installation failed. Please install manually.", Color.red);
        }
    }
    
    private async void InstallSpacetimeDBServer()
    {
        // Requires visible install processes and keep window open
        // Because the user has to interact with the installer window
        // Check if we can add yes to the command to auto-answer
        visibleInstallProcesses = true;
        keepWindowOpenForDebug = true;

        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (!hasWSL)
        {
            SetStatus("WSL2 with Debian is required to install SpacetimeDB Server. Please install WSL2 with Debian first.", Color.red);
            return;
        }
        if (!hasDebianTrixie)
        {
            SetStatus("Debian Trixie Update is required to install SpacetimeDB Server. Please install Debian Trixie Update first.", Color.red);
            return;
        }
        if (!hasCurl )
        {
            SetStatus("curl is required to install SpacetimeDB Server. Please install curl first.", Color.red);
            return;
        }
        if (string.IsNullOrEmpty(userName))
        {
            SetStatus("Please enter your Debian username to install SpacetimeDB Server.", Color.red);
            return;
        }
        if (hasSpacetimeDBServer && (spacetimeDBLatestVersion == spacetimeDBCurrentVersion) && !installIfAlreadyInstalled)
        {
            SetStatus("The latest version of SpacetimeDB Server is already installed.", Color.green);
            return;
        }
        
        SetStatus("Installing SpacetimeDB Server...", Color.green);
        
        // Command to install SpacetimeDB Server
        string spacetimeInstallCommand = $"wsl -d Debian -u " + userName + " bash -c \"echo y | curl -sSf https://install.spacetimedb.com | sh\"";

        // Use the ServerCMDProcess method to run the PowerShell command
        bool success = await cmdProcess.RunPowerShellInstallCommand(spacetimeInstallCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        
        if (success)
        {
            SetStatus("SpacetimeDB Server installation completed. Checking installation status...", Color.green);

            await serverManager.CheckSpacetimeDBVersion(); // Extra check to ensure version is updated
            spacetimeDBCurrentVersion = spacetimeDBLatestVersion;

            await Task.Delay(2000);
            
            CheckInstallationStatus();

            await Task.Delay(1000);
            
            if (hasSpacetimeDBServer)
            {
                SetStatus("SpacetimeDB Server installed successfully.", Color.green);
            }
            else
            {
                SetStatus("SpacetimeDB Server installation failed. Please install manually.", Color.red);
            }
        }
    }

    private async void InstallSpacetimeDBPath()
    {
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        // If already installed, just update UI
        if (hasSpacetimeDBPath)
        {
            SetStatus("SpacetimeDB PATH is already installed.", Color.green);
            return;
        }
        
        SetStatus("Installing SpacetimeDB PATH...", Color.green);
        
        // Use the ServerCMDProcess method to run the PowerShell command
        string command = string.Format(
            "wsl -d Debian -u {0} bash -c \"echo \\\"export PATH=/home/{0}/.local/bin:\\$PATH\\\" >> ~/.bashrc\"",
            userName
        );
        bool success = await cmdProcess.RunPowerShellInstallCommand(command, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);

        if (success)
        {
            SetStatus("SpacetimeDB PATH installation started. This may take some time.", Color.green);
            await Task.Delay(1000);
            CheckInstallationStatus();
            await Task.Delay(1000);
            if (hasSpacetimeDBPath)
            {
                SetStatus("SpacetimeDB PATH installed successfully.", Color.green);
            }
            else
            {
                SetStatus("SpacetimeDB PATH installation failed. Please install manually.", Color.red);
            }
        }    
    }

    private async void InstallSpacetimeDBService()
    {
        // Check prerequisites
        if (!hasWSL)
        {
            SetStatus("WSL is not installed. Please install WSL first.", Color.red);
            return;
        }

        if (!hasDebian)
        {
            SetStatus("Debian is not installed on WSL. Please install Debian first.", Color.red);
            return;
        }

        if (!hasSpacetimeDBServer)
        {
            SetStatus("SpacetimeDB Server is not installed. Please install SpacetimeDB Server first.", Color.red);
            return;
        }

        if (string.IsNullOrEmpty(userName))
        {
            SetStatus("Please enter a username first.", Color.red);
            return;
        }
        
        // Get the expected module name from the installer item
        string expectedModuleName = "";
        foreach (var item in installerItems)
        {
            if (item.title == "Install SpacetimeDB Service")
            {
                expectedModuleName = item.expectedModuleName;
                break;
            }
        }
        
        if (string.IsNullOrEmpty(expectedModuleName))
        {
            SetStatus("Expected module name for SpacetimeDB Service is not set. Please check the installer item configuration.", Color.red);
            return;
        }

        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (hasSpacetimeDBService && !installIfAlreadyInstalled)
        {
            SetStatus("SpacetimeDB Service is already installed.", Color.green);
            return;
        }
        
        SetStatus("Installing SpacetimeDB Service...", Color.yellow);
        
        // Create a bash script for SpacetimeDB Service installation via WSL
        string bashScript = 
            "#!/bin/bash\n\n" +
            "echo \"===== Installing SpacetimeDB Service =====\"\n\n" +
            
            "# Create directory for SpacetimeDB if it doesn't exist\n" +
            $"sudo mkdir -p /home/{userName}/.local/share/spacetime\n" +
            $"sudo chown {userName}:{userName} /home/{userName}/.local/share/spacetime\n\n" +
            
            "# Create the service file\n" +
            "echo \"Creating systemd service file...\"\n" +
            "sudo tee /etc/systemd/system/spacetimedb.service << 'EOF'\n" +
            "[Unit]\n" +
            "Description=SpacetimeDB Server\n" +
            "After=network.target\n\n" +
            "[Service]\n" +
            $"User={userName}\n" +
            $"Environment=HOME=/home/{userName}\n" +
            $"ExecStart=/home/{userName}/.local/bin/spacetime --root-dir=/home/{userName}/.local/share/spacetime start --listen-addr=0.0.0.0:3000\n" +
            "Restart=always\n" +
            $"WorkingDirectory=/home/{userName}\n\n"+
            "[Install]\n" +
            "WantedBy=multi-user.target\n" +
            "EOF\n\n" +
            
            "# Reload systemd to recognize the new service\n" +
            "echo \"Reloading systemd...\"\n" +
            "sudo systemctl daemon-reload\n\n" +
            
            "# Enable and start the service\n" +
            "echo \"Enabling and starting SpacetimeDB service...\"\n" +
            "sudo systemctl enable spacetimedb.service\n" +
            "sudo systemctl start spacetimedb.service\n\n" +
            
            "# Check service status\n" +
            "echo \"Checking service status...\"\n" +
            "sudo systemctl is-active spacetimedb.service && echo \"SpacetimeDB service is active\" || echo \"SpacetimeDB service is not active\"\n" +
            "sudo systemctl is-enabled spacetimedb.service && echo \"SpacetimeDB service is enabled\" || echo \"SpacetimeDB service is not enabled\"\n\n" +
            
            "# Create the database logs service file\n" +
            "echo \"Creating SpacetimeDB database logs service...\"\n" +
            "sudo tee /etc/systemd/system/spacetimedb-logs.service << 'EOF'\n" +
            "[Unit]\n" +
            "Description=SpacetimeDB Database Logs\n" +
            "After=spacetimedb.service\n" +
            "Requires=spacetimedb.service\n\n" +
            "[Service]\n" +
            $"User={userName}\n" +
            $"Environment=HOME=/home/{userName}\n" +
            "Type=simple\n" +
            "Restart=always\n" +
            "RestartSec=5\n" +
            $"ExecStart=/home/{userName}/.local/bin/spacetime logs {expectedModuleName} -f\n" +
            $"WorkingDirectory=/home/{userName}\n\n" +
            "[Install]\n" +
            "WantedBy=multi-user.target\n" +
            "EOF\n\n" +
            
            "# Reload systemd to recognize the new service\n" +
            "sudo systemctl daemon-reload\n\n" +
            
            "# Enable and start the database logs service\n" +
            "echo \"Enabling SpacetimeDB database logs service...\"\n" +
            "sudo systemctl enable spacetimedb-logs.service\n" +
            "sudo systemctl start spacetimedb-logs.service\n\n" +
            "# Check database logs service status\n" +
            "echo \"Checking SpacetimeDB database logs service status...\"\n" +
            "sudo systemctl is-active spacetimedb-logs.service && echo \"SpacetimeDB logs service is active\" || echo \"SpacetimeDB logs service is not active\"\n" +
            "sudo systemctl is-enabled spacetimedb-logs.service && echo \"SpacetimeDB logs service is enabled\" || echo \"SpacetimeDB logs service is not enabled\"\n\n" +
            
            "# Configure sudoers to allow systemctl commands without password\n"+
            "echo \"Configuring sudoers for passwordless systemctl operations...\"\n" +
            $"echo '{userName} ALL=(root) NOPASSWD: /usr/bin/systemctl start spacetimedb.service' | sudo tee -a /etc/sudoers.d/spacetimedb\n" +
            $"echo '{userName} ALL=(root) NOPASSWD: /usr/bin/systemctl stop spacetimedb.service' | sudo tee -a /etc/sudoers.d/spacetimedb\n" +
            $"echo '{userName} ALL=(root) NOPASSWD: /usr/bin/systemctl start spacetimedb-logs.service' | sudo tee -a /etc/sudoers.d/spacetimedb\n" +
            $"echo '{userName} ALL=(root) NOPASSWD: /usr/bin/systemctl stop spacetimedb-logs.service' | sudo tee -a /etc/sudoers.d/spacetimedb\n" +
            $"echo '{userName} ALL=(root) NOPASSWD: /usr/bin/systemctl status spacetimedb.service' | sudo tee -a /etc/sudoers.d/spacetimedb\n" +
            $"echo '{userName} ALL=(root) NOPASSWD: /usr/bin/systemctl status spacetimedb-logs.service' | sudo tee -a /etc/sudoers.d/spacetimedb\n" +
            "sudo chmod 440 /etc/sudoers.d/spacetimedb\n" +
            "echo \"Sudoers configuration completed.\"\n\n" +

            "echo \"===== Done! =====\"\n" +
            $"echo \"Database logs service configured for module: {expectedModuleName}\"\n" +
            $"echo \"Sudoers configured to allow passwordless systemctl operations for {userName}\"";
          // Create a temporary script file and execute it via WSL
        SetStatus("Installing SpacetimeDB Service in terminal window. Please follow the progress there...", Color.yellow);
        
        // Create a temporary script file path
        string tempScriptPath = System.IO.Path.GetTempFileName() + ".sh";
        
        try
        {
            // Write the bash script to the temporary file
            await System.IO.File.WriteAllTextAsync(tempScriptPath, bashScript);
            
            // Convert Windows path to WSL path for the script
            string wslScriptPath = tempScriptPath.Replace("\\", "/").Replace("C:", "/mnt/c");
            
            // Execute the script via WSL
            string command = $"wsl -d Debian -u {userName} bash {wslScriptPath}";
              bool success = await cmdProcess.RunPowerShellInstallCommand(command, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
            
            if (success)
            {
                SetStatus("SpacetimeDB Service installation completed. Checking status...", Color.green);
                await Task.Delay(2000);
                
                CheckInstallationStatus();
                await Task.Delay(1000);
                
                if (hasSpacetimeDBService)
                {
                    string logsServiceStatus = hasSpacetimeDBLogsService ? "Both SpacetimeDB services" : "SpacetimeDB service";
                    SetStatus($"{logsServiceStatus} installed successfully. Database logs configured for module: {expectedModuleName}", Color.green);
                }
                else
                {
                    SetStatus("SpacetimeDB Service installation verification failed. Please check the terminal output.", Color.yellow);
                }
            }
            else
            {
                SetStatus("SpacetimeDB Service installation process encountered issues. Please check the terminal output.", Color.red);
            }
        }
        catch (System.Exception ex)
        {
            SetStatus($"Error during SpacetimeDB Service installation: {ex.Message}", Color.red);
            Debug.LogError($"InstallSpacetimeDBService error: {ex}");
        }
        finally
        {
            // Clean up the temporary script file
            try
            {
                if (System.IO.File.Exists(tempScriptPath))
                {
                    System.IO.File.Delete(tempScriptPath);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Failed to delete temporary script file: {ex.Message}");
            }
        }
    }

    private async void InstallRust()
    {
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (hasRust && !installIfAlreadyInstalled)
        {
            SetStatus("Rust is already installed.", Color.green);
            return;
        }

        if (!hasCurl)
        {
            SetStatus("curl is required to install Rust. Please install curl first.", Color.red);
            return;
        }
        
        SetStatus("Installing Rust - Step 1: Update package list", Color.yellow);
        
        // First update package list
        string updateCommand = "wsl -d Debian -u root bash -c \"sudo apt update\"";
        bool updateSuccess = await cmdProcess.RunPowerShellInstallCommand(updateCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!updateSuccess)
        {
            SetStatus("Failed to update package list. Rust installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000); // Shorter delay
        
        // Then install Rust using rustup
        SetStatus("Installing Rust - Step 2: Installing rustup", Color.yellow);
        string rustInstallCommand = $"wsl -d Debian -u {userName} bash -c \"echo 1 | curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh\"";
        bool installSuccess = await cmdProcess.RunPowerShellInstallCommand(rustInstallCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!installSuccess)
        {
            if (WSL1Installed)
            {
                SetStatus("Rust installation continuing. (WSL1)", Color.green);
            } else {
                SetStatus("Failed to install Rust. Installation aborted.", Color.red);
                return;
            }
        }

        // Source the cargo environment
        SetStatus("Installing Rust - Step 3: Setting up Rust environment", Color.yellow);
        string sourceCommand = $"wsl -d Debian -u {userName} bash -c \". \\\"$HOME/.cargo/env\\\"\"";
        bool sourceSuccess = await cmdProcess.RunPowerShellInstallCommand(sourceCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!sourceSuccess)
        {   
            if (WSL1Installed)
            {
                SetStatus("Rust installation continuing. (WSL1)", Color.green);
            } else {
                SetStatus("Warning: Failed to source cargo environment. Rust may not be available in current session.", Color.yellow);
                return;
            }
        }

        // Install build-essential package
        SetStatus("Installing Rust - Step 4: Installing build-essential", Color.yellow);
        string buildEssentialCommand = "wsl -d Debian -u root bash -c \"sudo apt install -y build-essential\"";
        bool buildEssentialSuccess = await cmdProcess.RunPowerShellInstallCommand(buildEssentialCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!buildEssentialSuccess)
        {
            CheckInstallationStatus();
            await Task.Delay(1000);
            if (WSL1Installed && hasRust)
            {
                SetStatus("Rust installed successfully. (WSL1)", Color.green);
                return;
            } else {
                SetStatus("Warning: Failed to install build-essential. Some Rust packages may not compile correctly.", Color.yellow);
                return;
            }
        }

        // Check installation status
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (hasRust)
        {
            SetStatus("Rust installed successfully.", Color.green);
        }
        else
        {
            SetStatus("Rust installation failed. Please install manually.", Color.red);
        }
    }

    private async void InstallBinaryen()
    {
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (hasBinaryen && !installIfAlreadyInstalled)
        {
            SetStatus("Binaryen is already installed.", Color.green);
            return;
        }

        if (!hasCurl)
        {
            SetStatus("curl is required to install Binaryen. Please install curl first.", Color.red);
            return;
        }
        
        SetStatus("Installing Web Assembly Optimizer Binaryen...", Color.yellow);
        
        try
        {
            // Install Binaryen with the specific version and command provided
            string installCommand = $"wsl -d Debian -u {userName} bash -c \"" +
                "VERSION=123 && " +
                "curl -L \\\"https://github.com/WebAssembly/binaryen/releases/download/version_${{VERSION}}/binaryen-version_${{VERSION}}-x86_64-linux.tar.gz\\\" | " +
                "sudo tar -xz --strip-components=2 -C /usr/local/bin binaryen-version_${{VERSION}}/bin\"";
            
            bool installSuccess = await cmdProcess.RunPowerShellInstallCommand(
                installCommand, 
                LogMessage, 
                visibleInstallProcesses, 
                keepWindowOpenForDebug
            );
            
            if (!installSuccess)
            {
                CheckInstallationStatus();
                await Task.Delay(2000);
                if (WSL1Installed && hasBinaryen)
                {
                    SetStatus("Binaryen installed successfully. (WSL1)", Color.green);
                }
                else
                {
                    SetStatus("Failed to install Binaryen. Installation aborted.", Color.red);
                }
                return;
            }
            
            // Check installation status
            CheckInstallationStatus();
            await Task.Delay(1000);
            
            if (hasBinaryen)
            {
                SetStatus("Binaryen (Web Assembly Optimizer) installed successfully.", Color.green);
            }
            else
            {
                SetStatus("Binaryen installation failed. Please install manually.", Color.red);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error during Binaryen installation: {ex.Message}", Color.red);
            LogMessage($"Binaryen installation error: {ex}", -1);
        }
    }
    
    private async void InstallSpacetimeDBUnitySDK()
    {
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (hasSpacetimeDBUnitySDK && !installIfAlreadyInstalled)
        {
            SetStatus("SpacetimeDB Unity SDK is already installed.", Color.green);
            return;
        }
        
        SetStatus("Installing SpacetimeDB Unity SDK...", Color.yellow);

        // Display a warning to users about the installation process
        if (EditorUtility.DisplayDialog(
            "SpacetimeDB SDK Installation",
            "Installing the SpacetimeDB SDK will add a package from GitHub and may trigger a script reload.\n\n" +
            "The installation process may take up to a minute. Please don't close Unity during this time.",
            "Install",
            "Cancel"))
        {
            // Use the ServerSpacetimeSDKInstaller to install the SDK
            ServerSpacetimeSDKInstaller.InstallSDK((success, errorMessage) => 
            {
                if (success)
                {
                    SetStatus("SpacetimeDB Unity SDK installed successfully.", Color.green);
                    hasSpacetimeDBUnitySDK = true;
                    EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBUnitySDK", true);
                    UpdateInstallerItemsStatus();
                    
                    // After successful installation, ensure the window updates properly
                    EditorApplication.delayCall += () => {
                        CheckInstallationStatus();
                    };
                }
                else
                {
                    string errorMsg = string.IsNullOrEmpty(errorMessage) ? "Unknown error" : errorMessage;
                    SetStatus($"SpacetimeDB Unity SDK installation failed: {errorMsg}", Color.red);
                    
                    // Show a more detailed error dialog
                    EditorUtility.DisplayDialog(
                        "Installation Failed",
                        $"Failed to install SpacetimeDB Unity SDK: {errorMsg}\n\n" +
                        "You can try again later or install it manually via Package Manager (Window > Package Manager > Add package from git URL).",
                        "OK");
                }
            });
        }
        else
        {
            SetStatus("SpacetimeDB Unity SDK installation cancelled.", Color.yellow);
        }
    }
    #endregion
    
    #region Custom Installation Methods
    private async void InstallCustomUser()
    {
        try
        {
            // Ensure SSH session is active
            if (!customProcess.IsSessionActive())
            {
                SetStatus("SSH connection not active. Please connect first.", Color.red);
                return;
            }

            if (string.IsNullOrEmpty(tempCreateUserNameInput))
            {
                SetStatus("Please enter a username to create.", Color.red);
                return;
            }

            if (string.IsNullOrEmpty(EditorPrefs.GetString(PrefsKeyPrefix + "SSHPrivateKeyPath", "")))
            {
                SetStatus("Please first generate a SSH private key and enter the path.", Color.red);
                return;
            }
            
            string newUserName = tempCreateUserNameInput;

            // Check if the username already exists
            var checkUserResult = await customProcess.RunCustomCommandAsync($"id -u {newUserName} > /dev/null 2>&1 && echo 'exists' || echo 'notexists'");
            if (checkUserResult.success && checkUserResult.output.Trim() == "exists")
            {
                SetStatus($"User '{newUserName}' already exists on the remote server.", Color.yellow);
                return;
            }

            // Create commands to be executed in the terminal
            string commands = 
                "# Step 0: Install Sudo\n" +
                $"apt install -y sudo\n\n" +
                "# Step 1: Create new user (will prompt for password)\n" +
                $"adduser {newUserName}\n\n" +
                "# Step 2: Add user to sudo group\n" +
                $"usermod -aG sudo {newUserName}\n\n" +
                "# Step 3: Create SSH directory for the new user\n" +
                $"mkdir -p /home/{newUserName}/.ssh\n" +
                $"chmod 700 /home/{newUserName}/.ssh\n\n" +
                "# Step 4: Copy authorized_keys from root to new user (if exists)\n" +
                "if [ -f /root/.ssh/authorized_keys ]; then\n" +
                $"  cp /root/.ssh/authorized_keys /home/{newUserName}/.ssh/\n" +
                "fi\n\n" +
                "# Step 5: Set correct ownership and permissions\n" +
                $"chown -R {newUserName}:{newUserName} /home/{newUserName}/.ssh\n" +
                $"if [ -f /home/{newUserName}/.ssh/authorized_keys ]; then\n" +
                $"  chmod 600 /home/{newUserName}/.ssh/authorized_keys\n" +
                "fi\n\n" +
                "# Step 6: NOPASSWD for sudo - create with cat instead of printf\n" +
                $"cat > /etc/sudoers.d/{newUserName} << EOF\n" +
                $"{newUserName} ALL=(ALL) NOPASSWD: ALL\n" +
                "EOF\n" +
                $"chmod 0440 /etc/sudoers.d/{newUserName}\n\n" +
                
                "# Confirm success\n" +
                "echo \"\"\n" +
                "echo \"=======================\"\n" +
                $"echo \"User {newUserName} has been successfully created and added to the sudo group.\"\n" +
                "echo \"=======================\"\n";

            SetStatus($"Creating user '{newUserName}' on remote server. Please follow the prompts in the terminal window...", Color.yellow);

            // Use the RunVisibleSSHCommand method which won't block Unity's main thread
            // This is needed since adduser command requires interactive input for password
            bool success = await customProcess.RunVisibleSSHCommand(commands);
            
            if (success)
            {
                // Update the SSH username if it's not already set
                if (string.IsNullOrEmpty(sshUserName))
                {
                    sshUserName = newUserName;
                    EditorPrefs.SetString(PrefsKeyPrefix + "SSHUserName", sshUserName);
                }
                
                SetStatus($"User '{newUserName}' created successfully on remote server.", Color.green);
                
                // Update UI
                CheckCustomInstallationStatus();

                EditorUtility.DisplayDialog(
                    "New User Created",
                    $"User '{newUserName}' has been created successfully.\n\n" +
                    $"Please close the installer window and enter '{newUserName}' as your SSH username.\n\n" +
                    "Then press 'Check Pre-Requisites and Connect' and continue installing as your new user.",
                    "OK"
                );
            }
            else
            {
                SetStatus($"Failed to create user '{newUserName}' on remote server. Please check terminal output.", Color.red);
            }
        }
        finally
        {
            // Always refresh session status, regardless of how the method exits
            await customProcess.RefreshSessionStatus();
        }
    }    

    private async void InstallCustomDebianTrixie()
    {
        try
        {
            // Ensure SSH session is active
            if (!customProcess.IsSessionActive())
            {
                SetStatus("SSH connection not active. Please connect first.", Color.red);
                return;
            }

            CheckCustomInstallationStatus();
            await Task.Delay(1000);
            
            if (hasCustomDebianTrixie && !installIfAlreadyInstalled)
            {
                SetStatus("Remote Debian Trixie Update is already installed.", Color.green);
                return;
            }
            
            SetStatus("Installing Debian Trixie Update on remote server...", Color.yellow);
            
            // Create a single bash script with all the steps
            string commands = 
                "#!/bin/bash\n\n" +
                "echo \"===== Step 1: Updating package lists =====\"\n" +
                "sudo apt update\n" +
                "if [ $? -ne 0 ]; then\n" +
                "  echo \"ERROR: Failed to update package lists\"\n" +
                "  exit 1\n" +
                "fi\n\n" +
                
                "echo \"===== Step 2: Upgrading packages =====\"\n" +
                "sudo apt upgrade -y\n" +
                "# Continue even if this step has issues\n\n" +
                
                "echo \"===== Step 3: Installing update-manager-core =====\"\n" +
                "sudo apt install -y update-manager-core\n" +
                "# Continue even if this step has issues\n\n" +
                
                "echo \"===== Step 4: Updating sources.list to Trixie =====\"\n" +
                "sudo sed -i 's/bookworm/trixie/g' /etc/apt/sources.list\n" +
                "if [ $? -ne 0 ]; then\n" +
                "  echo \"ERROR: Failed to update sources.list\"\n" +
                "  exit 1\n" +
                "fi\n\n" +
                
                "echo \"===== Step 5: Updating package lists for Trixie =====\"\n" +
                "sudo apt update\n" +
                "if [ $? -ne 0 ]; then\n" +
                "  echo \"ERROR: Failed to update Trixie package lists\"\n" +
                "  exit 1\n" +
                "fi\n\n" +
                
                "echo \"===== Step 6: Performing full upgrade to Trixie =====\"\n" +
                "sudo apt full-upgrade -y\n" +
                "if [ $? -ne 0 ]; then\n" +
                "  echo \"WARNING: Full upgrade encountered issues. Check system status.\"\n" +
                "else\n" +
                "  echo \"===== Trixie upgrade completed successfully! =====\"\n" +
                "fi\n\n" +
                
                "echo \"===== Done! =====\"\n";

            // Use the RunVisibleSSHCommand method which won't block Unity's main thread
            SetStatus("Running Debian Trixie upgrade in terminal window. Please follow the progress there...", Color.yellow);
            bool success = await customProcess.RunVisibleSSHCommand(commands);
            
            if (success)
            {
                SetStatus("Debian Trixie Update installation completed. Checking installation status...", Color.green);
                
                // Wait for changes to apply
                await Task.Delay(3000);
                
                CheckCustomInstallationStatus();
                await Task.Delay(1000);
                
                if (hasCustomDebianTrixie)
                {
                    SetStatus("Debian Trixie Update installed successfully on remote server.", Color.green);
                }
                else
                {
                    SetStatus("Debian Trixie Update verification failed. The installation might still be successful.", Color.yellow);
                }
            }
            else
            {
                SetStatus("Debian Trixie Update installation process encountered issues. Please check the terminal output.", Color.red);
            }
        }
        finally
        {
            // Always refresh session status, regardless of how the method exits
            await customProcess.RefreshSessionStatus();
        }
    }

    private async void InstallCustomCurl()
    {
        try
        {
            // Ensure SSH session is active
            if (!customProcess.IsSessionActive())
            {
                SetStatus("SSH connection not active. Please connect first.", Color.red);
                return;
            }

            if (string.IsNullOrEmpty(sshUserName))
            {
                SetStatus("Please enter a SSH username first.", Color.red);
                return;
            }

            CheckCustomInstallationStatus();
            await Task.Delay(1000);
            
            if (hasCustomCurl && !installIfAlreadyInstalled)
            {
                SetStatus("cURL is already installed on the remote server.", Color.green);
                return;
            }
            
            SetStatus("Installing cURL on remote server...", Color.yellow);
            
            // Create a bash script for curl installation
            string commands = 
                "#!/bin/bash\n\n" +
                "echo \"===== Updating package lists =====\"\n" +
                "sudo apt update\n\n" +
                "echo \"===== Installing cURL =====\"\n" +
                "sudo apt install -y curl\n\n" +
                "# Check if curl was installed successfully\n" +
                "if command -v curl &> /dev/null; then\n" +
                "  echo \"===== cURL installed successfully! =====\"\n" +
                "  curl --version\n" +
                "else\n" +
                "  echo \"ERROR: cURL installation failed\"\n" +
                "  exit 1\n" +
                "fi\n\n" +
                "echo \"===== Done! =====\"\n";
            
            // Use the RunVisibleSSHCommand method which won't block Unity's main thread
            SetStatus("Installing cURL in terminal window. Please follow the progress there...", Color.yellow);
            bool success = await customProcess.RunVisibleSSHCommand(commands);
            
            if (success)
            {
                SetStatus("cURL installation completed. Checking installation status...", Color.green);
                
                await Task.Delay(2000);
                
                CheckCustomInstallationStatus();
                await Task.Delay(1000);
                
                if (hasCustomCurl)
                {
                    SetStatus("cURL installed successfully on remote server.", Color.green);
                }
                else
                {
                    SetStatus("cURL installation verification failed. Please check the terminal output.", Color.yellow);
                }
            }
            else
            {
                SetStatus("cURL installation process encountered issues. Please check the terminal output.", Color.red);
            }
        }
        finally
        {
            // Always refresh session status, regardless of how the method exits
            await customProcess.RefreshSessionStatus();
        }
    }

    private async void InstallCustomSpacetimeDBServer()
    {
        try
        {
            // Ensure SSH session is active
            if (!customProcess.IsSessionActive())
            {
                SetStatus("SSH connection not active. Please connect first.", Color.red);
                return;
            }

            if (string.IsNullOrEmpty(sshUserName))
            {
                SetStatus("Please enter a SSH username first.", Color.red);
                return;
            }

            CheckCustomInstallationStatus();
            await Task.Delay(1000);
            
            if (hasCustomSpacetimeDBServer && (spacetimeDBLatestVersion == spacetimeDBCurrentVersionCustom) && !installIfAlreadyInstalled)
            {
                SetStatus("The latest version of SpacetimeDB Server is already installed on the remote server.", Color.green);
                return;
            }
            
            SetStatus("Installing SpacetimeDB Server on remote server...", Color.yellow);
            
            // Create a bash script for SpacetimeDB Server installation
            string commands = 
                "#!/bin/bash\n\n" +
                "echo \"===== Installing SpacetimeDB Server =====\"\n" +
                $"# As user {sshUserName}\n" +
                $"curl -sSf https://install.spacetimedb.com/ | sh\n\n" +
                "# Check if installation was successful\n" +
                $"if [ -f /home/{sshUserName}/.local/bin/spacetime ]; then\n" +
                "  echo \"===== SpacetimeDB Server installed successfully! =====\"\n" +
                $"  sudo -u {sshUserName} /home/{sshUserName}/.local/bin/spacetime --version\n" +
                "else\n" +
                "  echo \"WARNING: SpacetimeDB installation verification failed\"\n" +
                "  echo \"Please verify installation manually\"\n" +
                "fi\n\n" +
                "echo \"===== Done! =====\"\n";
            
            // Use the RunVisibleSSHCommand method which won't block Unity's main thread
            SetStatus("Installing SpacetimeDB Server in terminal window. Please follow the progress there...", Color.yellow);
            bool success = await customProcess.RunVisibleSSHCommand(commands);
            
            if (success)
            {
                SetStatus("SpacetimeDB Server installation completed. Checking installation status...", Color.green);

                await customProcess.CheckSpacetimeDBVersionCustom(); // Extra check to ensure version is updated
                spacetimeDBCurrentVersionCustom = spacetimeDBLatestVersion;

                await Task.Delay(2000);
                
                CheckCustomInstallationStatus();

                await Task.Delay(1000);
                
                if (hasCustomSpacetimeDBServer)
                {
                    SetStatus("SpacetimeDB Server installed successfully on remote server.", Color.green);
                }
                else
                {
                    SetStatus("SpacetimeDB Server installation verification failed. Please check the terminal output.", Color.yellow);
                }
            }
            else
            {
                SetStatus("SpacetimeDB Server installation process encountered issues. Please check the terminal output.", Color.red);
            }
        }
        finally
        {
            // Always refresh session status, regardless of how the method exits
            await customProcess.RefreshSessionStatus();
        }
    }

    private async void InstallCustomSpacetimeDBPath()
    {
        try
        {
            // Ensure SSH session is active
            if (!customProcess.IsSessionActive())
            {
                SetStatus("SSH connection not active. Please connect first.", Color.red);
                return;
            }

            if (string.IsNullOrEmpty(sshUserName))
            {
                SetStatus("Please enter a SSH username first.", Color.red);
                return;
            }

            CheckCustomInstallationStatus();
            await Task.Delay(1000);
            
            if (hasCustomSpacetimeDBPath && !installIfAlreadyInstalled)
            {
                SetStatus("SpacetimeDB PATH is already configured on the remote server.", Color.green);
                return;
            }
            
            SetStatus("Configuring SpacetimeDB PATH on remote server...", Color.yellow);
            
            // Create a bash script for SpacetimeDB PATH configuration
            string commands = 
                "#!/bin/bash\n\n" +
                "echo \"===== Configuring SpacetimeDB PATH =====\"\n" +
                $"# Check if spacetime is already in PATH for user {sshUserName}\n" +
                $"if ! grep -q \"HOME/.spacetime/bin\" /home/{sshUserName}/.bashrc && ! grep -q \"HOME/.local/bin\" /home/{sshUserName}/.bashrc; then\n" +
                "  echo \"Adding SpacetimeDB to PATH in .bashrc file\"\n" +
                $"  echo 'export PATH=\"$HOME/.spacetime/bin:$HOME/.local/bin:$PATH\"' >> /home/{sshUserName}/.bashrc\n" +
                $"  chown {sshUserName}:{sshUserName} /home/{sshUserName}/.bashrc\n" +
                "  echo \"===== PATH updated successfully! =====\"\n" +
                "else\n" +
                "  echo \"===== SpacetimeDB PATH already configured! =====\"\n" +
                "fi\n\n" +
                
                $"# Show the current PATH configuration for user {sshUserName}\n" +
                $"echo \"Current PATH entries in /home/{sshUserName}/.bashrc:\"\n" +
                $"grep -i \"PATH\" /home/{sshUserName}/.bashrc | cat\n\n" +
                "echo \"===== Done! =====\"\n";
            
            // Use the RunVisibleSSHCommand method which won't block Unity's main thread
            SetStatus("Configuring SpacetimeDB PATH in terminal window. Please follow the progress there...", Color.yellow);
            bool success = await customProcess.RunVisibleSSHCommand(commands);
            
            if (success)
            {
                SetStatus("SpacetimeDB PATH configuration completed. Checking status...", Color.green);
                
                await Task.Delay(2000);
                
                CheckCustomInstallationStatus();
                await Task.Delay(1000);
                
                if (hasCustomSpacetimeDBPath)
                {
                    SetStatus("SpacetimeDB PATH configured successfully on remote server.", Color.green);
                }
                else
                {
                    SetStatus("SpacetimeDB PATH configuration verification may need a server restart to take effect.", Color.yellow);
                }
            }
            else
            {
                SetStatus("SpacetimeDB PATH configuration process encountered issues. Please check the terminal output.", Color.red);
            }
        }
        finally
        {
            // Always refresh session status, regardless of how the method exits
            await customProcess.RefreshSessionStatus();
        }
    }

    private async void InstallCustomRust()
    {
        try
        {
            // Ensure SSH session is active
            if (!customProcess.IsSessionActive())
            {
                SetStatus("SSH connection not active. Please connect first.", Color.red);
                return;
            }

            if (string.IsNullOrEmpty(sshUserName))
            {
                SetStatus("Please enter a SSH username first.", Color.red);
                return;
            }

            CheckCustomInstallationStatus();
            await Task.Delay(1000);
            
            if (hasCustomRust && !installIfAlreadyInstalled)
            {
                SetStatus("Rust is already installed on the remote server.", Color.green);
                return;
            }
            
            SetStatus("Installing Rust on remote server...", Color.yellow);
            
            // Create a more structured bash script for Rust installation
            string commands = 
                "#!/bin/bash\n\n" +
                "echo \"===== Installing Rust on Remote Server =====\"\n\n" +
                "echo \"===== Step 1: Installing prerequisites =====\"\n" +
                "sudo apt update\n" +
                "sudo apt install -y build-essential curl\n\n" +
                
                "echo \"===== Step 2: Downloading Rust installer =====\"\n" +
                $"cd /home/{sshUserName}\n" +
                "sudo curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs > rustup.sh\n" +
                "sudo chmod +x rustup.sh\n" +
                $"sudo chown {sshUserName}:{sshUserName} rustup.sh\n\n" +
                
                "echo \"===== Step 3: Installing Rust with automatic -y =====\"\n" +
                $"sudo -u {sshUserName} bash -c './rustup.sh -y'\n" +
                "sudo rm rustup.sh\n\n" +
                
                "echo \"===== Step 4: Setting up Rust environment =====\"\n" +
                $"# Add Rust to PATH for user {sshUserName} if not already present\n" +
                $"if ! grep -q \"HOME/.cargo/env\" /home/{sshUserName}/.bashrc; then\n" +
                $"  echo 'source \"$HOME/.cargo/env\"' >> /home/{sshUserName}/.bashrc\n" +
                $"  sudo chown {sshUserName}:{sshUserName} /home/{sshUserName}/.bashrc\n" +
                "fi\n\n" +
                
                "echo \"===== Step 5: Verifying installation =====\"\n" +
                "echo \"Checking Rust version:\"\n" +
                $"sudo -u {sshUserName} bash -c 'source \"$HOME/.cargo/env\" && rustc --version'\n\n" +
                "echo \"Checking Cargo version:\"\n" +
                $"sudo -u {sshUserName} bash -c 'source \"$HOME/.cargo/env\" && cargo --version'\n\n" +
                
                "echo \"===== Done! =====\"\n";
                
            // Use the RunVisibleSSHCommand method which won't block Unity's main thread
            SetStatus("Installing Rust in terminal window. Please follow the progress there...", Color.yellow);
            bool success = await customProcess.RunVisibleSSHCommand(commands);
            
            if (success)
            {
                SetStatus("Rust installation completed. Checking installation status...", Color.green);
                
                await Task.Delay(2000);
                
                CheckCustomInstallationStatus();
                await Task.Delay(1000);
                
                if (hasCustomRust)
                {
                    SetStatus("Rust installed successfully on remote server.", Color.green);
                }
                else
                {
                    SetStatus("Rust installation verification failed. It may take a server restart to take effect.", Color.yellow);
                }
            }
            else
            {
                SetStatus("Rust installation process encountered issues. Please check the terminal output.", Color.red);
            }
        }
        finally
        {
            // Always refresh session status, regardless of how the method exits
            await customProcess.RefreshSessionStatus();
        }
    }

    private async void InstallCustomSpacetimeDBService()
    {
        try
        {
            // Ensure SSH session is active
            if (!customProcess.IsSessionActive())
            {
                SetStatus("SSH connection not active. Please connect first.", Color.red);
                return;
            }

            if (string.IsNullOrEmpty(sshUserName))
            {
                SetStatus("Please enter a SSH username first.", Color.red);
                return;
            }
                    
            // Get the expected module name from the installer item
            string expectedModuleName = "";
            foreach (var item in customInstallerItems)
            {
                if (item.title == "Install SpacetimeDB Service")
                {
                    expectedModuleName = item.expectedModuleName;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(expectedModuleName))
            {
                SetStatus("Expected module name for SpacetimeDB Service is not set. Please check the installer item configuration.", Color.red);
                return;
            }

            CheckCustomInstallationStatus();
            await Task.Delay(1000);
            
            if (hasCustomSpacetimeDBService && !installIfAlreadyInstalled)
            {
                SetStatus("SpacetimeDB Service is already installed on the remote server.", Color.green);
                return;
            }
            
            SetStatus("Installing SpacetimeDB Service on remote server...", Color.yellow);
            
            // Create a bash script for SpacetimeDB Service installation
            string commands = 
                "#!/bin/bash\n\n" +
                "echo \"===== Installing SpacetimeDB Service =====\"\n\n" +
                
                "# Create directory for SpacetimeDB if it doesn't exist\n" +
                $"sudo mkdir -p /home/{sshUserName}/.local/share/spacetime\n" +
                $"sudo chown {sshUserName}:{sshUserName} /home/{sshUserName}/.local/share/spacetime\n\n" +
                
                "# Create the service file\n" +
                "echo \"Creating systemd service file...\"\n" +
                "sudo tee /etc/systemd/system/spacetimedb.service << 'EOF'\n" +
                "[Unit]\n" +
                "Description=SpacetimeDB Server\n" +
                "After=network.target\n\n" +
                "[Service]\n" +
                $"User={sshUserName}\n" +
                $"Environment=HOME=/home/{sshUserName}\n" +
                $"ExecStart=/home/{sshUserName}/.local/bin/spacetime \\\\\n" +
                $"    --root-dir=/home/{sshUserName}/.local/share/spacetime \\\\\n" +
                "    start \\\\\n" +
                "    --listen-addr=0.0.0.0:3000\n" +
                "Restart=always\n" +
                $"WorkingDirectory=/home/{sshUserName}\n\n" +
                "[Install]\n" +
                "WantedBy=multi-user.target\n" +
                "EOF\n\n" +
                
                "# Reload systemd to recognize the new service\n" +
                "echo \"Reloading systemd...\"\n" +
                "sudo systemctl daemon-reload\n\n" +
                
                "# Enable and start the service\n" +
                "echo \"Enabling and starting SpacetimeDB service...\"\n" +
                "sudo systemctl enable spacetimedb.service\n" +
                "sudo systemctl start spacetimedb.service\n\n" +
                
                "# Check service status\n" +
                "echo \"Checking service status...\"\n" +
                "sudo systemctl status spacetimedb.service\n\n" +
                
                "# Create the database logs service file\n" +
                "echo \"Creating SpacetimeDB database logs service...\"\n" +
                "sudo tee /etc/systemd/system/spacetimedb-logs.service << 'EOF'\n" +
                "[Unit]\n" +
                "Description=SpacetimeDB Database Logs\n" +
                "After=spacetimedb.service\n" +
                "Requires=spacetimedb.service\n\n" +
                "[Service]\n" +
                $"User={sshUserName}\n" +
                $"Environment=HOME=/home/{sshUserName}\n" +
                "Type=simple\n" +
                "Restart=always\n" +
                "RestartSec=5\n" +
                $"ExecStart=/home/{sshUserName}/.local/bin/spacetime logs {expectedModuleName} -f\n" +
                $"WorkingDirectory=/home/{sshUserName}\n\n" +
                "[Install]\n" +
                "WantedBy=multi-user.target\n" +
                "EOF\n\n" +
                
                "# Reload systemd to recognize the new service\n" +
                "sudo systemctl daemon-reload\n\n" +
                
                "# Enable and start the database logs service\n" +
                "echo \"Enabling SpacetimeDB database logs service...\"\n" +
                "sudo systemctl enable spacetimedb-logs.service\n" +
                "sudo systemctl start spacetimedb-logs.service\n\n" +

                "# Check database logs service status\n" +
                "echo \"Checking SpacetimeDB database logs service status...\"\n" +
                "sudo systemctl status spacetimedb-logs.service\n\n" +

                "echo \"===== Done! =====\"\n" +
                $"echo \"Database logs service configured for module: {expectedModuleName}\"";
            
            // Use the RunVisibleSSHCommand method which won't block Unity's main thread
            SetStatus("Installing SpacetimeDB Service in terminal window. Please follow the progress there...", Color.yellow);
            bool success = await customProcess.RunVisibleSSHCommand(commands);
            
            if (success)
            {
                SetStatus("SpacetimeDB Service installation completed. Checking status...", Color.green);
                
                await Task.Delay(2000);
                
                CheckCustomInstallationStatus();
                await Task.Delay(1000);
                
                if (hasCustomSpacetimeDBService)
                {
                    string logsServiceStatus = hasCustomSpacetimeDBLogsService ? "Both SpacetimeDB services" : "SpacetimeDB service";
                    SetStatus($"{logsServiceStatus} installed successfully. Database logs configured for module: {expectedModuleName}", Color.green);
                }
                else
                {
                    SetStatus("SpacetimeDB Service installation verification failed. Please check the terminal output.", Color.yellow);
                }
            }
            else
            {
                SetStatus("SpacetimeDB Service installation process encountered issues. Please check the terminal output.", Color.red);
            }
        }
        finally
        {
            // Always refresh session status, regardless of how the method exits
            await customProcess.RefreshSessionStatus();
        }
    }

    private async void InstallCustomBinaryen()
    {
        try
        {
            // Ensure SSH session is active
            if (!customProcess.IsSessionActive())
            {
                SetStatus("SSH connection not active. Please connect first.", Color.red);
                return;
            }

            if (string.IsNullOrEmpty(sshUserName))
            {
                SetStatus("Please enter a SSH username first.", Color.red);
                return;
            }

            CheckCustomInstallationStatus();
            await Task.Delay(1000);
            
            if (hasCustomBinaryen && !installIfAlreadyInstalled)
            {
                SetStatus("Binaryen is already installed on the remote server.", Color.green);
                return;
            }

            if (!hasCustomCurl)
            {
                SetStatus("curl is required to install Binaryen. Please install curl first.", Color.red);
                return;
            }

            SetStatus("Installing Web Assembly Optimizer Binaryen on remote server...", Color.yellow);

            // Use the exact URL that works, with proper error handling
            string commands = 
                "echo \"Downloading Binaryen...\" && " +
                "curl -L \"https://github.com/WebAssembly/binaryen/releases/download/version_123/binaryen-version_123-x86_64-linux.tar.gz\" -o /tmp/binaryen.tar.gz && " +
                "echo \"Extracting Binaryen...\" && " +
                "sudo tar -xz --strip-components=2 -C /usr/local/bin -f /tmp/binaryen.tar.gz binaryen-version_123/bin && " +
                "echo \"Cleaning up...\" && " +
                "rm -f /tmp/binaryen.tar.gz && " +
                "echo \"Verifying installation...\" && " +
                "wasm-opt --version 2>/dev/null || echo \"wasm-opt binary installed successfully\"";

            // Use the RunVisibleSSHCommand method which won't block Unity's main thread
            SetStatus("Installing Binaryen in terminal window. Please follow the progress there...", Color.yellow);
            bool success = await customProcess.RunVisibleSSHCommand(commands);
            
            if (success)
            {
                SetStatus("Binaryen installation completed. Checking installation status...", Color.green);
                
                await Task.Delay(2000);
                
                CheckCustomInstallationStatus();
                await Task.Delay(1000);
                
                if (hasCustomBinaryen)
                {
                    SetStatus("Binaryen (Web Assembly Optimizer) installed successfully on remote server.", Color.green);
                }
                else
                {
                    SetStatus("Binaryen installation verification failed. Please check the terminal output.", Color.yellow);
                }
            }
            else
            {
                SetStatus("Binaryen installation process encountered issues. Please check the terminal output.", Color.red);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error during Binaryen installation: {ex.Message}", Color.red);
            LogMessage($"Binaryen installation error: {ex}", -1);
        }
        finally
        {
            // Always refresh session status, regardless of how the method exits
            await customProcess.RefreshSessionStatus();
        }
    }
    #endregion

    #region Log Messages
    private void LogMessage(string message, int type)
    {
        switch (type)
        {
            case -1:
                SetStatus(message, Color.red);
                break;
            case 0:
                SetStatus(message, Color.yellow);
                break;
            case 1:
                SetStatus(message, Color.green);
                break;
            default:
                SetStatus(message, Color.grey);
                break;
        }
        RequestRepaint(); // Always request repaint on status change, but throttled
    }
    
    private void SetStatus(string message, Color color)
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
    }
    #endregion
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games