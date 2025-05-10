using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Check and install everything necessary to run SpacetimeDB in WSL with this window ///
////////////////////// made by Northern Rogue /// Mathias Toivonen /////////////////////

namespace NorthernRogue.CCCP.Editor {

public class ServerInstallerWindow : EditorWindow
{
    private List<InstallerItem> installerItems = new List<InstallerItem>();
    private ServerCMDProcess cmdProcess;
    
    // UI
    private Vector2 scrollPosition;
    private string statusMessage = "Ready to install components.";
    private bool userNamePrompt = false;
    private Color statusColor = Color.grey;
    private string statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
    private double lastRepaintTime = 0;
    private const double minRepaintInterval = 0.5; // Minimum time between repaints in seconds
    
    // EditorPrefs
    private string userName = "";
    // Temporary field for username input
    private string tempUserNameInput = "";
    
    // Styles
    private GUIStyle titleStyle;
    private GUIStyle itemTitleStyle;
    private GUIStyle installedStyle;
    private GUIStyle installButtonStyle;
    private bool stylesInitialized = false;
    
    // Installation states
    private bool isRefreshing = false;
    private bool hasWSL = false;
    private bool hasDebian = false;
    private bool hasDebianTrixie = false;
    private bool hasCurl = false;
    private bool hasSpacetimeDBServer = false;
    private bool hasSpacetimeDBPath = false;
    private bool hasRust = false;
    private bool hasSpacetimeDBUnitySDK = false;

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
        window.InitializeInstallerItems();
    }
    
    private void OnEnable()
    {
        cmdProcess = new ServerCMDProcess(LogMessage, false);
        
        // Check if this is the first time the window is opened
        bool isFirstTime = !EditorPrefs.HasKey(PrefsKeyPrefix+FirstTimeOpenKey);
        if (isFirstTime)
        {
            // Show first-time information dialog
            EditorApplication.delayCall += () => {
                bool continuePressed = EditorUtility.DisplayDialog(
                    "SpacetimeDB Automatic Installer",
                    "Welcome to the automatic installer window that can check and install everything needed for your Windows PC to run SpacetimeDB.\n\n" +
                    "All named software in this window is official and publicly available software that belongs to the respective parties and is provided by them for free.\n\n" +
                    "It works by entering the same commands as in the manual installation process for the purpose of ease of use.",
                    "Continue", "Documentation");
                
                if (!continuePressed) {
                    Application.OpenURL(ServerWindow.Documentation);
                }

                EditorPrefs.SetBool(PrefsKeyPrefix+FirstTimeOpenKey, true);
            };
        }
        
        // Load installation status from EditorPrefs
        hasWSL = EditorPrefs.GetBool(PrefsKeyPrefix + "HasWSL", false);
        hasDebian = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebian", false);
        hasDebianTrixie = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebianTrixie", false);
        hasCurl = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCurl", false);
        hasSpacetimeDBServer = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", false);
        hasSpacetimeDBPath = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", false);
        hasRust = EditorPrefs.GetBool(PrefsKeyPrefix + "HasRust", false);
        hasSpacetimeDBUnitySDK = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBUnitySDK", false);

        // WSL 1 requires unique install logic for Debian apps
        WSL1Installed = EditorPrefs.GetBool(PrefsKeyPrefix + "WSL1Installed", false);
        
        // Load install debug settings from EditorPrefs
        visibleInstallProcesses = EditorPrefs.GetBool(PrefsKeyPrefix + "VisibleInstallProcesses", true);
        keepWindowOpenForDebug = EditorPrefs.GetBool(PrefsKeyPrefix + "KeepWindowOpenForDebug", true);
        
        // Cache the current username
        userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        tempUserNameInput = userName; // Initialize the temp input with the stored username
        
        InitializeInstallerItems();
        
        // Reduce frequency of automatic repaints
        EditorApplication.update += OnEditorUpdate;
        
        CheckInstallationStatus();

        // Update installer items status based on loaded prefs
        UpdateInstallerItemsStatus();
    }
    
