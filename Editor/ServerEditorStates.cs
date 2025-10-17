using UnityEditor;
using NorthernRogue.CCCP.Editor.Settings;

// Manages editor states

namespace NorthernRogue.CCCP.Editor {

[InitializeOnLoad]
public static class ServerEditorStates
{
    public static bool debugMode = false; // Controlled by ServerWindow

    static ServerEditorStates()
    {
        EditorApplication.wantsToQuit += AutoCloseCLIOnQuit;
    }

    // Stop WSL or Docker on editor quit
    private static bool AutoCloseCLIOnQuit()
    {
        bool autoCloseCLI = CCCPSettingsAdapter.GetAutoCloseCLI();
        string cliProvider = CCCPSettingsAdapter.GetLocalCLIProvider();
        if (autoCloseCLI)
        {
            if (cliProvider == "Docker")
            {
                if (debugMode) UnityEngine.Debug.Log("[ServerEditorStates] Auto Close CLI is enabled. Attempting to shut down Docker Desktop.");
                ServerDockerProcess dockerProcess = new ServerDockerProcess((msg, level) => {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerEditorStates] {msg}");
                }, debugMode);
                dockerProcess.ShutdownDockerDesktop();
            } 
            else if (cliProvider == "WSL") 
            {
                if (debugMode) UnityEngine.Debug.Log("[ServerEditorStates] Auto Close CLI is enabled. Attempting to close WSL.");
                ServerWSLProcess wslProcess = new ServerWSLProcess((msg, level) => {
                    if (debugMode) UnityEngine.Debug.Log($"[ServerEditorStates] {msg}");
                }, debugMode);
                wslProcess.ShutdownWsl();
            }
        }
        return true;
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games