using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace NorthernRogue.CCCP.Editor.Settings
{
    /// <summary>
    /// Legacy compatibility adapter for EditorPrefs-style access to settings
    /// Provides backwards compatibility while transitioning to Settings Provider
    /// </summary>
    public static class CCCPSettingsAdapter
    {
        private const string PrefsKeyPrefix = "CCCP_";
        
        // Cached settings instance
        private static CCCPSettings _cachedSettings;
        private static CCCPSettings Settings
        {
            get
            {
                if (_cachedSettings == null)
                {
                    _cachedSettings = CCCPSettings.Instance;
                }
                return _cachedSettings;
            }
        }
        
        /// <summary>
        /// Save settings to disk immediately
        /// </summary>
        public static void SaveSettings()
        {
            if (_cachedSettings != null)
            {
                EditorUtility.SetDirty(_cachedSettings);
                AssetDatabase.SaveAssets();
            }
        }
        
        #region String Properties
        
        public static string GetUserName() => Settings.userName;
        public static void SetUserName(string value) { Settings.userName = value; SaveSettings(); }
        
        public static string GetServerUrl() => Settings.serverUrl;
        public static void SetServerUrl(string value) { Settings.serverUrl = value; SaveSettings(); }
        
        public static string GetAuthToken() => Settings.authToken;
        public static void SetAuthToken(string value) { Settings.authToken = value; SaveSettings(); }
        
        public static string GetBackupDirectory() => Settings.backupDirectory;
        public static void SetBackupDirectory(string value) { Settings.backupDirectory = value; SaveSettings(); }
        
        public static string GetServerDirectory() => Settings.serverDirectory;
        public static void SetServerDirectory(string value) { Settings.serverDirectory = value; SaveSettings(); }
        
        public static string GetClientDirectory() => Settings.clientDirectory;
        public static void SetClientDirectory(string value) { Settings.clientDirectory = value; SaveSettings(); }
        
        public static string GetServerLang() => Settings.serverLang;
        public static void SetServerLang(string value) { Settings.serverLang = value; SaveSettings(); }
        
        public static string GetUnityLang() => Settings.unityLang;
        public static void SetUnityLang(string value) { Settings.unityLang = value; SaveSettings(); }
        
        public static string GetModuleName() => Settings.moduleName;
        public static void SetModuleName(string value) { Settings.moduleName = value; SaveSettings(); }
        
        public static string GetMaincloudUrl() => Settings.maincloudUrl;
        public static void SetMaincloudUrl(string value) { Settings.maincloudUrl = value; SaveSettings(); }
        
        public static string GetMaincloudAuthToken() => Settings.maincloudAuthToken;
        public static void SetMaincloudAuthToken(string value) { Settings.maincloudAuthToken = value; SaveSettings(); }
        
        public static string GetSSHUserName() => Settings.sshUserName;
        public static void SetSSHUserName(string value) { Settings.sshUserName = value; SaveSettings(); }
        
        public static string GetSSHPrivateKeyPath() => Settings.sshPrivateKeyPath;
        public static void SetSSHPrivateKeyPath(string value) { Settings.sshPrivateKeyPath = value; SaveSettings(); }
        
        public static string GetCustomServerUrl() => Settings.customServerUrl;
        public static void SetCustomServerUrl(string value) { Settings.customServerUrl = value; SaveSettings(); }
        
        public static string GetCustomServerAuthToken() => Settings.customServerAuthToken;
        public static void SetCustomServerAuthToken(string value) { Settings.customServerAuthToken = value; SaveSettings(); }
        
        public static string GetSpacetimeDBCurrentVersion() => Settings.spacetimeDBCurrentVersion;
        public static void SetSpacetimeDBCurrentVersion(string value) { Settings.spacetimeDBCurrentVersion = value; SaveSettings(); }
        
        public static string GetSpacetimeDBCurrentVersionCustom() => Settings.spacetimeDBCurrentVersionCustom;
        public static void SetSpacetimeDBCurrentVersionCustom(string value) { Settings.spacetimeDBCurrentVersionCustom = value; SaveSettings(); }
        
        public static string GetSpacetimeDBCurrentVersionTool() => Settings.spacetimeDBCurrentVersionTool;
        public static void SetSpacetimeDBCurrentVersionTool(string value) { Settings.spacetimeDBCurrentVersionTool = value; SaveSettings(); }
        
        public static string GetSpacetimeDBLatestVersion() => Settings.spacetimeDBLatestVersion;
        public static void SetSpacetimeDBLatestVersion(string value) { Settings.spacetimeDBLatestVersion = value; SaveSettings(); }
        
        public static string GetRustCurrentVersion() => Settings.rustCurrentVersion;
        public static void SetRustCurrentVersion(string value) { Settings.rustCurrentVersion = value; SaveSettings(); }
        
        public static string GetRustLatestVersion() => Settings.rustLatestVersion;
        public static void SetRustLatestVersion(string value) { Settings.rustLatestVersion = value; SaveSettings(); }
        
        public static string GetRustupVersion() => Settings.rustupVersion;
        public static void SetRustupVersion(string value) { Settings.rustupVersion = value; SaveSettings(); }
        
        public static string GetOriginalFileInfo() => Settings.originalFileInfo;
        public static void SetOriginalFileInfo(string value) { Settings.originalFileInfo = value; SaveSettings(); }
        
        public static string GetCurrentFileInfo() => Settings.currentFileInfo;
        public static void SetCurrentFileInfo(string value) { Settings.currentFileInfo = value; SaveSettings(); }
        
        public static string GetColumnWidths() => Settings.columnWidths;
        public static void SetColumnWidths(string value) { Settings.columnWidths = value; SaveSettings(); }
        
        public static string GetLastSelectedTable() => Settings.lastSelectedTable;
        public static void SetLastSelectedTable(string value) { Settings.lastSelectedTable = value; SaveSettings(); }
        
        #endregion
        
        #region Integer Properties
        
        public static int GetServerPort() => Settings.serverPort;
        public static void SetServerPort(int value) { Settings.serverPort = value; SaveSettings(); }
        
        public static int GetSelectedModuleIndex() => Settings.selectedModuleIndex;
        public static void SetSelectedModuleIndex(int value) { Settings.selectedModuleIndex = value; SaveSettings(); }
        
        public static int GetCustomServerPort() => Settings.customServerPort;
        public static void SetCustomServerPort(int value) { Settings.customServerPort = value; SaveSettings(); }
        
        #endregion
        
        #region Boolean Properties
        
        public static bool GetHasWSL() => Settings.hasWSL;
        public static void SetHasWSL(bool value) { Settings.hasWSL = value; SaveSettings(); }
        
        public static bool GetHasDebian() => Settings.hasDebian;
        public static void SetHasDebian(bool value) { Settings.hasDebian = value; SaveSettings(); }
        
        public static bool GetHasDebianTrixie() => Settings.hasDebianTrixie;
        public static void SetHasDebianTrixie(bool value) { Settings.hasDebianTrixie = value; SaveSettings(); }
        
        public static bool GetHasCurl() => Settings.hasCurl;
        public static void SetHasCurl(bool value) { Settings.hasCurl = value; SaveSettings(); }
        
        public static bool GetHasSpacetimeDBServer() => Settings.hasSpacetimeDBServer;
        public static void SetHasSpacetimeDBServer(bool value) { Settings.hasSpacetimeDBServer = value; SaveSettings(); }
        
        public static bool GetHasSpacetimeDBPath() => Settings.hasSpacetimeDBPath;
        public static void SetHasSpacetimeDBPath(bool value) { Settings.hasSpacetimeDBPath = value; SaveSettings(); }
        
        public static bool GetHasSpacetimeDBService() => Settings.hasSpacetimeDBService;
        public static void SetHasSpacetimeDBService(bool value) { Settings.hasSpacetimeDBService = value; SaveSettings(); }
        
        public static bool GetHasSpacetimeDBLogsService() => Settings.hasSpacetimeDBLogsService;
        public static void SetHasSpacetimeDBLogsService(bool value) { Settings.hasSpacetimeDBLogsService = value; SaveSettings(); }
        
        public static bool GetHasRust() => Settings.hasRust;
        public static void SetHasRust(bool value) { Settings.hasRust = value; SaveSettings(); }
        
        public static bool GetHasNETSDK() => Settings.hasNETSDK;
        public static void SetHasNETSDK(bool value) { Settings.hasNETSDK = value; SaveSettings(); }
        
        public static bool GetHasBinaryen() => Settings.hasBinaryen;
        public static void SetHasBinaryen(bool value) { Settings.hasBinaryen = value; SaveSettings(); }
        
        public static bool GetHasGit() => Settings.hasGit;
        public static void SetHasGit(bool value) { Settings.hasGit = value; SaveSettings(); }
        
        public static bool GetHasSpacetimeDBUnitySDK() => Settings.hasSpacetimeDBUnitySDK;
        public static void SetHasSpacetimeDBUnitySDK(bool value) { Settings.hasSpacetimeDBUnitySDK = value; SaveSettings(); }
        
        public static bool GetHasCustomDebianUser() => Settings.hasCustomDebianUser;
        public static void SetHasCustomDebianUser(bool value) { Settings.hasCustomDebianUser = value; SaveSettings(); }
        
        public static bool GetHasCustomDebianTrixie() => Settings.hasCustomDebianTrixie;
        public static void SetHasCustomDebianTrixie(bool value) { Settings.hasCustomDebianTrixie = value; SaveSettings(); }
        
        public static bool GetHasCustomCurl() => Settings.hasCustomCurl;
        public static void SetHasCustomCurl(bool value) { Settings.hasCustomCurl = value; SaveSettings(); }
        
        public static bool GetHasCustomSpacetimeDBServer() => Settings.hasCustomSpacetimeDBServer;
        public static void SetHasCustomSpacetimeDBServer(bool value) { Settings.hasCustomSpacetimeDBServer = value; SaveSettings(); }
        
        public static bool GetHasCustomSpacetimeDBPath() => Settings.hasCustomSpacetimeDBPath;
        public static void SetHasCustomSpacetimeDBPath(bool value) { Settings.hasCustomSpacetimeDBPath = value; SaveSettings(); }
        
        public static bool GetHasCustomSpacetimeDBService() => Settings.hasCustomSpacetimeDBService;
        public static void SetHasCustomSpacetimeDBService(bool value) { Settings.hasCustomSpacetimeDBService = value; SaveSettings(); }
        
        public static bool GetHasCustomSpacetimeDBLogsService() => Settings.hasCustomSpacetimeDBLogsService;
        public static void SetHasCustomSpacetimeDBLogsService(bool value) { Settings.hasCustomSpacetimeDBLogsService = value; SaveSettings(); }
        
        public static bool GetWslPrerequisitesChecked() => Settings.wslPrerequisitesChecked;
        public static void SetWslPrerequisitesChecked(bool value) { Settings.wslPrerequisitesChecked = value; SaveSettings(); }
        
        public static bool GetInitializedFirstModule() => Settings.initializedFirstModule;
        public static void SetInitializedFirstModule(bool value) { Settings.initializedFirstModule = value; SaveSettings(); }
        
        public static bool GetPublishFirstModule() => Settings.publishFirstModule;
        public static void SetPublishFirstModule(bool value) { Settings.publishFirstModule = value; SaveSettings(); }
        
        public static bool GetHasAllPrerequisites() => Settings.hasAllPrerequisites;
        public static void SetHasAllPrerequisites(bool value) { Settings.hasAllPrerequisites = value; SaveSettings(); }
        
        public static bool GetHideWarnings() => Settings.hideWarnings;
        public static void SetHideWarnings(bool value) { Settings.hideWarnings = value; SaveSettings(); }
        
        public static bool GetDetectServerChanges() => Settings.detectServerChanges;
        public static void SetDetectServerChanges(bool value) { Settings.detectServerChanges = value; SaveSettings(); }
        
        public static bool GetAutoPublishMode() => Settings.autoPublishMode;
        public static void SetAutoPublishMode(bool value) { Settings.autoPublishMode = value; SaveSettings(); }
        
        public static bool GetPublishAndGenerateMode() => Settings.publishAndGenerateMode;
        public static void SetPublishAndGenerateMode(bool value) { Settings.publishAndGenerateMode = value; SaveSettings(); }
        
        public static bool GetSilentMode() => Settings.silentMode;
        public static void SetSilentMode(bool value) { Settings.silentMode = value; SaveSettings(); }
        
        public static bool GetDebugMode() => Settings.debugMode;
        public static void SetDebugMode(bool value) { Settings.debugMode = value; SaveSettings(); }
        
        public static bool GetClearModuleLogAtStart() => Settings.clearModuleLogAtStart;
        public static void SetClearModuleLogAtStart(bool value) { Settings.clearModuleLogAtStart = value; SaveSettings(); }
        
        public static bool GetClearDatabaseLogAtStart() => Settings.clearDatabaseLogAtStart;
        public static void SetClearDatabaseLogAtStart(bool value) { Settings.clearDatabaseLogAtStart = value; SaveSettings(); }
        
        public static bool GetAutoCloseWsl() => Settings.autoCloseWsl;
        public static void SetAutoCloseWsl(bool value) { Settings.autoCloseWsl = value; SaveSettings(); }
        
        public static bool GetEchoToConsole() => Settings.echoToConsole;
        public static void SetEchoToConsole(bool value) { Settings.echoToConsole = value; SaveSettings(); }
        
        public static bool GetRustUpdateAvailable() => Settings.rustUpdateAvailable;
        public static void SetRustUpdateAvailable(bool value) { Settings.rustUpdateAvailable = value; SaveSettings(); }
        
        public static bool GetSpacetimeSDKUpdateAvailable() => Settings.spacetimeSDKUpdateAvailable;
        public static void SetSpacetimeSDKUpdateAvailable(bool value) { Settings.spacetimeSDKUpdateAvailable = value; SaveSettings(); }
        
        public static bool GetWSL1Installed() => Settings.WSL1Installed;
        public static void SetWSL1Installed(bool value) { Settings.WSL1Installed = value; SaveSettings(); }
        
        public static bool GetVisibleInstallProcesses() => Settings.visibleInstallProcesses;
        public static void SetVisibleInstallProcesses(bool value) { Settings.visibleInstallProcesses = value; SaveSettings(); }
        
        public static bool GetKeepWindowOpenForDebug() => Settings.keepWindowOpenForDebug;
        public static void SetKeepWindowOpenForDebug(bool value) { Settings.keepWindowOpenForDebug = value; SaveSettings(); }
        
        public static bool GetUpdateCargoToml() => Settings.updateCargoToml;
        public static void SetUpdateCargoToml(bool value) { Settings.updateCargoToml = value; SaveSettings(); }
        
        public static bool GetServiceMode() => Settings.serviceMode;
        public static void SetServiceMode(bool value) { Settings.serviceMode = value; SaveSettings(); }
        
        public static bool GetAutoscroll() => Settings.autoscroll;
        public static void SetAutoscroll(bool value) { Settings.autoscroll = value; SaveSettings(); }
        
        public static bool GetColorLogo() => Settings.colorLogo;
        public static void SetColorLogo(bool value) { Settings.colorLogo = value; SaveSettings(); }
        
        public static bool GetShowPrerequisites() => Settings.showPrerequisites;
        public static void SetShowPrerequisites(bool value) { Settings.showPrerequisites = value; SaveSettings(); }
        
        public static bool GetShowSettingsWindow() => Settings.showSettingsWindow;
        public static void SetShowSettingsWindow(bool value) { Settings.showSettingsWindow = value; SaveSettings(); }
        
        public static bool GetShowUtilityCommands() => Settings.showUtilityCommands;
        public static void SetShowUtilityCommands(bool value) { Settings.showUtilityCommands = value; SaveSettings(); }
        
        public static bool GetServerChangesDetected() => Settings.serverChangesDetected;
        public static void SetServerChangesDetected(bool value) { Settings.serverChangesDetected = value; SaveSettings(); }
        
        #endregion
        
        #region Float Properties
        
        public static float GetLogUpdateFrequency() => Settings.logUpdateFrequency;
        public static void SetLogUpdateFrequency(float value) { Settings.logUpdateFrequency = value; SaveSettings(); }
        
        #endregion
        
        #region Enum Properties
        
        public static NorthernRogue.CCCP.Editor.ServerManager.ServerMode GetServerMode() => Settings.serverMode;
        public static void SetServerMode(NorthernRogue.CCCP.Editor.ServerManager.ServerMode value) { Settings.serverMode = value; SaveSettings(); }
        
        #endregion
        
        #region Module Management
        
        public static System.Collections.Generic.List<ModuleInfo> GetSavedModules() => Settings.savedModules;
        public static void SetSavedModules(System.Collections.Generic.List<ModuleInfo> modules) 
        { 
            Settings.savedModules = modules; 
            SaveSettings(); 
        }
        
        public static string GetSavedModulesJson()
        {
            if (Settings.savedModules == null || Settings.savedModules.Count == 0)
                return "";
                
            var wrapper = new ModuleListWrapper { modules = Settings.savedModules.ToArray() };
            return JsonUtility.ToJson(wrapper);
        }
        
        public static void SetSavedModulesFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Settings.savedModules.Clear();
                SaveSettings();
                return;
            }
            
            try
            {
                var wrapper = JsonUtility.FromJson<ModuleListWrapper>(json);
                if (wrapper != null && wrapper.modules != null)
                {
                    Settings.savedModules = wrapper.modules.ToList();
                    SaveSettings();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to parse modules JSON: {e.Message}");
            }
        }
        
        [System.Serializable]
        private class ModuleListWrapper
        {
            public ModuleInfo[] modules;
        }
        
        #endregion
        
        #region Legacy Compatibility Methods
        
        /// <summary>
        /// Legacy compatibility method for EditorPrefs-style string access
        /// </summary>
        public static string GetString(string key, string defaultValue = "")
        {
            // Migration support for users upgrading from GitHub version
            // Safe to leave in - only runs if old EditorPrefs keys exist
            
            switch (key.Replace(PrefsKeyPrefix, ""))
            {
                case "UserName": return GetUserName();
                case "ServerURL": return GetServerUrl();
                case "AuthToken": return GetAuthToken();
                case "BackupDirectory": return GetBackupDirectory();
                case "ServerDirectory": return GetServerDirectory();
                case "ClientDirectory": return GetClientDirectory();
                case "ServerLang": return GetServerLang();
                case "UnityLang": return GetUnityLang();
                case "ModuleName": return GetModuleName();
                case "MaincloudURL": return GetMaincloudUrl();
                case "MaincloudAuthToken": return GetMaincloudAuthToken();
                case "SSHUserName": return GetSSHUserName();
                case "SSHPrivateKeyPath": return GetSSHPrivateKeyPath();
                case "CustomServerURL": return GetCustomServerUrl();
                case "CustomServerAuthToken": return GetCustomServerAuthToken();
                case "SpacetimeDBVersion": return GetSpacetimeDBCurrentVersion();
                case "SpacetimeDBVersionCustom": return GetSpacetimeDBCurrentVersionCustom();
                case "SpacetimeDBVersionTool": return GetSpacetimeDBCurrentVersionTool();
                case "SpacetimeDBLatestVersion": return GetSpacetimeDBLatestVersion();
                case "RustVersion": return GetRustCurrentVersion();
                case "RustLatestVersion": return GetRustLatestVersion();
                case "RustupVersion": return GetRustupVersion();
                case "OriginalFileInfo": return GetOriginalFileInfo();
                case "CurrentFileInfo": return GetCurrentFileInfo();
                case "SavedModules": return GetSavedModulesJson();
                case "ServerMode": return GetServerMode().ToString();
                default:
                    Debug.LogWarning($"CCCP: Unknown string key '{key}' in settings adapter");
                    return defaultValue;
            }
        }
        
        /// <summary>
        /// Legacy compatibility method for EditorPrefs-style string setting
        /// </summary>
        public static void SetString(string key, string value)
        {
            // Migration support for users upgrading from GitHub version
            // Safe to leave in - only runs if old EditorPrefs keys exist
            
            switch (key.Replace(PrefsKeyPrefix, ""))
            {
                case "UserName": SetUserName(value); break;
                case "ServerURL": SetServerUrl(value); break;
                case "AuthToken": SetAuthToken(value); break;
                case "BackupDirectory": SetBackupDirectory(value); break;
                case "ServerDirectory": SetServerDirectory(value); break;
                case "ClientDirectory": SetClientDirectory(value); break;
                case "ServerLang": SetServerLang(value); break;
                case "UnityLang": SetUnityLang(value); break;
                case "ModuleName": SetModuleName(value); break;
                case "MaincloudURL": SetMaincloudUrl(value); break;
                case "MaincloudAuthToken": SetMaincloudAuthToken(value); break;
                case "SSHUserName": SetSSHUserName(value); break;
                case "SSHPrivateKeyPath": SetSSHPrivateKeyPath(value); break;
                case "CustomServerURL": SetCustomServerUrl(value); break;
                case "CustomServerAuthToken": SetCustomServerAuthToken(value); break;
                case "SpacetimeDBVersion": SetSpacetimeDBCurrentVersion(value); break;
                case "SpacetimeDBVersionCustom": SetSpacetimeDBCurrentVersionCustom(value); break;
                case "SpacetimeDBVersionTool": SetSpacetimeDBCurrentVersionTool(value); break;
                case "SpacetimeDBLatestVersion": SetSpacetimeDBLatestVersion(value); break;
                case "RustVersion": SetRustCurrentVersion(value); break;
                case "RustLatestVersion": SetRustLatestVersion(value); break;
                case "RustupVersion": SetRustupVersion(value); break;
                case "OriginalFileInfo": SetOriginalFileInfo(value); break;
                case "CurrentFileInfo": SetCurrentFileInfo(value); break;
                case "SavedModules": SetSavedModulesFromJson(value); break;
                case "ServerMode": 
                    if (System.Enum.TryParse<NorthernRogue.CCCP.Editor.ServerManager.ServerMode>(value, out var mode))
                        SetServerMode(mode);
                    break;
                default:
                    Debug.LogWarning($"CCCP: Unknown string key '{key}' in settings adapter");
                    break;
            }
        }
        
        /// <summary>
        /// Legacy compatibility method for EditorPrefs-style boolean access
        /// </summary>
        public static bool GetBool(string key, bool defaultValue = false)
        {
            // Migration support for users upgrading from GitHub version
            // Safe to leave in - only runs if old EditorPrefs keys exist
            
            switch (key.Replace(PrefsKeyPrefix, ""))
            {
                case "HasWSL": return GetHasWSL();
                case "HasDebian": return GetHasDebian();
                case "HasDebianTrixie": return GetHasDebianTrixie();
                case "HasCurl": return GetHasCurl();
                case "HasSpacetimeDBServer": return GetHasSpacetimeDBServer();
                case "HasSpacetimeDBPath": return GetHasSpacetimeDBPath();
                case "HasSpacetimeDBService": return GetHasSpacetimeDBService();
                case "HasSpacetimeDBLogsService": return GetHasSpacetimeDBLogsService();
                case "HasRust": return GetHasRust();
                case "HasNETSDK": return GetHasNETSDK();
                case "HasBinaryen": return GetHasBinaryen();
                case "HasGit": return GetHasGit();
                case "HasSpacetimeDBUnitySDK": return GetHasSpacetimeDBUnitySDK();
                case "HasCustomDebianUser": return GetHasCustomDebianUser();
                case "HasCustomDebianTrixie": return GetHasCustomDebianTrixie();
                case "HasCustomCurl": return GetHasCustomCurl();
                case "HasCustomSpacetimeDBServer": return GetHasCustomSpacetimeDBServer();
                case "HasCustomSpacetimeDBPath": return GetHasCustomSpacetimeDBPath();
                case "HasCustomSpacetimeDBService": return GetHasCustomSpacetimeDBService();
                case "HasCustomSpacetimeDBLogsService": return GetHasCustomSpacetimeDBLogsService();
                case "wslPrerequisitesChecked": return GetWslPrerequisitesChecked();
                case "InitializedFirstModule": return GetInitializedFirstModule();
                case "PublishFirstModule": return GetPublishFirstModule();
                case "HasAllPrerequisites": return GetHasAllPrerequisites();
                case "HideWarnings": return GetHideWarnings();
                case "DetectServerChanges": return GetDetectServerChanges();
                case "AutoPublishMode": return GetAutoPublishMode();
                case "PublishAndGenerateMode": return GetPublishAndGenerateMode();
                case "SilentMode": return GetSilentMode();
                case "DebugMode": return GetDebugMode();
                case "ClearModuleLogAtStart": return GetClearModuleLogAtStart();
                case "ClearDatabaseLogAtStart": return GetClearDatabaseLogAtStart();
                case "AutoCloseWsl": return GetAutoCloseWsl();
                case "EchoToConsole": return GetEchoToConsole();
                case "RustUpdateAvailable": return GetRustUpdateAvailable();
                case "WSL1Installed": return GetWSL1Installed();
                case "VisibleInstallProcesses": return GetVisibleInstallProcesses();
                case "KeepWindowOpenForDebug": return GetKeepWindowOpenForDebug();
                case "UpdateCargoToml": return GetUpdateCargoToml();
                case "ServiceMode": return GetServiceMode();
                case "Autoscroll": return GetAutoscroll();
                case "ColorLogo": return GetColorLogo();
                case "ShowPrerequisites": return GetShowPrerequisites();
                case "ShowSettingsWindow": return GetShowSettingsWindow();
                case "ShowUtilityCommands": return GetShowUtilityCommands();
                case "ServerChangesDetected": return GetServerChangesDetected();
                default:
                    Debug.LogWarning($"CCCP: Unknown boolean key '{key}' in settings adapter");
                    return defaultValue;
            }
        }
        
        /// <summary>
        /// Legacy compatibility method for EditorPrefs-style boolean setting
        /// </summary>
        public static void SetBool(string key, bool value)
        {
            // Migration support for users upgrading from GitHub version
            // Safe to leave in - only runs if old EditorPrefs keys exist
            
            switch (key.Replace(PrefsKeyPrefix, ""))
            {
                case "HasWSL": SetHasWSL(value); break;
                case "HasDebian": SetHasDebian(value); break;
                case "HasDebianTrixie": SetHasDebianTrixie(value); break;
                case "HasCurl": SetHasCurl(value); break;
                case "HasSpacetimeDBServer": SetHasSpacetimeDBServer(value); break;
                case "HasSpacetimeDBPath": SetHasSpacetimeDBPath(value); break;
                case "HasSpacetimeDBService": SetHasSpacetimeDBService(value); break;
                case "HasSpacetimeDBLogsService": SetHasSpacetimeDBLogsService(value); break;
                case "HasRust": SetHasRust(value); break;
                case "HasNETSDK": SetHasNETSDK(value); break;
                case "HasBinaryen": SetHasBinaryen(value); break;
                case "HasGit": SetHasGit(value); break;
                case "HasSpacetimeDBUnitySDK": SetHasSpacetimeDBUnitySDK(value); break;
                case "HasCustomDebianUser": SetHasCustomDebianUser(value); break;
                case "HasCustomDebianTrixie": SetHasCustomDebianTrixie(value); break;
                case "HasCustomCurl": SetHasCustomCurl(value); break;
                case "HasCustomSpacetimeDBServer": SetHasCustomSpacetimeDBServer(value); break;
                case "HasCustomSpacetimeDBPath": SetHasCustomSpacetimeDBPath(value); break;
                case "HasCustomSpacetimeDBService": SetHasCustomSpacetimeDBService(value); break;
                case "HasCustomSpacetimeDBLogsService": SetHasCustomSpacetimeDBLogsService(value); break;
                case "wslPrerequisitesChecked": SetWslPrerequisitesChecked(value); break;
                case "InitializedFirstModule": SetInitializedFirstModule(value); break;
                case "PublishFirstModule": SetPublishFirstModule(value); break;
                case "HasAllPrerequisites": SetHasAllPrerequisites(value); break;
                case "HideWarnings": SetHideWarnings(value); break;
                case "DetectServerChanges": SetDetectServerChanges(value); break;
                case "AutoPublishMode": SetAutoPublishMode(value); break;
                case "PublishAndGenerateMode": SetPublishAndGenerateMode(value); break;
                case "SilentMode": SetSilentMode(value); break;
                case "DebugMode": SetDebugMode(value); break;
                case "ClearModuleLogAtStart": SetClearModuleLogAtStart(value); break;
                case "ClearDatabaseLogAtStart": SetClearDatabaseLogAtStart(value); break;
                case "AutoCloseWsl": SetAutoCloseWsl(value); break;
                case "EchoToConsole": SetEchoToConsole(value); break;
                case "RustUpdateAvailable": SetRustUpdateAvailable(value); break;
                case "WSL1Installed": SetWSL1Installed(value); break;
                case "VisibleInstallProcesses": SetVisibleInstallProcesses(value); break;
                case "KeepWindowOpenForDebug": SetKeepWindowOpenForDebug(value); break;
                case "UpdateCargoToml": SetUpdateCargoToml(value); break;
                case "ServiceMode": SetServiceMode(value); break;
                case "Autoscroll": SetAutoscroll(value); break;
                case "ColorLogo": SetColorLogo(value); break;
                case "ShowPrerequisites": SetShowPrerequisites(value); break;
                case "ShowSettingsWindow": SetShowSettingsWindow(value); break;
                case "ShowUtilityCommands": SetShowUtilityCommands(value); break;
                case "ServerChangesDetected": SetServerChangesDetected(value); break;
                default:
                    Debug.LogWarning($"CCCP: Unknown boolean key '{key}' in settings adapter");
                    break;
            }
        }
        
        /// <summary>
        /// Legacy compatibility method for EditorPrefs-style integer access
        /// </summary>
        public static int GetInt(string key, int defaultValue = 0)
        {
            // Migration support for users upgrading from GitHub version
            // Safe to leave in - only runs if old EditorPrefs keys exist
            
            switch (key.Replace(PrefsKeyPrefix, ""))
            {
                case "ServerPort": return GetServerPort();
                case "SelectedModuleIndex": return GetSelectedModuleIndex();
                case "CustomServerPort": return GetCustomServerPort();
                case "ServerMode": return (int)GetServerMode();
                default:
                    Debug.LogWarning($"CCCP: Unknown integer key '{key}' in settings adapter");
                    return defaultValue;
            }
        }
        
        /// <summary>
        /// Legacy compatibility method for EditorPrefs-style integer setting
        /// </summary>
        public static void SetInt(string key, int value)
        {
            // Migration support for users upgrading from GitHub version
            // Safe to leave in - only runs if old EditorPrefs keys exist
            
            switch (key.Replace(PrefsKeyPrefix, ""))
            {
                case "ServerPort": SetServerPort(value); break;
                case "SelectedModuleIndex": SetSelectedModuleIndex(value); break;
                case "CustomServerPort": SetCustomServerPort(value); break;
                case "ServerMode": SetServerMode((NorthernRogue.CCCP.Editor.ServerManager.ServerMode)value); break;
                default:
                    Debug.LogWarning($"CCCP: Unknown integer key '{key}' in settings adapter");
                    break;
            }
        }
        
        /// <summary>
        /// Legacy compatibility method for EditorPrefs-style float access
        /// </summary>
        public static float GetFloat(string key, float defaultValue = 0f)
        {
            // Migration support for users upgrading from GitHub version
            // Safe to leave in - only runs if old EditorPrefs keys exist
            
            switch (key.Replace(PrefsKeyPrefix, ""))
            {
                case "LogUpdateFrequency": return GetLogUpdateFrequency();
                default:
                    Debug.LogWarning($"CCCP: Unknown float key '{key}' in settings adapter");
                    return defaultValue;
            }
        }
        
        /// <summary>
        /// Legacy compatibility method for EditorPrefs-style float setting
        /// </summary>
        public static void SetFloat(string key, float value)
        {
            // Migration support for users upgrading from GitHub version
            // Safe to leave in - only runs if old EditorPrefs keys exist
            
            switch (key.Replace(PrefsKeyPrefix, ""))
            {
                case "LogUpdateFrequency": SetLogUpdateFrequency(value); break;
                default:
                    Debug.LogWarning($"CCCP: Unknown float key '{key}' in settings adapter");
                    break;
            }
        }
        
        #endregion
    }
}