    private void OnDisable()
    {
        // Clean up the update callback when the window is closed
        EditorApplication.update -= OnEditorUpdate;

        // Update Pre-requisities so server can be started if requirements are met. // Added button instead
        //ServerWindow serverWindow = GetWindow<ServerWindow>();
        //serverWindow.CheckPrerequisites(); 
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
    
    #region Installer Items
    private void InitializeInstallerItems()
    {
        installerItems = new List<InstallerItem>
        {
            new InstallerItem
            {
                title = "Install WSL with Debian",
                description = "Windows Subsystem for Linux with Debian distribution\n"+
                "Important: Will launch a checker tool that determines if your system supports WSL1 or WSL2\n"+
                "Note: May require a system restart",
                isInstalled = hasDebian,
                isEnabled = true, // Always enabled as it's the first prerequisite
                installAction = InstallWSLDebian
            },
            new InstallerItem
            {
                title = "Install Debian Trixie Update",
                description = "Debian Trixie Update (Debian Version 13)\n"+
                "Required to run the SpacetimeDB Server\n"+
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
                "Note: Only supports installing to the users home directory (SpacetimedDB default)",
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
                title = "Install Rust",
                description = "Rust is a programming language that runs 2x faster than C#\n"+
                "Note: Required to use the SpacetimeDB Server with Rust Language",
                isInstalled = hasRust,
                isEnabled = hasWSL && hasDebian && hasCurl && !String.IsNullOrEmpty(userName), // Only enabled if WSL and Debian are installed
                installAction = InstallRust
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
    }

    private void UpdateInstallerItemsStatus()
    {
        bool repaintNeeded = false;
        foreach (var item in installerItems)
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
            else if (item.title.Contains("Rust"))
            {
                newState = hasRust;
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
        EditorGUI.BeginDisabledGroup(isRefreshing);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            CheckInstallationStatus();
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

        // Minimize GUIContent creation during rendering
        if (titleStyle != null)
        {
            GUILayout.Label("SpacetimeDB Server Installer", titleStyle);
        }
        else
        {
            EditorGUILayout.LabelField("SpacetimeDB Server Installer");
        }
        
        EditorGUILayout.LabelField("Install all the required software to run your local SpacetimeDB Server.\n"+
        "Alpha Version - May yet require manual control.",
            EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30));
        
        // To debug showusernameprompt
        //hasWSL = true; hasDebian = true; hasDebianTrixie = true; 
        //hasCurl = false; hasSpacetimeDBServer = false; hasSpacetimeDBPath = false; hasRust = false;

        // Show usernameprompt for clarity before SpacetimeDB install
        bool showUsernamePrompt = String.IsNullOrEmpty(userName) && hasWSL && hasDebian;
        
        if (showUsernamePrompt)
        {
            foreach (var item in installerItems) item.isEnabled = false;
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
                foreach (var item in installerItems) item.isEnabled = true;
                userNamePrompt = false;
                Debug.Log("Username submitted via Enter: " + userName);
                
                // Use the current event to prevent it from propagating
                e.Use();

                CheckInstallationStatus();
            }
            
            // Add a submit button for clarity
            if (GUILayout.Button("Set", GUILayout.Width(50)) && !string.IsNullOrEmpty(tempUserNameInput))
            {
                // Submit the username only on button click
                userName = tempUserNameInput;
                EditorPrefs.SetString(PrefsKeyPrefix + "UserName", userName);
                foreach (var item in installerItems) item.isEnabled = true;
                userNamePrompt = false;
                Debug.Log("Username submitted via button: " + userName);

                CheckInstallationStatus();
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
        }
        
        // Begin the scrollview for the installer items
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        if (installerItems.Count == 0)
        {
            EditorGUILayout.HelpBox("No installer items found.", MessageType.Info);
        }
        else
        {
            // Cache to reduce GC and memory allocations
            for (int i = 0; i < installerItems.Count; i++)
            {
                DrawInstallerItem(installerItems[i], i);
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
        
        // Status (installed or install button)
        if (item.isInstalled && !alwaysShowInstall)
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
        
        // Description
        EditorGUILayout.LabelField(item.description, EditorStyles.wordWrappedMiniLabel);
        
        // Add username field for SpacetimeDB Server installer
        if (item.title.Contains("SpacetimeDB Server"))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Debian Username:", GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            string newUserName = EditorGUILayout.TextField(userName, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck() && newUserName != userName)
            {
                userName = newUserName;
                EditorPrefs.SetString(PrefsKeyPrefix + "UserName", newUserName);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }
        
        // If disabled, add a note about prerequisites
        if (isDisabled && !userNamePrompt)
        {
            GUIStyle prereqStyle = new GUIStyle(EditorStyles.miniLabel);
            prereqStyle.normal.textColor = new Color(0.7f, 0.5f, 0.3f); // Orange
            if (!hasDebianTrixie)
            EditorGUILayout.LabelField("Requires WSL2 with Debian Trixie to be installed first", prereqStyle);
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
    private void CheckInstallationStatus()
    {
        if (isRefreshing) return; // Don't start a new refresh if one is already running
        
        isRefreshing = true;
        SetStatus("Checking installation status...", Color.yellow);
        
        // Check for SpacetimeDB Unity SDK separately
        ServerSpacetimeSDKInstaller.IsSDKInstalled((isSDKInstalled) => {
            hasSpacetimeDBUnitySDK = isSDKInstalled;
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBUnitySDK", hasSpacetimeDBUnitySDK);
            UpdateInstallerItemsStatus();
        });
        
        cmdProcess.CheckPrerequisites((wsl, debian, trixie, curl, spacetime, spacetimePath, rust) => {
            hasWSL = wsl;
            hasDebian = debian;
            hasDebianTrixie = trixie;
            hasCurl = curl;
            hasSpacetimeDBServer = spacetime;
            hasSpacetimeDBPath = spacetimePath;
            hasRust = rust;

            // Debug force to false to test the installer
            //hasWSL = false;
            //hasDebian = false;
            //hasDebianTrixie = false;
            //hasCurl = false;
            //hasSpacetimeDBServer = false;
            //hasSpacetimeDBPath = false;
            //hasRust = false;
            
            // Save state to EditorPrefs
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasWSL", hasWSL);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebian", hasDebian);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasDebianTrixie", hasDebianTrixie);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasCurl", hasCurl);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", hasSpacetimeDBServer);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", hasSpacetimeDBPath);
            EditorPrefs.SetBool(PrefsKeyPrefix + "HasRust", hasRust);
            EditorPrefs.SetBool(PrefsKeyPrefix + "VisibleInstallProcesses", visibleInstallProcesses);
            
            // Update UI
            UpdateInstallerItemsStatus();
            
            isRefreshing = false;
            SetStatus("Installation status updated.", Color.green); // This might request repaint (throttled)
        });
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
                string wsl1SetupCommand = "cmd.exe /c \"wsl --set-default-version 1 && wsl --install -d Debian\"";

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
                        EditorUtility.DisplayDialog("Restart Needed","Please restart your PC and try to install WSL1 again.", "OK");
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
                string wsl2InstallCommand = "wsl --set-default-version 2 && wsl --install -d Debian";
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
                    }
                    else
                    {
                        SetStatus("WSL2 with Debian installation failed or requires a restart. Please check console output and restart if prompted.", Color.red);
                    }
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
            SetStatus("cURL installation failed. Please check console output.", Color.red);
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
        
        if (hasSpacetimeDBServer)
        {
            SetStatus("SpacetimeDB Server is already installed.", Color.green);
            return;
        }
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
        
        SetStatus("Installing SpacetimeDB Server...", Color.green);
        
        // Command to install SpacetimeDB Server
        string spacetimeInstallCommand = $"wsl -d Debian -u " + userName + " bash -c \"echo y | curl -sSf https://install.spacetimedb.com | sh\"";

        // Use the ServerCMDProcess method to run the PowerShell command
        bool success = await cmdProcess.RunPowerShellInstallCommand(spacetimeInstallCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        
        if (success)
        {
            SetStatus("SpacetimeDB Server installation started. This may take some time.", Color.green);

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
    }
    #endregion
} // Class
} // Namespace