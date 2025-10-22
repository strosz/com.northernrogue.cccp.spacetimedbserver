using UnityEditor;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;
using NorthernRogue.CCCP.Editor.Settings;

// A basic database interface for managing the SQL of the server and exporting/importing it ///

namespace NorthernRogue.CCCP.Editor {

public class ServerDataWindow : EditorWindow
{
    public static bool debugMode = false;
    
    // Use NonSerialized to prevent Unity from trying to serialize the static instance
    [System.NonSerialized]
    private static ServerDataWindow currentInstance;
    
    // Unique identifier for each window instance
    [SerializeField] 
    private string instanceId = System.Guid.NewGuid().ToString();
        
    // Static constructor for initialization
    static ServerDataWindow()
    {
        if (debugMode) Debug.Log("[ServerDataWindow] Static constructor called");
    }

    // Configuration
    private string serverURL = "http://127.0.0.1:3000/v1";
    private string moduleName = "";
    private string authToken = ""; // Required to be authenticated to access all of the SQL API
    private string backupDirectory = "";
    private string customServerUrl = "";
    private string customServerAuthToken = "";
    private string maincloudUrl = "";
    private string maincloudAuthToken = "";
    private string serverUrlDocker = "";
    private string authTokenDocker = "";
    private string serverMode = "";

    // Data Storage
    // Stores raw JSON string received from the SQL API for each table
    private Dictionary<string, string> tableData = new Dictionary<string, string>();
    private List<string> tableNames = new List<string>(); // Fetched from schema
    private string selectedTable = null;
    private string statusMessage = "Ready. Load settings via ServerWindow and Refresh.";
    private Color statusColor = Color.grey; // Needed for dynamic coloring of messages
    private string statusTimestamp = DateTime.Now.ToString("HH:mm:ss"); // Track when status was set
    private bool isRefreshing = false;
    private bool isImporting = false; // Added flag for import process
    private bool showServerReachableInformation = false;

    // Scroll positions
    private Vector2 scrollPositionTables;
    private Vector2 scrollPositionData;

    // Parent window reference to get settings
    private ServerWindow parentServerWindow;

    // --- HttpClient Instance ---
    // Use a static instance for efficiency (reuse connections)
    private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    // --- JSON Helper Classes ---
    [Serializable]
    private class AuthRequest { public string username; public string password; } // Example structure
    [Serializable]
    private class AuthResponse { public string identity; public string token; }
    [Serializable]
    private class SchemaResponse { public List<TableDef> tables; }
    [Serializable]
    private class TableDef 
    { 
        public string name; 
        public TableAccessDef table_access; 
        /* Other schema details ignored for now */ 
    }
    [Serializable]
    private class TableAccessDef
    {
        public JArray Public;
    }
    [Serializable]
    private class SqlRequest { public string query; }
    // SqlResponse structure is complex (includes schema + rows), handle dynamically with JObject for now

    private GUIStyle clrButtonStyle;
    private GUIStyle titleStyle;
    private bool stylesInitialized = false;

    private Dictionary<string, Dictionary<string, float>> tableColumnWidths = new Dictionary<string, Dictionary<string, float>>();
    private const float MinColumnWidth = 30f;
    private float startDragX;
    private int draggingColumnIndex = -1;

    string urlBase;
    string sqlUrl;
    string schemaUrl;
    string reducerEndpoint;

    // --- Window Setup ---
    [MenuItem("Window/SpacetimeDB Server Manager/Browse Database")]
    public static void ShowWindow()
    {
        // Prevent opening windows during compilation
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (debugMode)
                Debug.LogWarning("[ServerDataWindow] Prevented ShowWindow during compilation/updating");
            return;
        }
        
        // Use Unity's proper singleton pattern - GetWindow without utility flag
        // This reuses existing instances and only creates new ones if needed
        ServerDataWindow window = GetWindow<ServerDataWindow>();
        window.titleContent = new GUIContent("Database");
        window.minSize = new Vector2(450, 350);
        window.Show();
        
        // Ensure proper initialization
        if (string.IsNullOrEmpty(window.instanceId))
        {
            window.instanceId = System.Guid.NewGuid().ToString();
        }
        currentInstance = window;
        
        if (debugMode)
            Debug.Log($"[ServerDataWindow] ShowWindow completed for instance {window.instanceId}");
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
        
        // Set this as the current instance
        currentInstance = this;
        
        if (debugMode)
            Debug.Log($"[ServerDataWindow] OnEnable called for instance {instanceId}");
        
        // Initialize the window
        FindParentWindow();
        LoadSettings();
        LoadColumnWidths();
        RefreshAllData();
    }

    private void InitializeStyles()
    {
        // Command button style (for CLR and DEL buttons)
        clrButtonStyle = new GUIStyle(GUI.skin.button);
        clrButtonStyle.fontSize = 10;
        clrButtonStyle.normal.textColor = ServerUtilityProvider.ColorManager.ClearDataButton;

        // Style for titles like table name and list of tables
        titleStyle = new GUIStyle(EditorStyles.largeLabel);
        titleStyle.fontSize = 14;
        
        // Confirm Styles Initialized for OnGUI
        stylesInitialized = true;
    }

    private void LoadColumnWidths()
    {
        string savedWidths = CCCPSettingsAdapter.GetColumnWidths();
        if (!string.IsNullOrEmpty(savedWidths))
        {
            try
            {
                tableColumnWidths = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, float>>>(savedWidths);
            }
            catch (Exception ex)
            {
                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Failed to load column widths: {ex.Message}");
                tableColumnWidths = new Dictionary<string, Dictionary<string, float>>();
            }
        }
    }

    private void SaveColumnWidths()
    {
        try
        {
            string serializedWidths = JsonConvert.SerializeObject(tableColumnWidths);
            CCCPSettingsAdapter.SetColumnWidths(serializedWidths);
        }
        catch (Exception ex)
        {
            if (debugMode) Debug.LogWarning($"[ServerDataWindow] Failed to save column widths: {ex.Message}");
        }
    }

    // Ensure column widths are saved when the window closes
    private void OnDisable()
    {
        // Clear the current instance if this is it
        if (currentInstance == this)
        {
            currentInstance = null;
        }
        
        SaveColumnWidths();
    }

    // Helper method to ensure server URL has /v1 suffix if missing
    private string GetApiBaseUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return "http://127.0.0.1:3000/v1"; // Default if empty
            
        // Trim any trailing slashes
        url = url.TrimEnd('/');
        
