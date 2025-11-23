using UnityEditor;
using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

// Manages and displays SpacetimeDB identity information for CLI and server databases ///

namespace NorthernRogue.CCCP.Editor {

public class ServerIdentityWindow : EditorWindow
{
    public static bool debugMode = false;
    
    // Use NonSerialized to prevent Unity from trying to serialize the static instance
    [System.NonSerialized]
    private static ServerIdentityWindow currentInstance;
    
    // Unique identifier for each window instance
    [SerializeField] 
    private string instanceId = System.Guid.NewGuid().ToString();
    
    // CLI Identity Data
    private string cliIdentity = "";
    private string cliAuthToken = "";
    private ServerIdentityManager cliServerIdentityManager = null;
    private string cliStatusMessage = "Click Refresh to fetch CLI identity";
    private Color cliStatusColor = Color.grey;
    
    // Server Identity Data
    private string serverIdentity = ""; // The "Associated database identities for" identity (what the CLI is using on server)
    private string[] databaseIdentities = new string[0]; // The actual database identities (will match if server identity matches CLI)
    private string serverStatusMessage = "Click Refresh to fetch server identity";
    private Color serverStatusColor = Color.grey;
    
    // UI State
    private bool isRefreshing = false;
    private Vector2 scrollPositionCli;
    private Vector2 scrollPositionServer;
    private bool hadFocus = true; // Track focus state
    
    // Parent window reference to get settings
    private ServerWindow parentServerWindow;
    private ServerManager serverManager;
    
    // Server configuration
    private string serverMode = "";
    
    // Styles
    private GUIStyle titleStyle;
    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private GUIStyle tokenStyle;
    private bool stylesInitialized = false;

    // --- Window Setup ---
    [MenuItem("Window/SpacetimeDB Server Manager/Identity Manager")]
    public static void ShowWindow()
    {
        // Prevent opening windows during compilation
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (debugMode)
                Debug.LogWarning("[ServerIdentityWindow] Prevented ShowWindow during compilation/updating");
            return;
        }
        
        // Use Unity's proper singleton pattern
        ServerIdentityWindow window = GetWindow<ServerIdentityWindow>();
        window.titleContent = new GUIContent("Identity Manager");
        window.minSize = new Vector2(800, 400);
        window.Show();
        
        // Ensure proper initialization
        if (string.IsNullOrEmpty(window.instanceId))
        {
            window.instanceId = System.Guid.NewGuid().ToString();
        }
        currentInstance = window;
        
        if (debugMode)
            Debug.Log($"[ServerIdentityWindow] ShowWindow completed for instance {window.instanceId}");
    }

    private void OnEnable()
    {
        // Ensure colors are initialized from the centralized ColorManager
        ServerUtilityProvider.ColorManager.EnsureInitialized();
        
        // Set unique instance ID if missing
        if (string.IsNullOrEmpty(instanceId))
        {
            instanceId = System.Guid.NewGuid().ToString();
        }
        
        currentInstance = this;
        
        // Find parent ServerWindow to get settings
        parentServerWindow = EditorWindow.GetWindow<ServerWindow>(null, false);
        
        if (debugMode)
            Debug.Log($"[ServerIdentityWindow] OnEnable called for instance {instanceId}");
        
        // Load initial settings
        LoadSettingsFromParent();
        
        // Try to get ServerManager from parent if not already set
        if (serverManager == null && parentServerWindow != null)
        {
            serverManager = parentServerWindow.GetServerManager();
            if (debugMode && serverManager != null)
                Debug.Log("[ServerIdentityWindow] ServerManager obtained from parent window");
        }

        // Subscribe to focus change events
        EditorApplication.focusChanged += OnEditorFocusChanged;

        RefreshIdentities();
    }

    private void OnDisable()
    {
        // Unsubscribe from focus change events
        EditorApplication.focusChanged -= OnEditorFocusChanged;
        
        if (currentInstance == this)
        {
            currentInstance = null;
        }
        
        if (debugMode)
            Debug.Log($"[ServerIdentityWindow] OnDisable called for instance {instanceId}");
    }

    private void OnDestroy()
    {
        // Ensure unsubscription
        EditorApplication.focusChanged -= OnEditorFocusChanged;
        
        if (currentInstance == this)
        {
            currentInstance = null;
        }
        
        if (debugMode)
            Debug.Log($"[ServerIdentityWindow] OnDestroy called for instance {instanceId}");
    }

    private void OnEditorFocusChanged(bool hasFocus)
    {
        // If Unity just gained focus, refresh identities
        if (hasFocus && !hadFocus)
        {
            if (debugMode)
                Debug.Log("[ServerIdentityWindow] Window regained focus, refreshing identities");
            RefreshIdentities();
        }
        hadFocus = hasFocus;
    }

