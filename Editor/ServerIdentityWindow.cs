using UnityEditor;
using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using NorthernRogue.CCCP.Editor.Settings;

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
        window.titleContent = new GUIContent("Identities");
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

        RefreshIdentities();
    }

    private void OnDisable()
    {
        if (currentInstance == this)
        {
            currentInstance = null;
        }
        
        if (debugMode)
            Debug.Log($"[ServerIdentityWindow] OnDisable called for instance {instanceId}");
    }

    private void OnDestroy()
    {
        if (currentInstance == this)
        {
            currentInstance = null;
        }
        
        if (debugMode)
            Debug.Log($"[ServerIdentityWindow] OnDestroy called for instance {instanceId}");
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
        titleContent = new GUIContent($"Identity Manager - {serverMode}");
        
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
        
        // Token style (monospace for better readability)
        tokenStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true,
            richText = false,
            font = Font.CreateDynamicFontFromOSFont("Courier New", 10)
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

        if (GUILayout.Button("Login", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            parentServerWindow.LogoutAndLogin(manual: true);
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
        
        // Status message
        GUIStyle statusStyle = new GUIStyle(EditorStyles.miniLabel);
        statusStyle.normal.textColor = cliStatusColor;
        statusStyle.fontStyle = FontStyle.Italic;
        EditorGUILayout.LabelField(cliStatusMessage, statusStyle);
        
        EditorGUILayout.Space(5);
        
        // Identity display
        if (!string.IsNullOrEmpty(cliIdentity))
        {
            EditorGUILayout.LabelField("Identity Hash:", EditorStyles.boldLabel);
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
            EditorGUILayout.LabelField("Server CLI Identity:", EditorStyles.boldLabel);
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
    /// Refresh both CLI and server identities
    /// </summary>
    private async void RefreshIdentities()
    {
        if (isRefreshing) return;
        
        if (serverManager == null)
        {
            cliStatusMessage = "Error: ServerManager not available";
            cliStatusColor = ServerUtilityProvider.ColorManager.StatusError;
            serverStatusMessage = "Error: ServerManager not available";
            serverStatusColor = ServerUtilityProvider.ColorManager.StatusError;
            Repaint();
            return;
        }
        
        isRefreshing = true;
        cliStatusMessage = "Fetching CLI identity...";
        cliStatusColor = ServerUtilityProvider.ColorManager.StatusInfo;
        serverStatusMessage = "Fetching server identity...";
        serverStatusColor = ServerUtilityProvider.ColorManager.StatusInfo;
        Repaint();
        
        try
        {
            // Fetch CLI identity
            await FetchCliIdentity();
            
            // Fetch server identity
            await FetchServerIdentity();
        }
        finally
        {
            isRefreshing = false;
            Repaint();
        }
    }

    /// <summary>
    /// Fetches CLI identity using "spacetime login show --token"
    /// </summary>
    private async Task FetchCliIdentity()
    {
        try
        {
            // Get the appropriate processor based on server mode
            var result = await ExecuteServerCommand("spacetime login show --token");
            
            if (result.success && !string.IsNullOrEmpty(result.output))
            {
                // Parse the output
                // Expected format:
                // "You are logged in as c200e65fe56afa9e90d01a1acbc9d3c3ac19d41070ecc31e17ab8a6438b390ca
                // Your auth token (don't share this!) is eyJhbGc..."
                
                string output = result.output;
                
                // Extract identity (first line after "logged in as")
                var identityMatch = Regex.Match(output, @"logged in as\s+([a-fA-F0-9]+)");
                if (identityMatch.Success)
                {
                    cliIdentity = identityMatch.Groups[1].Value;
                    if (debugMode)
                        Debug.Log($"[ServerIdentityWindow] Extracted CLI identity: {cliIdentity}");
                }
                else
                {
                    cliIdentity = "";
                    if (debugMode)
                        Debug.LogWarning($"[ServerIdentityWindow] Could not extract CLI identity from output: {output}");
                }
                
                // Extract auth token (second line after "auth token ... is")
                var tokenMatch = Regex.Match(output, @"auth token[^\n]*is\s+([^\s]+)", RegexOptions.Singleline);
                if (tokenMatch.Success)
                {
                    cliAuthToken = tokenMatch.Groups[1].Value.Trim();
                    if (debugMode)
                        Debug.Log($"[ServerIdentityWindow] Extracted CLI auth token (length: {cliAuthToken.Length})");
                }
                else
                {
                    cliAuthToken = "";
                    if (debugMode)
                        Debug.LogWarning($"[ServerIdentityWindow] Could not extract auth token from output: {output}");
                }
                
                if (!string.IsNullOrEmpty(cliIdentity))
                {
                    cliStatusMessage = "CLI identity loaded successfully";
                    cliStatusColor = ServerUtilityProvider.ColorManager.StatusSuccess;
                }
                else
                {
                    cliStatusMessage = "No CLI identity logged in";
                    cliStatusColor = ServerUtilityProvider.ColorManager.StatusWarning;
                }
            }
            else
            {
                cliIdentity = "";
                cliAuthToken = "";
                cliStatusMessage = !string.IsNullOrEmpty(result.error) ? $"Error: {result.error}" : "Failed to fetch CLI identity";
                cliStatusColor = ServerUtilityProvider.ColorManager.StatusError;
                
                if (debugMode)
                    Debug.LogWarning($"[ServerIdentityWindow] Failed to fetch CLI identity. Output: {result.output}, Error: {result.error}");
            }
        }
        catch (Exception ex)
        {
            cliIdentity = "";
            cliAuthToken = "";
            cliStatusMessage = $"Exception: {ex.Message}";
            cliStatusColor = ServerUtilityProvider.ColorManager.StatusError;
            
            if (debugMode)
                Debug.LogError($"[ServerIdentityWindow] Exception fetching CLI identity: {ex}");
        }
    }

    /// <summary>
    /// Fetches server database identity using "spacetime list"
    /// </summary>
    private async Task FetchServerIdentity()
    {
        try
        {
            var result = await ExecuteServerCommand("spacetime list");
            
            if (result.success && !string.IsNullOrEmpty(result.output))
            {
                // Parse the output
                // Expected format:
                // "Associated database identities for c200e65fe56afa9e90d01a1acbc9d3c3ac19d41070ecc31e17ab8a6438b390ca:
                //
                //  db_identity                                                      
                // ------------------------------------------------------------------
                //  c200087b19ff577ce680042655b9aad23cfa21d856c33209e7f0811cb621015e"
                
                string output = result.output;
                
                // Extract the server identity from "Associated database identities for <identity>:"
                var serverIdentityMatch = Regex.Match(output, @"Associated database identities for\s+([a-fA-F0-9]{64})");
                if (serverIdentityMatch.Success)
                {
                    serverIdentity = serverIdentityMatch.Groups[1].Value;
                    
                    if (debugMode)
                        Debug.Log($"[ServerIdentityWindow] Extracted server identity: {serverIdentity}");
                }
                else
                {
                    serverIdentity = "";
                    if (debugMode)
                        Debug.LogWarning($"[ServerIdentityWindow] Could not extract server identity from 'Associated database identities for' line");
                }
                
                // Extract all database identities (64-character hex strings after the table header)
                var databaseMatches = Regex.Matches(output, @"(?:db_identity[^\n]*\n[^\n]*\n\s*)([a-fA-F0-9]{64})");
                if (databaseMatches.Count > 0)
                {
                    databaseIdentities = new string[databaseMatches.Count];
                    for (int i = 0; i < databaseMatches.Count; i++)
                    {
                        databaseIdentities[i] = databaseMatches[i].Groups[1].Value;
                    }
                    
                    if (debugMode)
                        Debug.Log($"[ServerIdentityWindow] Extracted {databaseIdentities.Length} database identities");
                }
                else
                {
                    databaseIdentities = new string[0];
                    if (debugMode)
                        Debug.Log("[ServerIdentityWindow] No database identities found in output");
                }
                
                // Set status message
                if (!string.IsNullOrEmpty(serverIdentity))
                {
                    if (databaseIdentities.Length > 0)
                    {
                        serverStatusMessage = $"Server identity loaded with {databaseIdentities.Length} database(s)";
                    }
                    else
                    {
                        serverStatusMessage = "Server identity loaded (no databases published yet)";
                    }
                    serverStatusColor = ServerUtilityProvider.ColorManager.StatusSuccess;
                }
                else
                {
                    serverStatusMessage = "No server identity found - no databases published yet";
                    serverStatusColor = ServerUtilityProvider.ColorManager.StatusInfo;
                }
            }
            else
            {
                serverIdentity = "";
                databaseIdentities = new string[0];
                serverStatusMessage = !string.IsNullOrEmpty(result.error) ? $"Error: {result.error}" : "Failed to fetch server identity";
                serverStatusColor = ServerUtilityProvider.ColorManager.StatusError;
                
                if (debugMode)
                    Debug.LogWarning($"[ServerIdentityWindow] Failed to fetch server identity. Output: {result.output}, Error: {result.error}");
            }
        }
        catch (Exception ex)
        {
            serverIdentity = "";
            databaseIdentities = new string[0];
            serverStatusMessage = $"Exception: {ex.Message}";
            serverStatusColor = ServerUtilityProvider.ColorManager.StatusError;
            
            if (debugMode)
                Debug.LogError($"[ServerIdentityWindow] Exception fetching server identity: {ex}");
        }
    }

    /// <summary>
    /// Executes a server command using the appropriate processor (Docker or WSL)
    /// </summary>
    private async Task<(string output, string error, bool success)> ExecuteServerCommand(string command)
    {
        if (serverManager == null)
        {
            return ("", "ServerManager not available", false);
        }

        // Check prerequisites
        if (!serverManager.HasAllPrerequisites)
        {
            return ("", "Prerequisites not met", false);
        }

        try
        {
            // Get server directory for Docker/WSL commands
            string serverDirectory = serverManager.ServerDirectory;
            
            // Execute command based on CLI provider
            if (serverManager.LocalCLIProvider == "Docker")
            {
                var dockerProcessor = serverManager.GetDockerProcessor();
                if (dockerProcessor != null)
                {
                    return await dockerProcessor.RunServerCommandAsync(command, serverDirectory);
                }
                else
                {
                    return ("", "Docker processor not available", false);
                }
            }
            else // WSL
            {
                var wslProcessor = serverManager.GetWSLProcessor();
                if (wslProcessor != null)
                {
                    return await wslProcessor.RunServerCommandAsync(command, serverDirectory);
                }
                else
                {
                    return ("", "WSL processor not available", false);
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
                Debug.LogError($"[ServerIdentityWindow] Exception executing command: {ex}");
            return ("", ex.Message, false);
        }
    }
}

} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
