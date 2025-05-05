using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NorthernRogue.CCCP.Editor {

public class ServerInstallerWindow : EditorWindow
{
    #region Variables
    private List<InstallerItem> installerItems = new List<InstallerItem>();
    private ServerCMDProcess cmdProcess;
    
    // UI
    private Vector2 scrollPosition;
    private string statusMessage = "Ready to install components.";
    private Color statusColor = Color.grey;
    private string statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
    
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

    // Debug
    //private bool debugMode = false;
    private bool visibleInstallProcesses = true;

    // Debug install process
    private bool alwaysShowInstall = false;
    private bool installIfAlreadyInstalled = false;
    
    // Settings
    private const string PrefsKeyPrefix = "ServerWindow_"; // Use the same prefix as ServerWindow
    #endregion

    [MenuItem("SpacetimeDB/Server Installer")]
    public static void ShowWindow()
    {
        ServerInstallerWindow window = GetWindow<ServerInstallerWindow>("Server Installer");
        window.minSize = new Vector2(500, 400);
        window.InitializeInstallerItems();
    }
    
    private void OnEnable()
    {
        cmdProcess = new ServerCMDProcess(LogMessage, false);
        
        // Load installation status from EditorPrefs
        hasWSL = EditorPrefs.GetBool(PrefsKeyPrefix + "HasWSL", false);
        hasDebian = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebian", false);
        hasDebianTrixie = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebianTrixie", false);
        hasCurl = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCurl", false);
        hasSpacetimeDBServer = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", false);
        hasSpacetimeDBPath = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", false);
        hasRust = EditorPrefs.GetBool(PrefsKeyPrefix + "HasRust", false);
        hasSpacetimeDBUnitySDK = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBUnitySDK", false);
        // Load install debug settings from EditorPrefs
        visibleInstallProcesses = EditorPrefs.GetBool(PrefsKeyPrefix + "VisibleInstallProcesses", true);
        
        InitializeInstallerItems();
        
        CheckInstallationStatus();

        // Update installer items status based on loaded prefs
        UpdateInstallerItemsStatus();
    }
    
    #region Installer Items
    private void InitializeInstallerItems()
    {
        installerItems = new List<InstallerItem>
        {
            new InstallerItem
            {
                title = "Install WSL2 with Debian",
                description = "Windows Subsystem for Linux 2 with Debian Linux distribution\n"+" (May require a system restart)",
                isInstalled = hasDebian,
                installAction = InstallWSLDebian
            },
            new InstallerItem
            {
                title = "Install Debian Trixie Update",
                description = "Debian Trixie Update (Debian Version 13)\n"+" Required to run the SpacetimeDB Server",
                isInstalled = hasDebianTrixie,
                installAction = InstallDebianTrixie
            },
            new InstallerItem
            {
                title = "Install cURL",
                description = "cURL is a command-line tool for transferring data with URLs.\n"+" Required to install the SpacetimeDB Server",
                isInstalled = hasCurl,
                installAction = InstallCurl
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Server",
                description = "SpacetimeDB Server Installation for Debian\n"+" Will install to the defalt directory in the user's home directory",
                isInstalled = hasSpacetimeDBServer,
                installAction = InstallSpacetimeDBServer
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB PATH",
                description = "Add SpacetimeDB to the PATH environment variable of your Debian user",
                isInstalled = hasSpacetimeDBPath,
                installAction = InstallSpacetimeDBPath
            },
            new InstallerItem
            {
                title = "Install Rust",
                description = "Rust is a programming language that runs 2x faster than C#.\n"+" Required to use the SpacetimeDB Server with Rust Language",
                isInstalled = hasRust,
                installAction = InstallRust
            },
            new InstallerItem
            {
                title = "Install SpacetimeDB Unity SDK",
                description = "SpacetimeDB SDK contains essential scripts for SpacetimeDB development in Unity. \n"+" Examples include a network manager that syncs the client state with the database",
                isInstalled = hasSpacetimeDBUnitySDK,
                installAction = InstallSpacetimeDBUnitySDK
            }
        };
    }

    private void UpdateInstallerItemsStatus()
    {
        foreach (var item in installerItems)
        {
            if (item.title.Contains("WSL2"))
            {
                item.isInstalled = hasWSL && hasDebian;
            }
            else if (item.title.Contains("Debian Trixie"))
            {
                item.isInstalled = hasDebianTrixie;
            }
            else if (item.title.Contains("cURL"))
            {
                item.isInstalled = hasCurl;
            }
            else if (item.title.Contains("SpacetimeDB Server"))
            {
                item.isInstalled = hasSpacetimeDBServer;
            }
            else if (item.title.Contains("SpacetimeDB PATH"))
            {
                item.isInstalled = hasSpacetimeDBPath;
            }
            else if (item.title.Contains("Rust"))
            {
                item.isInstalled = hasRust;
            }
            else if (item.title.Contains("SpacetimeDB Unity SDK"))
            {
                item.isInstalled = hasSpacetimeDBUnitySDK;
            }
        }
        Repaint();
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
               
        // Visibility toggle
        EditorGUILayout.LabelField("Show install windows:", GUILayout.Width(125));
        bool newVisibleValue = EditorGUILayout.Toggle(visibleInstallProcesses, GUILayout.Width(20));
        if (newVisibleValue != visibleInstallProcesses)
        {
            visibleInstallProcesses = newVisibleValue;
            EditorPrefs.SetBool(PrefsKeyPrefix + "VisibleInstallProcesses", visibleInstallProcesses);
        }

        // Add space between elements
        GUILayout.FlexibleSpace();
        
        // Add a small space
        GUILayout.Space(10);
        
        // Username field
        EditorGUILayout.LabelField("Debian Username:", GUILayout.Width(110));
        string userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        string newUserName = EditorGUILayout.TextField(userName, GUILayout.Width(100));
        
        if (newUserName != userName)
        {
            EditorPrefs.SetString(PrefsKeyPrefix + "UserName", newUserName);
            cmdProcess.SetUserName(newUserName);
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawInstallerItemsList()
    {
        // Draw a box for the installer items list
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));

        EditorGUILayout.LabelField("SpacetimeDB Server Installer", titleStyle);
        EditorGUILayout.LabelField("Install all the required software to run your local SpacetimeDB Server.\n"+
        "Alpha Version - May yet require manual control.",
            EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30));
        
        // Begin the scrollview for the installer items
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        if (installerItems.Count == 0)
        {
            EditorGUILayout.HelpBox("No installer items found.", MessageType.Info);
        }
        else
        {
            foreach (var item in installerItems)
            {
                DrawInstallerItem(item);
            }
        }
        
        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();
    }
    
    private void DrawInstallerItem(InstallerItem item)
    {
        // Container box for each installer item
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        // Header with name and install button
        EditorGUILayout.BeginHorizontal();
        
        // Title
        EditorGUILayout.LabelField($" {item.title}", itemTitleStyle, GUILayout.ExpandWidth(true));
        
        // Status (installed or install button)
        if (item.isInstalled && !alwaysShowInstall)
        {
            EditorGUILayout.LabelField("✓ Installed", installedStyle, GUILayout.Width(100));
        }
        else
        {
            EditorGUILayout.Space(2);
            
            // Install button
            if (GUILayout.Button("Install", installButtonStyle, GUILayout.Width(100), GUILayout.Height(30)))
            {
                item.installAction?.Invoke();
            }
        }
        
        EditorGUILayout.EndHorizontal();
        
        // Description
        EditorGUILayout.LabelField($" {item.description}", EditorStyles.wordWrappedMiniLabel);
        
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
            SetStatus("Installation status updated.", Color.green);
            Repaint();
        });
    }
    #endregion
    
    #region Installation Methods
    private async void InstallWSLDebian()
    {
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        // If already installed, just update UI
        if (hasWSL && hasDebian)
        {
            SetStatus("WSL2 with Debian is already installed.", Color.green);
            return;
        }
        
        SetStatus("Installing WSL2 with Debian...", Color.yellow);
        
        // Use the ServerCMDProcess method to run the PowerShell command
        bool success = await cmdProcess.RunPowerShellInstallCommand("wsl --install -d Debian", LogMessage, visibleInstallProcesses);
        
        if (success)
        {
            SetStatus("WSL2 with Debian installation started. This may take some time.", Color.green);
            
            // Give some time before checking again
            await Task.Delay(5000);
            CheckInstallationStatus();
        }
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
        
        string userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        if (string.IsNullOrEmpty(userName))
        {
            SetStatus("Please enter your Debian username first.", Color.red);
            return;
        }
        
        SetStatus("Installing Debian Trixie Update - Step 1: apt update", Color.yellow);
        
        // Step 1: Update
        string updateCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo apt update\"";
        bool updateSuccess = await cmdProcess.RunPowerShellInstallCommand(updateCommand, LogMessage, visibleInstallProcesses);
        if (!updateSuccess)
        {
            SetStatus("Failed to update Debian. Trixie installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000); // Wait longer to ensure command completes
        
        // Step 2: Upgrade
        SetStatus("Installing Debian Trixie Update - Step 2: apt upgrade", Color.yellow);
        string upgradeCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo apt upgrade -y\"";
        bool upgradeSuccess = await cmdProcess.RunPowerShellInstallCommand(upgradeCommand, LogMessage, visibleInstallProcesses);
        if (!upgradeSuccess)
        {
            SetStatus("Failed to upgrade Debian. Trixie installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Step 3: Install update-manager-core
        SetStatus("Installing Debian Trixie Update - Step 3: install update-manager-core", Color.yellow);
        string coreCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo apt install -y update-manager-core\"";
        bool coreSuccess = await cmdProcess.RunPowerShellInstallCommand(coreCommand, LogMessage, visibleInstallProcesses);
        if (!coreSuccess)
        {
            SetStatus("Failed to install update-manager-core. Trixie installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Step 4: Change sources.list to trixie
        SetStatus("Installing Debian Trixie Update - Step 4: update sources to Trixie", Color.yellow);
        string sourcesCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo sed -i 's/bookworm/trixie/g' /etc/apt/sources.list\"";
        bool sourcesSuccess = await cmdProcess.RunPowerShellInstallCommand(sourcesCommand, LogMessage, visibleInstallProcesses);
        if (!sourcesSuccess)
        {
            SetStatus("Failed to update sources.list. Trixie installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Step 5: Update again for Trixie
        SetStatus("Installing Debian Trixie Update - Step 5: update package lists for Trixie", Color.yellow);
        string updateTrixieCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo apt update\"";
        bool updateTrixieSuccess = await cmdProcess.RunPowerShellInstallCommand(updateTrixieCommand, LogMessage, visibleInstallProcesses);
        if (!updateTrixieSuccess)
        {
            SetStatus("Failed to update package lists for Trixie. Installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Step 6: Full upgrade
        SetStatus("Installing Debian Trixie Update - Step 6: performing full upgrade to Trixie", Color.yellow);
        string fullUpgradeCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo apt full-upgrade -y\"";
        bool fullUpgradeSuccess = await cmdProcess.RunPowerShellInstallCommand(fullUpgradeCommand, LogMessage, visibleInstallProcesses);
        if (!fullUpgradeSuccess)
        {
            SetStatus("Failed to perform full upgrade to Trixie. Installation aborted.", Color.red);
            return;
        }
        await Task.Delay(10000);
        
        SetStatus("Debian Trixie Update installed. Shutting down WSL...", Color.green);
        await Task.Delay(2000);

        // WSL Shutdown
        cmdProcess.ShutdownWsl();
        await Task.Delay(5000); // Longer wait for shutdown

        // Restart WSL
        cmdProcess.StartWsl();
        SetStatus("WSL restarted. Checking installation status...", Color.green);
        await Task.Delay(5000); // Longer wait for startup
        CheckInstallationStatus();
        await Task.Delay(2000);
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
        
        string userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        if (string.IsNullOrEmpty(userName))
        {
            SetStatus("Please enter your Debian username first.", Color.red);
            return;
        }
        
        SetStatus("Installing curl - Step 1: Update package list", Color.yellow);
        
        // First update package list
        string updateCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo apt update\"";
        bool updateSuccess = await cmdProcess.RunPowerShellInstallCommand(updateCommand, LogMessage, visibleInstallProcesses);
        if (!updateSuccess)
        {
            SetStatus("Failed to update package list. Curl installation aborted.", Color.red);
            return;
        }
        await Task.Delay(5000); // Wait longer for update to complete
        
        // Then install curl
        SetStatus("Installing curl - Step 2: Installing curl package", Color.yellow);
        string curlInstallCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo apt install -y curl\"";
        bool installSuccess = await cmdProcess.RunPowerShellInstallCommand(curlInstallCommand, LogMessage, visibleInstallProcesses);
        if (!installSuccess)
        {
            SetStatus("Failed to install curl. Installation aborted.", Color.red);
            return;
        }
        await Task.Delay(5000); // Wait longer for installation to complete
        
        SetStatus("Curl installation complete. Checking status...", Color.green);
        await Task.Delay(2000);
        
        // Verify installation
        string verifyCommand = "wsl -d Debian -u " + userName + " bash -c \"curl --version\"";
        bool verifySuccess = await cmdProcess.RunPowerShellInstallCommand(verifyCommand, LogMessage, visibleInstallProcesses);
        if (!verifySuccess)
        {
            SetStatus("Couldn't verify curl installation. Please check manually.", Color.yellow);
        }
        
        // Check installation status
        CheckInstallationStatus();
        await Task.Delay(2000);
        
        if (hasCurl)
        {
            SetStatus("curl installed successfully.", Color.green);
        }
        else
        {
            SetStatus("curl installation failed. Please install manually.", Color.red);
        }
    }
    
    private async void InstallSpacetimeDBServer()
    {
        CheckInstallationStatus();
        await Task.Delay(1000);
        
        if (hasSpacetimeDBServer)
        {
            SetStatus("SpacetimeDB Server is already installed.", Color.green);
            return;
        }
        
        // Check prerequisites
        if (!hasCurl)
        {
            SetStatus("curl is required to install SpacetimeDB Server. Please install curl first.", Color.red);
            return;
        }
        if (!hasDebianTrixie)
        {
            SetStatus("Debian Trixie Update is required to install SpacetimeDB Server. Please install Debian Trixie Update first.", Color.red);
            return;
        }
        if (!hasWSL)
        {
            SetStatus("WSL2 with Debian is required to install SpacetimeDB Server. Please install WSL2 with Debian first.", Color.red);
            return;
        }
        
        SetStatus("Installing SpacetimeDB Server...", Color.yellow);
        
        // Command to install SpacetimeDB Server
        string userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        string spacetimeInstallCommand = $"wsl -d Debian -u {userName} bash -c \"curl -sSf https://install.spacetimedb.com | sh\"";
            
        // Use the ServerCMDProcess method to run the PowerShell command
        bool success = await cmdProcess.RunPowerShellInstallCommand(spacetimeInstallCommand, LogMessage, visibleInstallProcesses);
        
        if (success)
        {
            SetStatus("SpacetimeDB Server installation started. This may take some time.", Color.green);

            // Give some time before checking again
            await Task.Delay(5000);
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
        
        SetStatus("Installing SpacetimeDB PATH...", Color.yellow);
        
        // Use the ServerCMDProcess method to run the PowerShell command
        string userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        string command = string.Format(
            "wsl -d Debian -u {0} bash -c \"echo \\\"export PATH=/home/{0}/.local/bin:\\$PATH\\\" >> ~/.bashrc\"",
            userName
        );
        bool success = await cmdProcess.RunPowerShellInstallCommand(command, LogMessage, visibleInstallProcesses);

        if (success)
        {
            SetStatus("SpacetimeDB PATH installation started. This may take some time.", Color.green);
            
            // Give some time before checking again
            await Task.Delay(5000);
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
        
        string userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        if (string.IsNullOrEmpty(userName))
        {
            SetStatus("Please enter your Debian username first.", Color.red);
            return;
        }
        
        SetStatus("Installing Rust - Step 1: Update package list", Color.yellow);
        
        // First update package list
        string updateCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo apt update\"";
        bool updateSuccess = await cmdProcess.RunPowerShellInstallCommand(updateCommand, LogMessage, visibleInstallProcesses);
        if (!updateSuccess)
        {
            SetStatus("Failed to update package list. Rust installation aborted.", Color.red);
            return;
        }
        await Task.Delay(5000); // Wait longer for update to complete
        
        // Then install Rust
        SetStatus("Installing Rust - Step 2: Installing rustc package", Color.yellow);
        string rustInstallCommand = "wsl -d Debian -u " + userName + " bash -c \"sudo apt install -y rustc\"";
        bool installSuccess = await cmdProcess.RunPowerShellInstallCommand(rustInstallCommand, LogMessage, visibleInstallProcesses);
        if (!installSuccess)
        {
            SetStatus("Failed to install Rust. Installation aborted.", Color.red);
            return;
        }
        await Task.Delay(5000); // Wait longer for installation to complete
        
        SetStatus("Rust installation complete. Checking status...", Color.green);
        await Task.Delay(2000);
        
        // Verify installation
        string verifyCommand = "wsl -d Debian -u " + userName + " bash -c \"rustc --version\"";
        bool verifySuccess = await cmdProcess.RunPowerShellInstallCommand(verifyCommand, LogMessage, visibleInstallProcesses);
        if (!verifySuccess)
        {
            SetStatus("Couldn't verify Rust installation. Please check manually.", Color.yellow);
        }
        
        // Check installation status
        CheckInstallationStatus();
        await Task.Delay(2000);
        
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

        // Use the ServerSpacetimeSDKInstaller to install the SDK
        ServerSpacetimeSDKInstaller.InstallSDK((success, errorMessage) => 
        {
            if (success)
            {
                SetStatus("SpacetimeDB Unity SDK installed successfully.", Color.green);
                hasSpacetimeDBUnitySDK = true;
                EditorPrefs.SetBool(PrefsKeyPrefix + "HasSpacetimeDBUnitySDK", true);
                UpdateInstallerItemsStatus();
            }
            else
            {
                SetStatus($"SpacetimeDB Unity SDK installation failed: {errorMessage}", Color.red);
            }
        });
    }
    #endregion
    
    #region Install SpacetimeDB PATH
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
        Repaint();
    }
    
    private void SetStatus(string message, Color color)
    {
        statusMessage = message;
        statusColor = color;
        statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
    }
    #endregion
    
    #region Data Classes
    [Serializable]
    public class InstallerItem
    {
        public string title;
        public string description;
        public bool isInstalled;
        public Action installAction;
    }
    #endregion
} // Class
} // Namespace