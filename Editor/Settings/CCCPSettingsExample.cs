using UnityEngine;
using UnityEditor;
using NorthernRogue.CCCP.Editor.Settings;

namespace NorthernRogue.CCCP.Editor.Examples
{
    /// <summary>
    /// Example demonstrating how to use the new CCCP Settings system
    /// This replaces the old EditorPrefs-based approach with a modern Settings Provider
    /// </summary>
    public static class CCCPSettingsExample
    {
        [MenuItem("CCCP/Examples/Settings Example")]
        public static void ShowSettingsExample()
        {
            Debug.Log("=== CCCP Settings System Example ===");
            
            // Method 1: Direct access (recommended for new code)
            DirectAccessExample();
            
            // Method 2: Adapter access (for legacy compatibility)
            AdapterAccessExample();
            
            // Method 3: Migration example
            MigrationExample();
            
            Debug.Log("=== Example Complete ===");
        }
        
        /// <summary>
        /// Demonstrates direct access to settings (recommended approach)
        /// </summary>
        private static void DirectAccessExample()
        {
            Debug.Log("\n--- Direct Access Example ---");
            
            // Get the settings instance (automatically handles migration if needed)
            var settings = CCCPSettings.Instance;
            
            // Read settings
            Debug.Log($"Current user name: {settings.userName}");
            Debug.Log($"Current server mode: {settings.serverMode}");
            Debug.Log($"Debug mode enabled: {settings.debugMode}");
            
            // Modify settings (automatically saves to ScriptableObject)
            string originalUser = settings.userName;
            settings.userName = "ExampleUser";
            settings.debugMode = true;
            settings.serverUrl = "http://example.com:3000/";
            
            Debug.Log($"Updated user name to: {settings.userName}");
            
            // Settings are automatically saved when changed
            // The ScriptableObject is marked dirty and saved to disk
            
            // Restore original value
            settings.userName = originalUser;
            
            Debug.Log("Direct access example completed");
        }
        
        /// <summary>
        /// Demonstrates adapter access (for backward compatibility)
        /// </summary>
        private static void AdapterAccessExample()
        {
            Debug.Log("\n--- Adapter Access Example ---");
            
            // This approach maintains compatibility with existing code
            // that used EditorPrefs-style get/set methods
            
            // Read settings using adapter
            string userName = CCCPSettingsAdapter.GetUserName();
            bool debugMode = CCCPSettingsAdapter.GetDebugMode();
            int serverPort = CCCPSettingsAdapter.GetServerPort();
            
            Debug.Log($"Via adapter - User: {userName}, Debug: {debugMode}, Port: {serverPort}");
            
            // Modify settings using adapter
            string originalUser = userName;
            CCCPSettingsAdapter.SetUserName("AdapterUser");
            CCCPSettingsAdapter.SetDebugMode(false);
            CCCPSettingsAdapter.SetServerPort(3001);
            
            Debug.Log("Settings updated via adapter");
            
            // Read back the changes
            Debug.Log($"Updated via adapter - User: {CCCPSettingsAdapter.GetUserName()}");
            Debug.Log($"Updated via adapter - Port: {CCCPSettingsAdapter.GetServerPort()}");
            
            // Restore original value
            CCCPSettingsAdapter.SetUserName(originalUser);
            
            Debug.Log("Adapter access example completed");
        }
        
        /// <summary>
        /// Demonstrates migration functionality
        /// </summary>
        private static void MigrationExample()
        {
            Debug.Log("\n--- Migration Example ---");
            
            // Check if user has old EditorPrefs settings
            bool hasOldSettings = HasAnyOldEditorPrefs();
            Debug.Log($"Old EditorPrefs detected: {hasOldSettings}");
            
            if (hasOldSettings)
            {
                Debug.Log("Migration would be offered to user in Settings Provider UI");
                Debug.Log("Settings can be migrated via Project Settings > CCCP SpacetimeDB");
            }
            else
            {
                Debug.Log("No old settings found - using clean settings system");
            }
            
            // Show current migration status
            var settings = CCCPSettings.Instance;
            Debug.Log($"Migration completed: {settings.migratedFromEditorPrefs}");
            Debug.Log($"Migration version: {settings.migrationVersion}");
            
            Debug.Log("Migration example completed");
        }
        
        /// <summary>
        /// Check if user has any old EditorPrefs settings
        /// Migration support for users upgrading from GitHub version
        /// Safe to leave in - only runs if old EditorPrefs keys exist
        /// </summary>
        private static bool HasAnyOldEditorPrefs()
        {
            const string PrefsKeyPrefix = "CCCP_";
            
            return EditorPrefs.HasKey(PrefsKeyPrefix + "UserName") ||
                   EditorPrefs.HasKey(PrefsKeyPrefix + "ServerURL") ||
                   EditorPrefs.HasKey(PrefsKeyPrefix + "ServerMode") ||
                   EditorPrefs.HasKey(PrefsKeyPrefix + "ModuleName") ||
                   EditorPrefs.HasKey(PrefsKeyPrefix + "DebugMode");
        }
        
        [MenuItem("CCCP/Examples/Reset Settings to Defaults")]
        public static void ResetSettingsExample()
        {
            Debug.Log("=== Reset Settings Example ===");
            
            if (EditorUtility.DisplayDialog("Reset CCCP Settings", 
                "This will reset all CCCP settings to their default values. Are you sure?", 
                "Reset", "Cancel"))
            {
                var settings = CCCPSettings.Instance;
                settings.ResetToDefaults();
                
                // Mark the asset as dirty and save
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                
                Debug.Log("Settings have been reset to defaults");
            }
            else
            {
                Debug.Log("Reset cancelled by user");
            }
        }
        
        [MenuItem("CCCP/Examples/Show Settings Info")]
        public static void ShowSettingsInfo()
        {
            Debug.Log("=== CCCP Settings Information ===");
            
            var settings = CCCPSettings.Instance;
            
            // Show key settings
            Debug.Log($"Settings file location: Assets/Editor/Resources/CCCPSettings.asset");
            Debug.Log($"Migration status: {(settings.migratedFromEditorPrefs ? "Completed" : "Not needed")}");
            Debug.Log($"Current server mode: {settings.serverMode}");
            Debug.Log($"User name: {(string.IsNullOrEmpty(settings.userName) ? "Not set" : settings.userName)}");
            Debug.Log($"Server URL: {settings.serverUrl}");
            Debug.Log($"Debug mode: {settings.debugMode}");
            Debug.Log($"Total saved modules: {settings.savedModules.Count}");
            
            // Show how to access settings UI
            Debug.Log("\nTo edit settings:");
            Debug.Log("1. Go to Edit > Project Settings");
            Debug.Log("2. Look for 'CCCP SpacetimeDB' in the left panel");
            Debug.Log("3. Or use Window > SpacetimeDB > 1. Main Window for the main interface");
            
            Debug.Log("=== Settings Information Complete ===");
        }
    }
}
