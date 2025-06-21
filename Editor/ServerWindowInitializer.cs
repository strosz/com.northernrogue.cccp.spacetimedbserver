using UnityEditor;

// Manages editor states /// Can be moved to ServerWindow?

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
        bool autoCloseWsl = EditorPrefs.GetBool("ServerWindow_AutoCloseWsl", true);
        if (autoCloseWsl)
        {
            if (debugMode) UnityEngine.Debug.Log("[ServerWindowInitializer] AutoCloseWsl is enabled. Attempting to close WSL.");
            ServerCMDProcess cmdProcessor = new ServerCMDProcess((msg, level) => {
                if (debugMode) UnityEngine.Debug.Log($"[ServerWindowInitializer] {msg}");
            }, debugMode);
            cmdProcessor.ShutdownWsl();
        }
        return true;
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games