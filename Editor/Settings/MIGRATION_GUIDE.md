# CCCP Settings Migration Guide

## Overview

This document explains the migration from EditorPrefs to Unity's Settings Provider system for the CCCP SpacetimeDB package. The new system provides a professional, centralized way to manage settings with automatic migration support.

## What's New

### 1. Professional Settings UI
- Settings are now available in Unity's Project Settings under "CCCP SpacetimeDB"
- Organized into logical sections with proper labels
- Advanced settings are collapsed by default for a cleaner interface
- Migration notification appears if old EditorPrefs are detected

### 2. ScriptableObject-Based Storage
- Settings are stored in `Assets/Editor/Resources/CCCPSettings.asset`
- This allows for version control and team sharing
- More reliable than EditorPrefs which can be lost

### 3. Automatic Migration
- When the new system is first used, it automatically detects existing EditorPrefs
- Migrates all settings seamlessly with this comment:
  ```csharp
  // Migration support for users upgrading from GitHub version
  // Safe to leave in - only runs if old EditorPrefs keys exist
  ```
- Shows clear migration notifications to users

## New Architecture

### Core Files Created

1. **CCCPSettings.cs** - The main ScriptableObject containing all settings
2. **CCCPSettingsProvider.cs** - Unity Settings Provider for the Project Settings UI
3. **CCCPSettingsAdapter.cs** - Compatibility layer for existing code

### Key Features

- **Professional UI**: Organized settings in Unity's Project Settings
- **Automatic Migration**: Seamlessly migrates from EditorPrefs 
- **Backwards Compatibility**: Existing code continues to work
- **Reset Functionality**: Easy reset to defaults button
- **Advanced Settings**: Collapsible sections for better UX

## How to Use the New System

### Accessing Settings in Code

```csharp
using NorthernRogue.CCCP.Editor.Settings;

// Direct access (recommended for new code)
var settings = CCCPSettings.Instance;
string userName = settings.userName;
settings.userName = "newUser";

// Adapter access (for legacy compatibility)
string userName = CCCPSettingsAdapter.GetUserName();
CCCPSettingsAdapter.SetUserName("newUser");
```

### Settings Categories

The settings are organized into these categories:

- **Server Configuration**: Basic server settings (mode, URL, port, auth)
- **Directory Settings**: Paths for backup, server, and client directories
- **Language Settings**: Server and Unity language preferences
- **Module Configuration**: Module name and saved modules list
- **Behavior Settings**: Workflow and automation preferences
- **UI Settings**: Interface preferences like autoscroll
- **Advanced Settings**: Less commonly used settings (collapsed by default)

## Migration Process

### Automatic Detection
The system automatically checks for existing EditorPrefs on first run:

```csharp
private static bool HasEditorPrefsSettings()
{
    // Migration support for users upgrading from GitHub version
    // Safe to leave in - only runs if old EditorPrefs keys exist
    return EditorPrefs.HasKey(PrefsKeyPrefix + "UserName") ||
           EditorPrefs.HasKey(PrefsKeyPrefix + "ServerURL") ||
           EditorPrefs.HasKey(PrefsKeyPrefix + "ServerMode") ||
           EditorPrefs.HasKey(PrefsKeyPrefix + "ModuleName");
}
```

### Migration UI
When migration is needed, users see:
- Info box explaining migration is available
- "Migrate Settings" button to perform the migration
- Confirmation when migration is complete

### What Gets Migrated
All existing settings are migrated, including:
- Server configuration (URLs, ports, auth tokens)
- Directory paths
- Language preferences
- Module information
- Workflow settings
- Prerequisites status
- Version information

## Code Migration Examples

### Before (EditorPrefs)
```csharp
// Old way
string userName = EditorPrefs.GetString("CCCP_UserName", "");
EditorPrefs.SetString("CCCP_UserName", "newUser");

bool debugMode = EditorPrefs.GetBool("CCCP_DebugMode", false);
EditorPrefs.SetBool("CCCP_DebugMode", true);
```

### After (Settings Provider)
```csharp
// New way - Direct access
var settings = CCCPSettings.Instance;
string userName = settings.userName;
settings.userName = "newUser";

bool debugMode = settings.debugMode;
settings.debugMode = true;

// New way - Adapter (for compatibility)
string userName = CCCPSettingsAdapter.GetUserName();
CCCPSettingsAdapter.SetUserName("newUser");

bool debugMode = CCCPSettingsAdapter.GetDebugMode();
CCCPSettingsAdapter.SetDebugMode(true);
```

## Benefits

### For Users
- Professional settings interface in Project Settings
- Organized, searchable settings
- Settings persist with project (can be committed to version control)
- Easy reset to defaults
- Automatic migration from old settings

### For Developers
- Type-safe settings access
- Automatic serialization
- No more string-based keys
- Centralized settings management
- Easy to extend with new settings

## File Structure

```
Packages/com.northernrogue.cccp.spacetimedbserver/
├── Editor/
│   ├── Settings/
│   │   ├── CCCPSettings.cs              # Main settings ScriptableObject
│   │   ├── CCCPSettingsProvider.cs      # Unity Settings Provider
│   │   └── CCCPSettingsAdapter.cs       # Compatibility adapter
│   └── ServerWindow.cs                  # Updated to use new system (partial)
└── Assets/Editor/Resources/             # Created automatically
    └── CCCPSettings.asset               # Settings storage file
```

## Implementation Status

### ✅ Completed
- Core settings system architecture
- Settings Provider UI
- Automatic migration system
- ServerManager integration
- Backwards compatibility adapter

### 🔄 Partially Completed
- ServerWindow integration (started, needs completion)

### ⏳ Remaining Work
- Complete ServerWindow migration
- Update other editor windows
- Update installer windows
- Remove old EditorPrefs calls entirely (optional)

## Best Practices

1. **Use Direct Access**: For new code, use `CCCPSettings.Instance` directly
2. **Save After Changes**: Settings are automatically saved when changed
3. **Check Migration**: Let users know about migration when detected
4. **Organize Settings**: Keep related settings grouped together
5. **Provide Defaults**: Always have sensible default values

## Troubleshooting

### Settings Not Persisting
Make sure you're using the properties correctly:
```csharp
// ✅ Correct - automatically saves
settings.userName = "newValue";

// ❌ Incorrect - doesn't save
settings.userName = "newValue";
// Need to call CCCPSettingsAdapter.SaveSettings() or use adapter methods
```

### Migration Not Working
Check that the migration detection is working:
```csharp
// This should return true if old settings exist
bool hasOldSettings = EditorPrefs.HasKey("CCCP_UserName");
```

### Settings Reset
If settings are lost, check:
1. Is the CCCPSettings.asset file present?
2. Is it in the correct location (`Assets/Editor/Resources/`)?
3. Has it been excluded from version control accidentally?

## Future Enhancements

- Settings validation
- Import/export functionality
- Settings profiles for different environments
- Real-time settings sync across team members
- Settings change notifications

---

*This migration maintains full backwards compatibility while providing a modern, professional settings experience.*
