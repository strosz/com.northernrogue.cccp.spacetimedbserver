using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NorthernRogue.CCCP.Editor.Settings;

// Loads the available reducers from the server schema and can call them with custom params ///

namespace NorthernRogue.CCCP.Editor {

public class ServerReducerWindow : EditorWindow
{
    public static bool debugMode = false;
    
    // Use NonSerialized to prevent Unity from trying to serialize the static instance
    [System.NonSerialized]
    private static ServerReducerWindow currentInstance;
    
    // Unique identifier for each window instance
    [SerializeField] 
    private string instanceId = System.Guid.NewGuid().ToString();
    
    // Static constructor for initialization
    static ServerReducerWindow()
    {
        if (debugMode) Debug.Log("[ServerReducerWindow] Static constructor called");
    }

    private string serverURL = "http://127.0.0.1:3000/v1"; // Default, should be overridden
    private string moduleName = "magical"; // Default, should be overridden
    private string authToken = ""; // Required for authentication, to be able to run reducers and not just read schema

    private string customServerUrl = "";
    private string customServerAuthToken = "";
    private string maincloudUrl = "";
    private string maincloudAuthToken = "";
    private string serverUrlDocker = "";
    private string authTokenDocker = "";
    private string serverMode = "";

    private string schemaUrl = "";
    private string reducerUrl = "";

    // List of reducers from schema
    private List<ReducerInfo> reducers = new List<ReducerInfo>();
    private bool isRefreshing = false;
    private bool hasReducersWithParams = false;

    // UI
    private Vector2 scrollPosition;
    private string statusMessage = "Ready. Load settings via ServerWindow and Refresh.";
    private Color statusColor = Color.grey; // Dynamic color based on status
    private string statusTimestamp = DateTime.Now.ToString("HH:mm:ss");
    
    // Styles
    private GUIStyle titleStyle;
    private GUIStyle reducerTitleStyle;
    private GUIStyle runButtonStyle;
    private bool stylesInitialized = false;
    
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

        [NonSerialized]
        private List<ReducerParam> _cachedParameters = null;
        
