using UnityEngine;
using UnityEditor;
using System.Linq;
using System.IO;

// Builds the Unity's Project Settings UI and controls the logic for the settings asset

namespace NorthernRogue.CCCP.Editor.Settings {

public static class CCCPSettingsProvider
{
    private const string DefaultSettingsPath = "Assets/Cosmos Cove Control Panel Settings/CCCPSettings.asset"; // Default settings asset path if not user set
    private const string SettingsPathKey = "CCCP_SettingsPath"; // Where the settings asset is located
    private const string PrefsKeyPrefix = "CCCP_"; // For backwards compatibility with EditorPrefs

    public static string SettingsPath 
    { 
        get 
        { 
            string projectSpecificKey = GetProjectSpecificKey(SettingsPathKey);
            return EditorPrefs.GetString(projectSpecificKey, DefaultSettingsPath);
        }
        set
        {
            string projectSpecificKey = GetProjectSpecificKey(SettingsPathKey);
            EditorPrefs.SetString(projectSpecificKey, value);
        }
    }
    
    public static string ResourcesPath 
    { 
        get 
        { 
            string settingsPath = SettingsPath;
            return Path.GetDirectoryName(settingsPath);
        }
    }

    private static string GetProjectSpecificKey(string key)
    {
        // Use dataPath which is consistent and doesn't change with symlinks
        string projectPath = Application.dataPath;
        // Use a more reliable hash that won't change
        int hash = projectPath.GetHashCode();
        return $"{key}_Project_{hash:X}"; // Hex format is cleaner
    }
    
    /// <summary>
    /// Check if an asset exists at the given Unity asset path
    /// </summary>
    private static bool AssetExists(string assetPath)
    {
        return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null;
    }
    
