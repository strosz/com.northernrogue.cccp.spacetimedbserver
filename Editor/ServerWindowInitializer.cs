using UnityEditor;

// Detects editor states and manages the processes that are dependent on it ///
/////////////// made by Northern Rogue /// Mathias Toivonen ///////////////////

namespace NorthernRogue.CCCP.Editor {

[InitializeOnLoad]
public static class ServerWindowInitializer
{
    public static bool debugMode = false; // Controlled by ServerWindow

    static ServerWindowInitializer()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting += OnEditorQuitting;
        EditorApplication.delayCall += CheckTailProcessAfterReload;
        EditorApplication.wantsToQuit += OnEditorWantsToQuit;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // We want to check *after* the editor has potentially reloaded everything
        // when entering play mode or returning to edit mode.
        if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            // Use delayCall to give the window a chance to be re-created/re-enabled
            EditorApplication.delayCall += CheckTailProcessAfterReload;
        }
    }

    private static void CheckTailProcessAfterReload()
    {
        // Check the persisted state
        bool wasRunningSilently = SessionState.GetBool("ServerWindow_WasRunningSilently", false);
        if (debugMode) UnityEngine.Debug.Log($"[ServerWindowInitializer] Checking tail process after state change/reload. WasRunningSilently={wasRunningSilently}");

        if (wasRunningSilently)
        {
            // Only find existing windows, don't create a new one
            // This is the key change - check if the window is already open
            if (EditorWindow.HasOpenInstances<ServerWindow>())
            {
                ServerWindow[] windows = UnityEngine.Resources.FindObjectsOfTypeAll<ServerWindow>();
                if (windows != null && windows.Length > 0)
                {
                    ServerWindow window = windows[0];
                    if (debugMode) UnityEngine.Debug.Log($"[ServerWindowInitializer] Found ServerWindow instance. Calling AttemptTailRestartAfterReload.");
                    // Call the public method on the potentially reloaded instance
                    window.AttemptTailRestartAfterReload();
                    
                    // Also restart database logs
                    if (debugMode) UnityEngine.Debug.Log($"[ServerWindowInitializer] Calling AttemptDatabaseLogRestartAfterReload.");
                    window.AttemptDatabaseLogRestartAfterReload();
                }
            }
            else
            {
                if (debugMode) UnityEngine.Debug.LogWarning("[ServerWindowInitializer] Server was running silently, but ServerWindow is not currently open. Skipping tail restart.");
                // Don't open the window automatically anymore
            }
        }
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

    // Stop log processes on Debian
    private static void OnEditorQuitting()
    {
        if (debugMode) UnityEngine.Debug.Log("[ServerWindowInitializer] Editor is quitting. Attempting to find ServerWindow to stop tail process.");
        // Check if the server was running silently just before quit
        bool wasRunningSilently = SessionState.GetBool("ServerWindow_WasRunningSilently", false);
        if (wasRunningSilently) 
        {
            // Only find existing windows, don't create a new one
            if (EditorWindow.HasOpenInstances<ServerWindow>())
            {
                ServerWindow[] windows = UnityEngine.Resources.FindObjectsOfTypeAll<ServerWindow>();
                if (windows != null && windows.Length > 0)
                {
                    ServerWindow window = windows[0];
                    if (debugMode) UnityEngine.Debug.Log("[ServerWindowInitializer] Found ServerWindow instance. Calling StopTailProcessExplicitly.");
                    window.StopTailProcessExplicitly();
                }
            }
            else
            {
                if (debugMode) UnityEngine.Debug.LogWarning("[ServerWindowInitializer] ServerWindow instance not found during quit.");
            }
        }
    }
} // Class
} // Namespace