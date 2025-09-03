using UnityEngine;
using System.Collections.Generic;

namespace NorthernRogue.CCCP.Editor.Settings
{
    /// <summary>
    /// Main ScriptableObject for CCCP settings storage
    /// </summary>
    [System.Serializable]
    public class CCCPSettings : ScriptableObject
    {
        [Header("Server Configuration")]
        public NorthernRogue.CCCP.Editor.ServerManager.ServerMode serverMode = NorthernRogue.CCCP.Editor.ServerManager.ServerMode.WSLServer;
        public string userName = "";
        public string serverUrl = "http://0.0.0.0:3000/";
        public int serverPort = 3000;
        public string authToken = "";
        
        [Header("Directory Settings")]
        public string backupDirectory = "";
        public string serverDirectory = "";
        public string clientDirectory = "";
        
        [Header("Language Settings")]
        public string serverLang = "rust";
        public string unityLang = "csharp";
        
        [Header("Module Configuration")]
        public string moduleName = "";
        public int selectedModuleIndex = -1;
        public List<ModuleInfo> savedModules = new List<ModuleInfo>();
        
        [Header("Maincloud Configuration")]
        public string maincloudUrl = "https://maincloud.spacetimedb.com/";
        public string maincloudAuthToken = "";
        
        [Header("Custom Server Configuration")]
        public string sshUserName = "";
        public string sshPrivateKeyPath = "";
        public string customServerUrl = "";
        public int customServerPort = 0;
        public string customServerAuthToken = "";
        
        [Header("Prerequisites Status")]
        public bool hasWSL = false;
        public bool hasDebian = false;
        public bool hasDebianTrixie = false;
        public bool hasCurl = false;
        public bool hasSpacetimeDBServer = false;
        public bool hasSpacetimeDBPath = false;
        public bool hasSpacetimeDBService = false;
        public bool hasSpacetimeDBLogsService = false;
        public bool hasRust = false;
        public bool hasNETSDK = false;
        public bool hasBinaryen = false;
        public bool hasGit = false;
        public bool hasSpacetimeDBUnitySDK = false;
        
        [Header("Custom Prerequisites Status")]
        public bool hasCustomDebianUser = false;
        public bool hasCustomDebianTrixie = false;
        public bool hasCustomCurl = false;
        public bool hasCustomSpacetimeDBServer = false;
        public bool hasCustomSpacetimeDBPath = false;
        public bool hasCustomSpacetimeDBService = false;
        public bool hasCustomSpacetimeDBLogsService = false;
        
        [Header("Workflow Settings")]
        public bool wslPrerequisitesChecked = false;
        public bool initializedFirstModule = false;
        public bool publishFirstModule = false;
        public bool hasAllPrerequisites = false;
        
        [Header("Behavior Settings")]
        public bool hideWarnings = true;
        public bool detectServerChanges = true;
        public bool autoPublishMode = false;
        public bool publishAndGenerateMode = true;
        public bool silentMode = true;
        public bool debugMode = false;
        public bool clearModuleLogAtStart = true;
        public bool clearDatabaseLogAtStart = true;
        public bool autoCloseWsl = true;
        public bool echoToConsole = true;
        public bool showLocalTime = true;
        public bool welcomeWindowShown = false;

        [Header("Version Information")]
        public string spacetimeDBCurrentVersion = "";
        public string spacetimeDBCurrentVersionCustom = "";
        public string spacetimeDBCurrentVersionTool = "";
        public string spacetimeDBLatestVersion = "";
        public string spacetimeSDKLatestVersion = "";
        public string rustCurrentVersion = "";
        public string rustLatestVersion = "";
        public string rustupVersion = "";
        public string CCCPAssetStoreLatestVersion = "";
        public bool rustupUpdateAvailable = false;
        public bool rustUpdateAvailable = false;
        public bool spacetimeSDKUpdateAvailable = false;
        public bool SpacetimeDBUpdateAvailable = false;
        public bool CCCPGithubUpdateAvailable = false;
        public bool CCCPAssetStoreUpdateAvailable = false;
        public string distributionType = "";
        public string githubLastCommitSha = "";
        public bool isAssetStoreVersion = false;
        public bool isGitHubVersion = false;

        [Header("Installer Settings")]
        public bool WSL1Installed = false;
        public bool visibleInstallProcesses = true;
        public bool keepWindowOpenForDebug = true;
        public bool updateCargoToml = true;
        public bool serviceMode = true;
        public bool firstTimeOpenInstaller = true;