    /// <summary>
    /// Load settings from parent ServerWindow if available
    /// </summary>
    private void LoadSettingsFromParent()
    {
        if (parentServerWindow != null)
        {
            debugMode = parentServerWindow.debugMode;
            
            if (debugMode)
                Debug.Log("[ServerIdentityWindow] Loaded settings from parent ServerWindow");
        }
        else
        {
            if (debugMode)
                Debug.Log("[ServerIdentityWindow] Parent ServerWindow not found, using default settings");
        }
    }

    /// <summary>
    /// Public method to update settings from ServerWindow
    /// </summary>
    public void UpdateSettings(ServerWindow parent, ServerManager manager, string mode)
    {
        parentServerWindow = parent;
        serverManager = manager;
        serverMode = mode;
        
        if (parent != null)
        {
            debugMode = parent.debugMode;
        }
        
        // Update window title to indicate it was opened from ServerWindow
        titleContent = new GUIContent($"Identity Manager");
        
        if (debugMode)
            Debug.Log($"[ServerIdentityWindow] Settings updated: ServerMode={serverMode}");
        
        // Automatically refresh after settings are updated
        RefreshIdentities();
    }

    private void InitializeStyles()
    {
        if (stylesInitialized) return;
        
        // Title style
        titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = ServerUtilityProvider.ColorManager.ButtonText }
        };
        
        // Box style
        boxStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 10, 10)
        };
        
        // Label style
        labelStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true,
            richText = true
        };
        
        // Token style
        tokenStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true,
            richText = false
        };
        
        stylesInitialized = true;
    }

    private void OnGUI()
    {
        InitializeStyles();
        
        // Draw toolbar
        DrawToolbar();
        
        EditorGUILayout.Space(5);
        
        // Draw CLI Identity and Server Identity sections side by side
        EditorGUILayout.BeginHorizontal();
        
        // Left side - CLI Identity section
        EditorGUILayout.BeginVertical(GUILayout.Width(position.width / 2 - 10));
        DrawCliIdentitySection();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(10);
        
        // Right side - Server Identity section
        EditorGUILayout.BeginVertical(GUILayout.Width(position.width / 2 - 10));
        DrawServerIdentitySection();
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        // Refresh button
        EditorGUI.BeginDisabledGroup(isRefreshing);
        if (GUILayout.Button(isRefreshing ? "Refreshing..." : "Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            RefreshIdentities();
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Login with SSO", EditorStyles.toolbarButton, GUILayout.Width(120)))
        {
            HandleSSOLogin();
        }

        GUILayout.FlexibleSpace();
        
        EditorGUILayout.EndHorizontal();
    }

    private void DrawCliIdentitySection()
    {
        EditorGUILayout.BeginVertical(boxStyle);
        
        // Title
        EditorGUILayout.LabelField("CLI Identity", titleStyle);
        EditorGUILayout.Space(5);
        
        // Status message (always in grey as requested)
        GUIStyle statusStyle = new GUIStyle(EditorStyles.miniLabel);
        statusStyle.normal.textColor = Color.grey;
        statusStyle.fontStyle = FontStyle.Italic;
        EditorGUILayout.LabelField(cliStatusMessage, statusStyle);
        
        // Identity type indicator (bold text below status)
        if (cliServerIdentityManager != null && cliServerIdentityManager.Type != IdentityType.Unknown)
        {
            GUIStyle typeStyle = new GUIStyle(EditorStyles.boldLabel);
            typeStyle.fontSize = 12;
            
            // Green for SSO, Yellow for Offline
            if (cliServerIdentityManager.Type == IdentityType.SSOAuthenticated)
            {
                typeStyle.normal.textColor = ServerUtilityProvider.ColorManager.StatusSuccess;
                EditorGUILayout.LabelField($"{cliServerIdentityManager.GetTypeIcon()} {cliServerIdentityManager.GetTypeName()}", typeStyle);
            }
            else if (cliServerIdentityManager.Type == IdentityType.OfflineServerIssued)
            {
                typeStyle.normal.textColor = ServerUtilityProvider.ColorManager.StatusWarning;
                EditorGUILayout.LabelField($"{cliServerIdentityManager.GetTypeIcon()} {cliServerIdentityManager.GetTypeName()}", typeStyle);
            }
            
            // Show expiration info
            GUIStyle expirationStyle = new GUIStyle(EditorStyles.miniLabel);
            expirationStyle.fontStyle = FontStyle.Italic;
            EditorGUILayout.LabelField(cliServerIdentityManager.GetExpirationInfo(), expirationStyle);
        }
        
        EditorGUILayout.Space(5);
        
        // Identity display
        if (!string.IsNullOrEmpty(cliIdentity))
        {
            // Show identity type description
            if (cliServerIdentityManager != null && cliServerIdentityManager.Type != IdentityType.Unknown)
            {
                EditorGUILayout.HelpBox(cliServerIdentityManager.GetTypeDescription(), MessageType.Info);
                
                // Show prominent SSO login button for Offline Server Issued identities
                if (cliServerIdentityManager.Type == IdentityType.OfflineServerIssued)
                {
                    EditorGUILayout.Space(5);
                    
                    // Create a centered button with prominent styling
                    GUIStyle bigButtonStyle = new GUIStyle(GUI.skin.button);
                    bigButtonStyle.fontSize = 13;
                    bigButtonStyle.fontStyle = FontStyle.Bold;
                    bigButtonStyle.fixedHeight = 35;
                    bigButtonStyle.normal.textColor = Color.white;
                    
                    if (GUILayout.Button("🔐 Login with SSO", bigButtonStyle))
                    {
                        HandleSSOLogin();
                    }
                }
                
                EditorGUILayout.Space(5);
            }
            
            EditorGUILayout.LabelField("Identity:", EditorStyles.boldLabel);
            scrollPositionCli = EditorGUILayout.BeginScrollView(scrollPositionCli, GUILayout.Height(60));
            EditorGUILayout.SelectableLabel(cliIdentity, tokenStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.Space(5);
            
            // Copy button for identity
            if (GUILayout.Button("Copy Identity", GUILayout.Height(20)))
            {
                EditorGUIUtility.systemCopyBuffer = cliIdentity;
                ShowNotification(new GUIContent("Identity copied to clipboard!"));
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No CLI identity loaded. Click Refresh to fetch.", MessageType.Info);
        }
        
        // Auth token display (collapsible for security)
        if (!string.IsNullOrEmpty(cliAuthToken))
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Auth Token:", EditorStyles.boldLabel);
            GUIStyle warningStyle = new GUIStyle(EditorStyles.miniLabel);
            warningStyle.normal.textColor = ServerUtilityProvider.ColorManager.Warning;
            EditorGUILayout.LabelField("⚠ Don't share this token!", warningStyle);
            
            scrollPositionCli = EditorGUILayout.BeginScrollView(scrollPositionCli, GUILayout.Height(80));
            EditorGUILayout.SelectableLabel(cliAuthToken, tokenStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.Space(5);
            
            // Copy button for token
            if (GUILayout.Button("Copy Auth Token", GUILayout.Height(20)))
            {
                EditorGUIUtility.systemCopyBuffer = cliAuthToken;
                ShowNotification(new GUIContent("Auth token copied to clipboard!"));
            }
        }
        
        EditorGUILayout.EndVertical();
    }

    private void DrawServerIdentitySection()
    {
        EditorGUILayout.BeginVertical(boxStyle);
        
        // Title
        EditorGUILayout.LabelField("Server Identity", titleStyle);
        EditorGUILayout.Space(5);
        
        // Status message
        GUIStyle statusStyle = new GUIStyle(EditorStyles.miniLabel);
        statusStyle.normal.textColor = serverStatusColor;
        statusStyle.fontStyle = FontStyle.Italic;
        EditorGUILayout.LabelField(serverStatusMessage, statusStyle);
        
        EditorGUILayout.Space(5);
        
        // Server Identity display (the "Associated database identities for" identity)
        if (!string.IsNullOrEmpty(serverIdentity))
        {
            EditorGUILayout.LabelField("Identity:", EditorStyles.boldLabel);
            scrollPositionServer = EditorGUILayout.BeginScrollView(scrollPositionServer, GUILayout.Height(60));
            EditorGUILayout.SelectableLabel(serverIdentity, tokenStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.Space(5);
            
            // Copy button
            if (GUILayout.Button("Copy Server Identity", GUILayout.Height(20)))
            {
                EditorGUIUtility.systemCopyBuffer = serverIdentity;
                ShowNotification(new GUIContent("Server identity copied to clipboard!"));
            }
            
            // Ownership check
            EditorGUILayout.Space(10);
            if (cliIdentity == serverIdentity)
            {
                GUIStyle matchStyle = new GUIStyle(EditorStyles.label);
                matchStyle.normal.textColor = ServerUtilityProvider.ColorManager.StatusSuccess;
                matchStyle.fontStyle = FontStyle.Bold;
                EditorGUILayout.LabelField("✓ CLI identity matches server identity", matchStyle);
                EditorGUILayout.HelpBox("You own the databases on this server and can manage them freely.", MessageType.Info);
            }
            else if (!string.IsNullOrEmpty(cliIdentity))
            {
                GUIStyle mismatchStyle = new GUIStyle(EditorStyles.label);
                mismatchStyle.normal.textColor = ServerUtilityProvider.ColorManager.StatusError;
                mismatchStyle.fontStyle = FontStyle.Bold;
                EditorGUILayout.LabelField("✗ CLI identity does NOT match server identity", mismatchStyle);
                EditorGUILayout.HelpBox("Warning: You do not own the databases on this server. You may encounter ownership errors when trying to manage them.", MessageType.Warning);
            }
            
            // Show database identities if any exist
            if (databaseIdentities != null && databaseIdentities.Length > 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField($"Published Databases ({databaseIdentities.Length}):", EditorStyles.boldLabel);
                
                // Show each database identity in a compact format
                foreach (string dbIdentity in databaseIdentities)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUIStyle dbStyle = new GUIStyle(EditorStyles.miniLabel);
                    dbStyle.normal.textColor = ServerUtilityProvider.ColorManager.StatusSuccess;
                    EditorGUILayout.LabelField("• " + dbIdentity, dbStyle);
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No server identity found. This means no databases have been published to this server yet.", MessageType.Info);
        }
        
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Handles SSO login with appropriate warnings
    /// </summary>
    private void HandleSSOLogin()
    {
        // Check if we have an offline/server-issued identity that matches the server identity
        bool hasOfflineIdentity = cliServerIdentityManager != null && 
                                   cliServerIdentityManager.Type == IdentityType.OfflineServerIssued;
        bool identitiesMatch = !string.IsNullOrEmpty(cliIdentity) && 
                              !string.IsNullOrEmpty(serverIdentity) && 
                              cliIdentity == serverIdentity;
        bool hasPublishedDatabases = databaseIdentities != null && databaseIdentities.Length > 0;

        // If offline identity is in use on the server, show warning
        if (hasOfflineIdentity && identitiesMatch && hasPublishedDatabases)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Found Published Offline Databases",
                $"You currently have an offline/server-issued identity that matches the server identity.\n\n" +
                $"Published Databases: {databaseIdentities.Length}\n\n" +
                $"⚠ IMPORTANT: If you login with SSO, you will receive a new identity and will NO LONGER have access to any databases published under your current offline identity.\n\n" +
                $"These databases cannot be recovered unless you backup your current identity and auth token and later restore them to regain access\n\n" +
                $"Do you want to proceed with secure SSO login? You may need to republish your databases after logging in.",
                "Yes, SSO Login",
                "Cancel"
            );

            if (!proceed)
            {
                if (debugMode)
                    Debug.Log("[ServerIdentityWindow] User cancelled SSO login due to offline identity warning");
                return;
            }
            
            if (debugMode)
                Debug.Log("[ServerIdentityWindow] User confirmed SSO login despite offline identity warning");
        }

        // Proceed with SSO login
        if (parentServerWindow != null)
        {
            parentServerWindow.LogoutAndLogin(manual: true);
        }
        else
        {
            Debug.LogError("[ServerIdentityWindow] Cannot perform SSO login - parent window not found");
        }
    }

    /// <summary>
    /// Refresh both CLI and server identities
    /// </summary>
    private async void RefreshIdentities()
    {
        if (isRefreshing) return;
        
        if (serverManager == null)
        {
            cliStatusMessage = "ServerManager not available";
            cliStatusColor = Color.grey;
            serverStatusMessage = "ServerManager not available";
            serverStatusColor = ServerUtilityProvider.ColorManager.StatusError;
            Repaint();
            return;
        }
        
        isRefreshing = true;
        cliStatusMessage = "Fetching CLI identity...";
        cliStatusColor = Color.grey;
        serverStatusMessage = "Fetching server identity...";
        serverStatusColor = ServerUtilityProvider.ColorManager.StatusInfo;
        Repaint();
        
        try
        {
            // Fetch CLI identity
            var cliResult = await ServerIdentityManager.FetchCliIdentityAsync(serverManager, debugMode);
            cliIdentity = cliResult.identity;
            cliAuthToken = cliResult.token;
            cliServerIdentityManager = cliResult.info;
            cliStatusMessage = cliResult.statusMessage;
            cliStatusColor = Color.grey;
            
            // Fetch server identity
            var serverResult = await ServerIdentityManager.FetchServerIdentityAsync(serverManager, debugMode);
            serverIdentity = serverResult.serverIdentity;
            databaseIdentities = serverResult.databaseIdentities;
            serverStatusMessage = serverResult.statusMessage;
            serverStatusColor = !string.IsNullOrEmpty(serverResult.serverIdentity) 
                ? ServerUtilityProvider.ColorManager.StatusSuccess 
                : ServerUtilityProvider.ColorManager.StatusInfo;
        }
        finally
        {
            isRefreshing = false;
            Repaint();
        }
    }
}

} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