        public List<ReducerParam> GetParameters()
        {
            if (_cachedParameters != null)
            {
                return _cachedParameters;
            }

            _cachedParameters = new List<ReducerParam>();
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
                            // Ensure TagValue is initialized for new params, especially for text fields
                            if (param.TagValue == null)
                            {
                                param.TagValue = "";
                            }
                            _cachedParameters.Add(param);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error parsing reducer parameter: {ex.Message}");
                        }
                    }
                }
            }
            return _cachedParameters;
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
    
    #region Window Management
    [MenuItem("Window/SpacetimeDB Server Manager/Run Reducer")]
    public static void ShowWindow()
    {
        // Prevent opening windows during compilation
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (debugMode)
                Debug.LogWarning("[ServerReducerWindow] Prevented ShowWindow during compilation/updating");
            return;
        }
        
        // Use Unity's proper singleton pattern - GetWindow without utility flag
        // This reuses existing instances and only creates new ones if needed
        ServerReducerWindow window = GetWindow<ServerReducerWindow>();
        window.titleContent = new GUIContent("Run Server Reducer");
        window.minSize = new Vector2(500, 400);
        window.Show();
        
        // Ensure proper initialization
        if (string.IsNullOrEmpty(window.instanceId))
        {
            window.instanceId = System.Guid.NewGuid().ToString();
        }

        currentInstance = window;
        
        if (debugMode)
            Debug.Log($"[ServerReducerWindow] ShowWindow completed for instance {window.instanceId}");
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
            Debug.Log($"[ServerReducerWindow] OnEnable called for instance {instanceId}");
        
        LoadSettings();
        RefreshReducers();
    }

    private void OnDisable()
    {
        // Clear the current instance if this is it
        if (currentInstance == this)
        {
            currentInstance = null;
        }
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
        
        runButtonStyle = new GUIStyle(GUI.skin.button);
        runButtonStyle.fontSize = 10;
        runButtonStyle.normal.textColor = ServerUtilityProvider.ColorManager.RunButtonNormal;
        runButtonStyle.hover.textColor = ServerUtilityProvider.ColorManager.RunButtonHover;
        runButtonStyle.fontStyle = FontStyle.Bold;
        
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
        timeStyle.normal.textColor = ServerUtilityProvider.ColorManager.StatusTime; // Light grey
        timeStyle.alignment = TextAnchor.MiddleLeft;
        timeStyle.fontStyle = FontStyle.Italic;
        EditorGUILayout.LabelField(statusTimestamp, timeStyle, GUILayout.Width(60), GUILayout.Height(16));
        
        // Message section with status color
        GUIStyle msgStyle = new GUIStyle(EditorStyles.label);
        msgStyle.normal.textColor = statusColor; // Dynamic color based on status
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
        foreach (var reducer in reducers)
        {
            if (reducer.GetParameters().Count > 0)
            {
                hasReducersWithParams = true;
                break;
            }
            else
            {
                hasReducersWithParams = false;
                break;
            }
        }
        
        // Begin the scrollview for the reducers
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        if (reducers.Count == 0)
        {
            if (isRefreshing)
            {
                EditorGUILayout.LabelField("Refreshing...", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("Click 'Refresh' to load reducers from the server.", EditorStyles.miniLabel);
            }
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
        if (hasReducersWithParams)
        {
            GUIContent labelContent = new GUIContent($" {reducer.name}{lifecycleInfo}", "This reducer requires parameters. Fill them below before running.");
            EditorGUILayout.LabelField(labelContent, reducerTitleStyle, GUILayout.ExpandWidth(true));
        }
        else
        {
            EditorGUILayout.LabelField($" {reducer.name}{lifecycleInfo}", reducerTitleStyle, GUILayout.ExpandWidth(true));
        }
        
        EditorGUILayout.Space(2);

        // Run button
        if (GUILayout.Button("RUN", runButtonStyle, GUILayout.Width(60), GUILayout.Height(30)))
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
                    EditorGUI.DrawRect(paramSeparatorRect, ServerUtilityProvider.ColorManager.Separator);
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
    // Helper method to ensure server URL has /v1 suffix and handle localhost conversion
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
        // Load settings from Settings
        string rawServerUrl = CCCPSettingsAdapter.GetServerUrl();
        serverURL = GetApiBaseUrl(rawServerUrl);
        moduleName = CCCPSettingsAdapter.GetModuleName();
        authToken = CCCPSettingsAdapter.GetAuthToken();

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
    }
    
    public void RefreshReducers()
    {
        if (isRefreshing) return;

        LoadSettings();

        if (serverMode == "WSLServer")
        {
            if (string.IsNullOrEmpty(serverURL) || string.IsNullOrEmpty(moduleName))
            {
                SetStatus("Server URL and Module Name are required.", Color.red);
                return;
            }
        }
        else if (serverMode == "DockerServer")
        {
            if (string.IsNullOrEmpty(serverUrlDocker) || string.IsNullOrEmpty(moduleName))
            {
                SetStatus("Docker Server URL and Module Name are required.", Color.red);
                return;
            }
        }
        else if (serverMode == "CustomServer")
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

        if (serverMode == "WSLServer")
        {
            schemaUrl = $"{serverURL}/database/{moduleName}/schema?version=9";
        }
        else if (serverMode == "DockerServer")
        {
            schemaUrl = $"{serverUrlDocker}/database/{moduleName}/schema?version=9";
        }
        else if (serverMode == "CustomServer")
        {
            schemaUrl = $"{customServerUrl}/database/{moduleName}/schema?version=9";
        }
        else if (serverMode == "MaincloudServer")
        {
            schemaUrl = $"{maincloudUrl}/database/{moduleName}/schema?version=9";
        }

        var request = new HttpRequestMessage(HttpMethod.Get, schemaUrl);
        
        // Determine the correct auth token based on server mode
        string currentAuthToken = authToken;
        if (serverMode == "DockerServer")
            currentAuthToken = authTokenDocker;
        else if (serverMode == "CustomServer")
            currentAuthToken = customServerAuthToken;
        else if (serverMode == "MaincloudServer")
            currentAuthToken = maincloudAuthToken;
        
        // Add authorization if token is available
        if (!string.IsNullOrEmpty(currentAuthToken))
        {
            request.Headers.Add("Authorization", $"Bearer {currentAuthToken}");
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

        if (serverMode == "WSLServer" || serverMode == "DockerServer")
        {
            if (string.IsNullOrEmpty(serverURL) || string.IsNullOrEmpty(moduleName))
            {
                SetStatus("Server URL and Module Name are required.", Color.red);
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

        if ((CCCPSettingsAdapter.GetLocalCLIProvider() == "WSL" && string.IsNullOrEmpty(authToken)) ||
            (CCCPSettingsAdapter.GetLocalCLIProvider() == "Docker" && string.IsNullOrEmpty(authTokenDocker)) ||
            (serverMode == "CustomServer" && string.IsNullOrEmpty(customServerAuthToken)) ||
            (serverMode == "MaincloudServer" && string.IsNullOrEmpty(maincloudAuthToken)))
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
                string sentMsg = (paramValues != null && paramValues.Count > 0 && paramValues[0] != null)
                    ? $" Sent: {paramValues[0]}"
                    : "";
                SetStatus($"Reducer '{reducer.name}' successful!{sentMsg}", Color.green);
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
        if (serverMode == "WSLServer")
        {
            reducerUrl = $"{serverURL}/database/{moduleName}/call/{reducerName}";
        }
        else if (serverMode == "DockerServer")
        {
            reducerUrl = $"{serverUrlDocker}/database/{moduleName}/call/{reducerName}";
        }
        else if (serverMode == "CustomServer")
        {
            reducerUrl = $"{customServerUrl}/database/{moduleName}/call/{reducerName}";
        }
        else if (serverMode == "MaincloudServer")
        {
            reducerUrl = $"{maincloudUrl}/database/{moduleName}/call/{reducerName}";
        }
        
        // Prepare request
        var request = new HttpRequestMessage(HttpMethod.Post, reducerUrl);
        
        // Determine the correct auth token based on server mode
        string currentAuthToken = authToken;
        if (serverMode == "DockerServer")
            currentAuthToken = authTokenDocker;
        else if (serverMode == "CustomServer")
            currentAuthToken = customServerAuthToken;
        else if (serverMode == "MaincloudServer")
            currentAuthToken = maincloudAuthToken;
        
        // Add authorization header
        request.Headers.Add("Authorization", $"Bearer {currentAuthToken}");
        
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
        //Debug.Log($"Reducer: {jsonParameters}"); // Keep for debug
        
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