        [Header("UI Settings")]
        public bool autoscroll = true;
        public bool colorLogo = false;
        public bool showPrerequisites = false;
        public bool showSettingsWindow = false;
        public bool showUtilityCommands = false;
        
        [Header("Detection Settings")]
        public bool serverChangesDetected = false;
        public string originalFileInfo = "";
        public string currentFileInfo = "";
        
        [Header("Data Window Settings")]
        public string columnWidths = "";
        public string lastSelectedTable = "";
        
        [Header("Log Settings")]
        public float logUpdateFrequency = 1.0f;
        
        [Header("Migration")]
        public bool migratedFromEditorPrefs = false;
        public string migrationVersion = "1.0.0";
        
        // Static instance for easy access
        private static CCCPSettings _instance;
        public static CCCPSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CCCPSettingsProvider.GetOrCreateSettings();
                }
                return _instance;
            }
        }
        
        // Reset to defaults
        public void ResetToDefaults()
        {
            serverMode = NorthernRogue.CCCP.Editor.ServerManager.ServerMode.WSLServer;
            userName = "";
            serverUrl = "http://0.0.0.0:3000/";
            serverPort = 3000;
            authToken = "";
            backupDirectory = "";
            serverDirectory = "";
            clientDirectory = "";
            serverLang = "rust";
            unityLang = "csharp";
            moduleName = "";
            selectedModuleIndex = -1;
            savedModules.Clear();
            maincloudUrl = "https://maincloud.spacetimedb.com/";
            maincloudAuthToken = "";
            sshUserName = "";
            sshPrivateKeyPath = "";
            customServerUrl = "";
            customServerPort = 0;
            customServerAuthToken = "";
            
            // Reset all boolean flags to defaults
            hasWSL = false;
            hasDebian = false;
            hasDebianTrixie = false;
            hasCurl = false;
            hasSpacetimeDBServer = false;
            hasSpacetimeDBPath = false;
            hasSpacetimeDBService = false;
            hasSpacetimeDBLogsService = false;
            hasRust = false;
            hasNETSDK = false;
            hasBinaryen = false;
            hasGit = false;
            hasSpacetimeDBUnitySDK = false;
            
            hasCustomDebianUser = false;
            hasCustomDebianTrixie = false;
            hasCustomCurl = false;
            hasCustomSpacetimeDBServer = false;
            hasCustomSpacetimeDBPath = false;
            hasCustomSpacetimeDBService = false;
            hasCustomSpacetimeDBLogsService = false;
            
            wslPrerequisitesChecked = false;
            initializedFirstModule = false;
            publishFirstModule = false;
            hasAllPrerequisites = false;
            
            hideWarnings = true;
            detectServerChanges = true;
            autoPublishMode = false;
            publishAndGenerateMode = true;
            silentMode = true;
            debugMode = false;
            clearModuleLogAtStart = true;
            clearDatabaseLogAtStart = true;
            autoCloseWsl = true;
            echoToConsole = true;
            showLocalTime = true;
            welcomeWindowShown = false;

            spacetimeDBCurrentVersion = "";
            spacetimeDBCurrentVersionCustom = "";
            spacetimeDBCurrentVersionTool = "";
            spacetimeDBLatestVersion = "";
            spacetimeSDKLatestVersion = "";
            rustCurrentVersion = "";
            rustLatestVersion = "";
            rustupVersion = "";
            CCCPAssetStoreLatestVersion = "";
            rustUpdateAvailable = false;
            rustupUpdateAvailable = false;
            spacetimeSDKUpdateAvailable = false;
            SpacetimeDBUpdateAvailable = false;
            CCCPGithubUpdateAvailable = false;
            CCCPAssetStoreUpdateAvailable = false;
            distributionType = "";
            githubLastCommitSha = "";
            isAssetStoreVersion = false;
            isGitHubVersion = false;

            WSL1Installed = false;
            visibleInstallProcesses = true;
            keepWindowOpenForDebug = true;
            updateCargoToml = true;
            serviceMode = true;
            firstTimeOpenInstaller = true;

            autoscroll = true;
            colorLogo = false;
            showPrerequisites = false;
            showSettingsWindow = false;
            showUtilityCommands = false;
            
            serverChangesDetected = false;
            originalFileInfo = "";
            currentFileInfo = "";
            
            columnWidths = "";
            lastSelectedTable = "";
            
            logUpdateFrequency = 1.0f;
        }
    }
    
    [System.Serializable]
    public struct ModuleInfo
    {
        public string name;
        public string path;
    }
}
