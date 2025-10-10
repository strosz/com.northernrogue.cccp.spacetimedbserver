using UnityEditor;
using NorthernRogue.CCCP.Editor.Settings;

// Manages editor states

namespace NorthernRogue.CCCP.Editor {

[InitializeOnLoad]
public static class ServerWindowInitializer
{
    public static bool debugMode = false; // Controlled by ServerWindow

    static ServerWindowInitializer()
    {
        EditorApplication.wantsToQuit += OnEditorWantsToQuit;
    }

    // Stop WSL on editor quit
    private static bool OnEditorWantsToQuit()
    {
        bool autoCloseWsl = CCCPSettingsAdapter.GetAutoCloseWsl();
        if (autoCloseWsl)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerWindowInitializer] AutoCloseWsl is enabled. Attempting to close WSL.");
            ServerWSLProcess wslProcess = new ServerWSLProcess((msg, level) => {
                if (debugMode) UnityEngine.Debug.Log($"[ServerWindowInitializer] {msg}");
            }, debugMode);
            wslProcess.ShutdownWsl();
        }
        return true;
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games