        // Check if URL already ends with /v1
        if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            url = url + "/v1";
        }
        
        // Replace localhost addresses with localhost for consistency
        if (url.Contains("127.0.0.1"))
            url = url.Replace("127.0.0.1", "localhost");
        else if (url.Contains("0.0.0.0"))
            url = url.Replace("0.0.0.0", "localhost");
            
        return url;
    }

    private void LoadSettings()
    {
        // Load settings potentially set by ServerWindow
        // Provide defaults or fetch them dynamically if needed
        string rawServerUrl = CCCPSettingsAdapter.GetServerUrl();
        serverURL = GetApiBaseUrl(rawServerUrl); // Process URL to ensure it has /v1
        moduleName = CCCPSettingsAdapter.GetModuleName();
        authToken = CCCPSettingsAdapter.GetAuthToken();
        backupDirectory = CCCPSettingsAdapter.GetBackupDirectory();

        string rawMaincloudUrl = CCCPSettingsAdapter.GetMaincloudUrl();
        maincloudUrl = GetApiBaseUrl(rawMaincloudUrl);
        maincloudAuthToken = CCCPSettingsAdapter.GetMaincloudAuthToken();

        string rawCustomServerUrl = CCCPSettingsAdapter.GetCustomServerUrl();
        customServerUrl = GetApiBaseUrl(rawCustomServerUrl);
        customServerAuthToken = CCCPSettingsAdapter.GetCustomServerAuthToken();
        
        string rawServerUrlDocker = CCCPSettingsAdapter.GetServerUrlDocker();
        serverUrlDocker = GetApiBaseUrl(rawServerUrlDocker);
        authTokenDocker = CCCPSettingsAdapter.GetAuthTokenDocker();

        serverMode = CCCPSettingsAdapter.GetServerMode().ToString();

        if (string.IsNullOrEmpty(moduleName))
        {
            SetStatus("Warning: Module/DB Name not set in ServerWindow.", Color.yellow);
        }
         if (string.IsNullOrEmpty(backupDirectory))
        {
            SetStatus("Warning: Backup Directory not set in ServerWindow.", Color.yellow);
        }
        // Auth Token might be optional depending on server setup
    }

    private void FindParentWindow()
    {
        // This is a simple way, might need adjustment if multiple ServerWindows exist
        var windows = Resources.FindObjectsOfTypeAll<ServerWindow>();
        if (windows.Length > 0)
        {
            parentServerWindow = windows[0];
        }
        // else {
        //     if (debugMode) Debug.LogWarning("[ServerDataWindow] Could not find parent ServerWindow.");
        // }
    }

    private void OnGUI()
    {
        // Initialize styles in OnGUI where GUI.skin is accessible
        if (!stylesInitialized)
        {
            InitializeStyles();
        }
        
        DrawToolbar();
        EditorGUILayout.Space(5);
        DrawMainContent();
        EditorGUILayout.Space(5);
        DrawStatusMessage();
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginDisabledGroup(isRefreshing);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            RefreshAllData();
        }
        // Add Export Button
        if (GUILayout.Button("Export to JSON", EditorStyles.toolbarButton, GUILayout.Width(110)))
        {
            ExportToJson();
        }
        // Add Export to CSV Button
        if (GUILayout.Button("Export to CSV", EditorStyles.toolbarButton, GUILayout.Width(110)))
        {
            ExportToCsv();
        }
        EditorGUI.EndDisabledGroup();

        // Add Import Button
        EditorGUI.BeginDisabledGroup(isRefreshing || isImporting); // Also disable during import
        if (GUILayout.Button("Import from Folder...", EditorStyles.toolbarButton, GUILayout.Width(130)))
        {
            // Show editor dialog with a warning
            string importMessage = 
            "Importing is an experimental feature that currently requires a manually created import reducer in your lib.rs and will be updated for ease of use.\n\n"+
            "Please refer to the documentation for more information.\n\n"+
            "Have you updated your import reducer and want to continue?";
            if (EditorUtility.DisplayDialog("Import from Folder", importMessage, "Yes", "Cancel"))
            {
                ImportFromJson();
            }
        }
        if (GUILayout.Button("Import Single File...", EditorStyles.toolbarButton, GUILayout.Width(130)))
        {
            // Show editor dialog with a warning
            string importMessage = 
            "Importing is an experimental feature that currently requires a manually created import reducer in your lib.rs and will be updated for ease of use.\n\n"+
            "Please refer to the documentation for more information.\n\n"+
            "Have you updated your import reducer and want to continue?";
            if (EditorUtility.DisplayDialog("Import Single File", importMessage, "Yes", "Cancel"))
            {
                ImportSingleFileFromJson();
            }
        }
        EditorGUI.EndDisabledGroup(); // End group for import buttons

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void DrawStatusMessage()
    {
        EditorGUILayout.BeginHorizontal();
        
        // Timestamp section - using stored timestamp instead of current time
        GUIStyle timeStyle = new GUIStyle(EditorStyles.label);
        timeStyle.normal.textColor = ServerUtilityProvider.ColorManager.StatusTime; // Light grey
        timeStyle.alignment = TextAnchor.MiddleLeft;
        timeStyle.fontStyle = FontStyle.Italic;
        EditorGUILayout.LabelField(statusTimestamp, timeStyle, GUILayout.Width(60), GUILayout.Height(16)); 
        
        // Message section
        GUIStyle msgStyle = new GUIStyle(EditorStyles.label);
        msgStyle.normal.textColor = statusColor;
        msgStyle.alignment = TextAnchor.MiddleLeft;
        EditorGUILayout.LabelField(statusMessage, msgStyle, GUILayout.Height(16));
        
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMainContent()
    {
        GUILayout.BeginHorizontal();

        DrawTableList();

        DrawDataTable();

        GUILayout.EndHorizontal();
    }

    private void DrawTableList()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(150), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("            Public Tables", titleStyle);
        EditorGUILayout.Separator();
        scrollPositionTables = EditorGUILayout.BeginScrollView(scrollPositionTables);

        if (!tableNames.Any() && !isRefreshing)
        {
        EditorGUILayout.LabelField("No tables loaded.", EditorStyles.centeredGreyMiniLabel);
        if (GUILayout.Button("Refresh Schema"))
        {
            RefreshAllData();
        }
        }

        string tableToSelect = selectedTable;
        // Use tableNames list fetched from schema
        foreach (var tableName in tableNames)
        {
            // Create a horizontal layout for each table row and add the CLR button
            EditorGUILayout.BeginHorizontal();
            
            bool isSelected = tableName == selectedTable;
            GUIStyle tableStyle = new GUIStyle(GUI.skin.button);
            if (isSelected)
            {
                tableStyle.normal.textColor = ServerUtilityProvider.ColorManager.TableSelected;
                tableStyle.fontStyle = FontStyle.Bold;
            }

            // Table name button (primary button)
            if (GUILayout.Button(tableName, tableStyle, GUILayout.ExpandWidth(true)))
            {
                tableToSelect = tableName;
                // Save selected table to Settings
                CCCPSettingsAdapter.SetLastSelectedTable(tableName);
            }
            
            // Add clear button with the custom style
            EditorGUI.BeginDisabledGroup(isRefreshing || isImporting);
            if (GUILayout.Button("CLR", clrButtonStyle, GUILayout.Width(30)))
            {
                if (EditorUtility.DisplayDialog(
                    "Clear Table", 
                    $"Are you sure you want to delete ALL rows from the '{tableName}' table?\n\nThis action cannot be undone!", 
                    "Yes, Clear Table", "Cancel"))
                {
                    ClearTable(tableName);
                }
            }
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.EndHorizontal();
        }
        if (tableToSelect != selectedTable)
        {
            selectedTable = tableToSelect;
            scrollPositionData = Vector2.zero;
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawDataTable()
    {
        try // Add try/catch to prevent unhandled exceptions
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            
            // Check selectedTable against tableNames and tableData
            if (selectedTable != null && tableNames.Contains(selectedTable))
            {
                EditorGUILayout.LabelField($"        {selectedTable}", titleStyle);
                EditorGUILayout.Separator();

                // Check if data for the selected table has been loaded
                if (!tableData.ContainsKey(selectedTable))
                {
                    EditorGUILayout.LabelField("No data loaded. Press Refresh.", EditorStyles.centeredGreyMiniLabel);
                    if (GUILayout.Button("Refresh Data", GUILayout.Height(30)))
                    {
                        RefreshAllData(); // Consider refreshing only this table in the future
                    }
                    GUILayout.EndVertical();
                    return;
                }

                // Parse the stored raw JSON data
                string rawJson = tableData[selectedTable];
                // Change data structure to handle array-of-arrays row format
                List<List<JToken>> rows = new List<List<JToken>>(); 
                List<string> columns = new List<string>();

                try
                {
                    // SpacetimeDB SQL response is an array of statement results. Assume one statement.
                    JArray resultsArray = JArray.Parse(rawJson);
                    if (resultsArray.Count > 0)
                    {
                        JObject resultObj = resultsArray[0] as JObject;
                        if (resultObj != null)
                        {
                            // Extract column names from SCHEMA definition
                            if (resultObj.TryGetValue("schema", out JToken schemaToken) && schemaToken is JObject schemaObj &&
                                schemaObj.TryGetValue("elements", out JToken elementsToken) && elementsToken is JArray elementsArray)
                            {
                                columns = elementsArray.Select(el => el?["name"]?["some"]?.ToString())
                                                     .Where(name => name != null)
                                                     .ToList();
                            }
                            else
                            {
                                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Could not find schema->elements in JSON for table '{selectedTable}'.");
                                columns = new List<string>(); // Ensure columns is empty if schema is missing
                            }

                            // Extract ROWS data (if any)
                            if (resultObj.TryGetValue("rows", out JToken rowsToken) && rowsToken is JArray rowsArray)
                            {
                                // Convert JArray rows to List<List<JToken>>
                                foreach (var item in rowsArray)
                                {
                                    if (item is JArray rowArrayValues)
                                    {
                                        rows.Add(rowArrayValues.ToList()); // Add the inner array as a list of JTokens
                                    }
                                    else
                                    {
                                        if (debugMode) Debug.LogWarning($"[ServerDataWindow] Unexpected item type in 'rows' array for table '{selectedTable}'. Expected JArray, got {item.Type}");
                                    }
                                }
                            }
                            else
                            {
                                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Could not find 'rows' array in JSON for table '{selectedTable}'. Assuming empty.");
                                // rows list is already initialized empty, so nothing needed here
                            }
                        }
                        else
                        {
                            if (debugMode) Debug.LogWarning($"[ServerDataWindow] Unexpected JSON structure for table '{selectedTable}'. Could not find 'schema' or 'rows'.");
                            EditorGUILayout.LabelField("Error parsing table data structure. See console.", EditorStyles.wordWrappedLabel);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("No results returned from SQL query.", EditorStyles.centeredGreyMiniLabel);
                    }
                }
                catch (JsonReaderException jsonEx)
                {
                    if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to parse JSON for table '{selectedTable}': {jsonEx.Message}Raw JSON:{rawJson}");
                    EditorGUILayout.LabelField($"Error parsing JSON data: {jsonEx.Message}", EditorStyles.wordWrappedLabel);
                    GUILayout.EndVertical(); // Ensure we end the vertical layout
                    return;
                }
                catch (Exception ex)
                {
                    if (debugMode) Debug.LogError($"[ServerDataWindow] Error processing JSON for table '{selectedTable}': {ex}");
                    EditorGUILayout.LabelField($"Error processing data: {ex.Message}", EditorStyles.wordWrappedLabel);
                    GUILayout.EndVertical(); // Ensure we end the vertical layout
                    return;
                }

                if (!rows.Any())
                {
                    EditorGUILayout.LabelField("No data in this table.", EditorStyles.centeredGreyMiniLabel);
                }

                scrollPositionData = EditorGUILayout.BeginScrollView(scrollPositionData);

                // Get or initialize column widths - ensure columns is not null
                Dictionary<string, float> columnWidths = GetColumnWidths(selectedTable, columns ?? new List<string>());

                // --- Header Row ---
                if (columns != null && columns.Any())
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                    
                    // Space for DEL button
                    GUILayout.Space(30); 
                    
                    // Draw resizable column headers
                    Rect headerRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    Rect currentRect = headerRect;
                    
                    for (int i = 0; i < columns.Count; i++)
                    {
                        string col = columns[i];
                        float width = columnWidths.ContainsKey(col) ? columnWidths[col] : 100f;
                        
                        // Calculate column rect
                        currentRect.width = width;
                        
                        // Draw column header
                        GUI.Box(currentRect, col, EditorStyles.toolbarButton);
                        
                        // Draw resize handle after each column except the last
                        if (i < columns.Count - 1)
                        {
                            Rect resizeHandleRect = new Rect(currentRect.x + currentRect.width - 3, currentRect.y, 6, currentRect.height);
                            EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeHorizontal);
                            
                            // Handle dragging logic
                            if (Event.current.type == EventType.MouseDown && resizeHandleRect.Contains(Event.current.mousePosition))
                            {
                                draggingColumnIndex = i;
                                startDragX = Event.current.mousePosition.x;
                                Event.current.Use();
                            }
                        }
                        
                        // Move to next column position
                        currentRect.x += currentRect.width;
                    }
                    
                    // Handle resizing during drag
                    if (draggingColumnIndex >= 0 && draggingColumnIndex < columns.Count)
                    {
                        if (Event.current.type == EventType.MouseDrag)
                        {
                            // Calculate the width change
                            float delta = Event.current.mousePosition.x - startDragX;
                            
                            // Update the width, ensuring it doesn't go below minimum
                            string colName = columns[draggingColumnIndex];
                            columnWidths[colName] = Mathf.Max(MinColumnWidth, columnWidths[colName] + delta);
                            
                            // Update start position for next drag event
                            startDragX = Event.current.mousePosition.x;
                            
                            Event.current.Use();
                            Repaint(); // Force repaint to show resize in real-time
                        }
                        else if (Event.current.type == EventType.MouseUp)
                        {
                            // End drag
                            draggingColumnIndex = -1;
                            SaveColumnWidths(); // Save when a resize is complete
                            Event.current.Use();
                        }
                    }
                    
                    EditorGUILayout.EndHorizontal(); // End of header row
                }

                // --- Data Rows ---
                GUIStyle rowStyle = new GUIStyle(EditorStyles.label);
                rowStyle.wordWrap = false;
                rowStyle.alignment = TextAnchor.MiddleLeft;
                rowStyle.padding = new RectOffset(4, 4, 2, 2); // Add some padding

                for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
                {
                    var rowValues = rows[rowIdx];
                    
                    EditorGUILayout.BeginHorizontal(GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    
                    // Add compact Delete button for each row
                    EditorGUI.BeginDisabledGroup(isRefreshing || isImporting);
                    if (GUILayout.Button("DEL", clrButtonStyle, GUILayout.Width(30)))
                    {
                        // Find the primary key column and value for this row
                        if (TryGetPrimaryKeyInfo(columns, rowValues, out string pkColumn, out string pkValue))
                        {
                            if (EditorUtility.DisplayDialog(
                                "Delete Row", 
                                $"Are you sure you want to delete this row from '{selectedTable}'?\n\nPrimary Key: {pkColumn} = {pkValue}\n\nThis action cannot be undone!", 
                                "Yes, Delete", "Cancel"))
                            {
                                DeleteRow(selectedTable, pkColumn, pkValue);
                            }
                        }
                        else
                        {
                            EditorUtility.DisplayDialog(
                                "Cannot Delete Row", 
                                "Could not determine a unique primary key for this row. Delete operation requires a unique identifier.", 
                                "OK");
                        }
                    }
                    EditorGUI.EndDisabledGroup();
                    
                    // Use resized columns for data values with the proper widths
                    Rect rowRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    Rect cellRect = rowRect;
                    
                    // Iterate through the extracted columns for consistent display order
                    if (columns != null) // Null check for columns
                    {
                        for (int i = 0; i < columns.Count; i++)
                        {
                            string colName = columns[i];
                            string value = "<N/A>"; // Default value
                            float width = columnWidths.ContainsKey(colName) ? columnWidths[colName] : 100f;

                            if (i < rowValues.Count) // Check if index is valid for the row data
                            {
                                JToken token = rowValues[i];
                                // Handle complex types (arrays/objects) by serializing them, otherwise get simple value
                                if (token.Type == JTokenType.Array || token.Type == JTokenType.Object)
                                {
                                    value = token.ToString(Formatting.None); // Compact JSON
                                }
                                else
                                {
                                    value = token.ToString(); // Simple value
                                }
                            }
                            else
                            {
                                value = "<IndexErr>"; // Data array shorter than schema columns
                            }

                            // Set the cell rect width and position
                            cellRect.width = width;
                            
                            // Draw the cell content
                            EditorGUI.SelectableLabel(cellRect, value, rowStyle);
                            
                            // Move to next cell position
                            cellRect.x += cellRect.width;
                        }
                    }
                    
                    EditorGUILayout.EndHorizontal(); // End of row
                    
                    // Draw separator line
                    Rect separatorRect = EditorGUILayout.GetControlRect(false, 1);
                    separatorRect.height = 1;
                    EditorGUI.DrawRect(separatorRect, ServerUtilityProvider.ColorManager.Separator);
                }

                EditorGUILayout.EndScrollView(); // End scroll view for data
            }
            else
            {
                //EditorGUILayout.LabelField(tableNames.Any() ? "Select a table from the left." : "Refresh to load tables.", EditorStyles.centeredGreyMiniLabel);
                if (showServerReachableInformation) // Enabled if schema couldn't be found
                {
                    EditorGUILayout.HelpBox("Couldn't fetch database schema.\n" +
                    "Check that your server URL is correct, your module is selected/published and the server is running.", MessageType.Info);
                }
            }
            
            GUILayout.EndVertical(); // End main vertical layout
        }
        catch (Exception ex)
        {
            // Catch any other exceptions to prevent editor from crashing
            if (debugMode) Debug.LogError($"[ServerDataWindow] Exception in DrawDataTable: {ex}");
            if (Event.current.type == EventType.Repaint)
            {
                // Only try to end GUI layouts during repaint to avoid further corruption
                try { GUILayout.EndVertical(); } catch { /* Ignore */ }
            }
        }
    }
    
    // Helper method to determine primary key information for a row
    private bool TryGetPrimaryKeyInfo(List<string> columns, List<JToken> rowValues, out string pkColumn, out string pkValue)
    {
        // Initialize out parameters
        pkColumn = string.Empty;
        pkValue = string.Empty;
        
        // FIRST PRIORITY: Look for the actual primary key field "identity" which is standard in SpacetimeDB
        string[] primaryKeyNames = new string[] { "identity" };
        foreach (var pkName in primaryKeyNames)
        {
            int pkIndex = columns.FindIndex(col => col.Equals(pkName, StringComparison.OrdinalIgnoreCase));
            if (pkIndex >= 0 && pkIndex < rowValues.Count)
            {
                pkColumn = columns[pkIndex];
                pkValue = rowValues[pkIndex].ToString();
                return true;
            }
        }
        
        // Second priority: Look for common ID column patterns
        string[] idColumnNames = new string[] { "id", "player_id", "message_id", "entity_id", "schedule_id" };
        foreach (var idName in idColumnNames)
        {
            int idIndex = columns.FindIndex(col => col.Equals(idName, StringComparison.OrdinalIgnoreCase));
            if (idIndex >= 0 && idIndex < rowValues.Count)
            {
                pkColumn = columns[idIndex];
                pkValue = rowValues[idIndex].ToString();
                return true;
            }
        }
        
        // Third priority: Standard primary key column names
        string[] possiblePkNames = new string[] 
        { 
            "primary_key", "pk", "key", "uid", "uuid" // Other possible names
        };
        
        foreach (var possiblePk in possiblePkNames)
        {
            int columnIndex = columns.FindIndex(c => c.Equals(possiblePk, StringComparison.OrdinalIgnoreCase));
            if (columnIndex >= 0 && columnIndex < rowValues.Count)
            {
                pkColumn = columns[columnIndex];
                pkValue = rowValues[columnIndex].ToString();
                return true;
            }
        }
        
        // Fallback: Just use the first column 
        // This is a last resort but is not ideal for real-world scenarios
        if (columns.Count > 0 && rowValues.Count > 0)
        {
            pkColumn = columns[0];
            pkValue = rowValues[0].ToString();
            return true;
        }
        
        return false;
    }

    // Method to delete a specific row using SQL
    private void DeleteRow(string tableName, string pkColumn, string pkValue)
    {
        // Format the value appropriately based on type
        string formattedValue = FormatSqlValue(pkValue);
        
        // Construct the SQL DELETE statement (simplified to just use the ID)
        string deleteQuery = $"DELETE FROM {tableName} WHERE {pkColumn} = {formattedValue};";
        
        // Log the query for debugging
        if (debugMode) Debug.Log($"[ServerDataWindow] Executing DELETE query: {deleteQuery}");
        
        // Execute the delete operation with authentication
        ExecuteSqlStatement(deleteQuery, "row deletion");
    }

    // Method to execute a SQL statement and handle the response
    private void ExecuteSqlStatement(string sqlQuery, string operationType)
    {
        if (isRefreshing || isImporting)
        {
            SetStatus($"Cannot execute {operationType} while another operation is in progress.", Color.yellow);
            return;
        }
        
        LoadSettings(); // Reload just in case
        
        if (string.IsNullOrEmpty(moduleName))
        {
            SetStatus("Error: Module/DB Name not set in ServerWindow.", Color.red);
            return;
        }
        
        // Determine the correct auth token based on server mode
        string currentAuthToken = authToken;
        if (serverMode == "DockerServer")
            currentAuthToken = authTokenDocker;
        else if (serverMode == "CustomServer")
            currentAuthToken = customServerAuthToken;
        else if (serverMode == "MaincloudServer")
            currentAuthToken = maincloudAuthToken;
        
        if (string.IsNullOrEmpty(currentAuthToken))
        {
            SetStatus("Error: Authorization Token not set. DML operations require owner permissions.", Color.red);
            EditorUtility.DisplayDialog("Authorization Required", 
                "SQL DELETE operations require owner permissions. Please ensure your auth token is set in the ServerWindow settings.", "OK");
            return;
        }
        
        SetStatus($"Executing {operationType} operation...", Color.yellow);
        Repaint();
        
        // Use GetApiBaseUrl to ensure URL has /v1
        if (serverMode == "WSLServer")
            urlBase = GetApiBaseUrl(serverURL);
        else if (serverMode == "DockerServer")
            urlBase = GetApiBaseUrl(serverUrlDocker);
        else if (serverMode == "CustomServer")
            urlBase = GetApiBaseUrl(customServerUrl);
        else if (serverMode == "MaincloudServer")
            urlBase = GetApiBaseUrl(maincloudUrl);

        sqlUrl = $"{urlBase}/database/{moduleName}/sql";

        // Use TaskCompletionSource to manage the async operation within the synchonous method
        var tcs = new TaskCompletionSource<bool>();
        
        // Start the HTTP request on a background thread
        Task.Run(async () => {
            try
            {
                // Send the SQL DELETE statement
                using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, sqlUrl))
                {
                    // Add the authorization header
                    // SpacetimeDB uses Bearer token authentication per docs
                    requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", currentAuthToken);
                    
                    // Add the SQL as content
                    HttpContent content = new StringContent(sqlQuery, Encoding.UTF8, "text/plain");
                    requestMessage.Content = content;
                    
                    // Send the request
                    HttpResponseMessage response = await httpClient.SendAsync(requestMessage);
                    string responseBody = await response.Content.ReadAsStringAsync();
                    
                    bool success = response.IsSuccessStatusCode;
                    string message = success ? "Operation completed successfully." : 
                                    $"Error: Status {response.StatusCode}. {responseBody}";
                    
                    // Set the result for the waiting method
                    tcs.SetResult(success);
                    
                    // Update UI on the main thread
                    EditorApplication.delayCall += () => {
                        SetStatus($"{operationType} {(success ? "succeeded" : "failed")}: {message}", 
                                 success ? Color.green : Color.red);
                        
                        if (success)
                        {
                            // Automatically refresh the data to show the changes
                            RefreshAllData();
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Operation Failed", 
                                                      $"The {operationType} operation failed:\n{message}", 
                                                      "OK");
                        }
                        Repaint();
                    };
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Exception during {operationType}: {ex.Message}";
                if (debugMode) Debug.LogError($"[ServerDataWindow] {errorMsg}");
                
                // Set the result for the waiting method
                tcs.SetResult(false);
                
                // Update UI on the main thread
                EditorApplication.delayCall += () => {
                    SetStatus(errorMsg, Color.red);
                    EditorUtility.DisplayDialog("Exception", errorMsg, "OK");
                    Repaint();
                };
            }
        });
        
        // Wait for the operation to complete without blocking the UI
        // This works because we're in an editor window, not a game thread
        while (!tcs.Task.IsCompleted)
        {
            // Artificially handle events to keep the UI responsive
            if (EditorUtility.DisplayCancelableProgressBar(
                $"Executing {operationType}", 
                "Please wait...", 
                0.5f))
            {
                EditorUtility.ClearProgressBar();
                SetStatus($"{operationType} cancelled.", Color.yellow);
                return;
            }
            System.Threading.Thread.Sleep(100); // Short delay
        }
        
        EditorUtility.ClearProgressBar();
    }

    // Method to clear all rows from a table using SQL
    private void ClearTable(string tableName)
    {
        // Simple DELETE without WHERE clause removes all rows
        string sqlQuery = $"DELETE FROM {tableName};";
        
        // Execute the clear operation
        ExecuteSqlStatement(sqlQuery, "table clearing");
    }    // Formats a value for use in SQL based on its apparent type
    private string FormatSqlValue(string value)
    {
        // Check if the value is a JSON array (common for Identity values, timestamps, and Option types in SpacetimeDB)
        if (value.StartsWith("[") && value.EndsWith("]"))
        {
            try {
                // Parse the JSON array
                JArray array = JArray.Parse(value);                // Handle empty arrays (represents None for Option types)
                if (array.Count == 0)
                {
                    return value; // Keep original JSON array format []
                }
                
                // If it's an array with a single element
                if (array.Count == 1)
                {
                    // First element is what we want
                    JToken firstElement = array[0];
                    
                    // If it's a hex string (like a SpacetimeDB Identity)
                    if (firstElement.Type == JTokenType.String)
                    {
                        string hexValue = firstElement.ToString();
                        if (hexValue.StartsWith("0x") || (hexValue.Length >= 32 && 
                            hexValue.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))))
                        {
                            return $"0x{hexValue.Replace("0x", "")}"; // Return proper hex format
                        }
                        
                        // If it's just a string in an array, return the string value with quotes
                        return $"'{firstElement.ToString().Replace("'", "''")}'";
                    }
                    // If it's a number (like a SpacetimeDB timestamp or Option<u32>)
                    else if (firstElement.Type == JTokenType.Integer)
                    {
                        return firstElement.ToString(); // Return just the numeric value for id columns
                    }
                }                // Handle arrays with multiple elements (this might be an Option type with enum variants)
                // For SpacetimeDB Option types, we need to handle the format differently
                // Arrays like [0, 1] represent Some(1) where 0 is the variant tag
                // SpacetimeDB expects the raw JSON array format for Option types in SQL
                if (array.Count == 2 && array[0].Type == JTokenType.Integer && array[1].Type == JTokenType.Integer)
                {
                    int variantTag = (int)array[0];
                    if (variantTag == 0) // "Some" variant
                    {
                        // Return the raw JSON array format as SpacetimeDB expects it
                        return value; // Keep original JSON array format [0, 1]
                    }
                    else if (variantTag == 1) // "None" variant (though this should be just [1])
                    {
                        return value; // Keep original JSON array format [1, ...]
                    }
                }
                
                // Handle single element arrays that might be None variants for Option types
                if (array.Count == 1 && array[0].Type == JTokenType.Integer && (int)array[0] == 1)
                {
                    // This might be a None variant represented as [1]
                    return value; // Keep original JSON array format [1]
                }
                
                // If we can't parse the array structure, log a warning and treat as string
                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Unknown array format for value: {value}");
                return $"'{value.Replace("'", "''")}'";
            }
            catch (JsonException) {
                // If JSON parsing fails, fall back to treating it as a string
                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Failed to parse array value: {value}");
            }
        }

        // If it looks like a number (integer, float, etc.), return it as is
        if (decimal.TryParse(value, out _) || 
            int.TryParse(value, out _) || 
            long.TryParse(value, out _) ||
            double.TryParse(value, out _))
        {
            return value;
        }
        
        // If it looks like a hex string for an identity
        if (value.StartsWith("0x") || (value.Length >= 32 && 
            value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))))
        {
            return $"0x{value.Replace("0x", "")}"; // Format as HEX literal properly
        }
        
        // Default - treat as string and escape any single quotes
        return $"'{value.Replace("'", "''")}'";
    }

    // --- Data Fetching Logic (HTTP API) ---
    public void RefreshAllData()
    {
        if (isRefreshing) return;

        // Reload settings in case they changed in ServerWindow
        LoadSettings();

        if (string.IsNullOrEmpty(moduleName))
        {
            SetStatus("Error: Module/DB Name not set.", Color.red);
            return;
        }
        // Base URL validation could be added here

        isRefreshing = true;
        SetStatus("Starting refresh via HTTP API...", Color.yellow);

        // Clear old data
        tableData.Clear();
        tableNames.Clear();
        selectedTable = null;
        Repaint(); // Show clearing immediately

        // Use Editor Coroutine for non-blocking refresh with UnityWebRequest
        EditorCoroutineUtility.StartCoroutineOwnerless(RefreshDataCoroutine((success, message) =>
        {
            isRefreshing = false;
            SetStatus(message, success ? Color.green : Color.red);
            Repaint(); // Final repaint after completion
        }));
    }

    private IEnumerator RefreshDataCoroutine(Action<bool, string> callback)
    {
        // --- 1. Set Authentication Header ---
        // If you need dynamic authentication (e.g., username/password -> token)
        // Add a step here to call POST /v1/identity
        // For now, we assume authToken is provided via Settings or not needed.
        // Ensure Auth header is set on HttpClient if token exists

        // --- 2. Get Schema to find table names (Using HttpClient) ---
        SetStatus($"Fetching schema for '{moduleName}'...", Color.yellow);
        Repaint();

        // Determine the correct auth token and URL based on server mode
        string currentAuthToken = authToken;
        if (serverMode == "DockerServer")
        {
            currentAuthToken = authTokenDocker;
            urlBase = GetApiBaseUrl(serverUrlDocker);
        }
        else if (serverMode == "WSLServer")
        {
            urlBase = GetApiBaseUrl(serverURL);
        }
        else if (serverMode == "CustomServer")
        {
            currentAuthToken = customServerAuthToken;
            urlBase = GetApiBaseUrl(customServerUrl);
        }
        else if (serverMode == "MaincloudServer")
        {
            currentAuthToken = maincloudAuthToken;
            urlBase = GetApiBaseUrl(maincloudUrl);
        }

        // Log the values being used for the request (URL base already logged)
        string tokenSnippet = string.IsNullOrEmpty(currentAuthToken) ? "None" : currentAuthToken.Substring(0, Math.Min(currentAuthToken.Length, 5)) + "...";
        // if (debugMode) Debug.Log($"[ServerDataWindow] Attempting schema request to URL: {urlBase}, Module: {moduleName}, AuthToken provided: {!string.IsNullOrEmpty(currentAuthToken)}, Token start: {tokenSnippet}");

        schemaUrl = $"{urlBase}/database/{moduleName}/schema?version=9";

        string schemaResponseJson = null;
        bool schemaFetchOk = false;
        string schemaFetchError = "(unknown)";

        Task schemaTask = Task.Run(async () => {
           try
            {
                HttpResponseMessage response = await httpClient.GetAsync(schemaUrl);
                schemaResponseJson = await response.Content.ReadAsStringAsync();
                schemaFetchOk = response.IsSuccessStatusCode;
                if (!schemaFetchOk)
                {
                    schemaFetchError = $"HTTP Status: {response.StatusCode}. Response: {schemaResponseJson}";
                }
                response.Dispose();
            }
            catch (HttpRequestException httpEx)
            {
                schemaFetchOk = false;
                schemaFetchError = $"HttpRequestException: {httpEx.Message}";
            }
             catch (TaskCanceledException cancelEx) // Catches timeouts
            {
                 schemaFetchOk = false;
                 schemaFetchError = $"Timeout/Cancelled: {cancelEx.Message}";
            }
            catch (Exception ex)
            {
                schemaFetchOk = false;
                schemaFetchError = $"Exception: {ex.Message}";
            }
        });

        // Wait for the async task to complete
        while (!schemaTask.IsCompleted)
        {
            yield return null;
        }

        // Check result
        if (!schemaFetchOk)
        {
            if (debugMode) Debug.LogWarning($"[ServerDataWindow] Schema request (HttpClient) failed: {schemaFetchError}\nURL: {schemaUrl} \nResponse: Make sure you have entered the correct server URL and module name and that the server is running.");
            callback?.Invoke(false, $"Error fetching schema: {schemaFetchError}");
            showServerReachableInformation = true;
            yield break; // Stop coroutine on schema failure
        }

        // if (debugMode) Debug.Log($"[ServerDataWindow] Schema response JSON: {schemaResponseJson}"); // Optional: Log raw response

        try
        {
            // Parse the JSON string obtained from HttpClient
            SchemaResponse schemaResponse = JsonConvert.DeserializeObject<SchemaResponse>(schemaResponseJson);
            if (schemaResponse?.tables != null)
            {
                 // Get all tables from the schema
                 var allSchemaTables = schemaResponse.tables.Select(t => t.name).ToList();
                 
                 // Filter to get only public tables based on the table_access field
                 tableNames = schemaResponse.tables
                     .Where(t => t.table_access != null && t.table_access.Public != null)
                     .Select(t => t.name)
                     .ToList();

                 // Log excluded tables (optional)
                 var excludedTables = allSchemaTables.Except(tableNames).ToList();
                 if (excludedTables.Any()) {
                    //if (debugMode) Debug.Log($"[ServerDataWindow] Excluding non-queryable/non-public tables from UI list: {string.Join(", ", excludedTables)}");
                 }

                 if (!tableNames.Any()) {
                      // This condition might now mean no *queryable* tables were found
                      SetStatus("Schema loaded, but no queryable public tables found.", Color.yellow);
                 } else {
                      SetStatus($"Schema loaded. Found {tableNames.Count} queryable public table(s).", Color.Lerp(Color.yellow, Color.green, 0.5f));
                 }
            } else {
                throw new JsonException("Schema response or tables list was null.");
            }
        }
        catch (Exception ex)
        {
            if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to parse schema JSON: {ex.Message}Response: {schemaResponseJson}");
            callback?.Invoke(false, $"Error parsing schema: {ex.Message}");
            yield break;
        }

        schemaTask.Dispose(); // Clean up
        Repaint();
        yield return null;

         if (!tableNames.Any())
        {
            callback?.Invoke(true, "Schema loaded, but no tables found."); // Considered success, but with warning status
            yield break;
        }

        // --- 3. Fetch Data for Each Table (Using HttpClient) ---
        int tablesRefreshed = 0;
        int errorCount = 0;

        // Use the same urlBase as schema fetching
        string sqlUrl = $"{urlBase}/database/{moduleName}/sql";

        // ONLY fetch data for tables we know are public and queryable
        foreach (var tableName in tableNames)
        {
            // Skip if table name is null or empty (shouldn't happen)
            if (string.IsNullOrEmpty(tableName))
            {
                if (debugMode) Debug.LogWarning("[ServerDataWindow] Skipping data fetch for null or empty table name.");
                continue;
            }

            SetStatus($"Fetching data for '{tableName}' ({tablesRefreshed + errorCount + 1}/{tableNames.Count})...", Color.yellow);
            Repaint();

            string sqlQuery = $"SELECT * FROM {tableName};";
            // SpacetimeDB /sql endpoint expects the raw query string as the body
            HttpContent content = new StringContent(sqlQuery, Encoding.UTF8, "text/plain"); // Send as plain text

            string tableResponseJson = null;
            bool tableFetchOk = false;
            string tableFetchError = "(unknown)";

            Task dataTask = Task.Run(async () => {
                 try
                {
                    // POST the SQL query string directly
                    HttpResponseMessage response = await httpClient.PostAsync(sqlUrl, content);
                    tableResponseJson = await response.Content.ReadAsStringAsync();
                    tableFetchOk = response.IsSuccessStatusCode;
                    if (!tableFetchOk)
                    {
                         tableFetchError = $"HTTP Status: {response.StatusCode}. Response: {tableResponseJson}";
                    }
                    response.Dispose();
                }
                 catch (HttpRequestException httpEx)
                {
                    tableFetchOk = false;
                    tableFetchError = $"HttpRequestException: {httpEx.Message}";
                }
                 catch (TaskCanceledException cancelEx) // Catches timeouts
                {
                     tableFetchOk = false;
                     tableFetchError = $"Timeout/Cancelled: {cancelEx.Message}";
                }
                catch (Exception ex)
                {
                    tableFetchOk = false;
                    tableFetchError = $"Exception: {ex.Message}";
                }
            });

            // Wait for the async task to complete
            while (!dataTask.IsCompleted)
            {
                 yield return null;
            }

            // Dispose content after use
            content.Dispose();

            // Check result
            if (tableFetchOk)
            {
                tableData[tableName] = tableResponseJson; // Store raw JSON response
                tablesRefreshed++;

                // Log the raw JSON for the "message" table fetch for debugging
                //if (tableName == "message") {
                //     if (debugMode) Debug.Log($"[ServerDataWindow] Raw JSON for 'message' table:\n{tableResponseJson}");
                //}

                // Basic JSON validation (optional)
                try { JToken.Parse(tableData[tableName]); }
                catch (JsonException jsonEx) {
                     if (debugMode) Debug.LogWarning($"[ServerDataWindow] Fetched data for '{tableName}' but it seems invalid JSON: {jsonEx.Message}");
                     // Store a valid JSON error object instead of potentially corrupt data
                     tableData[tableName] = JsonConvert.SerializeObject(new { error = "Invalid JSON received from server", details = jsonEx.Message });
                }
            }
            else
            {
                errorCount++;
                if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to fetch data for table '{tableName}': {tableFetchError}\nURL: {sqlUrl}\nQuery: {sqlQuery}");
                 // Store error info as a valid JSON object
                 tableData[tableName] = JsonConvert.SerializeObject(new { error = "Failed to fetch data", details = tableFetchError });
            }
            dataTask.Dispose(); // Clean up task
             yield return null; // Small delay / UI update chance
        }

        // --- 4. Final Status ---
        string finalMessage;
        bool overallSuccess = errorCount == 0;
        if (errorCount == 0)
        {
            finalMessage = $"Data refreshed successfully for {tablesRefreshed} tables.";
        }
        else
        {
            finalMessage = $"Finished refreshing with {errorCount} error(s) for {tablesRefreshed} successful tables. Check console.";
        }

        // Check if we have a previously selected table saved
        string lastSelectedTable = CCCPSettingsAdapter.GetLastSelectedTable();
        
        if (tableNames.Any())
        {
            // Try to restore the last selected table if it exists
            if (!string.IsNullOrEmpty(lastSelectedTable) && tableNames.Contains(lastSelectedTable))
            {
                selectedTable = lastSelectedTable;
            }
            else if (selectedTable == null || !tableNames.Contains(selectedTable))
            {
                // Otherwise select the first table
                selectedTable = tableNames.First();
            }
        }

        callback?.Invoke(overallSuccess, finalMessage);
    }


    // --- Export Logic ---
    private void ExportToJson()
    {
        if (isRefreshing)
        {
            SetStatus("Cannot export while refreshing.", Color.yellow);
            return;
        }
        if (!tableData.Any())
        {
            SetStatus("No data loaded to export. Please Refresh first.", Color.yellow);
            return;
        }

        // Ensure backup directory is set
        LoadSettings(); // Reload just in case
        if (string.IsNullOrEmpty(backupDirectory))
        {
             SetStatus("Error: Backup Directory not set in ServerWindow.", Color.red);
             EditorUtility.DisplayDialog("Export Error", "Backup Directory is not set in the ServerWindow settings. Please configure it there first.", "OK");
             return;
        }
         if (!Directory.Exists(backupDirectory))
        {
            try
            {
                Directory.CreateDirectory(backupDirectory);
                SetStatus($"Created backup directory: {backupDirectory}", Color.blue);
            }
            catch (Exception ex)
            {
                 SetStatus($"Error: Backup directory does not exist and could not be created: {ex.Message}", Color.red);
                 if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to create backup directory '{backupDirectory}': {ex}");
                 EditorUtility.DisplayDialog("Export Error", $"Backup Directory does not exist and could not be created:{backupDirectory} Error: {ex.Message}", "OK");
                 return;
            }
        }

        // Create timestamped folder
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string exportFolderPath = Path.Combine(backupDirectory, $"SpacetimeDB_Backup_{moduleName}_{timestamp}");

        try
        {
            Directory.CreateDirectory(exportFolderPath);
        }
        catch (Exception ex)
        {
             SetStatus($"Error creating export folder: {ex.Message}", Color.red);
             if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to create export folder '{exportFolderPath}': {ex}");
             EditorUtility.DisplayDialog("Export Error", $"Could not create export folder:{exportFolderPath} Error: {ex.Message}", "OK");
             return;
        }

        // Save each table's JSON
        int filesSaved = 0;
        int errors = 0;
        SetStatus($"Exporting {tableData.Count} tables to {exportFolderPath}...", Color.blue);
        Repaint();

        foreach (var kvp in tableData)
        {
            string tableName = kvp.Key;
            string rawJson = kvp.Value;
            string filePath = Path.Combine(exportFolderPath, $"{tableName}.json");

            try
            {
                 // Try to format the JSON nicely before saving
                 string jsonToSave = rawJson;
                 try {
                      JToken parsedJson = JToken.Parse(rawJson);
                      jsonToSave = parsedJson.ToString(Formatting.Indented);
                 } catch {
                     // Save raw string if it's not valid JSON (e.g., error message we stored)
                 }

                 File.WriteAllText(filePath, jsonToSave, Encoding.UTF8);
                 filesSaved++;
            }
            catch (Exception ex)
            {
                 errors++;
                 if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to write JSON file '{filePath}': {ex}");
            }
        }

        string finalMessage = $"Export completed. {filesSaved} files saved";
         if (errors > 0) {
             finalMessage += $" with {errors} errors (check console).";
         }
         finalMessage += $" Location: {exportFolderPath}";
         SetStatus(finalMessage, errors == 0 ? Color.green : Color.red);
         EditorUtility.RevealInFinder(exportFolderPath); // Show the folder
    }

    // --- Export to CSV Logic ---
    private void ExportToCsv()
    {
        if (isRefreshing)
        {
            SetStatus("Cannot export while refreshing.", Color.yellow);
            return;
        }
        if (!tableData.Any())
        {
            SetStatus("No data loaded to export. Please Refresh first.", Color.yellow);
            return;
        }

        // Ensure backup directory is set
        LoadSettings(); // Reload just in case
        if (string.IsNullOrEmpty(backupDirectory))
        {
            SetStatus("Error: Backup Directory not set in ServerWindow.", Color.red);
            EditorUtility.DisplayDialog("Export Error", "Backup Directory is not set in the ServerWindow settings. Please configure it there first.", "OK");
            return;
        }
        if (!Directory.Exists(backupDirectory))
        {
            try
            {
                Directory.CreateDirectory(backupDirectory);
                SetStatus($"Created backup directory: {backupDirectory}", Color.blue);
            }
            catch (Exception ex)
            {
                SetStatus($"Error: Backup directory does not exist and could not be created: {ex.Message}", Color.red);
                if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to create backup directory '{backupDirectory}': {ex}");
                EditorUtility.DisplayDialog("Export Error", $"Backup Directory does not exist and could not be created:{backupDirectory} Error: {ex.Message}", "OK");
                return;
            }
        }

        // Prompt user for delimiter choice, adding a Cancel option
        string[] options = new string[] { "Semicolon (;)", "Comma (,)", "Cancel" };
        int delimiterChoice = EditorUtility.DisplayDialogComplex(
            "Choose CSV Delimiter",
            "Select the delimiter (column separator symbol) to use for CSV files.\n\n" +
            "EU Excel typically uses Semicolon (;)\n" +
            "US Excel typically uses Comma (,)\n\n" +
            "Choose based on your regional settings or Cancel the export.",
            options[0], // Semicolon (returns 0)
            options[1], // Comma (returns 1)
            options[2]  // Cancel (returns 2)
        );

        string delimiter;
        switch (delimiterChoice)
        {
            case 0: // Semicolon
                delimiter = ";";
                break;
            case 1: // Comma
                delimiter = ",";
                break;
            case 2: // Cancel button pressed or dialog closed via 'X'
                SetStatus("CSV Export cancelled.", Color.grey);
                return; // Exit the ExportToCsv method immediately
            default:
                // Should not happen with DisplayDialogComplex, but handle defensively
                if (debugMode) Debug.LogError("[ServerDataWindow] Unexpected delimiter choice result: " + delimiterChoice);
                SetStatus("CSV Export cancelled due to unexpected error.", Color.red);
                return;
        }

        // Create timestamped folder
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string exportFolderPath = Path.Combine(backupDirectory, $"SpacetimeDB_CSV_Backup_{moduleName}_{timestamp}");

        try
        {
            Directory.CreateDirectory(exportFolderPath);
        }
        catch (Exception ex)
        {
            SetStatus($"Error creating export folder: {ex.Message}", Color.red);
            if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to create export folder '{exportFolderPath}': {ex}");
            EditorUtility.DisplayDialog("Export Error", $"Could not create export folder:{exportFolderPath} Error: {ex.Message}", "OK");
            return;
        }

        // Save each table as CSV
        int filesSaved = 0;
        int errors = 0;
        SetStatus($"Exporting {tableData.Count} tables to CSV in {exportFolderPath}...", Color.blue);
        Repaint();

        foreach (var kvp in tableData)
        {
            string tableName = kvp.Key;
            string rawJson = kvp.Value;
            string filePath = Path.Combine(exportFolderPath, $"{tableName}.csv");

            try
            {
                // Parse the stored raw JSON data
                List<List<JToken>> rows = new List<List<JToken>>();
                List<string> columns = new List<string>();

                try
                {
                    // SpacetimeDB SQL response is an array of statement results. Assume one statement.
                    JArray resultsArray = JArray.Parse(rawJson);
                    if (resultsArray.Count > 0)
                    {
                        JObject resultObj = resultsArray[0] as JObject;
                        if (resultObj != null)
                        {
                            // Extract column names from SCHEMA definition
                            if (resultObj.TryGetValue("schema", out JToken schemaToken) && schemaToken is JObject schemaObj &&
                                schemaObj.TryGetValue("elements", out JToken elementsToken) && elementsToken is JArray elementsArray)
                            {
                                columns = elementsArray.Select(el => el?["name"]?["some"]?.ToString())
                                                     .Where(name => name != null)
                                                     .ToList();
                            }
                            else
                            {
                                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Could not find schema->elements in JSON for table '{tableName}'.");
                                columns = new List<string>(); // Ensure columns is empty if schema is missing
                            }

                            // Extract ROWS data (if any)
                            if (resultObj.TryGetValue("rows", out JToken rowsToken) && rowsToken is JArray rowsArray)
                            {
                                // Convert JArray rows to List<List<JToken>>
                                foreach (var item in rowsArray)
                                {
                                    if (item is JArray rowArrayValues)
                                    {
                                        rows.Add(rowArrayValues.ToList()); // Add the inner array as a list of JTokens
                                    }
                                    else
                                    {
                                        if (debugMode) Debug.LogWarning($"[ServerDataWindow] Unexpected item type in 'rows' array for table '{tableName}'. Expected JArray, got {item.Type}");
                                    }
                                }
                            }
                            else
                            {
                                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Could not find 'rows' array in JSON for table '{tableName}'. Assuming empty.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to parse JSON for table '{tableName}': {ex.Message}");
                }

                // Convert to CSV 
                // Use UTF8 encoding with BOM for Excel compatibility
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // Write the BOM (Byte Order Mark)
                    byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
                    fileStream.Write(bom, 0, bom.Length);
                    
                    using (var writer = new StreamWriter(fileStream, new UTF8Encoding(false)))
                    {
                        // Write header row with ID if no schema (fallback)
                        if (!columns.Any() && rows.Any())
                        {
                            // Create column headers based on the number of values in the first row
                            columns = Enumerable.Range(0, rows[0].Count)
                                              .Select(i => $"Column{i + 1}")
                                              .ToList();
                        }

                        // Write header row
                        if (columns.Any())
                        {
                            // Force quote all header fields and join with delimiter
                            string headerLine = string.Join(delimiter, columns.Select(c => $"\"{EscapeQuotes(c)}\""));
                            writer.WriteLine(headerLine);
                        }

                        // Write data rows
                        foreach (var rowValues in rows)
                        {
                            var rowFields = new List<string>();
                            
                            // Ensure we only process columns we have names for
                            for (int i = 0; i < Math.Min(columns.Count, rowValues.Count); i++)
                            {
                                JToken token = rowValues[i];
                                string value = "";
                                
                                // Extract values from arrays if they contain a single element
                                if (token.Type == JTokenType.Array && token is JArray array && array.Count == 1)
                                {
                                    // Get the first (and only) element in the array
                                    JToken firstItem = array[0];
                                    
                                    // Use the simple value directly without JSON formatting
                                    if (firstItem.Type != JTokenType.Array && firstItem.Type != JTokenType.Object)
                                    {
                                        value = firstItem.ToString();
                                    }
                                    else
                                    {
                                        // If the item inside the array is complex, convert to JSON
                                        value = firstItem.ToString(Formatting.None);
                                    }
                                }
                                // Handle regular arrays and objects by converting to JSON string
                                else if (token.Type == JTokenType.Array || token.Type == JTokenType.Object)
                                {
                                    value = token.ToString(Formatting.None);
                                }
                                // For simple values, just use the toString
                                else
                                {
                                    value = token.ToString();
                                }
                                
                                // Always quote fields for Excel compatibility
                                rowFields.Add($"\"{EscapeQuotes(value)}\"");
                            }
                            
                            writer.WriteLine(string.Join(delimiter, rowFields));
                        }
                    }
                }
                
                filesSaved++;
            }
            catch (Exception ex)
            {
                errors++;
                if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to write CSV file '{filePath}': {ex}");
            }
        }

        string finalMessage = $"CSV Export completed. {filesSaved} files saved";
        if (errors > 0)
        {
            finalMessage += $" with {errors} errors (check console).";
        }
        finalMessage += $" Location: {exportFolderPath}";
        SetStatus(finalMessage, errors == 0 ? Color.green : Color.red);
        EditorUtility.RevealInFinder(exportFolderPath); // Show the folder
    }

    // Helper method to escape quotes for CSV
    private string EscapeQuotes(string value)
    {
        return value?.Replace("\"", "\"\"") ?? "";
    }

    // --- Import Logic (Single File) ---
    private void ImportSingleFileFromJson()
    {
        if (isRefreshing || isImporting)
        {
            SetStatus("Cannot import while another operation is in progress.", Color.yellow);
            return;
        }

        // Ensure module name is set
        LoadSettings();
        if (string.IsNullOrEmpty(moduleName))
        {
            SetStatus("Error: Module/DB Name not set in ServerWindow.", Color.red);
            return;
        }
        
        // 1. Ask user to select a single JSON file
        string selectedFilePath = EditorUtility.OpenFilePanel("Select SpacetimeDB Table JSON or CSV File", backupDirectory, "json,csv");

        if (string.IsNullOrEmpty(selectedFilePath))
        {
            SetStatus("Import cancelled by user.", Color.grey);
            return;
        }

        // 2. Prepare data for the coroutine
        var tablesToImport = new List<(string tableName, string jsonData)>();
        string errorMessage = null;
        try
        {
            string tableName = Path.GetFileNameWithoutExtension(selectedFilePath);
            // Basic validation: Check if table name is in our known list (optional but good)
            if (!tableNames.Contains(tableName))
            {
                errorMessage = $"Selected file '{Path.GetFileName(selectedFilePath)}' corresponds to table '{tableName}', which is not in the queryable list. Import aborted.";
                if (debugMode) Debug.LogWarning($"[ServerDataWindow] {errorMessage}");
            }
            else
            {
                string fileExtension = Path.GetExtension(selectedFilePath).ToLowerInvariant();
                string jsonData;

                if (fileExtension == ".csv")
                {
                    // Process CSV file
                    jsonData = ConvertCsvToJson(selectedFilePath, tableName);
                    if (jsonData == null)
                    {
                        errorMessage = $"Failed to convert CSV file '{selectedFilePath}' to JSON format. Import aborted.";
                        if (debugMode) Debug.LogError($"[ServerDataWindow] {errorMessage}");
                    }
                }
                else if (fileExtension == ".json")
                {
                    // Process JSON file as before
                    jsonData = File.ReadAllText(selectedFilePath, Encoding.UTF8);
                    // Basic JSON validation before sending
                    try { JToken.Parse(jsonData); }
                    catch (JsonException jsonEx)
                    {
                        errorMessage = $"Invalid JSON content in file '{selectedFilePath}': {jsonEx.Message}. Import aborted.";
                        if (debugMode) Debug.LogError($"[ServerDataWindow] {errorMessage}");
                    }
                }
                else
                {
                    errorMessage = $"Unsupported file extension '{fileExtension}'. Only .json and .csv files are supported.";
                    if (debugMode) Debug.LogError($"[ServerDataWindow] {errorMessage}");
                    jsonData = null;
                }

                if (errorMessage == null && jsonData != null)
                {
                     tablesToImport.Add((tableName, jsonData));
                }
            }
        }
        catch (Exception ex)
        {
             errorMessage = $"Error reading or processing file '{selectedFilePath}': {ex.Message}. Import aborted.";
             if (debugMode) Debug.LogError($"[ServerDataWindow] {errorMessage}");
        }

        if (errorMessage != null)
        {
             SetStatus(errorMessage, Color.red);
             EditorUtility.DisplayDialog("Import Error", errorMessage, "OK");
             return;
        }

        if (!tablesToImport.Any()) // Should only happen if initial checks fail
        {
             SetStatus("No valid table data found to import.", Color.yellow);
             return;
        }

        // 3. Start the import coroutine (reusing the same one)
        isImporting = true;
        SetStatus($"Starting import of table '{tablesToImport[0].tableName}' from file...", Color.blue);
        Repaint();

        EditorCoroutineUtility.StartCoroutineOwnerless(ImportDataCoroutine(tablesToImport, (success, message) =>
        {
            isImporting = false;
            SetStatus(message, success ? Color.green : Color.red);
            Repaint(); // Final repaint

            if (success)
            {
                 if (EditorUtility.DisplayDialog("Import Complete", "Single table import finished successfully.\n\nDo you want to refresh the view now?", "Refresh Now", "Later"))
                 {
                      RefreshAllData();
                 }
            } else {
                 EditorUtility.DisplayDialog("Import Failed", "Single table import encountered errors. Check the console for details.", "OK");
            }
        }));
    }

    // --- Import Logic (Folder) ---
    private void ImportFromJson()
    {
        if (isRefreshing || isImporting)
        {
            SetStatus("Cannot export while another operation is in progress.", Color.yellow);
            return;
        }

        // Ensure backup directory setting is known (though we ask user for specific folder)
        LoadSettings();
        if (string.IsNullOrEmpty(moduleName))
        {
            SetStatus("Error: Module/DB Name not set in ServerWindow.", Color.red);
            return;
        }
         
        if (string.IsNullOrEmpty(backupDirectory))
        {
            SetStatus("Warning: Default Backup Directory not set, but proceeding.", Color.yellow);
            // Don't strictly require it, as user selects folder anyway.
        }

        // 1. Ask user to select the folder containing the JSON files
        string selectedFolderPath = EditorUtility.OpenFolderPanel("Select SpacetimeDB Backup Folder", backupDirectory, "");

        if (string.IsNullOrEmpty(selectedFolderPath))
        {
            SetStatus("Import cancelled by user.", Color.grey);
            return;
        }

        // 2. Find all .json and .csv files in the selected directory
        string[] allFiles;
        try
        {
             string[] jsonFiles = Directory.GetFiles(selectedFolderPath, "*.json");
             string[] csvFiles = Directory.GetFiles(selectedFolderPath, "*.csv");
             allFiles = jsonFiles.Concat(csvFiles).ToArray();
             
             if (!allFiles.Any())
             {
                 SetStatus($"No .json or .csv files found in '{selectedFolderPath}'.", Color.yellow);
                 EditorUtility.DisplayDialog("Import Error", $"No .json or .csv files were found in the selected directory:\n{selectedFolderPath}", "OK");
                 return;
             }
        }
        catch (Exception ex)
        {
             SetStatus($"Error reading directory '{selectedFolderPath}': {ex.Message}", Color.red);
             if (debugMode) Debug.LogError($"[ServerDataWindow] Error reading directory '{selectedFolderPath}': {ex}");
             EditorUtility.DisplayDialog("Import Error", $"Could not read the selected directory:\n{selectedFolderPath}\nError: {ex.Message}", "OK");
             return;
        }

        // 3. Prepare data for the coroutine
        var tablesToImport = new List<(string tableName, string jsonData)>();
        int readFileErrors = 0;
        foreach (string filePath in allFiles)
        {
            try
            {
                 string tableName = Path.GetFileNameWithoutExtension(filePath);
                 // Basic validation: Check if table name is in our known list (optional but good)
                 if (!tableNames.Contains(tableName)) {
                     if (debugMode) Debug.LogWarning($"[ServerDataWindow] Skipping import for file '{Path.GetFileName(filePath)}' as table '{tableName}' is not in the queryable list.");
                     continue;
                 }

                 string fileExtension = Path.GetExtension(filePath).ToLowerInvariant();
                 string jsonData;

                 if (fileExtension == ".csv")
                 {
                     // Process CSV file and convert to JSON
                     jsonData = ConvertCsvToJson(filePath, tableName);
                     if (jsonData == null)
                     {
                         if (debugMode) Debug.LogError($"[ServerDataWindow] Failed to convert CSV file '{filePath}' to JSON format. Skipping this file.");
                         readFileErrors++;
                         continue;
                     }
                 }
                 else // .json
                 {
                     // Process JSON file as before
                     jsonData = File.ReadAllText(filePath, Encoding.UTF8);
                 }

                 // Basic JSON validation before sending
                 try { JToken.Parse(jsonData); }
                 catch (JsonException jsonEx) {
                      if (debugMode) Debug.LogError($"[ServerDataWindow] Invalid JSON content in file '{filePath}': {jsonEx.Message}. Skipping this file.");
                      readFileErrors++;
                      continue; // Skip this file
                 }

                 tablesToImport.Add((tableName, jsonData));
            }
            catch (Exception ex)
            {
                if (debugMode) Debug.LogError($"[ServerDataWindow] Error reading or processing file '{filePath}': {ex.Message}");
                readFileErrors++;
            }
        }

        if (readFileErrors > 0)
        {
             SetStatus($"Import continuing with {readFileErrors} file read/parse error(s). Check console.", Color.yellow);
        }

        if (!tablesToImport.Any())
        {
             SetStatus("No valid, queryable table files found to import.", Color.yellow);
             EditorUtility.DisplayDialog("Import Error", "No valid, queryable table files were found in the selected directory.", "OK");
             return;
        }

        // 4. Start the import coroutine
        isImporting = true;
        SetStatus($"Starting import of {tablesToImport.Count} tables...", Color.blue);
        Repaint();

        EditorCoroutineUtility.StartCoroutineOwnerless(ImportDataCoroutine(tablesToImport, (success, message) =>
        {
            isImporting = false;
            SetStatus(message, success ? Color.green : Color.red);
            Repaint(); // Final repaint

            // Optionally refresh data view after successful import
            if (success)
            {
                 // Ask user if they want to refresh
                 if (EditorUtility.DisplayDialog("Import Complete", "Data import finished successfully.\n\nDo you want to refresh the view now to see the changes?", "Refresh Now", "Later"))
                 {
                      RefreshAllData();
                 }
            } else {
                 EditorUtility.DisplayDialog("Import Failed", "Data import encountered errors. Check the console for details.", "OK");
            }
        }));
    }

    // New method to convert CSV to SpacetimeDB JSON format
    private string ConvertCsvToJson(string csvFilePath, string tableName)
    {
        try
        {
            // Check if file exists
            if (!File.Exists(csvFilePath))
            {
                if (debugMode) Debug.LogError($"[ServerDataWindow] CSV file '{csvFilePath}' does not exist.");
                return null;
            }

            string firstLine = null;
            List<string[]> allRows = new List<string[]>();
            char delimiter;

            // 1. Read all CSV content safely with file sharing
            try
            {
                // First read the entire file content with FileShare.ReadWrite
                string fileContent;
                using (var fileStream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fileStream, Encoding.UTF8))
                {
                    fileContent = reader.ReadToEnd();
                }

                // Split the content into lines
                string[] lines = fileContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                
                if (lines.Length == 0 || string.IsNullOrEmpty(lines[0]))
                {
                    if (debugMode) Debug.LogError($"[ServerDataWindow] CSV file '{csvFilePath}' appears to be empty.");
                    return null;
                }

                // Get the first line for delimiter detection
                firstLine = lines[0];
                
                // Auto-detect the delimiter
                delimiter = AutoDetectDelimiter(firstLine);
                if (debugMode) Debug.Log($"[ServerDataWindow] Auto-detected delimiter '{delimiter}' for CSV file '{csvFilePath}'");

                // Parse all lines into rows
                foreach (string line in lines)
                {
                    // Skip empty lines
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    // Parse the CSV line (handling quotes and escaped delimiters)
                    string[] fields = ParseCsvLine(line, delimiter);
                    allRows.Add(fields);
                }
            }
            catch (IOException ioEx)
            {
                if (debugMode) Debug.LogError($"[ServerDataWindow] IO Error reading CSV file '{csvFilePath}': {ioEx.Message}");
                return null;
            }
            catch (Exception ex)
            {
                if (debugMode) Debug.LogError($"[ServerDataWindow] Error reading CSV file '{csvFilePath}': {ex.Message}");
                return null;
            }

            if (allRows.Count <= 1) // Only headers or empty
            {
                if (debugMode) Debug.LogError($"[ServerDataWindow] CSV file '{csvFilePath}' has no data rows.");
                return null;
            }

            // 3. Extract headers and data
            string[] headers = allRows[0];
            List<string[]> dataRows = allRows.Skip(1).ToList();

            // 4. Get schema information for this table to reconstruct proper JSON types
            // We need this to know which columns are arrays (like identities and timestamps)
            Dictionary<string, string> columnTypes = GetColumnTypesFromSchema(tableName);

            // 5. Build the SpacetimeDB JSON format
            JObject resultObj = new JObject();
            
            // 5.1 Build schema part
            JObject schemaObj = new JObject();
            JArray elementsArray = new JArray();
            
            foreach (string header in headers)
            {
                JObject elementObj = new JObject();
                JObject nameObj = new JObject();
                nameObj["some"] = header;
                elementObj["name"] = nameObj;
                
                // We're not fully reconstructing the complex schema, but we need this structure
                // for the SpacetimeDB import to recognize it as valid
                JObject typeObj = new JObject();
                if (columnTypes.TryGetValue(header, out string columnType))
                {
                    // Add specific type info if we have it
                    if (columnType == "identity")
                    {
                        // Use U256 for identity types
                        JObject productObj = new JObject();
                        JArray elementsArr = new JArray();
                        JObject identityElement = new JObject();
                        
                        JObject identityNameObj = new JObject();
                        identityNameObj["some"] = "__identity__";
                        identityElement["name"] = identityNameObj;
                        
                        JObject identityTypeObj = new JObject();
                        identityTypeObj["U256"] = new JArray();
                        identityElement["algebraic_type"] = identityTypeObj;
                        
                        elementsArr.Add(identityElement);
                        productObj["elements"] = elementsArr;
                        typeObj["Product"] = productObj;
                    }
                    else if (columnType == "timestamp")
                    {
                        // Use I64 for timestamp types in product form
                        JObject productObj = new JObject();
                        JArray elementsArr = new JArray();
                        JObject timeElement = new JObject();
                        
                        JObject timeNameObj = new JObject();
                        timeNameObj["some"] = "__timestamp_micros_since_unix_epoch__";
                        timeElement["name"] = timeNameObj;
                        
                        JObject timeTypeObj = new JObject();
                        timeTypeObj["I64"] = new JArray();
                        timeElement["algebraic_type"] = timeTypeObj;
                        
                        elementsArr.Add(timeElement);
                        productObj["elements"] = elementsArr;
                        typeObj["Product"] = productObj;
                    }
                    else if (columnType == "string")
                    {
                        typeObj["String"] = new JArray();
                    }
                    else if (columnType == "u32")
                    {
                        typeObj["U32"] = new JArray();
                    }
                    else if (columnType == "entity_id_option")
                    {
                        // Entity ID is a complex sum type
                        JObject sumObj = new JObject();
                        JArray variantsArr = new JArray();
                        
                        // "some" variant (0, entityId)
                        JObject someVariant = new JObject();
                        JObject someNameObj = new JObject();
                        someNameObj["some"] = "some";
                        someVariant["name"] = someNameObj;
                        JObject someTypeObj = new JObject();
                        someTypeObj["U32"] = new JArray();
                        someVariant["algebraic_type"] = someTypeObj;
                        
                        // "none" variant (1, [])
                        JObject noneVariant = new JObject();
                        JObject noneNameObj = new JObject();
                        noneNameObj["some"] = "none";
                        noneVariant["name"] = noneNameObj;
                        JObject noneTypeObj = new JObject();
                        JObject noneProductObj = new JObject();
                        noneProductObj["elements"] = new JArray();
                        noneTypeObj["Product"] = noneProductObj;
                        noneVariant["algebraic_type"] = noneTypeObj;
                        
                        variantsArr.Add(someVariant);
                        variantsArr.Add(noneVariant);
                        sumObj["variants"] = variantsArr;
                        typeObj["Sum"] = sumObj;
                    }
                    else if (columnType == "vector3")
                    {
                        // Vector3 is a product type with x, y, z fields
                        JObject productObj = new JObject();
                        JArray elementsArr = new JArray();
                        
                        string[] fieldNames = new[] { "x", "y", "z" };
                        foreach (string fieldName in fieldNames)
                        {
                            JObject fieldElement = new JObject();
                            JObject fieldNameObj = new JObject();
                            fieldNameObj["some"] = fieldName;
                            fieldElement["name"] = fieldNameObj;
                            
                            JObject fieldTypeObj = new JObject();
                            fieldTypeObj["F32"] = new JArray();
                            fieldElement["algebraic_type"] = fieldTypeObj;
                            
                            elementsArr.Add(fieldElement);
                        }
                        
                        productObj["elements"] = elementsArr;
                        typeObj["Product"] = productObj;
                    }
                    else if (columnType == "f32")
                    {
                        typeObj["F32"] = new JArray();
                    }
                    else
                    {
                        // Default to string if type is unknown
                        typeObj["String"] = new JArray();
                    }
                }
                else
                {
                    // Default to string if type is unknown
                    typeObj["String"] = new JArray();
                }
                
                elementObj["algebraic_type"] = typeObj;
                elementsArray.Add(elementObj);
            }
            
            schemaObj["elements"] = elementsArray;
            resultObj["schema"] = schemaObj;
            
            // 5.2 Build rows part
            JArray rowsArray = new JArray();
            
            foreach (string[] dataRow in dataRows)
            {
                JArray rowArray = new JArray();
                
                for (int i = 0; i < Math.Min(headers.Length, dataRow.Length); i++)
                {
                    string header = headers[i];
                    string value = dataRow[i];
                    
                    // Convert values based on column type
                    if (columnTypes.TryGetValue(header, out string columnType))
                    {
                        if (columnType == "identity")
                        {
                            // Identity values should be in an array
                            JArray identityArray = new JArray();
                            
                            // Clean up hex format if needed (remove quotes, ensure 0x prefix)
                            string cleanHex = value.Trim('"', ' ');
                            if (!cleanHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                cleanHex = "0x" + cleanHex.TrimStart('0', 'x');
                                
                            identityArray.Add(cleanHex);
                            rowArray.Add(identityArray);
                        }
                        else if (columnType == "timestamp")
                        {
                            // Timestamp values should be in an array
                            JArray timestampArray = new JArray();
                            
                            // Try to parse the timestamp (should be a long number)
                            if (long.TryParse(value, out long timestamp))
                            {
                                timestampArray.Add(timestamp);
                            }
                            else
                            {
                                // If parsing fails, just use the raw value
                                timestampArray.Add(value);
                            }
                            
                            rowArray.Add(timestampArray);
                        }
                        else if (columnType == "u32" || header == "entity_id" || header.EndsWith("_id"))
                        {
                            // Try to parse as integer - critical for IDs!
                            // Convert direct to JValue with numeric type (not string)
                            if (uint.TryParse(value, out uint uintValue))
                            {
                                // CRITICAL: Add as raw number, not as string or JValue wrapper
                                rowArray.Add(new JValue(uintValue));
                            }
                            else if (string.IsNullOrWhiteSpace(value))
                            {
                                // Empty value, add 0 for numeric fields
                                rowArray.Add(new JValue(0));
                            }
                            else
                            {
                                // Fall back to string (though this might cause reducer errors)
                                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Could not parse ID field '{header}' value '{value}' as number. Server may reject this.");
                                rowArray.Add(value);
                            }
                        }
                        else if (columnType == "entity_id_option")
                        {
                            // Try to parse the entity_id array structure from the text value
                            if (value.StartsWith("[") && value.EndsWith("]"))
                            {
                                try
                                {
                                    // Parse the entity_id complex structure
                                    JArray entityIdArr = JArray.Parse(value);
                                    rowArray.Add(entityIdArr);
                                }
                                catch
                                {
                                    // If parsing fails, use a default "none" entity ID (1, [])
                                    JArray defaultEntityId = new JArray();
                                    defaultEntityId.Add(1);
                                    defaultEntityId.Add(new JArray());
                                    rowArray.Add(defaultEntityId);
                                }
                            }
                            else
                            {
                                // Not in array format, use a default "none" entity ID (1, [])
                                JArray defaultEntityId = new JArray();
                                defaultEntityId.Add(1);
                                defaultEntityId.Add(new JArray());
                                rowArray.Add(defaultEntityId);
                            }
                        }
                        else if (columnType == "vector3")
                        {
                            // Handle DbVector3 fields (position, direction)
                            try
                            {
                                if (value.StartsWith("[") && value.EndsWith("]"))
                                {
                                    // Try to parse as JSON array [x, y, z]
                                    JArray vectorArr = JArray.Parse(value);
                                    rowArray.Add(vectorArr);
                                }
                                else if (value.StartsWith("{") && value.EndsWith("}"))
                                {
                                    // Try to parse as JSON object {x: 0, y: 0, z: 0}
                                    JObject vectorObj = JObject.Parse(value);
                                    rowArray.Add(vectorObj);
                                }
                                else
                                {
                                    // Try to parse from comma-separated values
                                    string[] parts = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 3)
                                    {
                                        // Create a DbVector3 JSON object
                                        JObject vectorObj = new JObject();
                                        
                                        if (float.TryParse(parts[0].Trim(), out float x))
                                            vectorObj["x"] = x;
                                        else
                                            vectorObj["x"] = 0f;
                                            
                                        if (float.TryParse(parts[1].Trim(), out float y))
                                            vectorObj["y"] = y;
                                        else
                                            vectorObj["y"] = 0f;
                                            
                                        if (float.TryParse(parts[2].Trim(), out float z))
                                            vectorObj["z"] = z;
                                        else
                                            vectorObj["z"] = 0f;
                                            
                                        rowArray.Add(vectorObj);
                                    }
                                    else
                                    {
                                        // Default to zero vector if we can't parse
                                        JObject defaultVector = new JObject();
                                        defaultVector["x"] = 0f;
                                        defaultVector["y"] = 0f;
                                        defaultVector["z"] = 0f;
                                        rowArray.Add(defaultVector);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (debugMode) Debug.LogWarning($"Failed to parse vector3 value '{value}': {ex.Message}. Using default zero vector.");
                                // Default to zero vector on error
                                JObject defaultVector = new JObject();
                                defaultVector["x"] = 0f;
                                defaultVector["y"] = 0f;
                                defaultVector["z"] = 0f;
                                rowArray.Add(defaultVector);
                            }
                        }
                        else if (columnType == "f32" || columnType == "f64")
                        {
                            // Handle float values
                            if (float.TryParse(value, out float floatValue))
                            {
                                rowArray.Add(new JValue(floatValue));
                            }
                            else if (string.IsNullOrWhiteSpace(value))
                            {
                                rowArray.Add(new JValue(0f));
                            }
                            else
                            {
                                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Could not parse float field '{header}' value '{value}' as number. Server may reject this.");
                                rowArray.Add(value);
                            }
                        }
                        else
                        {
                            // Regular string or other simple type
                            rowArray.Add(value);
                        }
                    }
                    else
                    {
                        // Default handling if type is unknown
                        // For any field ending with _id, try to treat as number
                        if (header == "entity_id" || header.EndsWith("_id"))
                        {
                            if (uint.TryParse(value, out uint uintValue))
                            {
                                // Direct numeric value for IDs
                                rowArray.Add(new JValue(uintValue));
                            }
                            else
                            {
                                rowArray.Add(value);
                            }
                        }
                        else
                        {
                            rowArray.Add(value);
                        }
                    }
                }
                
                rowsArray.Add(rowArray);
            }
            
            resultObj["rows"] = rowsArray;
            
            // Add extra fields expected by the SpacetimeDB format
            resultObj["total_duration_micros"] = 0;
            JObject statsObj = new JObject();
            statsObj["rows_inserted"] = 0;
            statsObj["rows_deleted"] = 0;
            statsObj["rows_updated"] = 0;
            resultObj["stats"] = statsObj;
            
            // Wrap in an array as per SpacetimeDB format
            JArray finalResultArray = new JArray();
            finalResultArray.Add(resultObj);
            
            return finalResultArray.ToString(Formatting.None);
        }
        catch (Exception ex)
        {
            if (debugMode) Debug.LogError($"[ServerDataWindow] Error converting CSV to JSON: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    // Helper method to auto-detect CSV delimiter
    private char AutoDetectDelimiter(string csvLine)
    {
        // Count occurrences of common delimiters
        int commaCount = csvLine.Count(c => c == ',');
        int semicolonCount = csvLine.Count(c => c == ';');
        int tabCount = csvLine.Count(c => c == '\t');

        // Return the most frequent one
        if (semicolonCount >= commaCount && semicolonCount >= tabCount)
            return ';';
        else if (commaCount >= semicolonCount && commaCount >= tabCount)
            return ',';
        else
            return '\t';
    }

    // Helper method to parse a CSV line, handling quoted fields correctly
    private string[] ParseCsvLine(string line, char delimiter)
    {
        // Handle CSV with quotes properly
        List<string> fields = new List<string>();
        bool inQuotes = false;
        StringBuilder currentField = new StringBuilder();
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            
            if (c == '"')
            {
                // Check if this is an escaped quote (two double quotes together)
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Add a single quote to the field
                    currentField.Append('"');
                    i++; // Skip the next quote
                }
                else
                {
                    // Toggle quote mode
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                // End of field
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                // Add character to current field
                currentField.Append(c);
            }
        }
        
        // Add the last field
        fields.Add(currentField.ToString());
        
        return fields.ToArray();
    }

    // Helper method to get column types from schema
    private Dictionary<string, string> GetColumnTypesFromSchema(string tableName)
    {
        Dictionary<string, string> columnTypes = new Dictionary<string, string>();
        
        try
        {
            if (serverMode == "WSLServer" || serverMode == "DockerServer")
                urlBase = GetApiBaseUrl(serverURL);
            else if (serverMode == "CustomServer")
                urlBase = GetApiBaseUrl(customServerUrl);
            else if (serverMode == "MaincloudServer")
                urlBase = GetApiBaseUrl(maincloudUrl);

            schemaUrl = $"{urlBase}/database/{moduleName}/schema?version=9";

            // Make a synchronous request since this is called from a synchronous context
            var response = httpClient.GetAsync(schemaUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                if (debugMode) Debug.LogWarning($"[ServerDataWindow] Failed to fetch schema for column types: {response.StatusCode}");
                return columnTypes;
            }
            
            string schemaJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            JObject schema = JObject.Parse(schemaJson);
            
            // Find the table in the schema
            JArray tables = schema["tables"] as JArray;
            if (tables == null) return columnTypes;
            
            JObject targetTable = null;
            foreach (JObject table in tables)
            {
                if (table["name"]?.ToString() == tableName)
                {
                    targetTable = table;
                    break;
                }
            }
            
            if (targetTable == null) return columnTypes;
            
            // Get the product type reference for this table
            int productTypeRef = targetTable["product_type_ref"].Value<int>();
            
            // Find the corresponding type in the typespace
            JArray types = schema["typespace"]?["types"] as JArray;
            if (types == null) return columnTypes;
            
            JObject productType = types[productTypeRef]?["Product"] as JObject;
            if (productType == null) return columnTypes;
            
            // Get the elements array which contains the column definitions
            JArray elements = productType["elements"] as JArray;
            if (elements == null) return columnTypes;
            
            // Process each column
            foreach (JObject element in elements)
            {
                string columnName = element["name"]?["some"]?.ToString();
                if (string.IsNullOrEmpty(columnName)) continue;
                
                JObject algebraicType = element["algebraic_type"] as JObject;
                if (algebraicType == null) continue;
                
                // Determine the type based on the algebraic_type structure
                if (algebraicType["U32"] != null)
                {
                    columnTypes[columnName] = "u32";
                }
                else if (algebraicType["U64"] != null)
                {
                    columnTypes[columnName] = "u64";
                }
                else if (algebraicType["I64"] != null)
                {
                    columnTypes[columnName] = "i64";
                }
                else if (algebraicType["F32"] != null)
                {
                    columnTypes[columnName] = "f32";
                }
                else if (algebraicType["String"] != null)
                {
                    columnTypes[columnName] = "string";
                }
                else if (algebraicType["Product"] != null)
                {
                    // Check for special product types
                    JObject product = algebraicType["Product"] as JObject;
                    JArray productElements = product["elements"] as JArray;
                    
                    if (productElements != null && productElements.Count > 0)
                    {
                        JObject firstElement = productElements[0] as JObject;
                        string elementName = firstElement["name"]?["some"]?.ToString();
                        
                        if (elementName == "__identity__")
                        {
                            columnTypes[columnName] = "identity";
                        }
                        else if (elementName == "__timestamp_micros_since_unix_epoch__")
                        {
                            columnTypes[columnName] = "timestamp";
                        }
                        else if (elementName == "x" || elementName == "y" || elementName == "z")
                        {
                            columnTypes[columnName] = "vector3";
                        }
                    }
                }
                else if (algebraicType["Sum"] != null)
                {
                    // Check for Option types (Sum with some/none variants)
                    JObject sum = algebraicType["Sum"] as JObject;
                    JArray variants = sum["variants"] as JArray;
                    
                    if (variants != null && variants.Count == 2)
                    {
                        string variant1Name = variants[0]["name"]?["some"]?.ToString();
                        string variant2Name = variants[1]["name"]?["some"]?.ToString();
                        
                        if ((variant1Name == "some" && variant2Name == "none") ||
                            (variant1Name == "none" && variant2Name == "some"))
                        {
                            columnTypes[columnName] = "entity_id_option";
                        }
                    }
                }
                else if (algebraicType["Ref"] != null)
                {
                    // Handle references to other types
                    int refIndex = algebraicType["Ref"].Value<int>();
                    JObject refType = types[refIndex]?["Product"] as JObject;
                    
                    if (refType != null)
                    {
                        JArray refElements = refType["elements"] as JArray;
                        if (refElements != null && refElements.Count >= 3)
                        {
                            bool isVector3 = true;
                            foreach (JObject refElement in refElements)
                            {
                                string refElementName = refElement["name"]?["some"]?.ToString();
                                if (refElementName != "x" && refElementName != "y" && refElementName != "z")
                                {
                                    isVector3 = false;
                                    break;
                                }
                            }
                            
                            if (isVector3)
                            {
                                columnTypes[columnName] = "vector3";
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode) Debug.LogError($"[ServerDataWindow] Error getting column types from schema: {ex.Message}");
        }
        
        return columnTypes;
    }

    // --- Coroutine for Importing Data (Used by both folder and single file) ---
    private IEnumerator ImportDataCoroutine(List<(string tableName, string jsonData)> tablesToImport, Action<bool, string> callback)
    {
        // Reuse HttpClient and base URL logic from Refresh
        if (serverMode == "WSLServer" || serverMode == "DockerServer")
            urlBase = GetApiBaseUrl(serverURL);
        else if (serverMode == "CustomServer")
            urlBase = GetApiBaseUrl(customServerUrl);
        else if (serverMode == "MaincloudServer")
            urlBase = GetApiBaseUrl(maincloudUrl);

        reducerEndpoint = $"{urlBase}/database/{moduleName}/call/import_table_data";

        int successCount = 0;
        int errorCount = 0;
        StringBuilder errorDetails = new StringBuilder();

        for (int i = 0; i < tablesToImport.Count; i++)
        {
            var (tableName, jsonData) = tablesToImport[i];
            SetStatus($"Importing '{tableName}' ({i + 1}/{tablesToImport.Count})...", Color.blue);
            Repaint();
            yield return null; // Allow UI update before blocking task

            // Prepare JSON payload for the reducer: ["tableName", "jsonDataString"]
            string payloadJson = JsonConvert.SerializeObject(new object[] { tableName, jsonData });

            string responseBody = null;
            bool importOk = false;
            string importError = "(unknown)";
            HttpStatusCode httpStatusCode = HttpStatusCode.Unused; // Store status code

            // Use TaskCompletionSource to manage the async operation within the coroutine
            var tcs = new TaskCompletionSource<bool>();

            // Run the HTTP request on a background thread
            _ = Task.Run(async () => {
                try
                {
                    using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, reducerEndpoint))
                    {
                        using (var stringContent = new StringContent(payloadJson, Encoding.UTF8))
                        {
                            stringContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                            requestMessage.Content = stringContent;

                            HttpResponseMessage response = await httpClient.SendAsync(requestMessage);
                            httpStatusCode = response.StatusCode; // Capture status code
                            responseBody = await response.Content.ReadAsStringAsync();
                            importOk = response.IsSuccessStatusCode;
                            response.Dispose();
                        }
                    }
                    tcs.SetResult(importOk); // Signal completion
                }
                catch (HttpRequestException httpEx) { importOk = false; importError = $"HttpRequestException: {httpEx.Message}"; tcs.SetResult(false); }
                catch (TaskCanceledException cancelEx) { importOk = false; importError = $"Timeout/Cancelled: {cancelEx.Message}"; tcs.SetResult(false); }
                catch (Exception ex) { importOk = false; importError = $"Exception: {ex.Message}"; tcs.SetResult(false); }
            });

            // Wait for the background task to complete without blocking the main thread entirely
            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            // Task is complete, check results (importOk, importError are set by the task)
            if (!importOk && importError == "(unknown)") // Error occurred, but not caught by specific exceptions
            {
                string spacetimeError = TryParseSpacetimeError(responseBody);
                importError = $"HTTP Status: {httpStatusCode}. Server Response: {spacetimeError}";
            }

            // Check result
            if (importOk)
            {
                successCount++;
                if (debugMode) Debug.Log($"[ServerDataWindow] Successfully imported data for table '{tableName}'.");
            }
            else
            {
                errorCount++;
                string errorMsg = $"[ServerDataWindow] Failed to import data for table '{tableName}': {importError}\nEndpoint: {reducerEndpoint}\nPayload Sent (first 100 chars): {(payloadJson.Length > 100 ? payloadJson.Substring(0, 100) + "..." : payloadJson)}";
                if (debugMode) Debug.LogError(errorMsg);
                errorDetails.AppendLine($"- {tableName}: {importError}");
            }
            // No task dispose needed here as Task.Run task completes automatically
            yield return null; // Small delay / UI update chance after processing each table
        }

        // Final Status
        string finalMessage;
        bool overallSuccess = errorCount == 0;
        if (overallSuccess)
        {
             finalMessage = $"Import completed successfully for {successCount} tables at {DateTime.Now:HH:mm:ss}.";
        }
        else
        {
             finalMessage = $"Import finished with {errorCount} error(s) for {successCount} successful tables. See console.";
             if (debugMode) Debug.LogError($"[ServerDataWindow] Import Errors Summary:\n{errorDetails}");
        }

        callback?.Invoke(overallSuccess, finalMessage);
    }

    // Helper to try and parse a standard SpacetimeDB error response
    private string TryParseSpacetimeError(string responseBody)
    {
        try
        {
             // Errors are often like: {"call_error": {"reducer_error": "..."}} or {"call_error": "..."}
             JObject json = JObject.Parse(responseBody);
             if (json.TryGetValue("call_error", out JToken callErrorToken))
             {
                 if (callErrorToken is JObject callErrorObj && callErrorObj.TryGetValue("reducer_error", out JToken reducerErrorToken))
                 {
                      return reducerErrorToken.ToString(); // Prefer reducer_error
                 }
                 return callErrorToken.ToString(); // Otherwise return the whole call_error content
             }
             return responseBody; // Return raw if no "call_error" found
        }
        catch
        {
            return responseBody; // Return raw if JSON parsing fails
        }
    }

    // Get or initialize column widths for a table
    private Dictionary<string, float> GetColumnWidths(string tableName, List<string> columns)
    {
        // Null safety checks
        if (tableName == null || columns == null)
        {
            if (debugMode) Debug.LogWarning($"[ServerDataWindow] GetColumnWidths called with null parameters: tableName={tableName == null}, columns={columns == null}");
            return new Dictionary<string, float>(); // Return empty dictionary as fallback
        }

        // Ensure tableColumnWidths is initialized
        if (tableColumnWidths == null)
        {
            tableColumnWidths = new Dictionary<string, Dictionary<string, float>>();
        }
        
        // Initialize if not already present
        if (!tableColumnWidths.ContainsKey(tableName))
        {
            tableColumnWidths[tableName] = new Dictionary<string, float>();
        }
        
        // Initialize any missing columns with default width
        foreach (var column in columns)
        {
            if (column != null && !tableColumnWidths[tableName].ContainsKey(column))
            {
                tableColumnWidths[tableName][column] = 100f; // Default width
            }
        }
        
        return tableColumnWidths[tableName];
    }

    private void SetStatus(string message, Color color)
    {
        // Update timestamp when status changes
        statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
        statusMessage = message; // Raw message without timestamp
        statusColor = color;
        
        // No repaint here, let the caller handle it or rely on OnGUI loop
    }
}

// Helper for running Editor Coroutines without a MonoBehaviour
public static class EditorCoroutineUtility
{
    public static EditorCoroutine StartCoroutineOwnerless(System.Collections.IEnumerator routine)
    {
        EditorCoroutine coroutine = new EditorCoroutine(routine);
        coroutine.Start();
        return coroutine;
    }
}

public class EditorCoroutine
{
    private System.Collections.IEnumerator routine;

    public EditorCoroutine(System.Collections.IEnumerator routine)
    {
        this.routine = routine;
    }

    public void Start()
    {
        EditorApplication.update += Update;
    }

    public void Stop()
    {
        EditorApplication.update -= Update;
    }

    private void Update()
    {
        // Need to check if routine is null before calling MoveNext, in case Stop was called externally
        if (routine != null && !routine.MoveNext())
        {
            Stop();
        }
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games