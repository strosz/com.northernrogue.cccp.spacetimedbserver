using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NorthernRogue.CCCP.Editor.Settings;

// Contains all installation methods for development builds only ///
// Asset Store builds cannot include installation code due to Unity Asset Store guidelines ///

namespace NorthernRogue.CCCP.Editor {

public class ServerInstallProcess
{
    // Reference to parent window
    private ServerSetupWindow window;
    
    // Constructor to pass parent window reference
    public ServerInstallProcess(ServerSetupWindow parentWindow)
    {
        window = parentWindow;
    }
    
    // Convenience accessors for parent window state
    private ServerWSLProcess wslProcess => window.wslProcess;
    private ServerCustomProcess customProcess => window.customProcess;
    private ServerManager serverManager => window.serverManager;
    
    private bool hasWSL => window.hasWSL;
    private bool hasDebian => window.hasDebian;
    private bool hasDebianTrixie => window.hasDebianTrixie;
    private bool hasCurl => window.hasCurl;
    private bool hasSpacetimeDBServer => window.hasSpacetimeDBServer;
    private bool hasSpacetimeDBPath => window.hasSpacetimeDBPath;
    private bool hasSpacetimeDBService => window.hasSpacetimeDBService;
    internal bool hasSpacetimeDBLogsService => window.hasSpacetimeDBLogsService;
    private bool hasRust => window.hasRust;
    private bool hasNETSDK => window.hasNETSDK;
    private bool hasBinaryen => window.hasBinaryen;
    private bool hasGit => window.hasGit;
    internal bool hasSpacetimeDBUnitySDK {
        get => window.hasSpacetimeDBUnitySDK;
        set => window.hasSpacetimeDBUnitySDK = value;
    }
    
    private bool hasCustomDebianUser => window.hasCustomDebianUser;
    private bool hasCustomDebianTrixie => window.hasCustomDebianTrixie;
    private bool hasCustomCurl => window.hasCustomCurl;
    private bool hasCustomSpacetimeDBServer => window.hasCustomSpacetimeDBServer;
    private bool hasCustomSpacetimeDBPath => window.hasCustomSpacetimeDBPath;
    private bool hasCustomSpacetimeDBService => window.hasCustomSpacetimeDBService;
    internal bool hasCustomSpacetimeDBLogsService => window.hasCustomSpacetimeDBLogsService;
    
    private bool installIfAlreadyInstalled => window.installIfAlreadyInstalled;
    internal bool visibleInstallProcesses {
        get => window.visibleInstallProcesses;
        set => window.visibleInstallProcesses = value;
    }
    internal bool keepWindowOpenForDebug {
        get => window.keepWindowOpenForDebug;
        set => window.keepWindowOpenForDebug = value;
    }
    private bool updateCargoToml => window.updateCargoToml;
    internal bool WSL1Installed {
        get => window.WSL1Installed;
        set => window.WSL1Installed = value;
    }
    
    private string userName => window.userName;
    private string sshUserName => window.sshUserName;
    private string serverDirectory => window.serverDirectory;
    private string spacetimeDBLatestVersion => window.spacetimeDBLatestVersion;
    internal string spacetimeDBCurrentVersion {
        get => window.spacetimeDBCurrentVersion;
        set => window.spacetimeDBCurrentVersion = value;
    }
    internal string spacetimeDBCurrentVersionCustom {
        get => window.spacetimeDBCurrentVersionCustom;
        set => window.spacetimeDBCurrentVersionCustom = value;
    }
    private string spacetimeDBCurrentVersionTool => window.spacetimeDBCurrentVersionTool;
    private string tempCreateUserNameInput => window.tempCreateUserNameInput;
    
    internal string rustCurrentVersion {
        get => window.rustCurrentVersion;
        set => window.rustCurrentVersion = value;
    }
    internal string rustLatestVersion {
        get => window.rustLatestVersion;
        set => window.rustLatestVersion = value;
    }
    internal bool rustUpdateAvailable {
        get => window.rustUpdateAvailable;
        set => window.rustUpdateAvailable = value;
    }
    
    internal string spacetimeSDKCurrentVersion {
        get => window.spacetimeSDKCurrentVersion;
        set => window.spacetimeSDKCurrentVersion = value;
    }
    internal string spacetimeSDKLatestVersion => window.spacetimeSDKLatestVersion;
    internal bool spacetimeSDKUpdateAvailable {
        get => window.spacetimeSDKUpdateAvailable;
        set => window.spacetimeSDKUpdateAvailable = value;
    }
    
    private List<ServerSetupWindow.InstallerItem> installerItems => window.installerItems;
    private List<ServerSetupWindow.InstallerItem> customInstallerItems => window.customInstallerItems;
    
    // Helper methods to call parent window methods
    private void SetStatus(string message, Color color) => window.SetStatusInternal(message, color);
    private void LogMessage(string message, int type) => window.LogMessageInternal(message, type);
    private void CheckPrerequisitesWSL() => window.CheckPrerequisitesWSL();
    private void CheckPrerequisitesDocker() => window.CheckPrerequisitesDocker();
    private void CheckPrerequisitesCustom() => window.CheckPrerequisitesCustom();
    private void UpdateInstallerItemsStatus() => window.UpdateInstallerItemsStatus();
    private void Repaint() => window.Repaint();
    private void UpdateCargoSpacetimeDBVersion() => window.UpdateCargoSpacetimeDBVersion();
    
