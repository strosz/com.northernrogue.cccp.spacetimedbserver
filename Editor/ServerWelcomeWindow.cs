using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using NorthernRogue.CCCP.Editor.Settings;

// Displays the welcome window with information about the asset and how to get started ///

namespace NorthernRogue.CCCP.Editor {
    
public class ServerWelcomeWindow : EditorWindow
{
    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        // Check if the welcome window has been shown before
        bool hasShown = CCCPSettingsAdapter.GetWelcomeWindowShown();
        
        if (!hasShown)
        {
            // Schedule opening the window after Unity is fully initialized
            EditorApplication.delayCall += () =>
            {
                ShowWindow();
                // Mark the window as shown
                CCCPSettingsAdapter.SetWelcomeWindowShown(true);
            };
        }
    }
    
    [MenuItem("Window/SpacetimeDB Server Manager/~ Welcome Window ~")]
    public static void ShowWindow()
    {
        ServerWelcomeWindow window = GetWindow<ServerWelcomeWindow>("Welcome to Cosmos Cove");
        window.minSize = new Vector2(450f, 570f);
        window.maxSize = new Vector2(450f, 570f);
    }
    
    private void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        
        // Load and display the logo image
        Texture2D logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.northernrogue.cccp.spacetimedbserver/Editor/cosmos_logo.png");
        if (logoTexture != null)
        {
            float maxHeight = 70f;
            float aspectRatio = (float)logoTexture.width / logoTexture.height;
            float width = maxHeight * aspectRatio;
            float height = maxHeight;
            
            // Center the image
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(logoTexture, GUILayout.Width(width), GUILayout.Height(height));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
        }
        else
        {
            // Fallback if image not found
            GUIStyle titleStyle = new GUIStyle(EditorStyles.whiteLargeLabel);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontSize = 18;
            GUILayout.Label("Cosmos Cove Control Panel", titleStyle);
            EditorGUILayout.Space(10);
        }
        
        // Welcome text
        GUIStyle textStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        textStyle.alignment = TextAnchor.MiddleCenter;
        textStyle.fontSize = 12;
        textStyle.richText = true;

        string welcomeMainWindow = 
        "Welcome to Cosmos Cove Control Panel,\n a SpacetimeDB server manager within Unity.\n\n" +
        "You can find a new menu at\n <b>Window>SpacetimeDB Server Manager</b>.\n\n" +
        "<size=125%><b>Quick Start</b></size>\n" +
        "1. Open the Main Window.\n <size=90%><color=grey>Enter the essential Shared Settings\n in Pre-Requisites.</color></size>\n\n";
        string welcomeSetupWindow =
        "2. Open the Setup Window.\n <size=90%><color=grey>Check and setup the essential Software\n for either a Docker or WSL Local CLI.</color></size>\n\n";
        string welcomeStartServer =
        "3. You can now publish and start the server!\n <size=90%><color=grey>Enter the Auth Token from Commands\n for full access to all features.</color></size>\n\n For a detailed Quick Start check the documentation";

        // Text Main Window
        EditorGUILayout.LabelField(welcomeMainWindow, textStyle);
        
        EditorGUILayout.Space(-30);
        
        // Button Main Window
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Main Window", GUILayout.Height(30), GUILayout.Width(200)))
        {
            ServerWindow.ShowWindow();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // Text Setup Window
        EditorGUILayout.LabelField(welcomeSetupWindow, textStyle);
        EditorGUILayout.Space(-30);
        
        // Button Setup Window
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Setup Window", GUILayout.Height(30), GUILayout.Width(200)))
        {
            ServerSetupWindow.ShowWindow();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // Text Start Server
        EditorGUILayout.LabelField(welcomeStartServer, textStyle);

        // Button Documentation
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Documentation", GUILayout.Height(30), GUILayout.Width(200)))
        {
            Application.OpenURL(ServerWindow.Documentation);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        EditorGUILayout.Space(-5);

        // Welcome message depending on version (Github Dev version if both versions are true)
        if (ServerUpdateProcess.IsGithubVersion() && ServerUpdateProcess.IsAssetStoreVersion())
        {
            // Display GitHub welcome window if this is a GitHub version
            Type githubClass = Type.GetType("NorthernRogue.CCCP.Editor.Github");
            if (githubClass != null)
            {
                MethodInfo methodInfo = githubClass.GetMethod("WelcomeWindow");
                if (methodInfo != null)
                {
                    EditorGUILayout.BeginVertical();
                    methodInfo.Invoke(null, null);
                    EditorGUILayout.EndVertical();
                }
            }
        }
        // Asset store version if not GitHub version
        if (!ServerUpdateProcess.IsGithubVersion() && ServerUpdateProcess.IsAssetStoreVersion())
        {
            // Display Asset Store welcome window if this is an Asset Store version
            Type assetStoreClass = Type.GetType("NorthernRogue.CCCP.Editor.AssetStore");
            if (assetStoreClass != null)
            {
                MethodInfo methodInfo = assetStoreClass.GetMethod("WelcomeWindow");
                if (methodInfo != null)
                {
                    EditorGUILayout.BeginVertical();
                    methodInfo.Invoke(null, null);
                    EditorGUILayout.EndVertical();
                }
            }
        }

        EditorGUILayout.EndVertical();
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games