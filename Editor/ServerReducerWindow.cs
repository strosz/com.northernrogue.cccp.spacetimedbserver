using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Loads the available reducers from the server schema and can call them with custom params ///

namespace NorthernRogue.CCCP.Editor {

public class ServerReducerWindow : EditorWindow
{
    #region Variables
    private string serverURL = "http://127.0.0.1:3000/v1"; // Default, should be overridden
    private string moduleName = "magical"; // Default, should be overridden
    private string authToken = ""; // Should be overridden if needed

    private string customServerUrl = "";
    private string maincloudUrl = "";
    private string serverMode = "";

    private string schemaUrl = "";
    private string reducerUrl = "";

    // List of reducers from schema
    private List<ReducerInfo> reducers = new List<ReducerInfo>();
    private bool isRefreshing = false;
    
    // UI
    private Vector2 scrollPosition;
    private string statusMessage = "Ready. Load settings via ServerWindow and Refresh.";
    private Color statusColor = Color.grey;
    private string statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
    
    // Styles
    private GUIStyle titleStyle;
    private GUIStyle reducerTitleStyle;
    private GUIStyle cmdButtonStyle;
    private bool stylesInitialized = false;
    
    // Constants
    private const string PrefsKeyPrefix = "CCCP_"; // Use the same prefix as ServerWindow
    
    // HTTP Client
    private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    
    // Classes for Schema parsing
    [Serializable]
    private class ReducerParam
    {
        public JObject name;
        public JObject algebraic_type;
        [NonSerialized] public string TagValue;
        
        public string GetNameString()
        {
            if (name != null && name["some"] != null)
            {
                return name["some"].ToString();
            }
            return "unnamed";
        }
        
        public string GetTypeString()
        {
            if (algebraic_type == null) return "unknown";
            
            if (algebraic_type["String"] != null) return "String";
            if (algebraic_type["U32"] != null) return "U32";
            if (algebraic_type["I32"] != null) return "I32";
            if (algebraic_type["F32"] != null) return "F32";
            if (algebraic_type["Bool"] != null) return "Bool";
            if (algebraic_type["Ref"] != null) return $"Ref({algebraic_type["Ref"].ToString()})";
            
            // Default case for unknown types
            return algebraic_type.First.Path;
        }
    }
    
    [Serializable]
    private class ReducerInfo
    {
        public string name;
        public JObject @params;
        public JObject lifecycle;
        
        public List<ReducerParam> GetParameters()
        {
            List<ReducerParam> result = new List<ReducerParam>();
            if (@params != null && @params["elements"] != null)
            {
                JArray elements = @params["elements"] as JArray;
                if (elements != null)
                {
                    foreach (JToken element in elements)
                    {
                        try
                        {
                            ReducerParam param = element.ToObject<ReducerParam>();
                            result.Add(param);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error parsing reducer parameter: {ex.Message}");
                        }
                    }
                }
            }
            return result;
        }
        
        public bool IsLifecycleReducer()
        {
            return lifecycle != null && lifecycle["some"] != null;
        }
        
        public string GetLifecycleType()
        {
            if (lifecycle == null || lifecycle["some"] == null) return "None";
            
            JObject lifecycleType = lifecycle["some"] as JObject;
            if (lifecycleType != null)
            {
                return lifecycleType.First.Path;
            }
            return "Unknown";
        }
    }
    
    [Serializable]
    private class SchemaResponse
    {
        public List<ReducerInfo> reducers;
    }
    
    [Serializable]
    private class SqlRequest
    {
        public string query;
    }
    #endregion
    
    #region Window Management
    [MenuItem("SpacetimeDB/Server Run Reducer")]
    public static void ShowWindow()
    {
        ServerReducerWindow window = GetWindow<ServerReducerWindow>("Run Server Reducer");
        window.minSize = new Vector2(500, 400);
        window.LoadSettings();
    }
    
    private void OnEnable()
    {
        LoadSettings();
        RefreshReducers();
    }
    