    #region WSL Installation Methods
    public async void InstallWSLDebian()
    {
        CheckPrerequisitesWSL();
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

        SetStatus("Running WSL compatibility test...", Color.green);

        bool installedSuccessfully = false;

        // Define installation actions for WSL1 and WSL2
        Action installWSL1 = async () => 
        {
            SetStatus("Installing WSL1 with Debian...", Color.green);

            if (EditorUtility.DisplayDialog("Install WSL1 with Debian",
            "This will install WSL1 with Debian.\n"+
            "You may have to press keys to create your user credentials during the installation process.\n"+
            "Note: When you type the password it is updated even if you don't see it.\n"+
            "Do you want to continue?", 
            "Yes", "No"))
            {
                string dismCommand = "powershell.exe -Command \"Start-Process powershell -Verb RunAs -ArgumentList '-Command dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart'\"";
                string wsl1SetupCommand = "cmd.exe /c \"wsl --update & wsl --set-default-version 1 && wsl --install -d Debian\"";

                bool dismSuccess = await wslProcess.RunPowerShellInstallCommand(dismCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug, true);
                if (dismSuccess)
                {
                    SetStatus("DISM successful. Proceeding with WSL1 setup...", Color.yellow);
                    installedSuccessfully = await wslProcess.RunPowerShellInstallCommand(wsl1SetupCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug, true);
                }
                else
                {
                    installedSuccessfully = false;
                    SetStatus("DISM command failed for WSL1 setup. Please check console output.", Color.red);
                }

                if (installedSuccessfully)
                {
                    CheckPrerequisitesWSL();
                    await Task.Delay(1000);
                    if (hasWSL && hasDebian)
                    {
                        SetStatus("WSL1 with Debian installed successfully.", Color.green);

                        WSL1Installed = true; // To handle WSL1 Debian installs uniquely
                        CCCPSettingsAdapter.SetWSL1Installed(WSL1Installed);

                        UpdateInstallerItemsStatus();

                        // Display dialog informing user about Debian first-time setup
                        EditorUtility.DisplayDialog(
                            "Debian First-Time Setup",
                            "Starting Debian for the first time so you can create your user credentials. You can close the Debian window afterwards.",
                            "OK"
                        );
                        // Launch visible Debian window without username required
                        bool userNameReq = false;
                        wslProcess.OpenDebianWindow(userNameReq);
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
            
            if (EditorUtility.DisplayDialog("Install WSL2 with Debian", 
            "This will install WSL2 with Debian.\n"+
            "You may have to press keys to create your user credentials during the installation process.\n"+
            "Note: When you type the password it is updated even if you don't see it.\n"+
            "Do you want to continue?", 
            "Yes", "No"))
            {
                string wsl2InstallCommand = "cmd.exe /c \"wsl --update & wsl --set-default-version 2 && wsl --install -d Debian\"";
                await wslProcess.RunPowerShellInstallCommand(wsl2InstallCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug, true);
                CheckPrerequisitesWSL();
                if (hasWSL && hasDebian)
                {
                    // Display dialog informing user about Debian first-time setup
                    bool createDebianCredentials = EditorUtility.DisplayDialog(
                        "Debian First-Time Setup",
                        "Starting Debian for the first time so that you are asked to create your user credentials. You can close the Debian window afterwards.\n\n" +
                        "Note! In some WSL versions the credentials were already created and you may skip.",
                        "Create Credentials", "Skip"
                    );

                    if (createDebianCredentials)
                    {
                        // If the user proceeds, we will open Debian without username required
                        bool userNameReq = false;
                        wslProcess.OpenDebianWindow(userNameReq);
                    }

                    SetStatus("WSL2 with Debian installed successfully.", Color.green);
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

    public async void InstallDebianTrixie()
    {
        CheckPrerequisitesWSL();
        await Task.Delay(1000);
        
        if (hasDebianTrixie && !installIfAlreadyInstalled)
        {
            SetStatus("Debian Trixie Update is already installed.", Color.green);
            return;
        }

        EditorUtility.DisplayDialog(
            "Debian Trixie Upgrade",
            "Installer windows will appear during this installation which requires user input.\n\n" +
            "Please press any key when asked and create and write down your new credentials when asked.\n\n" +
            "There may be a window titled >Configuring libc6< and there you can press Yes.",
            "OK"
        );
        
        SetStatus("Installing Debian Trixie Update - Step 1: apt update", Color.yellow);
        
        // Step 1: Update
        string updateCommand = "wsl -d Debian -u root bash -c \"sudo apt update\"";
        bool updateSuccess = await wslProcess.RunPowerShellInstallCommand(updateCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!updateSuccess)
        {
            SetStatus("Failed to update Debian. Trixie installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000); // Wait longer to ensure command completes
        
        // Step 2: Upgrade
        SetStatus("Installing Debian Trixie Update - Step 2: apt upgrade", Color.yellow);
        string upgradeCommand = "wsl -d Debian -u root bash -c \"sudo apt upgrade -y\"";
        bool upgradeSuccess = await wslProcess.RunPowerShellInstallCommand(upgradeCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!upgradeSuccess)
        {
            // It's common to get a failed install here, but the install process will work anyway
            SetStatus("Failed to upgrade for Trixie install. Attempting to continue.", Color.green);
        }
        await Task.Delay(2000);
        
        // Step 3: Install update-manager-core
        SetStatus("Installing Debian Trixie Update - Step 3: install update-manager-core", Color.yellow);
        string coreCommand = "wsl -d Debian -u root bash -c \"sudo apt install -y update-manager-core\"";
        bool coreSuccess = await wslProcess.RunPowerShellInstallCommand(coreCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!coreSuccess)
        {
            // It's common to get a failed install here, but the install process will work anyway
            SetStatus("Failed to install update-manager-core. Attempting to continue.", Color.green);
        }
        await Task.Delay(2000);
        
        // Step 4: Change sources.list to trixie
        SetStatus("Installing Debian Trixie Update - Step 4: update sources to Trixie", Color.yellow);
        string sourcesCommand = "wsl -d Debian -u root bash -c \"sudo sed -i 's/bookworm/trixie/g' /etc/apt/sources.list\"";
        bool sourcesSuccess = await wslProcess.RunPowerShellInstallCommand(sourcesCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!sourcesSuccess)
        {
            SetStatus("Failed to update sources.list. Trixie installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Step 5: Update again for Trixie
        SetStatus("Installing Debian Trixie Update - Step 5: update package lists for Trixie", Color.yellow);
        string updateTrixieCommand = "wsl -d Debian -u root bash -c \"sudo apt update\"";
        bool updateTrixieSuccess = await wslProcess.RunPowerShellInstallCommand(updateTrixieCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!updateTrixieSuccess)
        {
            SetStatus("Failed to update package lists for Trixie. Installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Step 6: Full upgrade
        SetStatus("Installing Debian Trixie Update - Step 6: performing full upgrade to Trixie", Color.yellow);
        string fullUpgradeCommand = "wsl -d Debian -u root bash -c \"sudo apt full-upgrade -y\"";
        bool fullUpgradeSuccess = await wslProcess.RunPowerShellInstallCommand(fullUpgradeCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!fullUpgradeSuccess)
        {
            CheckPrerequisitesWSL();
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
        wslProcess.ShutdownWsl();
        await Task.Delay(3000); // Longer wait for shutdown

        // Restart WSL
        wslProcess.StartWsl();
        SetStatus("WSL restarted. Checking installation status...", Color.green);
        await Task.Delay(5000); // Longer wait for startup
        CheckPrerequisitesWSL();
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
    
    public async void InstallCurl()
    {
        CheckPrerequisitesWSL();
        await Task.Delay(1000);
        
        if (hasCurl && !installIfAlreadyInstalled)
        {
            SetStatus("curl is already installed.", Color.green);
            return;
        }
                
        SetStatus("Installing curl...", Color.green);
        
        string updateCommand = $"wsl -d Debian -u root bash -c \"echo 'Updating package lists...' && sudo apt update\"";
        SetStatus("Running: apt update", Color.yellow);
        bool updateSuccess = await wslProcess.RunPowerShellInstallCommand(
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
        bool installSuccess = await wslProcess.RunPowerShellInstallCommand(
            installCommand, 
            LogMessage, 
            visibleInstallProcesses, 
            keepWindowOpenForDebug
        );
        if (!installSuccess)
        {
            CheckPrerequisitesWSL();
            await Task.Delay(2000);
            if (WSL1Installed && hasCurl)
            SetStatus("cURL installed successfully. (WSL1)", Color.green);
            else
            SetStatus("Failed to install cURL. Installation aborted.", Color.red);
            
            return;
        }
        
        // Check installation status
        CheckPrerequisitesWSL();
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
    
    public async void InstallSpacetimeDBServer()
    {
        // Requires visible install processes and keep window open
        // Because the user has to interact with the installer window
        // Check if we can add yes to the command to auto-answer
        visibleInstallProcesses = true;
        keepWindowOpenForDebug = true;

        CheckPrerequisitesWSL();
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

        // Use the ServerwslProcess method to run the PowerShell command
        bool success = await wslProcess.RunPowerShellInstallCommand(spacetimeInstallCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        
        if (success)
        {
            SetStatus("SpacetimeDB Server installation completed. Checking installation status...", Color.green);

            await serverManager.CheckSpacetimeDBVersionWSL();
            spacetimeDBCurrentVersion = spacetimeDBLatestVersion;
            
            CheckPrerequisitesWSL();

            await Task.Delay(1000);
            
            if (hasSpacetimeDBServer)
            {
                SetStatus("SpacetimeDB Server installed successfully.", Color.green);
                // Update Cargo.toml spacetimedb version if needed
                if (updateCargoToml) UpdateCargoSpacetimeDBVersion();
            }
            else
            {
                SetStatus("SpacetimeDB Server installation failed. Please install manually.", Color.red);
            }

            Repaint();
        }
    }

    public async void InstallSpacetimeDBPath()
    {
        CheckPrerequisitesWSL();
        await Task.Delay(1000);
        
        // If already installed, just update UI
        if (hasSpacetimeDBPath)
        {
            SetStatus("SpacetimeDB PATH is already installed.", Color.green);
            return;
        }
        
        SetStatus("Installing SpacetimeDB PATH...", Color.green);
        
        // Use the ServerwslProcess method to run the PowerShell command
        string command = string.Format(
            "wsl -d Debian -u {0} bash -c \"echo \\\"export PATH=/home/{0}/.local/bin:\\$PATH\\\" >> ~/.bashrc\"",
            userName
        );
        bool success = await wslProcess.RunPowerShellInstallCommand(command, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);

        if (success)
        {
            SetStatus("SpacetimeDB PATH installation started. This may take some time.", Color.green);
            await Task.Delay(1000);
            CheckPrerequisitesWSL();
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

    public async void InstallSpacetimeDBService()
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

        // Get the expected module name from the installer item or Settings
        string expectedModuleName = "";
        foreach (var item in installerItems)
        {
            if (item.title == "Install SpacetimeDB Service")
            {
                expectedModuleName = item.expectedModuleName;
                break;
            }
        }

        // If expectedModuleName is empty, try to get it from Settings
        if (string.IsNullOrEmpty(expectedModuleName))
        {
            expectedModuleName = CCCPSettingsAdapter.GetModuleName();
        }
        
        if (string.IsNullOrEmpty(expectedModuleName))
        {
            EditorUtility.DisplayDialog("Module Name Required",
            "You haven't added any module in Pre-Requisites. It is required to install the SpacetimeDB Service.",
            "OK");
            SetStatus("Module name is not set. Please add one in Pre-Requisites.", Color.yellow);
            return;
        }

        CheckPrerequisitesWSL();
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
            
            "# Create the database logs wrapper script for dynamic module switching\n" +
            "echo \"Creating SpacetimeDB database logs wrapper script...\"\n" +
            $"sudo tee /home/{userName}/.local/bin/spacetime-database-logs-switching.sh << 'EOF'\n" +
            "#!/bin/bash\n" +
            "# SpacetimeDB Database Logs Service Wrapper\n" +
            "# Dynamically reads current module from config file\n" +
            "\n" +
            "# Remove strict error handling to prevent unnecessary exits\n" +
            "set -u  # Only exit on undefined variables, not on all errors\n" +
            "\n" +
            $"CURRENT_MODULE_FILE=\"/home/{userName}/.local/current_spacetime_module\"\n" +
            $"SPACETIME_PATH=\"/home/{userName}/.local/bin/spacetime\"\n" +
            "LOG_FILE=\"/tmp/spacetime-logs-wrapper.log\"\n" +
            "\n" +
            "# Function to log with timestamp (only to file, not stdout to avoid spam)\n" +
            "log_debug() {\n" +
            "    echo \"$(date '+%Y-%m-%d %H:%M:%S') - $1\" >> \"$LOG_FILE\"\n" +
            "}\n" +
            "\n" +
            "# Function to handle script termination gracefully\n" +
            "cleanup() {\n" +
            "    log_debug \"Wrapper script received termination signal\"\n" +
            "    exit 0\n" +
            "}\n" +
            "\n" +
            "# Set up signal handlers\n" +
            "trap cleanup SIGTERM SIGINT\n" +
            "\n" +
            "# Validate config file exists and is readable\n" +
            "if [ ! -f \"$CURRENT_MODULE_FILE\" ]; then\n" +
            "    log_debug \"ERROR: Module config file does not exist: $CURRENT_MODULE_FILE\"\n" +
            "    sleep 30  # Wait before exiting to prevent rapid restarts\n" +
            "    exit 1\n" +
            "fi\n" +
            "\n" +
            "if [ ! -r \"$CURRENT_MODULE_FILE\" ]; then\n" +
            "    log_debug \"ERROR: Cannot read module config file: $CURRENT_MODULE_FILE\"\n" +
            "    sleep 30\n" +
            "    exit 1\n" +
            "fi\n" +
            "\n" +
            "# Read and validate module name\n" +
            "MODULE=$(cat \"$CURRENT_MODULE_FILE\" | tr -d '\\n\\r' | xargs)\n" +
            "\n" +
            "if [ -z \"$MODULE\" ]; then\n" +
            "    log_debug \"ERROR: Module config file is empty: $CURRENT_MODULE_FILE\"\n" +
            "    sleep 30\n" +
            "    exit 1\n" +
            "fi\n" +
            "\n" +
            "# Validate spacetime binary exists\n" +
            "if [ ! -x \"$SPACETIME_PATH\" ]; then\n" +
            "    log_debug \"ERROR: SpacetimeDB binary not found or not executable: $SPACETIME_PATH\"\n" +
            "    sleep 30\n" +
            "    exit 1\n" +
            "fi\n" +
            "\n" +
            "# Log start (only once to debug file, not to systemd logs)\n" +
            "log_debug \"Starting database logs for module: $MODULE\"\n" +
            "\n" +
            "# Start logging - use exec to replace shell process\n" +
            "exec \"$SPACETIME_PATH\" logs \"$MODULE\" -f\n" +
            "EOF\n\n" +
            
            "# Make the wrapper script executable\n" +
            $"sudo chmod +x /home/{userName}/.local/bin/spacetime-database-logs-switching.sh\n\n" +
            
            "# Create initial module config file with expected module\n" +
            $"mkdir -p /home/{userName}/.local\n" +
            $"echo '{expectedModuleName}' > /home/{userName}/.local/current_spacetime_module\n" +
            $"chmod 644 /home/{userName}/.local/current_spacetime_module\n" +
            $"chown {userName}:{userName} /home/{userName}/.local/current_spacetime_module\n\n" +
            
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
            "Restart=on-failure\n" +
            "RestartSec=30\n" +
            "StartLimitIntervalSec=300\n" +
            "StartLimitBurst=3\n" +
            $"ExecStart=/home/{userName}/.local/bin/spacetime-database-logs-switching.sh\n" +
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
            $"echo '{userName} ALL=(root) NOPASSWD: /usr/bin/systemctl restart spacetimedb-logs.service' | sudo tee -a /etc/sudoers.d/spacetimedb\n" +
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
              bool success = await wslProcess.RunPowerShellInstallCommand(command, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
            
            if (success)
            {
                SetStatus("SpacetimeDB Service installation completed. Checking status...", Color.green);
                await Task.Delay(2000);
                
                CheckPrerequisitesWSL();
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

    public async void InstallRust()
    {
        CheckPrerequisitesWSL();
        await Task.Delay(1000);
        
        bool isUpdate = hasRust && rustUpdateAvailable && !string.IsNullOrEmpty(rustLatestVersion);
        
        if (hasRust && !installIfAlreadyInstalled && !isUpdate)
        {
            SetStatus("Rust is already installed.", Color.green);
            return;
        }

        if (!hasCurl)
        {
            SetStatus("curl is required to install Rust. Please install curl first.", Color.red);
            return;
        }
        
        if (isUpdate)
        {
            SetStatus($"Updating Rust from v{rustCurrentVersion} to v{rustLatestVersion} - Step 1: Running rustup update", Color.yellow);
        }
        else
        {
            SetStatus("Installing Rust - Step 1: Update package list", Color.yellow);
        }
        
        // Handle Rust update case
        if (isUpdate)
        {
            SetStatus($"Updating Rust from v{rustCurrentVersion} to v{rustLatestVersion} - Running rustup update", Color.yellow);
            string rustUpdateCommand = $"wsl -d Debian -u {userName} bash -c \". \\\"$HOME/.cargo/env\\\" && rustup update\"";
            bool rustUpdateSuccess = await wslProcess.RunPowerShellInstallCommand(rustUpdateCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
            
            if (rustUpdateSuccess)
            {
                // Update the current version to the latest version
                rustCurrentVersion = rustLatestVersion;
                CCCPSettingsAdapter.SetRustCurrentVersionWSL(rustCurrentVersion);

                // Clear the update available flags
                rustUpdateAvailable = false;
                rustLatestVersion = "";
                CCCPSettingsAdapter.SetRustUpdateAvailable(false);
                CCCPSettingsAdapter.SetRustLatestVersionWSL("");
                
                SetStatus($"Rust updated successfully to v{rustCurrentVersion}!", Color.green);
            }
            else
            {
                SetStatus("Failed to update Rust. Update aborted.", Color.red);
            }
            
            UpdateInstallerItemsStatus();
            return;
        }
        
        // First update package list
        string updateCommand = "wsl -d Debian -u root bash -c \"sudo apt update\"";
        bool updateSuccess = await wslProcess.RunPowerShellInstallCommand(updateCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!updateSuccess)
        {
            SetStatus("Failed to update package list. Rust installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000); // Shorter delay
        
        // Then install Rust using rustup
        SetStatus("Installing Rust - Step 2: Installing rustup", Color.yellow);
        string rustInstallCommand = $"wsl -d Debian -u {userName} bash -c \"echo 1 | curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh\"";
        bool installSuccess = await wslProcess.RunPowerShellInstallCommand(rustInstallCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
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
        bool sourceSuccess = await wslProcess.RunPowerShellInstallCommand(sourceCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
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
        bool buildEssentialSuccess = await wslProcess.RunPowerShellInstallCommand(buildEssentialCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!buildEssentialSuccess)
        {
            CheckPrerequisitesWSL(); // If failed to install build-essential, we still check if Rust is installed
            await Task.Delay(1000);
            if (WSL1Installed && hasRust)
            {
                SetStatus("Rust installed successfully. (WSL1)", Color.green); // If WSL1 Rust is successfully installed here
                return;
            } else {
                SetStatus("Warning: Failed to install build-essential. Some Rust packages may not compile correctly.", Color.yellow);
                return;
            }
        }

        // Check installation status
        CheckPrerequisitesWSL();
        await Task.Delay(1000);
        
        if (hasRust)
        {
            // Fetch the actual Rust version for immediate display
            await serverManager.CheckRustVersionWSL();
            UpdateInstallerItemsStatus();
            SetStatus("Rust installed successfully.", Color.green);
        }
        else
        {
            SetStatus("Rust installation failed. Please install manually.", Color.red);
        }

        Repaint();
    }

    public async void InstallNETSDK()
    {
        CheckPrerequisitesWSL();
        await Task.Delay(1000);
        
        if (hasNETSDK && !installIfAlreadyInstalled)
        {
            SetStatus(".NET SDK 8.0 is already installed.", Color.green);
            return;
        }

        if (!hasCurl)
        {
            SetStatus("curl is required to install .NET SDK 8.0. Please install curl first.", Color.red);
            return;
        }
        
        SetStatus("Installing .NET SDK 8.0 - Step 1: Update package list", Color.yellow);
        
        // First update package list
        string updateCommand = "wsl -d Debian -u root bash -c \"sudo apt update\"";
        bool updateSuccess = await wslProcess.RunPowerShellInstallCommand(updateCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!updateSuccess)
        {
            SetStatus("Failed to update package list. .NET SDK 8.0 installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Install required packages for .NET SDK
        SetStatus("Installing .NET SDK 8.0 - Step 2: Installing required packages", Color.yellow);
        string prereqCommand = "wsl -d Debian -u root bash -c \"sudo apt install -y wget\"";
        bool prereqSuccess = await wslProcess.RunPowerShellInstallCommand(prereqCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!prereqSuccess)
        {
            SetStatus("Failed to install prerequisites for .NET SDK 8.0. Installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Download Microsoft package signing key
        SetStatus("Installing .NET SDK 8.0 - Step 3: Adding Microsoft package signing key", Color.yellow);
        string keyCommand = $"wsl -d Debian -u {userName} bash -c \"wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb\"";
        bool keySuccess = await wslProcess.RunPowerShellInstallCommand(keyCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!keySuccess)
        {
            if (WSL1Installed) // WSL1 continues despite if some commands are unsuccessful
            {
                SetStatus(".NET SDK 8.0 installation continuing. (WSL1)", Color.green);
            } else {
                SetStatus("Failed to download Microsoft package signing key. Installation aborted.", Color.red);
                return;
            }
        }
        await Task.Delay(2000);
        
        // Install Microsoft package
        SetStatus("Installing .NET SDK 8.0 - Step 4: Installing Microsoft package repository", Color.yellow);
        string installMSPackageCommand = $"wsl -d Debian -u {userName} bash -c \"sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb\"";
        bool installMSPackageSuccess = await wslProcess.RunPowerShellInstallCommand(installMSPackageCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!installMSPackageSuccess)
        {
            if (WSL1Installed)
            {
                SetStatus(".NET SDK 8.0 installation continuing. (WSL1)", Color.green);
            } else {
                SetStatus("Failed to install Microsoft package repository. Installation aborted.", Color.red);
                return;
            }
        }
        await Task.Delay(2000);
        
        // Update package list again with Microsoft repository
        SetStatus("Installing .NET SDK 8.0 - Step 5: Updating package list with Microsoft repository", Color.yellow);
        string updateMSCommand = "wsl -d Debian -u root bash -c \"sudo apt update\"";
        bool updateMSSuccess = await wslProcess.RunPowerShellInstallCommand(updateMSCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!updateMSSuccess)
        {
            SetStatus("Failed to update package list with Microsoft repository. Installation aborted.", Color.red);
            return;
        }
        await Task.Delay(2000);
        
        // Install .NET SDK 8.0
        SetStatus("Installing .NET SDK 8.0 - Step 6: Installing .NET SDK 8.0", Color.yellow);
        string netSDKInstallCommand = "wsl -d Debian -u root bash -c \"sudo apt install -y dotnet-sdk-8.0\"";
        bool netSDKInstallSuccess = await wslProcess.RunPowerShellInstallCommand(netSDKInstallCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!netSDKInstallSuccess)
        {
            CheckPrerequisitesWSL();
            await Task.Delay(1000);
            if (WSL1Installed && hasNETSDK)
            {
                SetStatus(".NET SDK 8.0 installed successfully.", Color.green);
            } else {
                SetStatus("Failed to install .NET SDK 8.0. Installation aborted.", Color.red);
                return;
            }
        }

        // Install WASI experimental workload
        SetStatus("Installing .NET SDK 8.0 - Step 7: Installing WASI experimental workload", Color.yellow);
        string wasiWorkloadCommand = "wsl -d Debian -- sudo dotnet workload install wasi-experimental";
        bool wasiWorkloadSuccess = await wslProcess.RunPowerShellInstallCommand(wasiWorkloadCommand, LogMessage, visibleInstallProcesses, keepWindowOpenForDebug);
        if (!wasiWorkloadSuccess)
        {
            if (WSL1Installed)
            {
                SetStatus(".NET SDK 8.0 with WASI workload installed successfully.", Color.green);
            } else {
                SetStatus("Warning: Failed to install WASI experimental workload. SpacetimeDB C# modules may not compile correctly.", Color.yellow);
            }
        }

        // Check installation status
        CheckPrerequisitesWSL();
        await Task.Delay(1000);
        
        if (hasNETSDK)
        {
            SetStatus(".NET SDK 8.0 with WASI experimental workload installed successfully.", Color.green);
            UpdateInstallerItemsStatus();
        }
        else
        {
            SetStatus(".NET SDK 8.0 installation failed. Please install manually.", Color.red);
        }

        Repaint();
    }

    public async void InstallBinaryen()
    {
        CheckPrerequisitesWSL();
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
            // Install Binaryen with the specific version directly in the URL
            string installCommand = $"wsl -d Debian -u {userName} bash -c \"" +
                "curl -L \\\"https://github.com/WebAssembly/binaryen/releases/download/version_123/binaryen-version_123-x86_64-linux.tar.gz\\\" | " +
                "sudo tar -xz --strip-components=2 -C /usr/local/bin binaryen-version_123/bin\"";
            
            bool installSuccess = await wslProcess.RunPowerShellInstallCommand(
                installCommand, 
                LogMessage, 
                visibleInstallProcesses, 
                keepWindowOpenForDebug
            );
            
            if (!installSuccess)
            {
                CheckPrerequisitesWSL();
                await Task.Delay(2000);
                if (WSL1Installed && hasBinaryen)
                {
                    SetStatus("Binaryen installed successfully.", Color.green);
                }
                else
                {
                    SetStatus("Failed to install Binaryen. Installation aborted.", Color.red);
                }
                return;
            }
            
            // Check installation status
            CheckPrerequisitesWSL();
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
    
    public async void InstallGit()
    {
        CheckPrerequisitesWSL();
        await Task.Delay(1000);
        
        if (hasGit && !installIfAlreadyInstalled)
        {
            SetStatus("Git is already installed.", Color.green);
            return;
        }
        
        SetStatus("Installing Git...", Color.green);
        
        string updateCommand = $"wsl -d Debian -u root bash -c \"echo 'Updating package lists...' && sudo apt update\"";
        SetStatus("Running: apt update", Color.yellow);
        bool updateSuccess = await wslProcess.RunPowerShellInstallCommand(
            updateCommand, 
            LogMessage, 
            visibleInstallProcesses, 
            keepWindowOpenForDebug
        );
        if (!updateSuccess)
        {
            SetStatus("Failed to update package list. Git installation aborted.", Color.red);
            return;
        }
        
        // Delay between commands to ensure UI updates
        await Task.Delay(1000);
        
        // Now install git
        string installCommand = $"wsl -d Debian -u root bash -c \"echo 'Installing git...' && sudo apt install -y git\"";
        SetStatus("Running: apt install -y git", Color.yellow);
        bool installSuccess = await wslProcess.RunPowerShellInstallCommand(
            installCommand, 
            LogMessage, 
            visibleInstallProcesses, 
            keepWindowOpenForDebug
        );
        if (!installSuccess)
        {
            CheckPrerequisitesWSL();
            await Task.Delay(2000);
            if (WSL1Installed && hasGit)
                SetStatus("Git installed successfully.", Color.green);
            else
                SetStatus("Failed to install Git. Installation aborted.", Color.red);
            
            return;
        }
        
        // Check installation status
        CheckPrerequisitesWSL();
        await Task.Delay(1000);
        
        if (hasGit)
        {
            SetStatus("Git installed successfully.", Color.green);
        }
        else
        {
            SetStatus("Git installation failed. Please install manually.", Color.red);
        }
    }
    
    public async void InstallSpacetimeDBUnitySDK()
    {
        // Check if hasSpacetimeDBUnitySDK to see if this is an update or fresh install
        if (CCCPSettingsAdapter.GetLocalCLIProvider() == "Docker")
        {
            CheckPrerequisitesDocker();
        }
        else if (CCCPSettingsAdapter.GetLocalCLIProvider() == "WSL")
        {
            CheckPrerequisitesWSL();
        }
        await Task.Delay(1000);
        
        // Check if this is an update or fresh install
        bool isUpdate = hasSpacetimeDBUnitySDK && spacetimeSDKUpdateAvailable && !string.IsNullOrEmpty(spacetimeSDKLatestVersion);
        
        if (hasSpacetimeDBUnitySDK && !installIfAlreadyInstalled && !isUpdate)
        {
            SetStatus("SpacetimeDB Unity SDK is already installed.", Color.green);
            return;
        }
        
        // Set appropriate status message
        if (isUpdate)
        {
            SetStatus($"Updating SpacetimeDB Unity SDK from v{spacetimeSDKCurrentVersion} to v{spacetimeSDKLatestVersion}...", Color.yellow);
        }
        else
        {
            SetStatus("Installing SpacetimeDB Unity SDK...", Color.yellow);
        }

        // Display appropriate dialog
        string dialogTitle = isUpdate ? "SpacetimeDB SDK Update" : "SpacetimeDB SDK Installation";
        string dialogMessage = isUpdate 
            ? $"Updating the SpacetimeDB SDK from v{spacetimeSDKCurrentVersion} to v{spacetimeSDKLatestVersion} will download the latest package from GitHub and may trigger a script reload.\n\n" +
              "The update process may take up to a minute. Please don't close Unity during this time.\n\n" +
              "If you encounter any errors or warnings you can safely dismiss them after the update is complete."
            : "Installing the SpacetimeDB SDK will add a package from GitHub and may trigger a script reload.\n\n" +
              "The installation process may take up to a minute. Please don't close Unity during this time.\n\n" +
              "If you encounter any errors or warnings you can safely dismiss them after the install is complete.";

        string actionButton = isUpdate ? "Update" : "Install";
        
        if (EditorUtility.DisplayDialog(dialogTitle, dialogMessage, actionButton, "Cancel"))
        {
            // Use the ServerSpacetimeSDKInstaller to install/update the SDK
            // Pass isUpdate as forceUpdate so the package is refreshed when updating
            ServerSpacetimeSDKInstaller.InstallSDK((success, errorMessage) => 
            {
                if (success)
                {
                    if (isUpdate)
                    {
                        SetStatus($"SpacetimeDB Unity SDK updated successfully to v{spacetimeSDKLatestVersion}!", Color.green);
                        
                        // Clear the update available flags after successful update
                        spacetimeSDKUpdateAvailable = false;
                        spacetimeSDKCurrentVersion = spacetimeSDKLatestVersion;
                    }
                    else
                    {
                        SetStatus("SpacetimeDB Unity SDK installed successfully.", Color.green);
                    }
                    
                    hasSpacetimeDBUnitySDK = true;
                    CCCPSettingsAdapter.SetHasSpacetimeDBUnitySDK(true);
                    UpdateInstallerItemsStatus();
                    
                    // After successful installation, ensure the window updates properly
                    if (CCCPSettingsAdapter.GetLocalCLIProvider() == "Docker")
                    {
                        CheckPrerequisitesDocker();
                    }
                    else if (CCCPSettingsAdapter.GetLocalCLIProvider() == "WSL")
                    {
                        CheckPrerequisitesWSL();
                    }
                }
                else
                {
                    string errorMsg = string.IsNullOrEmpty(errorMessage) ? "Unknown error" : errorMessage;
                    string failureMessage = isUpdate 
                        ? $"SpacetimeDB Unity SDK update failed: {errorMsg}"
                        : $"SpacetimeDB Unity SDK installation failed: {errorMsg}";
                    
                    SetStatus(failureMessage, Color.red);
                    
                    // Show a more detailed error dialog
                    EditorUtility.DisplayDialog(
                        isUpdate ? "Update Failed" : "Installation Failed",
                        $"{failureMessage}\n\n" +
                        "You can try again later or install it manually via Package Manager (Window > Package Manager > Add package from git URL).",
                        "OK");
                }
            }, isUpdate);
        }
        else
        {
            string cancelMessage = isUpdate ? "SpacetimeDB Unity SDK update cancelled." : "SpacetimeDB Unity SDK installation cancelled.";
            SetStatus(cancelMessage, Color.yellow);
        }
    }
    #endregion

    #region Custom Installation
    public async void InstallCustomUser()
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

            if (string.IsNullOrEmpty(CCCPSettingsAdapter.GetSSHPrivateKeyPath()))
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
                    CCCPSettingsAdapter.SetSSHUserName(newUserName);
                }
                
                SetStatus($"User '{newUserName}' created successfully on remote server.", Color.green);
                
                // Update UI
                CheckPrerequisitesCustom();

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

    public async void InstallCustomDebianTrixie()
    {
        try
        {
            // Ensure SSH session is active
            if (!customProcess.IsSessionActive())
            {
                SetStatus("SSH connection not active. Please connect first.", Color.red);
                return;
            }

            CheckPrerequisitesCustom();
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
                
                CheckPrerequisitesCustom();
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

    public async void InstallCustomCurl()
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

            CheckPrerequisitesCustom();
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
                
                CheckPrerequisitesCustom();
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

    public async void InstallCustomSpacetimeDBServer()
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

            CheckPrerequisitesCustom();
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
                
                CheckPrerequisitesCustom();

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

    public async void InstallCustomSpacetimeDBPath()
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

            CheckPrerequisitesCustom();
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
                
                CheckPrerequisitesCustom();
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

    public async void InstallCustomSpacetimeDBService()
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

            // Get the expected module name from the installer item or Settings
            string expectedModuleName = "";
            foreach (var item in customInstallerItems)
            {
                if (item.title == "Install SpacetimeDB Service")
                {
                    expectedModuleName = item.expectedModuleName;
                    break;
                }
            }

            // If expectedModuleName is empty, try to get it from Settings
            if (string.IsNullOrEmpty(expectedModuleName))
            {
                expectedModuleName = CCCPSettingsAdapter.GetModuleName();
            }
            
            if (string.IsNullOrEmpty(expectedModuleName))
            {
                EditorUtility.DisplayDialog("Module Name Required",
                "You haven't added any module in Pre-Requisites. It is required to install the SpacetimeDB Service.",
                "OK");
                SetStatus("Module name is not set. Please add one in Pre-Requisites.", Color.yellow);
                return;
            }

            CheckPrerequisitesCustom();
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
                
                "# Create the database logs wrapper script for dynamic module switching\n" +
                "echo \"Creating SpacetimeDB database logs wrapper script...\"\n" +
                $"sudo tee /home/{sshUserName}/.local/bin/spacetime-database-logs-switching.sh << 'EOF'\n" +
                "#!/bin/bash\n" +
                "# SpacetimeDB Database Logs Service Wrapper\n" +
                "# Dynamically reads current module from config file\n" +
                "\n" +
                "# Remove strict error handling to prevent unnecessary exits\n" +
                "set -u  # Only exit on undefined variables, not on all errors\n" +
                "\n" +
                $"CURRENT_MODULE_FILE=\"/home/{sshUserName}/.local/current_spacetime_module\"\n" +
                $"SPACETIME_PATH=\"/home/{sshUserName}/.local/bin/spacetime\"\n" +
                "LOG_FILE=\"/tmp/spacetime-logs-wrapper.log\"\n" +
                "\n" +
                "# Function to log with timestamp (only to file, not stdout to avoid spam)\n" +
                "log_debug() {\n" +
                "    echo \"$(date '+%Y-%m-%d %H:%M:%S') - $1\" >> \"$LOG_FILE\"\n" +
                "}\n" +
                "\n" +
                "# Function to handle script termination gracefully\n" +
                "cleanup() {\n" +
                "    log_debug \"Wrapper script received termination signal\"\n" +
                "    exit 0\n" +
                "}\n" +
                "\n" +
                "# Set up signal handlers\n" +
                "trap cleanup SIGTERM SIGINT\n" +
                "\n" +
                "# Validate config file exists and is readable\n" +
                "if [ ! -f \"$CURRENT_MODULE_FILE\" ]; then\n" +
                "    log_debug \"ERROR: Module config file does not exist: $CURRENT_MODULE_FILE\"\n" +
                "    sleep 30  # Wait before exiting to prevent rapid restarts\n" +
                "    exit 1\n" +
                "fi\n" +
                "\n" +
                "if [ ! -r \"$CURRENT_MODULE_FILE\" ]; then\n" +
                "    log_debug \"ERROR: Cannot read module config file: $CURRENT_MODULE_FILE\"\n" +
                "    sleep 30\n" +
                "    exit 1\n" +
                "fi\n" +
                "\n" +
                "# Read and validate module name\n" +
                "MODULE=$(cat \"$CURRENT_MODULE_FILE\" | tr -d '\\n\\r' | xargs)\n" +
                "\n" +
                "if [ -z \"$MODULE\" ]; then\n" +
                "    log_debug \"ERROR: Module config file is empty: $CURRENT_MODULE_FILE\"\n" +
                "    sleep 30\n" +
                "    exit 1\n" +
                "fi\n" +
                "\n" +
                "# Validate spacetime binary exists\n" +
                "if [ ! -x \"$SPACETIME_PATH\" ]; then\n" +
                "    log_debug \"ERROR: SpacetimeDB binary not found or not executable: $SPACETIME_PATH\"\n" +
                "    sleep 30\n" +
                "    exit 1\n" +
                "fi\n" +
                "\n" +
                "# Log start (only once to debug file, not to systemd logs)\n" +
                "log_debug \"Starting database logs for module: $MODULE\"\n" +
                "\n" +
                "# Start logging - use exec to replace shell process\n" +
                "exec \"$SPACETIME_PATH\" logs \"$MODULE\" -f\n" +
                "EOF\n\n" +
                
                "# Make the wrapper script executable\n" +
                $"sudo chmod +x /home/{sshUserName}/.local/bin/spacetime-database-logs-switching.sh\n\n" +
                
                "# Create initial module config file with expected module\n" +
                $"mkdir -p /home/{sshUserName}/.local\n" +
                $"echo '{expectedModuleName}' > /home/{sshUserName}/.local/current_spacetime_module\n" +
                $"chmod 644 /home/{sshUserName}/.local/current_spacetime_module\n" +
                $"chown {sshUserName}:{sshUserName} /home/{sshUserName}/.local/current_spacetime_module\n\n" +
                
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
                "Restart=on-failure\n" +
                "RestartSec=30\n" +
                "StartLimitIntervalSec=300\n" +
                "StartLimitBurst=3\n" +
                $"ExecStart=/home/{sshUserName}/.local/bin/spacetime-database-logs-switching.sh\n" +
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
                
                CheckPrerequisitesCustom();
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
    #endregion
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games