using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

namespace NorthernRogue.CCCP.Editor.Settings
{
    /// <summary>
    /// Settings Provider for CCCP settings in Unity's Project Settings
    /// </summary>
    public static class CCCPSettingsProvider
    {
        public const string SettingsPath = "Assets/Editor/Resources/CCCPSettings.asset";
        public const string ResourcesPath = "Assets/Editor/Resources";
        private const string PrefsKeyPrefix = "CCCP_";
        
        [SettingsProvider]
        public static SettingsProvider CreateCCCPSettingsProvider()
        {
            var provider = new SettingsProvider("Project/CCCP SpacetimeDB", SettingsScope.Project)
            {
                label = "CCCP SpacetimeDB",
                guiHandler = (searchContext) =>
                {
                    var settings = GetOrCreateSettings();
                    
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("SpacetimeDB Control Panel Settings", EditorStyles.boldLabel);
                    EditorGUILayout.Space();
                    
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
                        
                        // Server Configuration
                        EditorGUILayout.LabelField("Server Configuration", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverMode"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("userName"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverUrl"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverPort"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("authToken"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                        
                        // Directory Settings
                        EditorGUILayout.LabelField("Directory Settings", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("backupDirectory"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverDirectory"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("clientDirectory"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                        
                        // Language Settings
                        EditorGUILayout.LabelField("Language Settings", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("serverLang"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("unityLang"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                        
                        // Module Configuration
                        EditorGUILayout.LabelField("Module Configuration", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("moduleName"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("selectedModuleIndex"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("savedModules"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                        
                        // Behavior Settings
                        EditorGUILayout.LabelField("Behavior Settings", EditorStyles.boldLabel);
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
                        
                        // UI Settings
                        EditorGUILayout.LabelField("UI Settings", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoscroll"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("colorLogo"));
                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space();
                        
                        // Advanced Settings (collapsed by default)
                        var showAdvanced = EditorGUILayout.Foldout(
                            EditorPrefs.GetBool("CCCP_ShowAdvancedSettings", false), 
                            "Advanced Settings"
                        );
                        EditorPrefs.SetBool("CCCP_ShowAdvancedSettings", showAdvanced);
                        
                        if (showAdvanced)
                        {
                            EditorGUI.indentLevel++;
                            
                            // Maincloud Configuration
                            EditorGUILayout.LabelField("Maincloud Configuration", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("maincloudUrl"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("maincloudAuthToken"));
                            EditorGUI.indentLevel--;
                            EditorGUILayout.Space();
                            
                            // Custom Server Configuration
                            EditorGUILayout.LabelField("Custom Server Configuration", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sshUserName"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("sshPrivateKeyPath"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("customServerUrl"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("customServerPort"));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("customServerAuthToken"));
                            EditorGUI.indentLevel--;
                            EditorGUILayout.Space();
                            
                            // Prerequisites Status (read-only)
                            EditorGUILayout.LabelField("Prerequisites Status (Read-Only)", EditorStyles.boldLabel);
                            EditorGUI.indentLevel++;
                            EditorGUI.BeginDisabledGroup(true);
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
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("hasSpacetimeDBUnitySDK"));
                            EditorGUI.EndDisabledGroup();
                            EditorGUI.indentLevel--;
                            EditorGUILayout.Space();
                            
                            EditorGUI.indentLevel--;
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
        
        public static CCCPSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CCCPSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<CCCPSettings>();
                
                // Ensure the Resources directory exists
                if (!AssetDatabase.IsValidFolder(ResourcesPath))
                {
                    var editorPath = "Assets/Editor";
                    if (!AssetDatabase.IsValidFolder(editorPath))
                    {
                        AssetDatabase.CreateFolder("Assets", "Editor");
                    }
                    AssetDatabase.CreateFolder(editorPath, "Resources");
                }
                
                AssetDatabase.CreateAsset(settings, SettingsPath);
                
                // Check if migration is needed
                if (HasEditorPrefsSettings())
                {
                    MigrateFromEditorPrefs(settings);
                }
                
                AssetDatabase.SaveAssets();
            }
            
            return settings;
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
        /// Migrate settings from EditorPrefs to the new settings system
        /// </summary>
        private static void MigrateFromEditorPrefs(CCCPSettings settings)
        {
            // Migration support for users upgrading from GitHub version
            // Safe to leave in - only runs if old EditorPrefs keys exist
            
            Debug.Log("CCCP: Migrating settings from EditorPrefs to Settings Provider...");
            
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
                    var moduleWrapper = JsonUtility.FromJson<ModuleListWrapper>(savedModulesJson);
                    if (moduleWrapper != null && moduleWrapper.modules != null)
                    {
                        settings.savedModules = moduleWrapper.modules.ToList();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"CCCP: Failed to migrate saved modules: {e.Message}");
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
            settings.rustCurrentVersion = EditorPrefs.GetString(PrefsKeyPrefix + "RustVersion", settings.rustCurrentVersion);
            settings.rustLatestVersion = EditorPrefs.GetString(PrefsKeyPrefix + "RustLatestVersion", settings.rustLatestVersion);
            settings.rustupVersion = EditorPrefs.GetString(PrefsKeyPrefix + "RustupVersion", settings.rustupVersion);
            settings.rustUpdateAvailable = EditorPrefs.GetBool(PrefsKeyPrefix + "RustUpdateAvailable", settings.rustUpdateAvailable);
            
            // Installer Settings
            settings.WSL1Installed = EditorPrefs.GetBool(PrefsKeyPrefix + "WSL1Installed", settings.WSL1Installed);
            settings.visibleInstallProcesses = EditorPrefs.GetBool(PrefsKeyPrefix + "VisibleInstallProcesses", settings.visibleInstallProcesses);
            settings.keepWindowOpenForDebug = EditorPrefs.GetBool(PrefsKeyPrefix + "KeepWindowOpenForDebug", settings.keepWindowOpenForDebug);
            settings.updateCargoToml = EditorPrefs.GetBool(PrefsKeyPrefix + "UpdateCargoToml", settings.updateCargoToml);
            settings.serviceMode = EditorPrefs.GetBool(PrefsKeyPrefix + "ServiceMode", settings.serviceMode);
            
            // UI Settings
            settings.autoscroll = EditorPrefs.GetBool(PrefsKeyPrefix + "Autoscroll", settings.autoscroll);
            settings.colorLogo = EditorPrefs.GetBool(PrefsKeyPrefix + "ColorLogo", settings.colorLogo);
            settings.showPrerequisites = EditorPrefs.GetBool(PrefsKeyPrefix + "ShowPrerequisites", settings.showPrerequisites);
            settings.showSettingsWindow = EditorPrefs.GetBool(PrefsKeyPrefix + "ShowSettingsWindow", settings.showSettingsWindow);
            settings.showUtilityCommands = EditorPrefs.GetBool(PrefsKeyPrefix + "ShowUtilityCommands", settings.showUtilityCommands);
            
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
            
            Debug.Log("CCCP: Settings migration completed successfully!");
        }
        
        [System.Serializable]
        private class ModuleListWrapper
        {
            public ModuleInfo[] modules;
        }
    }
}