    private void OnGUI()
    {
        if (!stylesInitialized)
        {
            InitializeStyles();
        }
        
        EditorGUILayout.BeginVertical();
        
        DrawToolbar();
        EditorGUILayout.Space(5);
        
        DrawReducersList();
        EditorGUILayout.Space(5);
        
        DrawStatusMessage();
        
        EditorGUILayout.EndVertical();
    }
    
    private void InitializeStyles()
    {
        // Match ServerDataWindow's style initialization
        titleStyle = new GUIStyle(EditorStyles.largeLabel);
        titleStyle.fontSize = 14;
        titleStyle.alignment = TextAnchor.MiddleCenter;

        reducerTitleStyle = new GUIStyle(EditorStyles.largeLabel);
        reducerTitleStyle.fontSize = 14;
        
        cmdButtonStyle = new GUIStyle(GUI.skin.button);
        cmdButtonStyle.fontSize = 10;
        cmdButtonStyle.normal.textColor = new Color(0.2f, 0.7f, 0.2f);
        cmdButtonStyle.hover.textColor = new Color(0.3f, 0.8f, 0.3f);
        cmdButtonStyle.fontStyle = FontStyle.Bold;
        
        stylesInitialized = true;
    }
    #endregion
    
    #region UI Drawing
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        // Refresh Button
        EditorGUI.BeginDisabledGroup(isRefreshing);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            RefreshReducers();
        }
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawStatusMessage()
    {
        // Match ServerDataWindow's status message style
        EditorGUILayout.BeginHorizontal();
        
        // Timestamp section with light grey color
        GUIStyle timeStyle = new GUIStyle(EditorStyles.label);
        timeStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f); // Light grey
        timeStyle.alignment = TextAnchor.MiddleLeft;
        timeStyle.fontStyle = FontStyle.Italic;
        EditorGUILayout.LabelField(statusTimestamp, timeStyle, GUILayout.Width(60), GUILayout.Height(16));
        
        // Message section with status color
        GUIStyle msgStyle = new GUIStyle(EditorStyles.label);
        msgStyle.normal.textColor = statusColor;
        msgStyle.alignment = TextAnchor.MiddleLeft;
        EditorGUILayout.LabelField(statusMessage, msgStyle, GUILayout.Height(16));
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawReducersList()
    {
        // Draw a box for the reducers list using GUI.skin.box like ServerDataWindow
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("   Server Reducers", titleStyle);

        // Add helper message about parameters if we have reducers with parameters
        bool hasReducersWithParams = false;
        foreach (var reducer in reducers)
        {
            if (reducer.GetParameters().Count > 0)
            {
                hasReducersWithParams = true;
                break;
            }
        }
        if (hasReducersWithParams)
        {
            EditorGUILayout.LabelField("Fill in parameter values before running a reducer.\n"+
            "For complex parametres like Ref, enter values in the appropriate format.", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(30));
        }
        
        // Begin the scrollview for the reducers
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        if (reducers.Count == 0)
        {
            EditorGUILayout.HelpBox("No reducers found. Click 'Refresh Reducers' to fetch available reducers.\n" +
            "If you are running a local server, make sure it is running and the module is loaded.", MessageType.Info);
        }
        else
        {
            foreach (var reducer in reducers)
            {
                DrawReducerItem(reducer);
            }
        }
        
        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();
    }
    
