using UnityEngine;
using UnityEditor;
using System.Linq;

namespace NorthernRogue.CCCP.Editor.Settings {
/// <summary>
/// Legacy compatibility adapter for EditorPrefs-style access to settings
/// Provides backwards compatibility while transitioning to Settings Provider
/// 
/// Performance Optimizations:
/// - Value comparison checks prevent unnecessary saves
/// - MarkDirty() for frequently updated settings uses deferred saving
/// - SaveSettings() for important settings that need immediate persistence
/// - MarkUISettingsDirty() for UI toggles - only saved on window disable
/// - Cached settings instance reduces CCCPSettings.Instance overhead
/// </summary>
public static class CCCPSettingsAdapter
{
    public static bool debugMode = false;

    private const string PrefsKeyPrefix = "CCCP_";
    
    // Cached settings instance
    private static CCCPSettings _cachedSettings;
    
    // Domain reload handler to refresh cached settings
    static CCCPSettingsAdapter()
    {
        AssemblyReloadEvents.beforeAssemblyReload += () => {
            // Clear the cached instance so it gets reloaded properly after domain reload
            _cachedSettings = null;
        };
    }
    
    private static CCCPSettings Settings
    {
        get
        {
            // Only create instance once and cache it
            if (_cachedSettings == null)
            {
                _cachedSettings = CCCPSettings.Instance;
            }
            return _cachedSettings;
        }
    }
    
    /// <summary>
    /// Force refresh the cached settings instance (use after domain reload)
    /// </summary>
    public static void RefreshSettingsCache()
    {
        _cachedSettings = null;
    }
    
    private static bool _pendingSave = false;
    private static bool _pendingUISave = false; // For UI-only settings that can be batched
    
    /// <summary>
    /// Save settings to disk immediately
    /// </summary>
    public static void SaveSettings()
    {
        if (_cachedSettings != null)
        {
            try
            {
                EditorUtility.SetDirty(_cachedSettings);
                AssetDatabase.SaveAssets();
                _pendingSave = false;
                _pendingUISave = false;
            }
            catch (System.Exception ex)
            {
                if (debugMode) Debug.LogWarning($"CCCP Settings: Failed to save settings: {ex.Message}");
                _pendingSave = false;
                _pendingUISave = false;
            }
        }
    }
    
    /// <summary>
    /// Mark settings as dirty without immediately saving to disk
    /// Use this for frequent updates to avoid performance issues
    /// </summary>
    private static void MarkDirty()
    {
        if (_cachedSettings != null)
        {
            EditorUtility.SetDirty(_cachedSettings);
            
            if (!_pendingSave)
            {
                _pendingSave = true;
                // Use a safer approach that checks if we're in a valid state to save
                EditorApplication.delayCall += SafeDeferredSave;
            }
        }
    }
    
    /// <summary>
    /// Safely save assets with proper context checking
    /// </summary>
    private static void SafeDeferredSave()
    {
        try
        {
            if (!_pendingSave)
                return;
                
            _pendingSave = false;
            
            // Check if we're in a safe context to save assets
            // Avoid saving during asset post-processing or when UI is not ready
            if (EditorApplication.isCompiling || 
                EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                (!EditorApplication.isPlaying && EditorApplication.isPaused) ||
                AssetDatabase.IsAssetImportWorkerProcess())
            {
                // Retry later if we're not in a safe state
                _pendingSave = true;
                EditorApplication.delayCall += SafeDeferredSave;
                return;
            }
            
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception ex)
        {
            // Log the error but don't let it break the application
            if (debugMode) Debug.LogWarning($"CCCP Settings: Failed to save assets in deferred call: {ex.Message}");
            _pendingSave = false; // Reset the flag even if save failed
        }
    }
    
    /// <summary>
    /// Mark UI settings as dirty without any immediate saving
    /// These will only be saved when ForceUISettingsSave() is called
    /// </summary>
    private static void MarkUISettingsDirty()
    {
        if (_cachedSettings != null)
        {
            EditorUtility.SetDirty(_cachedSettings);
            _pendingUISave = true;
        }
    }
    
    /// <summary>
    /// Force save UI settings (call this from OnDisable or similar)
    /// </summary>
    public static void ForceUISettingsSave()
    {
        if (_pendingUISave && _cachedSettings != null)
        {
            // Only save if we're not in the middle of asset processing or compilation
            if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && 
                !AssetDatabase.IsAssetImportWorkerProcess())
            {
                try
                {
                    AssetDatabase.SaveAssets();
                }
                catch (System.Exception e)
                {
                    // Log but don't throw - UI settings save failures shouldn't break the workflow
                    if (debugMode) Debug.LogWarning($"Failed to save UI settings: {e.Message}");
                }
            }
            _pendingUISave = false;
        }
    }
    
    #region String Properties
    
