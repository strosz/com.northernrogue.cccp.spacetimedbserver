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
    
    [MenuItem("Window/SpacetimeDB/~ Welcome Window ~")]
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

        string welcomeText = 
        "This is a Windows WSL integration of SpacetimeDB within Unity.\n" +
        "SpacetimeDB has been added to your editor's menu at\n <b>Window>SpacetimeDB</b>.\n\n" +
        "<size=125%><b>Quick Start</b></size>\n" +
        "1. Open the Main Window.\n Enter the essential Pre-Requisites.\n <size=75%><color=grey>Init New Module after step 2 is done.\n Auth token can be entered after all steps are done.</color></size>\n\n" +
        "2. Open the Installer Window.\n Check and install all essential software.\n\n" +
        "3. You can now publish and start the server! \n\n (For a detailed Quick Start check the documentation)";

        EditorGUILayout.LabelField(welcomeText, textStyle);
        
        EditorGUILayout.Space(20);
        
        // Buttons
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Main Window", GUILayout.Height(30), GUILayout.Width(200)))
        {
            ServerWindow.ShowWindow();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Installer Window", GUILayout.Height(30), GUILayout.Width(200)))
        {
            ServerInstallerWindow.ShowWindow();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Documentation", GUILayout.Height(30), GUILayout.Width(200)))
        {
            Application.OpenURL(ServerWindow.Documentation);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

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