    [SettingsProvider]
    public static SettingsProvider CreateCCCPSettingsProvider()
    {
        var provider = new SettingsProvider("Project/Cosmos Cove Control Panel", SettingsScope.Project)
        {
            label = "Cosmos Cove Control Panel",
            guiHandler = (searchContext) =>
            {
                var settings = GetOrCreateSettings();
                
                // Check if settings creation failed
                if (settings == null)
                {
                    EditorGUILayout.HelpBox("Failed to create or load CCCP settings. This might be due to missing directories or permission issues.", MessageType.Error);
                    EditorGUILayout.Space();
                    
                    if (GUILayout.Button("Try to Create Settings Again"))
                    {
                        // Force refresh and try again
                        AssetDatabase.Refresh();
                        CCCPSettings.RefreshInstance();
                        settings = GetOrCreateSettings();
                    }
                    
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Current Settings Path:", SettingsPath);
                    
                    if (GUILayout.Button("Change Settings Path"))
                    {
                        string selectedPath = EditorUtility.SaveFilePanel(
                            "Select Settings File Location",
                            Application.dataPath,
                            "CCCPSettings.asset",
                            "asset"
                        );
                        
                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            string relativePath = FileUtil.GetProjectRelativePath(selectedPath);
                            if (!string.IsNullOrEmpty(relativePath))
                            {
                                SettingsPath = relativePath;
                                settings = GetOrCreateSettings();
                            }
                        }
                    }
                    
                    if (settings == null)
                    {
                        return; // Exit early if we still can't create settings
                    }
                }
                
                using (var changeCheck = new EditorGUI.ChangeCheckScope())
                {
                    var serializedObject = new SerializedObject(settings);
                    
                    // Migration section
                    if (!settings.migratedFromEditorPrefs)
                    {
                        EditorGUILayout.HelpBox("Migration from EditorPrefs detected. Click 'Migrate Settings' to transfer your existing settings.", MessageType.Info);
                        if (GUILayout.Button("Migrate Settings from EditorPrefs"))
                        {
                            MigrateFromEditorPrefs(settings);
                            EditorUtility.SetDirty(settings);
                            AssetDatabase.SaveAssets();
                        }
                        EditorGUILayout.Space();
                    }

                    // Set or Move Settings Asset
                    EditorGUILayout.LabelField("Set or Move Settings Asset", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Settings Path:", GUILayout.Width(100));
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.TextField(SettingsPath);
                    EditorGUI.EndDisabledGroup();
                    
                    if (GUILayout.Button("Browse...", GUILayout.Width(80)))
                    {
                        string currentDirectory = Path.GetDirectoryName(SettingsPath);
                        if (!Directory.Exists(currentDirectory))
                        {
                            currentDirectory = Application.dataPath;
                        }
                        
                        string selectedPath = EditorUtility.SaveFilePanel(
                            "Select Settings File Location",
                            currentDirectory,
                            "CCCPSettings.asset",
                            "asset"
                        );
                        
                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            // Convert absolute path to relative path
                            string relativePath = FileUtil.GetProjectRelativePath(selectedPath);
                            if (!string.IsNullOrEmpty(relativePath))
                            {
                                // Check if we need to move the existing settings file
                                if (SettingsPath != relativePath && AssetExists(SettingsPath))
                                {
                                    if (EditorUtility.DisplayDialog("Move Settings File", 
                                        $"Move existing settings file from:\n{SettingsPath}\n\nto:\n{relativePath}?", 
                                        "Move", "Keep Current"))
                                    {
                                        MoveSettingsFile(SettingsPath, relativePath);
                                    }
                                }
                                
                                SettingsPath = relativePath;
                                
                                // Refresh the settings reference
                                settings = GetOrCreateSettings();
                                serializedObject = new SerializedObject(settings);
                            }
                            else
                            {
                                EditorUtility.DisplayDialog("Invalid Path", 
                                    "The selected path must be within the Assets folder.", "OK");
                            }
                        }
                    }
                    
                    if (GUILayout.Button("Reset", GUILayout.Width(60)))
                    {
                        if (EditorUtility.DisplayDialog("Reset Settings Path", 
                            "Reset settings path to default location?", "Reset", "Cancel"))
                        {
                            if (SettingsPath != DefaultSettingsPath && AssetExists(SettingsPath))
                            {
                                if (EditorUtility.DisplayDialog("Move Settings File", 
                                    $"Move existing settings file to default location?\n{DefaultSettingsPath}", 
                                    "Move", "Keep Current"))
                                {
                                    MoveSettingsFile(SettingsPath, DefaultSettingsPath);
                                }
                            }
                            
                            SettingsPath = DefaultSettingsPath;
                            settings = GetOrCreateSettings();
                            serializedObject = new SerializedObject(settings);
                        }
                    }
                    
                    EditorGUILayout.EndHorizontal();
                    
                    // Show file status
                    if (AssetExists(SettingsPath))
                    {
                        EditorGUILayout.HelpBox($"Settings file found at: {SettingsPath}", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox($"Settings file will be created at: {SettingsPath}", MessageType.Warning);
                    }
                    
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space();

                    EditorGUILayout.LabelField("Settings Visible Here for Reference - Please set them in the Main Window", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(40));
                    EditorGUI.BeginDisabledGroup(true);

                    // Server Configuration (Foldout)
                    var showServerConfig = EditorGUILayout.Foldout(
                        EditorPrefs.GetBool("CCCP_ShowServerConfig", true), 
                        "Server Configuration", 
                        true
                    );
                    EditorPrefs.SetBool("CCCP_ShowServerConfig", showServerConfig);
                    
                    if (showServerConfig)
                    {
                        // Increase label width to make titles wider
                        float prevLabelWidth = EditorGUIUtility.labelWidth;
                        EditorGUIUtility.labelWidth = 160f;

                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverMode"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("userName"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverUrl"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverPort"));

                        // Reset label width
                        EditorGUIUtility.labelWidth = prevLabelWidth;
                        
                        // Auth Token as password field
                        var authTokenProp = serializedObject.FindProperty("authToken");
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(authTokenProp.displayName, GUILayout.Width(143f));
                        authTokenProp.stringValue = EditorGUILayout.PasswordField(authTokenProp.stringValue);
                        EditorGUILayout.EndHorizontal();
                        
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                    }
                    
                    // Directory Settings (Foldout)
                    var showDirectorySettings = EditorGUILayout.Foldout(
                        EditorPrefs.GetBool("CCCP_ShowDirectorySettings", false), 
                        "Directory Settings", 
                        true
                    );
                    EditorPrefs.SetBool("CCCP_ShowDirectorySettings", showDirectorySettings);
                    
                    if (showDirectorySettings)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("backupDirectory"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverDirectory"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("clientDirectory"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                    }
                    
                    // Language Settings (Foldout)
                    var showLanguageSettings = EditorGUILayout.Foldout(
                        EditorPrefs.GetBool("CCCP_ShowLanguageSettings", false), 
                        "Language Settings", 
                        true
                    );
                    EditorPrefs.SetBool("CCCP_ShowLanguageSettings", showLanguageSettings);
                    
                    if (showLanguageSettings)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverLang"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("unityLang"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                    }
                    
                    // Module Configuration (Foldout)
                    var showModuleConfig = EditorGUILayout.Foldout(
                        EditorPrefs.GetBool("CCCP_ShowModuleConfig", false), 
                        "Module Configuration", 
                        true
                    );
                    EditorPrefs.SetBool("CCCP_ShowModuleConfig", showModuleConfig);
                    
                    if (showModuleConfig)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("moduleName"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("selectedModuleIndex"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("savedModules"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                    }
                    
                    // Behavior Settings (Foldout)
                    var showBehaviorSettings = EditorGUILayout.Foldout(
                        EditorPrefs.GetBool("CCCP_ShowBehaviorSettings", false), 
                        "Behavior Settings", 
                        true
                    );
                    EditorPrefs.SetBool("CCCP_ShowBehaviorSettings", showBehaviorSettings);
                    
                    if (showBehaviorSettings)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("hideWarnings"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("detectServerChanges"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoPublishMode"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("publishAndGenerateMode"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("silentMode"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("clearModuleLogAtStart"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("clearDatabaseLogAtStart"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoCloseWsl"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("echoToConsole"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                    }
                    
                    // UI Settings (Foldout)
                    var showUISettings = EditorGUILayout.Foldout(
                        EditorPrefs.GetBool("CCCP_ShowUISettings", false), 
                        "UI Settings", 
                        true
                    );
                    EditorPrefs.SetBool("CCCP_ShowUISettings", showUISettings);
                    
                    if (showUISettings)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoscroll"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("colorLogo"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("showLocalTime"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("welcomeWindowShown"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                    }
                    
                    // Advanced Settings (Foldout)
                    var showAdvanced = EditorGUILayout.Foldout(
                        EditorPrefs.GetBool("CCCP_ShowAdvancedSettings", false), 
                        "Advanced Settings",
                        true
                    );
                    EditorPrefs.SetBool("CCCP_ShowAdvancedSettings", showAdvanced);
                    
                    if (showAdvanced)
                    {
                        EditorGUI.indentLevel++;
                        
                        // Maincloud Configuration
                        var showMaincloudConfig = EditorGUILayout.Foldout(
                            EditorPrefs.GetBool("CCCP_ShowMaincloudConfig", false), 
                            "Maincloud Configuration", 
                            true
                        );
                        EditorPrefs.SetBool("CCCP_ShowMaincloudConfig", showMaincloudConfig);
                        
                        if (showMaincloudConfig)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("maincloudUrl"));
                            
                            // Maincloud Auth Token as password field
                            var maincloudAuthTokenProp = serializedObject.FindProperty("maincloudAuthToken");
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel(maincloudAuthTokenProp.displayName);
                            maincloudAuthTokenProp.stringValue = EditorGUILayout.PasswordField(maincloudAuthTokenProp.stringValue);
                            EditorGUILayout.EndHorizontal();
                            
                            EditorGUI.indentLevel--;
                            EditorGUILayout.Space();
                        }
                        
                        // Custom Server Configuration
                        var showCustomServerConfig = EditorGUILayout.Foldout(
                            EditorPrefs.GetBool("CCCP_ShowCustomServerConfig", false), 
                            "Custom Server Configuration", 
                            true
                        );
                        EditorPrefs.SetBool("CCCP_ShowCustomServerConfig", showCustomServerConfig);
                        
                        if (showCustomServerConfig)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sshUserName"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sshPrivateKeyPath"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("customServerUrl"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("customServerPort"));
                            
                            // Custom Server Auth Token as password field
                            var customServerAuthTokenProp = serializedObject.FindProperty("customServerAuthToken");
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel(customServerAuthTokenProp.displayName);
                            customServerAuthTokenProp.stringValue = EditorGUILayout.PasswordField(customServerAuthTokenProp.stringValue);
                            EditorGUILayout.EndHorizontal();
                            
                            EditorGUI.indentLevel--;
                            EditorGUILayout.Space();
                        }
                        
                        // Prerequisites Status (read-only)
                        var showPrerequisitesStatus = EditorGUILayout.Foldout(
                            EditorPrefs.GetBool("CCCP_ShowPrerequisitesStatus", false), 
                            "Prerequisites Status (Read-Only)", 
                            true
                        );
                        EditorPrefs.SetBool("CCCP_ShowPrerequisitesStatus", showPrerequisitesStatus);
                        
                        if (showPrerequisitesStatus)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUI.BeginDisabledGroup(true);
                            
                            EditorGUILayout.LabelField("WSL Prerequisites", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasWSL"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasDebian"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasDebianTrixie"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasCurl"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasSpacetimeDBServer"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasSpacetimeDBPath"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasSpacetimeDBService"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasSpacetimeDBLogsService"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasRust"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasNETSDK"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasBinaryen"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasGit"));
                            EditorGUI.indentLevel--;
                            
                            EditorGUILayout.Space();
                            EditorGUILayout.LabelField("Custom Server Prerequisites", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasCustomDebianUser"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasCustomDebianTrixie"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasCustomCurl"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasCustomSpacetimeDBServer"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasCustomSpacetimeDBPath"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasCustomSpacetimeDBService"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasCustomSpacetimeDBLogsService"));
                            EditorGUI.indentLevel--;
                            
                            EditorGUILayout.Space();
                            EditorGUILayout.LabelField("Unity Prerequisites", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasSpacetimeDBUnitySDK"));
                            EditorGUI.indentLevel--;
                            
                            EditorGUI.EndDisabledGroup();
                            EditorGUI.indentLevel--;
                            EditorGUILayout.Space();
                        }
                        
                        // Version Information
                        var showVersionInfo = EditorGUILayout.Foldout(
                            EditorPrefs.GetBool("CCCP_ShowVersionInfo", false), 
                            "Version Information (Read-Only)", 
                            true
                        );
                        EditorPrefs.SetBool("CCCP_ShowVersionInfo", showVersionInfo);
                        
                        if (showVersionInfo)
                        {
                            EditorGUI.indentLevel++;
                            
                            // Increase label width to make titles wider
                            float prevLabelWidth = EditorGUIUtility.labelWidth;
                            EditorGUIUtility.labelWidth = 300f;
                            
                            EditorGUILayout.LabelField("SpacetimeDB Versions", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("spacetimeDBCurrentVersion"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("spacetimeDBCurrentVersionCustom"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("spacetimeDBCurrentVersionTool"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("spacetimeDBLatestVersion"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("spacetimeSDKLatestVersion"));
                            EditorGUI.indentLevel--;
                            
                            EditorGUILayout.Space();
                            EditorGUILayout.LabelField("Rust Versions", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("rustCurrentVersion"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("rustLatestVersion"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("rustupVersion"));
                            EditorGUI.indentLevel--;
                            
                            EditorGUILayout.Space();
                            
                            EditorGUILayout.LabelField("CCCP Versions", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("CCCPAssetStoreLatestVersion"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("distributionType"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("githubLastCommitSha"));
                            EditorGUI.indentLevel--;
                            
                            EditorGUI.indentLevel--;
                            EditorGUILayout.Space();
                            // Reset label width
                            EditorGUIUtility.labelWidth = prevLabelWidth;
                        }
                        EditorGUI.indentLevel--;
                    }

                    EditorGUI.EndDisabledGroup();

                    // Migration Tools
                    var showMigrationTools = EditorGUILayout.Foldout(
                        EditorPrefs.GetBool("CCCP_ShowMigrationTools", false), 
                        "Migration From EditorPrefs", 
                        true
                    );
                    EditorPrefs.SetBool("CCCP_ShowMigrationTools", showMigrationTools);
                    
                    if (showMigrationTools)
                    {
                        EditorGUI.indentLevel++;
                        
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"Migration Status: {(settings.migratedFromEditorPrefs ? "Completed" : "Not Migrated")}", 
                            GUILayout.ExpandWidth(true));
                        
                        EditorGUI.BeginDisabledGroup(!HasEditorPrefsSettings());
                        if (GUILayout.Button("Force Migration from EditorPrefs", GUILayout.Width(200)))
                        {
                            if (EditorUtility.DisplayDialog("Force Migration", 
                                "This will overwrite current settings with EditorPrefs data. Continue?", 
                                "Migrate", "Cancel"))
                            {
                                settings.migratedFromEditorPrefs = false; // Reset flag to allow re-migration
                                MigrateFromEditorPrefs(settings);
                                EditorUtility.SetDirty(settings);
                                AssetDatabase.SaveAssets();
                            }
                        }
                        EditorGUI.EndDisabledGroup();
                        EditorGUILayout.EndHorizontal();
                        
                        if (!HasEditorPrefsSettings())
                        {
                            EditorGUILayout.HelpBox("No EditorPrefs settings found to migrate.", MessageType.Info);
                        }
                        
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                    }

                    // Reset button
                    EditorGUILayout.Space();
                    if (GUILayout.Button("Reset to Defaults"))
                    {
                        if (EditorUtility.DisplayDialog("Reset Settings", 
                            "Are you sure you want to reset all CCCP settings to their default values?", 
                            "Reset", "Cancel"))
                        {
                            settings.ResetToDefaults();
                            EditorUtility.SetDirty(settings);
                            AssetDatabase.SaveAssets();
                        }
                    }
                    
                    if (changeCheck.changed)
                    {
                        serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(settings);
                    }
                }
            },
            
            keywords = new[] { "CCCP", "SpacetimeDB", "Server", "Settings", "Configuration" }
        };
        
        return provider;
    }
    
    /// <summary>
    /// Ensures that the directory structure exists for the given path
    /// </summary>
    /// <param name="directoryPath">The directory path to create</param>
    /// <returns>True if successful, false otherwise</returns>
    private static bool EnsureDirectoryExists(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            Debug.LogWarning("Cosmos Cove Control Panel: Directory path is null or empty");
            return false;
        }
        
        // Normalize path separators to forward slashes for Unity consistency
        directoryPath = directoryPath.Replace('\\', '/');
        
        // Check if directory exists using both AssetDatabase and physical file system
        bool initialAssetDbCheck = AssetDatabase.IsValidFolder(directoryPath);
        bool initialPhysicalCheck = Directory.Exists(Path.Combine(Application.dataPath, directoryPath.Substring("Assets/".Length)));
        
        if (initialAssetDbCheck || initialPhysicalCheck)
        {
            return true;
        }
        
        // Try using System.IO as a fallback if AssetDatabase is not ready
        string physicalPath = Path.Combine(Application.dataPath, directoryPath.Substring("Assets/".Length));
        if (!Directory.Exists(physicalPath))
        {
            try
            {
                Directory.CreateDirectory(physicalPath);
                AssetDatabase.Refresh();
                
                // Give Unity a moment to process the refresh
                System.Threading.Thread.Sleep(100);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Cosmos Cove Control Panel: Failed to create directory using System.IO: {e.Message}");
            }
        }
        
        // Now try the AssetDatabase approach (check again after System.IO creation and refresh)
        if (AssetDatabase.IsValidFolder(directoryPath) || Directory.Exists(physicalPath))
        {
            return true;
        }
        
        try
        {
            // Create the directory structure recursively
            string[] pathParts = directoryPath.Split('/');
            string currentPath = pathParts[0]; // "Assets"
            
            // Verify we're starting with Assets
            if (currentPath != "Assets")
            {
                Debug.LogWarning($"Cosmos Cove Control Panel: Invalid path structure, must start with 'Assets'. Got: {directoryPath}");
                return false;
            }
            
            // Check if the entire path already exists physically (from System.IO fallback)
            string fullPhysicalPath = Path.Combine(Application.dataPath, directoryPath.Substring("Assets/".Length));
            if (Directory.Exists(fullPhysicalPath))
            {
                Debug.Log($"Cosmos Cove Control Panel: Directory already exists physically, refreshing AssetDatabase: {directoryPath}");
                AssetDatabase.Refresh();
                System.Threading.Thread.Sleep(200);
                
                if (AssetDatabase.IsValidFolder(directoryPath))
                {
                    Debug.Log($"Cosmos Cove Control Panel: Directory now recognized by AssetDatabase: {directoryPath}");
                    return true;
                }
                Debug.LogWarning($"Cosmos Cove Control Panel: Directory exists physically but not recognized by AssetDatabase: {directoryPath}");
                // Continue with AssetDatabase.CreateFolder approach
            }
            
            for (int i = 1; i < pathParts.Length; i++)
            {
                string nextPath = currentPath + "/" + pathParts[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    Debug.Log($"Cosmos Cove Control Panel: Creating folder: {nextPath}");
                    string guid = AssetDatabase.CreateFolder(currentPath, pathParts[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        Debug.LogWarning($"Cosmos Cove Control Panel: Failed to create settings folder: {nextPath}");
                        return false;
                    }
                    Debug.Log($"Cosmos Cove Control Panel: Successfully created folder: {nextPath} (GUID: {guid})");
                    
                    // Immediately verify the creation was successful
                    if (!AssetDatabase.IsValidFolder(nextPath))
                    {
                        Debug.LogWarning($"Cosmos Cove Control Panel: Folder creation reported success but validation failed immediately: {nextPath}");
                        // Don't return false here, continue with the process as the folder might exist physically
                    }
                }
                else
                {
                    Debug.Log($"Cosmos Cove Control Panel: Folder already exists: {nextPath}");
                }
                currentPath = nextPath;
            }
            
            AssetDatabase.Refresh(); // Refresh to ensure Unity recognizes the new folders
            
            // Give Unity a moment to process the refresh and update the AssetDatabase
            System.Threading.Thread.Sleep(200);
            
            // Double-check that the directory now exists using multiple validation methods
            bool assetDbValid = AssetDatabase.IsValidFolder(directoryPath);
            bool physicalExists = Directory.Exists(Path.Combine(Application.dataPath, directoryPath.Substring("Assets/".Length)));
            
            if (assetDbValid || physicalExists)
            {
                Debug.Log($"Cosmos Cove Control Panel: Directory structure successfully created: {directoryPath} (AssetDB: {assetDbValid}, Physical: {physicalExists})");
                return true;
            }
            else
            {
                Debug.LogWarning($"Cosmos Cove Control Panel: Directory still doesn't exist after creation attempt: {directoryPath} (AssetDB: {assetDbValid}, Physical: {physicalExists})");
                return false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Cosmos Cove Control Panel: Error creating directory structure: {e.Message}");
            return false;
        }
    }
    
    public static CCCPSettings GetOrCreateSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<CCCPSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<CCCPSettings>();
            
            // Ensure the directory structure exists for the settings file
            string directoryPath = Path.GetDirectoryName(SettingsPath);
            // Normalize path separators to forward slashes for Unity consistency
            directoryPath = directoryPath.Replace('\\', '/');
            
            if (!EnsureDirectoryExists(directoryPath))
            {
                Debug.LogError($"Cosmos Cove Control Panel: Failed to create directory structure for settings at: {directoryPath}.");
                Debug.LogError("Cosmos Cove Control Panel: This may be due to Unity's AssetDatabase not being ready during initialization.");
                Debug.LogError("Cosmos Cove Control Panel: Please manually create the directory in your Project window, or change the settings path in Project Settings > Cosmos Cove Control Panel.");
                Debug.LogError($"Cosmos Cove Control Panel: Expected directory: {directoryPath}");
                return null;
            }
            
            try
            {
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Cosmos Cove Control Panel: Error creating settings asset at {SettingsPath}: {e.Message}. Please try to set it manually in the Project Settings.");
                return null;
            }
            
            // Check if migration is needed for new settings
            if (HasEditorPrefsSettings())
            {
                MigrateFromEditorPrefs(settings);
                EditorUtility.SetDirty(settings); // Mark as dirty after migration
            }
            
            AssetDatabase.SaveAssets();
        }
        else
        {
            // Check if migration is needed for existing settings that haven't been migrated
            if (!settings.migratedFromEditorPrefs && HasEditorPrefsSettings())
            {
                MigrateFromEditorPrefs(settings);
                EditorUtility.SetDirty(settings); // Mark as dirty after migration
                AssetDatabase.SaveAssets();
            }
        }
        
        return settings;
    }
    
    /// <summary>
    /// Move settings file from one location to another
    /// </summary>
    private static void MoveSettingsFile(string oldPath, string newPath)
    {
        try
        {
            // Ensure the target directory exists
            string targetDirectory = Path.GetDirectoryName(newPath);
            if (!EnsureDirectoryExists(targetDirectory))
            {
                EditorUtility.DisplayDialog("Folder Creation Failed", 
                    $"Failed to create target directory: {targetDirectory}", "OK");
                return;
            }
            
            // Move the asset
            string moveResult = AssetDatabase.MoveAsset(oldPath, newPath);
            if (!string.IsNullOrEmpty(moveResult))
            {
                Debug.LogWarning($"Cosmos Cove Control Panel: Failed to move settings file: {moveResult}. Please try to set it manually in the Project Settings.");
                EditorUtility.DisplayDialog("Move Failed", 
                    $"Failed to move settings file:\n{moveResult}", "OK");
            }
            else
            {
                Debug.Log($"Cosmos Cove Control Panel: Settings file moved from {oldPath} to {newPath}");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Cosmos Cove Control Panel: Exception while moving settings file: {e.Message}. Please try to set it manually in the Project Settings.");
            EditorUtility.DisplayDialog("Move Failed", 
                $"Exception while moving settings file:\n{e.Message}", "OK");
        }
    }
    
    /// <summary>
    /// Check if the user has existing EditorPrefs settings
    /// </summary>
    private static bool HasEditorPrefsSettings()
    {
        // Migration support for users upgrading from GitHub version
        // Safe to leave in - only runs if old EditorPrefs keys exist
        return EditorPrefs.HasKey(PrefsKeyPrefix + "UserName") ||
                EditorPrefs.HasKey(PrefsKeyPrefix + "ServerURL") ||
                EditorPrefs.HasKey(PrefsKeyPrefix + "ServerMode") ||
                EditorPrefs.HasKey(PrefsKeyPrefix + "ModuleName");
    }
    
    /// <summary>
    /// Migration support for users upgrading from GitHub version
    /// Safe to leave in - only runs if old EditorPrefs keys exist
    /// </summary>
    private static void MigrateFromEditorPrefs(CCCPSettings settings)
    {
        Debug.Log("Cosmos Cove Control Panel: Migrating settings from EditorPrefs to Settings Provider...");
        
        // Server Configuration
        if (EditorPrefs.HasKey(PrefsKeyPrefix + "ServerMode"))
        {
            string modeName = EditorPrefs.GetString(PrefsKeyPrefix + "ServerMode", "WslServer");
            if (System.Enum.TryParse<NorthernRogue.CCCP.Editor.ServerManager.ServerMode>(modeName, out var mode))
            {
                settings.serverMode = mode;
            }
        }
        
        settings.userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", settings.userName);
        settings.serverUrl = EditorPrefs.GetString(PrefsKeyPrefix + "ServerURL", settings.serverUrl);
        settings.serverPort = EditorPrefs.GetInt(PrefsKeyPrefix + "ServerPort", settings.serverPort);
        settings.authToken = EditorPrefs.GetString(PrefsKeyPrefix + "AuthToken", settings.authToken);
        
        // Directory Settings
        settings.backupDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "BackupDirectory", settings.backupDirectory);
        settings.serverDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "ServerDirectory", settings.serverDirectory);
        settings.clientDirectory = EditorPrefs.GetString(PrefsKeyPrefix + "ClientDirectory", settings.clientDirectory);
        
        // Language Settings
        settings.serverLang = EditorPrefs.GetString(PrefsKeyPrefix + "ServerLang", settings.serverLang);
        settings.unityLang = EditorPrefs.GetString(PrefsKeyPrefix + "UnityLang", settings.unityLang);
        
        // Module Configuration
        settings.moduleName = EditorPrefs.GetString(PrefsKeyPrefix + "ModuleName", settings.moduleName);
        settings.selectedModuleIndex = EditorPrefs.GetInt(PrefsKeyPrefix + "SelectedModuleIndex", settings.selectedModuleIndex);
        
        // Parse saved modules if exists
        string savedModulesJson = EditorPrefs.GetString(PrefsKeyPrefix + "SavedModules", "");
        if (!string.IsNullOrEmpty(savedModulesJson))
        {
            try
            {
                // First try the old format (SerializableList with "items" property)
                var oldFormatWrapper = JsonUtility.FromJson<SerializableListWrapper>(savedModulesJson);
                if (oldFormatWrapper != null && oldFormatWrapper.items != null)
                {
                    settings.savedModules = oldFormatWrapper.items.ToList();
                    Debug.Log($"Cosmos Cove Control Panel: Migrated {settings.savedModules.Count} modules from EditorPrefs (old format)");
                }
                else
                {
                    // Try the new format (ModuleListWrapper with "modules" property)
                    var moduleWrapper = JsonUtility.FromJson<ModuleListWrapper>(savedModulesJson);
                    if (moduleWrapper != null && moduleWrapper.modules != null)
                    {
                        settings.savedModules = moduleWrapper.modules.ToList();
                        Debug.Log($"Cosmos Cove Control Panel: Migrated {settings.savedModules.Count} modules from EditorPrefs (new format)");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Cosmos Cove Control Panel: Failed to migrate saved modules: {e.Message}");
            }
        }
        
        // Maincloud Configuration
        settings.maincloudUrl = EditorPrefs.GetString(PrefsKeyPrefix + "MaincloudURL", settings.maincloudUrl);
        settings.maincloudAuthToken = EditorPrefs.GetString(PrefsKeyPrefix + "MaincloudAuthToken", settings.maincloudAuthToken);
        
        // Custom Server Configuration
        settings.sshUserName = EditorPrefs.GetString(PrefsKeyPrefix + "SSHUserName", settings.sshUserName);
        settings.sshPrivateKeyPath = EditorPrefs.GetString(PrefsKeyPrefix + "SSHPrivateKeyPath", settings.sshPrivateKeyPath);
        settings.customServerUrl = EditorPrefs.GetString(PrefsKeyPrefix + "CustomServerURL", settings.customServerUrl);
        settings.customServerPort = EditorPrefs.GetInt(PrefsKeyPrefix + "CustomServerPort", settings.customServerPort);
        settings.customServerAuthToken = EditorPrefs.GetString(PrefsKeyPrefix + "CustomServerAuthToken", settings.customServerAuthToken);
        
        // Prerequisites Status
        settings.hasWSL = EditorPrefs.GetBool(PrefsKeyPrefix + "HasWSL", settings.hasWSL);
        settings.hasDebian = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebian", settings.hasDebian);
        settings.hasDebianTrixie = EditorPrefs.GetBool(PrefsKeyPrefix + "HasDebianTrixie", settings.hasDebianTrixie);
        settings.hasCurl = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCurl", settings.hasCurl);
        settings.hasSpacetimeDBServer = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBServer", settings.hasSpacetimeDBServer);
        settings.hasSpacetimeDBPath = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBPath", settings.hasSpacetimeDBPath);
        settings.hasSpacetimeDBService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBService", settings.hasSpacetimeDBService);
        settings.hasSpacetimeDBLogsService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBLogsService", settings.hasSpacetimeDBLogsService);
        settings.hasRust = EditorPrefs.GetBool(PrefsKeyPrefix + "HasRust", settings.hasRust);
        settings.hasNETSDK = EditorPrefs.GetBool(PrefsKeyPrefix + "HasNETSDK", settings.hasNETSDK);
        settings.hasBinaryen = EditorPrefs.GetBool(PrefsKeyPrefix + "HasBinaryen", settings.hasBinaryen);
        settings.hasGit = EditorPrefs.GetBool(PrefsKeyPrefix + "HasGit", settings.hasGit);
        settings.hasSpacetimeDBUnitySDK = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBUnitySDK", settings.hasSpacetimeDBUnitySDK);
        
        // Custom Prerequisites Status
        settings.hasCustomDebianUser = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomDebianUser", settings.hasCustomDebianUser);
        settings.hasCustomDebianTrixie = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomDebianTrixie", settings.hasCustomDebianTrixie);
        settings.hasCustomCurl = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomCurl", settings.hasCustomCurl);
        settings.hasCustomSpacetimeDBServer = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBServer", settings.hasCustomSpacetimeDBServer);
        settings.hasCustomSpacetimeDBPath = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBPath", settings.hasCustomSpacetimeDBPath);
        settings.hasCustomSpacetimeDBService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBService", settings.hasCustomSpacetimeDBService);
        settings.hasCustomSpacetimeDBLogsService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasCustomSpacetimeDBLogsService", settings.hasCustomSpacetimeDBLogsService);
        
        // Workflow Settings
        settings.wslPrerequisitesChecked = EditorPrefs.GetBool(PrefsKeyPrefix + "wslPrerequisitesChecked", settings.wslPrerequisitesChecked);
        settings.initializedFirstModule = EditorPrefs.GetBool(PrefsKeyPrefix + "InitializedFirstModule", settings.initializedFirstModule);
        settings.publishFirstModule = EditorPrefs.GetBool(PrefsKeyPrefix + "PublishFirstModule", settings.publishFirstModule);
        settings.hasAllPrerequisites = EditorPrefs.GetBool(PrefsKeyPrefix + "HasAllPrerequisites", settings.hasAllPrerequisites);
        
        // Behavior Settings
        settings.hideWarnings = EditorPrefs.GetBool(PrefsKeyPrefix + "HideWarnings", settings.hideWarnings);
        settings.detectServerChanges = EditorPrefs.GetBool(PrefsKeyPrefix + "DetectServerChanges", settings.detectServerChanges);
        settings.autoPublishMode = EditorPrefs.GetBool(PrefsKeyPrefix + "AutoPublishMode", settings.autoPublishMode);
        settings.publishAndGenerateMode = EditorPrefs.GetBool(PrefsKeyPrefix + "PublishAndGenerateMode", settings.publishAndGenerateMode);
        settings.silentMode = EditorPrefs.GetBool(PrefsKeyPrefix + "SilentMode", settings.silentMode);
        settings.debugMode = EditorPrefs.GetBool(PrefsKeyPrefix + "DebugMode", settings.debugMode);
        settings.clearModuleLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearModuleLogAtStart", settings.clearModuleLogAtStart);
        settings.clearDatabaseLogAtStart = EditorPrefs.GetBool(PrefsKeyPrefix + "ClearDatabaseLogAtStart", settings.clearDatabaseLogAtStart);
        settings.autoCloseWsl = EditorPrefs.GetBool(PrefsKeyPrefix + "AutoCloseWsl", settings.autoCloseWsl);
        settings.echoToConsole = EditorPrefs.GetBool(PrefsKeyPrefix + "EchoToConsole", settings.echoToConsole);
        
        // Version Information
        settings.spacetimeDBCurrentVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersion", settings.spacetimeDBCurrentVersion);
        settings.spacetimeDBCurrentVersionCustom = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersionCustom", settings.spacetimeDBCurrentVersionCustom);
        settings.spacetimeDBCurrentVersionTool = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBVersionTool", settings.spacetimeDBCurrentVersionTool);
        settings.spacetimeDBLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeDBLatestVersion", settings.spacetimeDBLatestVersion);
        settings.spacetimeSDKLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "SpacetimeSDKLatestVersion", settings.spacetimeSDKLatestVersion);
        settings.CCCPAssetStoreLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "CCCPAssetStoreLatestVersion", settings.CCCPAssetStoreLatestVersion);
        settings.rustCurrentVersion = EditorPrefs.GetString(PrefsKeyPrefix + "RustVersion", settings.rustCurrentVersion);
        settings.rustLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "RustLatestVersion", settings.rustLatestVersion);
        settings.rustupVersion = EditorPrefs.GetString(PrefsKeyPrefix + "RustupVersion", settings.rustupVersion);
        settings.rustupUpdateAvailable = EditorPrefs.GetBool(PrefsKeyPrefix + "RustupUpdateAvailable", settings.rustupUpdateAvailable);
        settings.rustUpdateAvailable = EditorPrefs.GetBool(PrefsKeyPrefix + "RustUpdateAvailable", settings.rustUpdateAvailable);
        settings.SpacetimeDBUpdateAvailable = EditorPrefs.GetBool(PrefsKeyPrefix + "SpacetimeDBUpdateAvailable", settings.SpacetimeDBUpdateAvailable);
        settings.spacetimeSDKUpdateAvailable = EditorPrefs.GetBool(PrefsKeyPrefix + "SpacetimeSDKUpdateAvailable", settings.spacetimeSDKUpdateAvailable);
        settings.CCCPGithubUpdateAvailable = EditorPrefs.GetBool(PrefsKeyPrefix + "GithubUpdateAvailable", settings.CCCPGithubUpdateAvailable);
        settings.CCCPAssetStoreUpdateAvailable = EditorPrefs.GetBool(PrefsKeyPrefix + "AssetStoreUpdateAvailable", settings.CCCPAssetStoreUpdateAvailable);
        
        // Distribution and Version Control Information
        settings.distributionType = EditorPrefs.GetString(PrefsKeyPrefix + "DistributionType", settings.distributionType);
        settings.githubLastCommitSha = EditorPrefs.GetString(PrefsKeyPrefix + "GithubLastCommitSha", settings.githubLastCommitSha);
        settings.isAssetStoreVersion = EditorPrefs.GetBool(PrefsKeyPrefix + "IsAssetStoreVersion", settings.isAssetStoreVersion);
        settings.isGitHubVersion = EditorPrefs.GetBool(PrefsKeyPrefix + "IsGitHubVersion", settings.isGitHubVersion);

        // Installer Settings
        settings.WSL1Installed = EditorPrefs.GetBool(PrefsKeyPrefix + "WSL1Installed", settings.WSL1Installed);
        settings.visibleInstallProcesses = EditorPrefs.GetBool(PrefsKeyPrefix + "VisibleInstallProcesses", settings.visibleInstallProcesses);
        settings.keepWindowOpenForDebug = EditorPrefs.GetBool(PrefsKeyPrefix + "KeepWindowOpenForDebug", settings.keepWindowOpenForDebug);
        settings.updateCargoToml = EditorPrefs.GetBool(PrefsKeyPrefix + "UpdateCargoToml", settings.updateCargoToml);
        settings.serviceMode = EditorPrefs.GetBool(PrefsKeyPrefix + "ServiceMode", settings.serviceMode);
        settings.firstTimeOpenInstaller = EditorPrefs.GetBool(PrefsKeyPrefix + "FirstTimeOpen", settings.firstTimeOpenInstaller);
        
        // UI Settings
        settings.autoscroll = EditorPrefs.GetBool(PrefsKeyPrefix + "Autoscroll", settings.autoscroll);
        settings.colorLogo = EditorPrefs.GetBool(PrefsKeyPrefix + "ColorLogo", settings.colorLogo);
        settings.showPrerequisites = EditorPrefs.GetBool(PrefsKeyPrefix + "ShowPrerequisites", settings.showPrerequisites);
        settings.showSettingsWindow = EditorPrefs.GetBool(PrefsKeyPrefix + "ShowSettingsWindow", settings.showSettingsWindow);
        settings.showUtilityCommands = EditorPrefs.GetBool(PrefsKeyPrefix + "ShowUtilityCommands", settings.showUtilityCommands);
        settings.showLocalTime = EditorPrefs.GetBool(PrefsKeyPrefix + "ShowLocalTime", settings.showLocalTime);
        settings.welcomeWindowShown = EditorPrefs.GetBool(PrefsKeyPrefix + "WelcomeWindowShown", settings.welcomeWindowShown);
        
        // Detection Settings
        settings.serverChangesDetected = EditorPrefs.GetBool(PrefsKeyPrefix + "ServerChangesDetected", settings.serverChangesDetected);
        settings.originalFileInfo = EditorPrefs.GetString(PrefsKeyPrefix + "OriginalFileInfo", settings.originalFileInfo);
        settings.currentFileInfo = EditorPrefs.GetString(PrefsKeyPrefix + "CurrentFileInfo", settings.currentFileInfo);
        
        // Data Window Settings
        string columnWidthKey = "CCCP_DataWindow_ColumnWidths";
        settings.columnWidths = EditorPrefs.GetString(columnWidthKey, settings.columnWidths);
        string lastTableKey = "CCCP_DataWindow_LastSelectedTable";
        settings.lastSelectedTable = EditorPrefs.GetString(lastTableKey, settings.lastSelectedTable);
        
        // Log Settings
        settings.logUpdateFrequency = EditorPrefs.GetFloat(PrefsKeyPrefix + "LogUpdateFrequency", settings.logUpdateFrequency);
        
        // Mark as migrated
        settings.migratedFromEditorPrefs = true;
        settings.migrationVersion = "1.0.0";
        
        // Refresh cached instances to ensure they pick up the migrated data
        CCCPSettingsAdapter.RefreshSettingsCache();
        CCCPSettings.RefreshInstance();
        
        Debug.Log("Cosmos Cove Control Panel: Settings migration completed successfully!");
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
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games