    public static string GetUserName() => Settings.userName;
    public static void SetUserName(string value) 
    { 
        if (Settings.userName != value)
        {
            Settings.userName = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetServerUrl() => Settings.serverUrl;
    public static void SetServerUrl(string value) 
    { 
        if (Settings.serverUrl != value)
        {
            Settings.serverUrl = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetAuthToken() => Settings.authToken;
    public static void SetAuthToken(string value) 
    { 
        if (Settings.authToken != value)
        {
            Settings.authToken = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetServerUrlDocker() => Settings.serverUrlDocker;
    public static void SetServerUrlDocker(string value) 
    { 
        if (Settings.serverUrlDocker != value)
        {
            Settings.serverUrlDocker = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetAuthTokenDocker() => Settings.authTokenDocker;
    public static void SetAuthTokenDocker(string value) 
    { 
        if (Settings.authTokenDocker != value)
        {
            Settings.authTokenDocker = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetBackupDirectory() => Settings.backupDirectory;
    public static void SetBackupDirectory(string value) 
    { 
        if (Settings.backupDirectory != value)
        {
            Settings.backupDirectory = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetServerDirectory() => Settings.serverDirectory;
    public static void SetServerDirectory(string value) 
    { 
        if (Settings.serverDirectory != value)
        {
            Settings.serverDirectory = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetClientDirectory() => Settings.clientDirectory;
    public static void SetClientDirectory(string value) 
    { 
        if (Settings.clientDirectory != value)
        {
            Settings.clientDirectory = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetServerLang() => Settings.serverLang;
    public static void SetServerLang(string value) 
    { 
        if (Settings.serverLang != value)
        {
            Settings.serverLang = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetUnityLang() => Settings.unityLang;
    public static void SetUnityLang(string value) 
    { 
        if (Settings.unityLang != value)
        {
            Settings.unityLang = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetModuleName() => Settings.moduleName;
    public static void SetModuleName(string value) 
    { 
        if (Settings.moduleName != value)
        {
            Settings.moduleName = value; 
            SaveSettings(); // Important setting - save immediately
        }
    }
    
    public static string GetMaincloudUrl() => Settings.maincloudUrl;
    public static void SetMaincloudUrl(string value) 
    { 
        if (Settings.maincloudUrl != value)
        {
            Settings.maincloudUrl = value; 
            SaveSettings(); 
        }
    }
    
    public static string GetMaincloudAuthToken() => Settings.maincloudAuthToken;
    public static void SetMaincloudAuthToken(string value) 
    { 
        if (Settings.maincloudAuthToken != value)
        {
            Settings.maincloudAuthToken = value; 
            SaveSettings(); 
        }
    }
    
    public static string GetSSHUserName() => Settings.sshUserName;
    public static void SetSSHUserName(string value) 
    { 
        if (Settings.sshUserName != value)
        {
            Settings.sshUserName = value; 
            SaveSettings(); 
        }
    }
    
    public static string GetSSHPrivateKeyPath() => Settings.sshPrivateKeyPath;
    public static void SetSSHPrivateKeyPath(string value) 
    { 
        if (Settings.sshPrivateKeyPath != value)
        {
            Settings.sshPrivateKeyPath = value; 
            SaveSettings(); 
        }
    }
    
    public static string GetCustomServerUrl() => Settings.customServerUrl;
    public static void SetCustomServerUrl(string value) 
    { 
        if (Settings.customServerUrl != value)
        {
            Settings.customServerUrl = value; 
            SaveSettings(); 
        }
    }
    
    public static string GetCustomServerAuthToken() => Settings.customServerAuthToken;
    public static void SetCustomServerAuthToken(string value) 
    { 
        if (Settings.customServerAuthToken != value)
        {
            Settings.customServerAuthToken = value; 
            SaveSettings(); 
        }
    }
    
    public static string GetSpacetimeDBCurrentVersionWSL() => Settings.spacetimeDBCurrentVersionWSL;
    public static void SetSpacetimeDBCurrentVersionWSL(string value) 
    { 
        if (Settings.spacetimeDBCurrentVersionWSL != value)
        {
            Settings.spacetimeDBCurrentVersionWSL = value; 
            MarkDirty();
        }
    }
    
    public static string GetSpacetimeDBCurrentVersionDocker() => Settings.spacetimeDBCurrentVersionDocker;
    public static void SetSpacetimeDBCurrentVersionDocker(string value) 
    { 
        if (Settings.spacetimeDBCurrentVersionDocker != value)
        {
            Settings.spacetimeDBCurrentVersionDocker = value; 
            MarkDirty();
        }
    }
    
    public static string GetSpacetimeDBCurrentVersionCustom() => Settings.spacetimeDBCurrentVersionCustom;
    public static void SetSpacetimeDBCurrentVersionCustom(string value) 
    { 
        if (Settings.spacetimeDBCurrentVersionCustom != value)
        {
            Settings.spacetimeDBCurrentVersionCustom = value; 
            MarkDirty();
        }
    }
    
    public static string GetSpacetimeDBCurrentVersionTool() => Settings.spacetimeDBCurrentVersionTool;
    public static void SetSpacetimeDBCurrentVersionTool(string value) 
    { 
        if (Settings.spacetimeDBCurrentVersionTool != value)
        {
            Settings.spacetimeDBCurrentVersionTool = value; 
            MarkDirty();
        }
    }
    
    public static string GetSpacetimeDBLatestVersion() => Settings.spacetimeDBLatestVersion;
    public static void SetSpacetimeDBLatestVersion(string value) 
    { 
        if (Settings.spacetimeDBLatestVersion != value)
        {
            Settings.spacetimeDBLatestVersion = value; 
            MarkDirty();
        }
    }
    
    public static string GetDockerImageCurrentTag() => Settings.dockerImageCurrentTag;
    public static void SetDockerImageCurrentTag(string value) 
    { 
        if (Settings.dockerImageCurrentTag != value)
        {
            Settings.dockerImageCurrentTag = value; 
            MarkDirty();
        }
    }
    
    public static string GetDockerImageLatestTag() => Settings.dockerImageLatestTag;
    public static void SetDockerImageLatestTag(string value) 
    { 
        if (Settings.dockerImageLatestTag != value)
        {
            Settings.dockerImageLatestTag = value; 
            MarkDirty();
        }
    }
    
    public static bool GetDockerImageUpdateAvailable() => Settings.dockerImageUpdateAvailable;
    public static void SetDockerImageUpdateAvailable(bool value) 
    { 
        if (Settings.dockerImageUpdateAvailable != value)
        {
            Settings.dockerImageUpdateAvailable = value; 
            MarkDirty();
        }
    }
    
    public static string GetRustCurrentVersionWSL() => Settings.rustCurrentVersionWSL;
    public static void SetRustCurrentVersionWSL(string value) 
    { 
        if (Settings.rustCurrentVersionWSL != value)
        {
            Settings.rustCurrentVersionWSL = value; 
            MarkDirty();
        }
    }
    
    public static string GetRustLatestVersionWSL() => Settings.rustLatestVersionWSL;
    public static void SetRustLatestVersionWSL(string value) 
    { 
        if (Settings.rustLatestVersionWSL != value)
        {
            Settings.rustLatestVersionWSL = value; 
            MarkDirty();
        }
    }
    
    public static string GetRustupVersionWSL() => Settings.rustupVersionWSL;
    public static void SetRustupVersionWSL(string value) 
    { 
        if (Settings.rustupVersionWSL != value)
        {
            Settings.rustupVersionWSL = value; 
            MarkDirty();
        }
    }

    public static string GetRustCurrentVersionDocker() => Settings.rustCurrentVersionDocker;
    public static void SetRustCurrentVersionDocker(string value) 
    { 
        if (Settings.rustCurrentVersionDocker != value)
        {
            Settings.rustCurrentVersionDocker = value; 
            MarkDirty();
        }
    }
    
    public static string GetRustLatestVersionDocker() => Settings.rustLatestVersionDocker;
    public static void SetRustLatestVersionDocker(string value) 
    { 
        if (Settings.rustLatestVersionDocker != value)
        {
            Settings.rustLatestVersionDocker = value; 
            MarkDirty();
        }
    }
    
    public static string GetRustupVersionDocker() => Settings.rustupVersionDocker;
    public static void SetRustupVersionDocker(string value) 
    { 
        if (Settings.rustupVersionDocker != value)
        {
            Settings.rustupVersionDocker = value; 
            MarkDirty();
        }
    }

    public static string GetCCCPAssetStoreLatestVersion() => Settings.CCCPAssetStoreLatestVersion;
    public static void SetCCCPAssetStoreLatestVersion(string value) 
    { 
        if (Settings.CCCPAssetStoreLatestVersion != value)
        {
            Settings.CCCPAssetStoreLatestVersion = value; 
            MarkDirty(); 
        }
    }

    public static string GetSpacetimeSDKLatestVersion() => Settings.spacetimeSDKLatestVersion;
    public static void SetSpacetimeSDKLatestVersion(string value) 
    { 
        if (Settings.spacetimeSDKLatestVersion != value)
        {
            Settings.spacetimeSDKLatestVersion = value; 
            MarkDirty(); 
        }
    }

    public static string GetOriginalFileInfo() => Settings.originalFileInfo;
    public static void SetOriginalFileInfo(string value) 
    { 
        if (Settings.originalFileInfo != value)
        {
            Settings.originalFileInfo = value; 
            MarkDirty(); 
        }
    }
    
    public static string GetCurrentFileInfo() => Settings.currentFileInfo;
    public static void SetCurrentFileInfo(string value) 
    { 
        if (Settings.currentFileInfo != value)
        {
            Settings.currentFileInfo = value; 
            MarkDirty(); 
        }
    }
    
    public static string GetColumnWidths() => Settings.columnWidths;
    public static void SetColumnWidths(string value) 
    { 
        if (Settings.columnWidths != value)
        {
            Settings.columnWidths = value; 
            MarkDirty(); 
        }
    }
    
    public static string GetLastSelectedTable() => Settings.lastSelectedTable;
    public static void SetLastSelectedTable(string value) 
    { 
        if (Settings.lastSelectedTable != value)
        {
            Settings.lastSelectedTable = value; 
            MarkDirty(); 
        }
    }

    public static string GetDistributionType() => Settings.distributionType;
    public static void SetDistributionType(string value) 
    { 
        if (Settings.distributionType != value)
        {
            Settings.distributionType = value; 
            MarkDirty(); 
        }
    }

    public static string GetGithubLastCommitSha() => Settings.githubLastCommitSha;
    public static void SetGithubLastCommitSha(string value) 
    { 
        if (Settings.githubLastCommitSha != value)
        {
            Settings.githubLastCommitSha = value; 
            MarkDirty(); 
        }
    }

    #endregion
    
    #region Integer Properties
    
    public static int GetServerPort() => Settings.serverPort;
    public static void SetServerPort(int value) 
    { 
        if (Settings.serverPort != value)
        {
            Settings.serverPort = value; 
            SaveSettings(); 
        }
    }
    
    public static int GetServerPortDocker() => Settings.serverPortDocker;
    public static void SetServerPortDocker(int value) 
    { 
        if (Settings.serverPortDocker != value)
        {
            Settings.serverPortDocker = value; 
            SaveSettings(); 
        }
    }
    
    public static int GetSelectedModuleIndex() => Settings.selectedModuleIndex;
    public static void SetSelectedModuleIndex(int value) 
    { 
        if (Settings.selectedModuleIndex != value)
        {
            Settings.selectedModuleIndex = value; 
            SaveSettings(); 
        }
    }
    
    public static int GetCustomServerPort() => Settings.customServerPort;
    public static void SetCustomServerPort(int value) 
    { 
        if (Settings.customServerPort != value)
        {
            Settings.customServerPort = value; 
            SaveSettings(); 
        }
    }
    
    #endregion
    
    #region Boolean Properties
    
    public static bool GetHasWSL() => Settings.hasWSL;
    public static void SetHasWSL(bool value) 
    { 
        if (Settings.hasWSL != value)
        {
            Settings.hasWSL = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasDebian() => Settings.hasDebian;
    public static void SetHasDebian(bool value) 
    { 
        if (Settings.hasDebian != value)
        {
            Settings.hasDebian = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasDebianTrixie() => Settings.hasDebianTrixie;
    public static void SetHasDebianTrixie(bool value) 
    { 
        if (Settings.hasDebianTrixie != value)
        {
            Settings.hasDebianTrixie = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasCurl() => Settings.hasCurl;
    public static void SetHasCurl(bool value) 
    { 
        if (Settings.hasCurl != value)
        {
            Settings.hasCurl = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasSpacetimeDBServer() => Settings.hasSpacetimeDBServer;
    public static void SetHasSpacetimeDBServer(bool value) 
    { 
        if (Settings.hasSpacetimeDBServer != value)
        {
            Settings.hasSpacetimeDBServer = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasSpacetimeDBPath() => Settings.hasSpacetimeDBPath;
    public static void SetHasSpacetimeDBPath(bool value) 
    { 
        if (Settings.hasSpacetimeDBPath != value)
        {
            Settings.hasSpacetimeDBPath = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasSpacetimeDBService() => Settings.hasSpacetimeDBService;
    public static void SetHasSpacetimeDBService(bool value) 
    { 
        if (Settings.hasSpacetimeDBService != value)
        {
            Settings.hasSpacetimeDBService = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasSpacetimeDBLogsService() => Settings.hasSpacetimeDBLogsService;
    public static void SetHasSpacetimeDBLogsService(bool value) 
    { 
        if (Settings.hasSpacetimeDBLogsService != value)
        {
            Settings.hasSpacetimeDBLogsService = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasRust() => Settings.hasRust;
    public static void SetHasRust(bool value) 
    { 
        if (Settings.hasRust != value)
        {
            Settings.hasRust = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasNETSDK() => Settings.hasNETSDK;
    public static void SetHasNETSDK(bool value) 
    { 
        if (Settings.hasNETSDK != value)
        {
            Settings.hasNETSDK = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasBinaryen() => Settings.hasBinaryen;
    public static void SetHasBinaryen(bool value) 
    { 
        if (Settings.hasBinaryen != value)
        {
            Settings.hasBinaryen = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasGit() => Settings.hasGit;
    public static void SetHasGit(bool value) 
    { 
        if (Settings.hasGit != value)
        {
            Settings.hasGit = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasSpacetimeDBUnitySDK() => Settings.hasSpacetimeDBUnitySDK;
    public static void SetHasSpacetimeDBUnitySDK(bool value) 
    { 
        if (Settings.hasSpacetimeDBUnitySDK != value)
        {
            Settings.hasSpacetimeDBUnitySDK = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasCustomDebianUser() => Settings.hasCustomDebianUser;
    public static void SetHasCustomDebianUser(bool value) 
    { 
        if (Settings.hasCustomDebianUser != value)
        {
            Settings.hasCustomDebianUser = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasCustomDebianTrixie() => Settings.hasCustomDebianTrixie;
    public static void SetHasCustomDebianTrixie(bool value) 
    { 
        if (Settings.hasCustomDebianTrixie != value)
        {
            Settings.hasCustomDebianTrixie = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasCustomCurl() => Settings.hasCustomCurl;
    public static void SetHasCustomCurl(bool value) 
    { 
        if (Settings.hasCustomCurl != value)
        {
            Settings.hasCustomCurl = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasCustomSpacetimeDBServer() => Settings.hasCustomSpacetimeDBServer;
    public static void SetHasCustomSpacetimeDBServer(bool value) 
    { 
        if (Settings.hasCustomSpacetimeDBServer != value)
        {
            Settings.hasCustomSpacetimeDBServer = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasCustomSpacetimeDBPath() => Settings.hasCustomSpacetimeDBPath;
    public static void SetHasCustomSpacetimeDBPath(bool value) 
    { 
        if (Settings.hasCustomSpacetimeDBPath != value)
        {
            Settings.hasCustomSpacetimeDBPath = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasCustomSpacetimeDBService() => Settings.hasCustomSpacetimeDBService;
    public static void SetHasCustomSpacetimeDBService(bool value) 
    { 
        if (Settings.hasCustomSpacetimeDBService != value)
        {
            Settings.hasCustomSpacetimeDBService = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasCustomSpacetimeDBLogsService() => Settings.hasCustomSpacetimeDBLogsService;
    public static void SetHasCustomSpacetimeDBLogsService(bool value) 
    { 
        if (Settings.hasCustomSpacetimeDBLogsService != value)
        {
            Settings.hasCustomSpacetimeDBLogsService = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetWslPrerequisitesChecked() => Settings.wslPrerequisitesChecked;
    public static void SetWslPrerequisitesChecked(bool value) 
    { 
        if (Settings.wslPrerequisitesChecked != value)
        {
            Settings.wslPrerequisitesChecked = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetInitializedFirstModule() => Settings.initializedFirstModule;
    public static void SetInitializedFirstModule(bool value) 
    { 
        if (Settings.initializedFirstModule != value)
        {
            Settings.initializedFirstModule = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetPublishFirstModule() => Settings.publishFirstModule;
    public static void SetPublishFirstModule(bool value) 
    { 
        if (Settings.publishFirstModule != value)
        {
            Settings.publishFirstModule = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasAllPrerequisites() => Settings.hasAllPrerequisites;
    public static void SetHasAllPrerequisites(bool value) 
    { 
        if (Settings.hasAllPrerequisites != value)
        {
            Settings.hasAllPrerequisites = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasDocker() => Settings.hasDocker;
    public static void SetHasDocker(bool value) 
    { 
        if (Settings.hasDocker != value)
        {
            Settings.hasDocker = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasDockerCompose() => Settings.hasDockerCompose;
    public static void SetHasDockerCompose(bool value) 
    { 
        if (Settings.hasDockerCompose != value)
        {
            Settings.hasDockerCompose = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetHasDockerImage() => Settings.hasDockerImage;
    public static void SetHasDockerImage(bool value) 
    { 
        if (Settings.hasDockerImage != value)
        {
            Settings.hasDockerImage = value; 
            SaveSettings(); 
        }
    }

    public static bool GetHasDockerContainerMounts() => Settings.hasDockerContainerMounts;
    public static void SetHasDockerContainerMounts(bool value) 
    { 
        if (Settings.hasDockerContainerMounts != value)
        {
            Settings.hasDockerContainerMounts = value; 
            SaveSettings(); 
        }
    }

    public static bool GetHideWarnings() => Settings.hideWarnings;
    public static void SetHideWarnings(bool value) 
    { 
        if (Settings.hideWarnings != value)
        {
            Settings.hideWarnings = value; 
            MarkDirty(); 
        }
    }
    
    public static bool GetDetectServerChanges() => Settings.detectServerChanges;
    public static void SetDetectServerChanges(bool value) 
    { 
        if (Settings.detectServerChanges != value)
        {
            Settings.detectServerChanges = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetAutoPublishMode() => Settings.autoPublishMode;
    public static void SetAutoPublishMode(bool value) 
    { 
        if (Settings.autoPublishMode != value)
        {
            Settings.autoPublishMode = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetPublishAndGenerateMode() => Settings.publishAndGenerateMode;
    public static void SetPublishAndGenerateMode(bool value) 
    { 
        if (Settings.publishAndGenerateMode != value)
        {
            Settings.publishAndGenerateMode = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetSilentMode() => Settings.silentMode;
    public static void SetSilentMode(bool value) 
    { 
        if (Settings.silentMode != value)
        {
            Settings.silentMode = value; 
            SaveSettings(); 
        }
    }
    
    public static bool GetDebugMode() => Settings.debugMode;
    public static void SetDebugMode(bool value) 
    { 
        if (Settings.debugMode != value)
        {
            Settings.debugMode = value; 
            MarkDirty(); 
        }
    }
    
    public static bool GetClearModuleLogAtStart() => Settings.clearModuleLogAtStart;
    public static void SetClearModuleLogAtStart(bool value) 
    { 
        if (Settings.clearModuleLogAtStart != value)
        {
            Settings.clearModuleLogAtStart = value; 
            MarkDirty(); 
        }
    }
    
    public static bool GetClearDatabaseLogAtStart() => Settings.clearDatabaseLogAtStart;
    public static void SetClearDatabaseLogAtStart(bool value) 
    { 
        if (Settings.clearDatabaseLogAtStart != value)
        {
            Settings.clearDatabaseLogAtStart = value; 
            MarkDirty(); 
        }
    }

    public static bool GetAutoCloseCLI() => Settings.autoCloseCLI;
    public static void SetAutoCloseCLI(bool value) 
    { 
        if (Settings.autoCloseCLI != value)
        {
            Settings.autoCloseCLI = value; 
            MarkDirty(); 
        }
    }
    
    public static bool GetEchoToConsole() => Settings.echoToConsole;
    public static void SetEchoToConsole(bool value) 
    { 
        if (Settings.echoToConsole != value)
        {
            Settings.echoToConsole = value; 
            MarkDirty(); 
        }
    }

    public static bool GetShowLocalTime() => Settings.showLocalTime;
    public static void SetShowLocalTime(bool value) 
    { 
        if (Settings.showLocalTime != value)
        {
            Settings.showLocalTime = value; 
            MarkDirty(); 
        }
    }

    public static bool GetRustUpdateAvailable() => Settings.rustUpdateAvailable;
    public static void SetRustUpdateAvailable(bool value) 
    { 
        if (Settings.rustUpdateAvailable != value)
        {
            Settings.rustUpdateAvailable = value; 
            MarkDirty(); 
        }
    }

    public static bool GetRustupUpdateAvailable() => Settings.rustupUpdateAvailable;
    public static void SetRustupUpdateAvailable(bool value) 
    { 
        if (Settings.rustupUpdateAvailable != value)
        {
            Settings.rustupUpdateAvailable = value; 
            MarkDirty(); 
        }
    }

    public static bool GetSpacetimeSDKUpdateAvailable() => Settings.spacetimeSDKUpdateAvailable;
    public static void SetSpacetimeSDKUpdateAvailable(bool value) 
    { 
        if (Settings.spacetimeSDKUpdateAvailable != value)
        {
            Settings.spacetimeSDKUpdateAvailable = value; 
            MarkDirty(); 
        }
    }

    public static bool GetSpacetimeDBUpdateAvailable() => Settings.SpacetimeDBUpdateAvailable;
    public static void SetSpacetimeDBUpdateAvailable(bool value) 
    { 
        if (Settings.SpacetimeDBUpdateAvailable != value)
        {
            Settings.SpacetimeDBUpdateAvailable = value; 
            MarkDirty(); 
        }
    }

    public static bool GetCCCPGithubUpdateAvailable() => Settings.CCCPGithubUpdateAvailable;
    public static void SetCCCPGithubUpdateAvailable(bool value) 
    { 
        if (Settings.CCCPGithubUpdateAvailable != value)
        {
            Settings.CCCPGithubUpdateAvailable = value; 
            MarkDirty(); 
        }
    }

    public static bool GetCCCPAssetStoreUpdateAvailable() => Settings.CCCPAssetStoreUpdateAvailable;
    public static void SetCCCPAssetStoreUpdateAvailable(bool value) 
    { 
        if (Settings.CCCPAssetStoreUpdateAvailable != value)
        {
            Settings.CCCPAssetStoreUpdateAvailable = value; 
            MarkDirty(); 
        }
    }

    public static bool GetIsAssetStoreVersion() => Settings.isAssetStoreVersion;
    public static void SetIsAssetStoreVersion(bool value) 
    { 
        if (Settings.isAssetStoreVersion != value)
        {
            Settings.isAssetStoreVersion = value; 
            MarkDirty(); 
        }
    }

    public static bool GetIsGitHubVersion() => Settings.isGitHubVersion;
    public static void SetIsGitHubVersion(bool value) 
    { 
        if (Settings.isGitHubVersion != value)
        {
            Settings.isGitHubVersion = value; 
            MarkDirty(); 
        }
    }

    public static bool GetWSL1Installed() => Settings.WSL1Installed;
    public static void SetWSL1Installed(bool value) 
    { 
        if (Settings.WSL1Installed != value)
        {
            Settings.WSL1Installed = value; 
            MarkDirty(); 
        }
    }
    
    public static bool GetVisibleInstallProcesses() => Settings.visibleInstallProcesses;
    public static void SetVisibleInstallProcesses(bool value) 
    { 
        if (Settings.visibleInstallProcesses != value)
        {
            Settings.visibleInstallProcesses = value; 
            MarkDirty(); 
        }
    }
    
    public static bool GetKeepWindowOpenForDebug() => Settings.keepWindowOpenForDebug;
    public static void SetKeepWindowOpenForDebug(bool value) 
    { 
        if (Settings.keepWindowOpenForDebug != value)
        {
            Settings.keepWindowOpenForDebug = value; 
            MarkDirty(); 
        }
    }
    
    public static bool GetUpdateCargoToml() => Settings.updateCargoToml;
    public static void SetUpdateCargoToml(bool value) 
    { 
        if (Settings.updateCargoToml != value)
        {
            Settings.updateCargoToml = value; 
            MarkDirty(); 
        }
    }
    
    public static bool GetServiceMode() => Settings.serviceMode;
    public static void SetServiceMode(bool value) 
    { 
        if (Settings.serviceMode != value)
        {
            Settings.serviceMode = value; 
            MarkDirty(); 
        }
    }

    public static bool GetFirstTimeOpenInstaller() => Settings.firstTimeOpenInstaller;
    public static void SetFirstTimeOpenInstaller(bool value) 
    { 
        if (Settings.firstTimeOpenInstaller != value)
        {
            Settings.firstTimeOpenInstaller = value; 
            MarkDirty(); 
        }
    }

    public static bool GetWelcomeWindowShown() => Settings.welcomeWindowShown;
    public static void SetWelcomeWindowShown(bool value) 
    { 
        if (Settings.welcomeWindowShown != value)
        {
            Settings.welcomeWindowShown = value; 
            MarkDirty(); 
        }
    }

    public static bool GetAutoscroll() => Settings.autoscroll;
    public static void SetAutoscroll(bool value) 
    { 
        if (Settings.autoscroll != value)
        {
            Settings.autoscroll = value; 
            MarkUISettingsDirty(); 
        }
    }
    
    public static bool GetColorLogo() => Settings.colorLogo;
    public static void SetColorLogo(bool value) 
    { 
        if (Settings.colorLogo != value)
        {
            Settings.colorLogo = value; 
            MarkUISettingsDirty(); 
        }
    }
    
    public static bool GetShowPrerequisites() => Settings.showPrerequisites;
    public static void SetShowPrerequisites(bool value) 
    { 
        if (Settings.showPrerequisites != value)
        {
            Settings.showPrerequisites = value; 
            MarkUISettingsDirty(); 
        }
    }

    public static bool GetShowSettings() => Settings.showSettings;
    public static void SetShowSettings(bool value) 
    { 
        if (Settings.showSettings != value)
        {
            Settings.showSettings = value; 
            MarkUISettingsDirty(); 
        }
    }

    public static bool GetShowCommands() => Settings.showCommands;
    public static void SetShowCommands(bool value) 
    { 
        if (Settings.showCommands != value)
        {
            Settings.showCommands = value; 
            MarkUISettingsDirty(); 
        }
    }
    
    public static bool GetServerChangesDetected() => Settings.serverChangesDetected;
    public static void SetServerChangesDetected(bool value) 
    { 
        if (Settings.serverChangesDetected != value)
        {
            Settings.serverChangesDetected = value; 
            MarkDirty(); 
        }
    }
    
    #endregion
    
    #region Float Properties
    
    public static float GetLogUpdateFrequency() => Settings.logUpdateFrequency;
    public static void SetLogUpdateFrequency(float value) 
    { 
        if (Settings.logUpdateFrequency != value)
        {
            Settings.logUpdateFrequency = value; 
            MarkDirty(); 
        }
    }
    
    #endregion
    
    #region Enum Properties
    
    public static NorthernRogue.CCCP.Editor.ServerManager.ServerMode GetServerMode() => Settings.serverMode;
    public static void SetServerMode(NorthernRogue.CCCP.Editor.ServerManager.ServerMode value) 
    { 
        if (Settings.serverMode != value)
        {
            Settings.serverMode = value; 
            MarkDirty(); // Use deferred save to avoid asset postprocessing issues
        }
    }
    
    public static NorthernRogue.CCCP.Editor.ServerManager.ServerMode GetLastLocalServerMode() => Settings.lastLocalServerMode;
    public static void SetLastLocalServerMode(NorthernRogue.CCCP.Editor.ServerManager.ServerMode value) 
    { 
        if (Settings.lastLocalServerMode != value)
        {
            Settings.lastLocalServerMode = value; 
            MarkDirty(); // Use deferred save to avoid asset postprocessing issues
        }
    }
    
    public static string GetLocalCLIProvider() => Settings.localCLIProvider;
    public static void SetLocalCLIProvider(string value) 
    { 
        if (Settings.localCLIProvider != value)
        {
            Settings.localCLIProvider = value; 
            SaveSettings();
        }
    }
    
    #endregion
    
    #region Module Management
    
    public static System.Collections.Generic.List<ModuleInfo> GetSavedModules() => Settings.savedModules;
    public static void SetSavedModules(System.Collections.Generic.List<ModuleInfo> modules) 
    { 
        if (Settings.savedModules != modules)
        {
            Settings.savedModules = modules; 
            SaveSettings(); // Important data - save immediately
        }
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
            if (Settings.savedModules.Count > 0)
            {
                Settings.savedModules.Clear();
                SaveSettings();
            }
            return;
        }
        
        try
        {
            // First try the new format (ModuleListWrapper with "modules" property)
            var wrapper = JsonUtility.FromJson<ModuleListWrapper>(json);
            if (wrapper != null && wrapper.modules != null)
            {
                Settings.savedModules = wrapper.modules.ToList();
                SaveSettings();
                return;
            }
            
            // Try the old format (SerializableListWrapper with "items" property)
            var oldWrapper = JsonUtility.FromJson<SerializableListWrapper>(json);
            if (oldWrapper != null && oldWrapper.items != null)
            {
                Settings.savedModules = oldWrapper.items.ToList();
                SaveSettings();
                return;
            }
        }
        catch (System.Exception e)
        {
            if (debugMode) Debug.LogError($"Failed to parse modules JSON: {e.Message}");
        }
    }
    
    [System.Serializable]
    private class ModuleListWrapper
    {
        public ModuleInfo[] modules;
    }
    
    [System.Serializable]
    private class SerializableListWrapper
    {
        public ModuleInfo[] items;
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
            case "ServerURLDocker": return GetServerUrlDocker();
            case "AuthTokenDocker": return GetAuthTokenDocker();
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
            case "SpacetimeDBVersion": return GetSpacetimeDBCurrentVersionWSL();
            case "SpacetimeDBVersionCustom": return GetSpacetimeDBCurrentVersionCustom();
            case "SpacetimeDBVersionTool": return GetSpacetimeDBCurrentVersionTool();
            case "SpacetimeDBLatestVersion": return GetSpacetimeDBLatestVersion();
            case "CCCPAssetStoreLatestVersion": return GetCCCPAssetStoreLatestVersion();
            case "SpacetimeSDKLatestVersion": return GetSpacetimeSDKLatestVersion();
            case "RustVersion": return GetRustCurrentVersionWSL();
            case "RustLatestVersion": return GetRustLatestVersionWSL();
            case "RustupVersion": return GetRustupVersionWSL();
            case "DistributionType": return GetDistributionType();
            case "GithubLastCommitSha": return GetGithubLastCommitSha();
            case "OriginalFileInfo": return GetOriginalFileInfo();
            case "CurrentFileInfo": return GetCurrentFileInfo();
            case "SavedModules": return GetSavedModulesJson();
            case "ServerMode": return GetServerMode().ToString();
            default:
                if (debugMode) Debug.LogWarning($"CCCP: Unknown string key '{key}' in settings adapter");
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
            case "ServerURLDocker": SetServerUrlDocker(value); break;
            case "AuthTokenDocker": SetAuthTokenDocker(value); break;
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
            case "SpacetimeDBVersion": SetSpacetimeDBCurrentVersionWSL(value); break;
            case "SpacetimeDBVersionCustom": SetSpacetimeDBCurrentVersionCustom(value); break;
            case "SpacetimeDBVersionTool": SetSpacetimeDBCurrentVersionTool(value); break;
            case "SpacetimeDBLatestVersion": SetSpacetimeDBLatestVersion(value); break;
            case "CCCPAssetStoreLatestVersion": SetCCCPAssetStoreLatestVersion(value); break;
            case "SpacetimeSDKLatestVersion": SetSpacetimeSDKLatestVersion(value); break;
            case "RustVersion": SetRustCurrentVersionWSL(value); break;
            case "RustLatestVersion": SetRustLatestVersionWSL(value); break;
            case "RustupVersion": SetRustupVersionWSL(value); break;
            case "DistributionType": SetDistributionType(value); break;
            case "GithubLastCommitSha": SetGithubLastCommitSha(value); break;
            case "OriginalFileInfo": SetOriginalFileInfo(value); break;
            case "CurrentFileInfo": SetCurrentFileInfo(value); break;
            case "SavedModules": SetSavedModulesFromJson(value); break;
            case "ServerMode": 
                if (System.Enum.TryParse<NorthernRogue.CCCP.Editor.ServerManager.ServerMode>(value, out var mode))
                    SetServerMode(mode);
                break;
            default:
                if (debugMode) Debug.LogWarning($"CCCP: Unknown string key '{key}' in settings adapter");
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
            case "AutoCloseWsl": return GetAutoCloseCLI();
            case "EchoToConsole": return GetEchoToConsole();
            case "ShowLocalTime": return GetShowLocalTime();
            case "RustUpdateAvailable": return GetRustUpdateAvailable();
            case "RustupUpdateAvailable": return GetRustupUpdateAvailable();
            case "SpacetimeDBUpdateAvailable": return GetSpacetimeDBUpdateAvailable();
            case "GithubUpdateAvailable": return GetCCCPGithubUpdateAvailable();
            case "AssetStoreUpdateAvailable": return GetCCCPAssetStoreUpdateAvailable();
            case "IsAssetStoreVersion": return GetIsAssetStoreVersion();
            case "IsGitHubVersion": return GetIsGitHubVersion();
            case "WSL1Installed": return GetWSL1Installed();
            case "VisibleInstallProcesses": return GetVisibleInstallProcesses();
            case "KeepWindowOpenForDebug": return GetKeepWindowOpenForDebug();
            case "UpdateCargoToml": return GetUpdateCargoToml();
            case "ServiceMode": return GetServiceMode();
            case "Autoscroll": return GetAutoscroll();
            case "ColorLogo": return GetColorLogo();
            case "ShowPrerequisites": return GetShowPrerequisites();
            case "ShowSettingsWindow": return GetShowSettings();
            case "ShowUtilityCommands": return GetShowCommands();
            case "ServerChangesDetected": return GetServerChangesDetected();
            case "ServerWelcomeWindow_WelcomeWindowShown": return GetWelcomeWindowShown();
            default:
                if (debugMode) Debug.LogWarning($"CCCP: Unknown boolean key '{key}' in settings adapter");
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
            case "AutoCloseWsl": SetAutoCloseCLI(value); break;
            case "EchoToConsole": SetEchoToConsole(value); break;
            case "ShowLocalTime": SetShowLocalTime(value); break;
            case "RustUpdateAvailable": SetRustUpdateAvailable(value); break;
            case "RustupUpdateAvailable": SetRustupUpdateAvailable(value); break;
            case "SpacetimeDBUpdateAvailable": SetSpacetimeDBUpdateAvailable(value); break;
            case "GithubUpdateAvailable": SetCCCPGithubUpdateAvailable(value); break;
            case "AssetStoreUpdateAvailable": SetCCCPAssetStoreUpdateAvailable(value); break;
            case "IsAssetStoreVersion": SetIsAssetStoreVersion(value); break;
            case "IsGitHubVersion": SetIsGitHubVersion(value); break;
            case "WSL1Installed": SetWSL1Installed(value); break;
            case "VisibleInstallProcesses": SetVisibleInstallProcesses(value); break;
            case "KeepWindowOpenForDebug": SetKeepWindowOpenForDebug(value); break;
            case "UpdateCargoToml": SetUpdateCargoToml(value); break;
            case "ServiceMode": SetServiceMode(value); break;
            case "FirstTimeOpenInstaller": SetFirstTimeOpenInstaller(value); break;
            case "Autoscroll": SetAutoscroll(value); break;
            case "ColorLogo": SetColorLogo(value); break;
            case "ShowPrerequisites": SetShowPrerequisites(value); break;
            case "ShowSettingsWindow": SetShowSettings(value); break;
            case "ShowUtilityCommands": SetShowCommands(value); break;
            case "ServerChangesDetected": SetServerChangesDetected(value); break;
            case "ServerWelcomeWindow_WelcomeWindowShown": SetWelcomeWindowShown(value); break;
            default:
                if (debugMode) Debug.LogWarning($"CCCP: Unknown boolean key '{key}' in settings adapter");
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
            case "ServerPortDocker": return GetServerPortDocker();
            case "SelectedModuleIndex": return GetSelectedModuleIndex();
            case "CustomServerPort": return GetCustomServerPort();
            case "ServerMode": return (int)GetServerMode();
            default:
                if (debugMode) Debug.LogWarning($"CCCP: Unknown integer key '{key}' in settings adapter");
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
            case "ServerPortDocker": SetServerPortDocker(value); break;
            case "SelectedModuleIndex": SetSelectedModuleIndex(value); break;
            case "CustomServerPort": SetCustomServerPort(value); break;
            case "ServerMode": SetServerMode((NorthernRogue.CCCP.Editor.ServerManager.ServerMode)value); break;
            default:
                if (debugMode) Debug.LogWarning($"CCCP: Unknown integer key '{key}' in settings adapter");
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
                if (debugMode) Debug.LogWarning($"CCCP: Unknown float key '{key}' in settings adapter");
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
                if (debugMode) Debug.LogWarning($"CCCP: Unknown float key '{key}' in settings adapter");
                break;
        }
    }
    
    #endregion
} // Class
} // Namespace