    private void DrawReducerItem(ReducerInfo reducer)
    {
        // Container box for each reducer
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        // Header with name and lifecycle info
        EditorGUILayout.BeginHorizontal();
        string lifecycleInfo = reducer.IsLifecycleReducer() ? $" [{reducer.GetLifecycleType()}]" : "";
        EditorGUILayout.LabelField($" {reducer.name}{lifecycleInfo}", reducerTitleStyle, GUILayout.ExpandWidth(true));
        
        EditorGUILayout.Space(2);

        // Run button
        if (GUILayout.Button("RUN", cmdButtonStyle, GUILayout.Width(60), GUILayout.Height(30)))
        {
            RunReducer(reducer);
        }
        EditorGUILayout.EndHorizontal();
        
        // Parameters section
        List<ReducerParam> parameters = reducer.GetParameters();
        if (parameters.Count > 0)
        {
            EditorGUILayout.Space(2);
            
            // Parameters header
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(" Parameters", EditorStyles.label, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
            
            // Draw separator line
            //Rect separatorRect = EditorGUILayout.GetControlRect(false, 1);
            //separatorRect.height = 4;
            //EditorGUI.DrawRect(separatorRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
            
            for (int i = 0; i < parameters.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                
                // Parameter name and type with appropriate width
                string paramName = parameters[i].GetNameString();
                string paramType = parameters[i].GetTypeString();
                EditorGUILayout.LabelField($" {paramName}: {paramType}", 
                    EditorStyles.miniLabel, GUILayout.Width(200));
                
                // Initialize TagValue if null
                if (parameters[i].TagValue == null)
                {
                    parameters[i].TagValue = "";
                }
                
                // Different field types based on parameter type
                switch (paramType)
                {
                    case "Bool":
                        bool currentBool = false;
                        if (!string.IsNullOrEmpty(parameters[i].TagValue))
                        {
                            bool.TryParse(parameters[i].TagValue, out currentBool);
                        }
                        bool newBool = EditorGUILayout.Toggle(currentBool);
                        if (newBool != currentBool)
                        {
                            parameters[i].TagValue = newBool.ToString().ToLower();
                        }
                        break;
                        
                    case "U32":
                    case "I32":
                        int currentInt = 0;
                        if (!string.IsNullOrEmpty(parameters[i].TagValue))
                        {
                            int.TryParse(parameters[i].TagValue, out currentInt);
                        }
                        int newInt = EditorGUILayout.IntField(currentInt);
                        if (newInt != currentInt)
                        {
                            parameters[i].TagValue = newInt.ToString();
                        }
                        break;
                        
                    case "F32":
                        float currentFloat = 0f;
                        if (!string.IsNullOrEmpty(parameters[i].TagValue))
                        {
                            float.TryParse(parameters[i].TagValue, out currentFloat);
                        }
                        float newFloat = EditorGUILayout.FloatField(currentFloat);
                        if (newFloat != currentFloat)
                        {
                            parameters[i].TagValue = newFloat.ToString();
                        }
                        break;
                        
                    default:
                        // For String and complex types, use text field
                        parameters[i].TagValue = EditorGUILayout.TextField(parameters[i].TagValue);
                        break;
                }
                
                EditorGUILayout.EndHorizontal();
                
                // Draw separator between parameters (except last one)
                if (i < parameters.Count - 1)
                {
                    Rect paramSeparatorRect = EditorGUILayout.GetControlRect(false, 1);
                    paramSeparatorRect.height = 1;
                    EditorGUI.DrawRect(paramSeparatorRect, new Color(0.5f, 0.5f, 0.5f, 0.05f));
                }

                EditorGUILayout.Space(2);
            }
        }
        else
        {
            EditorGUILayout.LabelField("", EditorStyles.miniLabel);
        }
        
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }
    #endregion
    
    #region Load Settings
    private void LoadSettings()
    {
        // Load settings from EditorPrefs (shared with ServerWindow)
        serverURL = EditorPrefs.GetString(PrefsKeyPrefix + "ServerURL", serverURL);
        moduleName = EditorPrefs.GetString(PrefsKeyPrefix + "ModuleName", moduleName);
        authToken = EditorPrefs.GetString(PrefsKeyPrefix + "AuthToken", authToken);
        
        // If URL doesn't have a protocol, add http://
        if (!string.IsNullOrEmpty(serverURL) && !serverURL.StartsWith("http"))
        {
            serverURL = "http://" + serverURL;
        }
        
        // Add default port and API path if missing
        if (!string.IsNullOrEmpty(serverURL))
        {
            Uri uri;
            if (Uri.TryCreate(serverURL, UriKind.Absolute, out uri))
            {
                if (uri.Segments.Length == 1)
                {
                    // Add v1 path if missing
                    serverURL = serverURL.TrimEnd('/') + "/v1";
                }
            }
        }
        maincloudUrl = EditorPrefs.GetString(PrefsKeyPrefix + "MaincloudURL", "https://maincloud.spacetimedb.com/");
        serverMode = EditorPrefs.GetString(PrefsKeyPrefix + "ServerMode", "");
        
        if (!string.IsNullOrEmpty(maincloudUrl))
        {
            Uri uri;
            if (Uri.TryCreate(maincloudUrl, UriKind.Absolute, out uri))
            {
                if (uri.Segments.Length == 1)
                {
                    // Add v1 path if missing
                    maincloudUrl = maincloudUrl.TrimEnd('/') + "/v1";
                }
            }
        }
        customServerUrl = EditorPrefs.GetString(PrefsKeyPrefix + "CustomServerURL", "");
        if (!string.IsNullOrEmpty(customServerUrl))
        {
            Uri uri;
            if (Uri.TryCreate(customServerUrl, UriKind.Absolute, out uri))
            {
                if (uri.Segments.Length == 1)
                {
                    // Add v1 path if missing
                    customServerUrl = customServerUrl.TrimEnd('/') + "/v1";
                }
            }
        }
    }
    
    private void RefreshReducers()
    {
        if (isRefreshing) return;

        LoadSettings();

        if (serverMode == "WslServer")
        {
            if (string.IsNullOrEmpty(serverURL) || string.IsNullOrEmpty(moduleName))
            {
                SetStatus("WSL Server URL and Module Name are required.", Color.red);
                return;
            }
        } else if (serverMode == "CustomServer")
        {
            if (string.IsNullOrEmpty(customServerUrl) || string.IsNullOrEmpty(moduleName))
            {
                SetStatus("Custom Server URL and Module Name are required.", Color.red);
                return;
            }
        } else if (serverMode == "MaincloudServer")
        {
            if (string.IsNullOrEmpty(maincloudUrl) || string.IsNullOrEmpty(moduleName))
            {
                SetStatus("Maincloud URL and Module Name are required.", Color.red);
                return;
            }
        }
        
        isRefreshing = true;
        SetStatus("Fetching reducers from server...", Color.yellow);
        
        StartCoroutineOwnerless(RefreshReducersCoroutine((success, message) => {
            isRefreshing = false;
            if (success)
            {
                SetStatus($"Found {reducers.Count} reducers.", Color.green);
            }
            else
            {
                SetStatus("Error: " + message, Color.red);
            }
            Repaint();
        }));
    }
    
    private IEnumerator RefreshReducersCoroutine(Action<bool, string> callback)
    {
        // Move try-catch outside of the iterator
        // Add version parameter to match ServerDataWindow's schema request

        if (serverMode == "WslServer"){
            schemaUrl = $"{serverURL}/database/{moduleName}/schema?version=9";
        } else if (serverMode == "CustomServer") {
            schemaUrl = $"{customServerUrl}/database/{moduleName}/schema?version=9";
        } else if (serverMode == "MaincloudServer") {
            schemaUrl = $"{maincloudUrl}/database/{moduleName}/schema?version=9";
        }

        var request = new HttpRequestMessage(HttpMethod.Get, schemaUrl);
        
        // Add authorization if token is available
        if (!string.IsNullOrEmpty(authToken))
        {
            request.Headers.Add("Authorization", $"Bearer {authToken}");
        }
        
        // Send the request - keep yield outside try-catch
        var sendTask = httpClient.SendAsync(request);
        while (!sendTask.IsCompleted)
        {
            yield return null;
        }
        
        // Handle task result - wrap just the result access in try-catch
        HttpResponseMessage response;
        try
        {
            if (sendTask.IsFaulted)
            {
                callback(false, $"Request failed: {sendTask.Exception.InnerException.Message}");
                yield break;
            }
            response = sendTask.Result;
        }
        catch (Exception ex)
        {
            callback(false, $"Error accessing response: {ex.Message}");
            yield break;
        }
        
        // Read response content - again keep yield outside try-catch
        var readTask = response.Content.ReadAsStringAsync();
        while (!readTask.IsCompleted)
        {
            yield return null;
        }
        
        // Get content with error handling
        string responseContent;
        try
        {
            responseContent = readTask.Result;
        }
        catch (Exception ex)
        {
            callback(false, $"Error reading response: {ex.Message}");
            yield break;
        }
        
        if (!response.IsSuccessStatusCode)
        {
            callback(false, $"HTTP error {(int)response.StatusCode}: {response.ReasonPhrase}. {responseContent}");
            yield break;
        }
        
        // Parse schema response
        try
        {
            JObject schemaJson = JObject.Parse(responseContent);
            JArray reducersArray = schemaJson["reducers"] as JArray;
            
            if (reducersArray != null)
            {
                reducers.Clear();
                foreach (JToken reducerToken in reducersArray)
                {
                    try
                    {
                        ReducerInfo reducer = reducerToken.ToObject<ReducerInfo>();
                        reducers.Add(reducer);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error parsing reducer: {ex.Message}");
                    }
                }
                
                // Sort reducers by name
                reducers.Sort((a, b) => a.name.CompareTo(b.name));
                
                callback(true, $"Found {reducers.Count} reducers");
            }
            else
            {
                callback(false, "Unable to parse reducers from schema (not an array)");
            }
        }
        catch (Exception ex)
        {
            callback(false, $"Error parsing schema: {ex.Message}");
        }
    }
    
    private void RunReducer(ReducerInfo reducer)
    {
        LoadSettings();

        if (serverMode == "WslServer")
        {
            if (string.IsNullOrEmpty(serverURL) || string.IsNullOrEmpty(moduleName))
            {
                SetStatus("WSL Server URL and Module Name are required.", Color.red);
                return;
            }
        } else if (serverMode == "CustomServer")
        {
            if (string.IsNullOrEmpty(customServerUrl) || string.IsNullOrEmpty(moduleName))
            {
                SetStatus("Custom Server URL and Module Name are required.", Color.red);
                return;
            }
        } else if (serverMode == "MaincloudServer")
        {
            if (string.IsNullOrEmpty(maincloudUrl) || string.IsNullOrEmpty(moduleName))
            {
                SetStatus("Maincloud URL and Module Name are required.", Color.red);
                return;
            }
        }
        
        if (string.IsNullOrEmpty(authToken))
        {
            SetStatus("Authentication token is required to run reducers.", Color.red);
            return;
        }
        
        SetStatus($"Running reducer '{reducer.name}'...", Color.yellow);
        
        // Get parameter values
        List<ReducerParam> parameters = reducer.GetParameters();
        List<object> paramValues = new List<object>();
        
        foreach (var param in parameters)
        {
            // Convert parameter value based on type
            string paramType = param.GetTypeString();
            string paramValue = param.TagValue ?? "";
            
            object convertedValue;
            switch (paramType)
            {
                case "String":
                    convertedValue = paramValue;
                    break;
                case "U32":
                case "I32":
                    int intValue;
                    if (int.TryParse(paramValue, out intValue))
                    {
                        convertedValue = intValue;
                    }
                    else
                    {
                        SetStatus($"Invalid {paramType} value for parameter '{param.GetNameString()}'", Color.red);
                        return;
                    }
                    break;
                case "F32":
                    float floatValue;
                    if (float.TryParse(paramValue, out floatValue))
                    {
                        convertedValue = floatValue;
                    }
                    else
                    {
                        SetStatus($"Invalid {paramType} value for parameter '{param.GetNameString()}'", Color.red);
                        return;
                    }
                    break;
                case "Bool":
                    bool boolValue;
                    if (bool.TryParse(paramValue, out boolValue))
                    {
                        convertedValue = boolValue;
                    }
                    else
                    {
                        SetStatus($"Invalid Bool value for parameter '{param.GetNameString()}'", Color.red);
                        return;
                    }
                    break;
                default:
                    // For complex types, pass as string and let server handle parsing
                    convertedValue = paramValue;
                    break;
            }
            
            paramValues.Add(convertedValue);
        }
        
        // Call the reducer with parameters
        StartCoroutineOwnerless(RunReducerCoroutine(reducer.name, paramValues, (success, message) => {
            if (success)
            {
                SetStatus($"Reducer '{reducer.name}' executed successfully: {message}", Color.green);
            }
            else
            {
                SetStatus($"Reducer '{reducer.name}' failed: {message}", Color.red);
                Debug.LogError($"Reducer '{reducer.name}' failed: {message}");
            }
            Repaint();
        }));
    }
    
    private IEnumerator RunReducerCoroutine(string reducerName, List<object> parameters, Action<bool, string> callback)
    {
        if (serverMode == "WslServer"){
            reducerUrl = $"{serverURL}/database/{moduleName}/call/{reducerName}";
        } else if (serverMode == "CustomServer") {
            reducerUrl = $"{customServerUrl}/database/{moduleName}/call/{reducerName}";
        } else if (serverMode == "MaincloudServer") {
            reducerUrl = $"{maincloudUrl}/database/{moduleName}/call/{reducerName}";
        }
        // Prepare request
        var request = new HttpRequestMessage(HttpMethod.Post, reducerUrl);
        
        // Add authorization header
        request.Headers.Add("Authorization", $"Bearer {authToken}");
        
        // Add parameters as JSON array in request body
        string jsonParameters;
        try
        {
            jsonParameters = JsonConvert.SerializeObject(parameters);
            request.Content = new StringContent(jsonParameters, Encoding.UTF8, "application/json");
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }
        catch (Exception ex)
        {
            callback(false, $"Error serializing parameters: {ex.Message}");
            yield break;
        }
        
        // Send the request - keep yield outside try-catch
        var sendTask = httpClient.SendAsync(request);
        while (!sendTask.IsCompleted)
        {
            yield return null;
        }
        
        // Handle task result - wrap just the result access in try-catch
        HttpResponseMessage response;
        try
        {
            if (sendTask.IsFaulted)
            {
                callback(false, $"Request failed: {sendTask.Exception.InnerException.Message}");
                yield break;
            }
            response = sendTask.Result;
        }
        catch (Exception ex)
        {
            callback(false, $"Error accessing response: {ex.Message}");
            yield break;
        }
        
        // Read response content - again keep yield outside try-catch
        var readTask = response.Content.ReadAsStringAsync();
        while (!readTask.IsCompleted)
        {
            yield return null;
        }
        
        // Get content with error handling
        string responseContent;
        try
        {
            responseContent = readTask.Result;
        }
        catch (Exception ex)
        {
            callback(false, $"Error reading response: {ex.Message}");
            yield break;
        }
        
        if (!response.IsSuccessStatusCode)
        {
            callback(false, $"HTTP error {(int)response.StatusCode}: {response.ReasonPhrase}. {TryParseSpacetimeError(responseContent)}");
            yield break;
        }
        
        // Return success with response content (if any)
        callback(true, string.IsNullOrEmpty(responseContent) ? "No response" : responseContent);
    }
    
    private string TryParseSpacetimeError(string responseBody)
    {
        try
        {
            // Try to parse as JSON to extract any error message
            JObject errorJson = JObject.Parse(responseBody);
            
            // Check for common SpacetimeDB error patterns
            if (errorJson["error"] != null)
            {
                return errorJson["error"].ToString();
            }
            else if (errorJson["message"] != null)
            {
                return errorJson["message"].ToString();
            }
            else
            {
                // Return formatted JSON if no specific error field found
                return JsonConvert.SerializeObject(errorJson, Formatting.Indented);
            }
        }
        catch
        {
            // If not valid JSON, return as is
            return responseBody;
        }
    }
    
    private void SetStatus(string message, Color color)
    {
        statusMessage = message;
        statusColor = color;
        statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
    }
    #endregion
    
    // Helper method to start coroutines from a static context
    private static EditorCoroutine StartCoroutineOwnerless(IEnumerator routine)
    {
        return EditorCoroutineUtility.StartCoroutineOwnerless(routine);
    }
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games