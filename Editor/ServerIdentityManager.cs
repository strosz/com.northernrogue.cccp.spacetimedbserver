using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

namespace NorthernRogue.CCCP.Editor
{
    public enum IdentityType
    {
        Unknown,
        OfflineServerIssued,
        SSOAuthenticated
    }

    public class ServerIdentityManager
    {
        public string Identity;
        public string Token;
        public IdentityType Type;
        public string Issuer;
        public DateTime? ExpiresAt;
        public string Algorithm;

        // Persistent identity state keys
        private const string EditorPrefsKeyIdentityType = "ServerIdentity_Type";
        private const string EditorPrefsKeyIdentity = "ServerIdentity_Identity";
        private const string EditorPrefsKeyLastCheck = "ServerIdentity_LastCheck";

        /// <summary>
        /// Detects the identity type from a JWT token
        /// </summary>
        public static IdentityType DetectIdentityType(string token)
        {
            try
            {
                // JWT format: header.payload.signature
                var parts = token.Split('.');
                if (parts.Length != 3) return IdentityType.Unknown;

                // Decode payload (Base64Url)
                var payloadJson = DecodeBase64Url(parts[1]);

                // Check issuer - most reliable method
                if (payloadJson.Contains("\"iss\":\"https://auth.spacetimedb.com\""))
                {
                    return IdentityType.SSOAuthenticated;
                }
                else if (payloadJson.Contains("\"iss\":\"localhost\"") || 
                         payloadJson.Contains("\"iss\":\"http"))
                {
                    return IdentityType.OfflineServerIssued;
                }

                return IdentityType.Unknown;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerIdentityManager] Failed to detect identity type: {ex.Message}");
                return IdentityType.Unknown;
            }
        }

        /// <summary>
        /// Parses full identity information from a JWT token
        /// </summary>
        public static ServerIdentityManager ParseServerIdentityManager(string token, string identity)
        {
            var info = new ServerIdentityManager
            {
                Identity = identity,
                Token = token,
                Type = IdentityType.Unknown
            };

            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return info;

                // Decode header
                var headerJson = DecodeBase64Url(parts[0]);

                // Decode payload
                var payloadJson = DecodeBase64Url(parts[1]);

                // Extract algorithm from header
                var algMatch = Regex.Match(headerJson, @"""alg""\s*:\s*""([^""]+)""");
                if (algMatch.Success)
                {
                    info.Algorithm = algMatch.Groups[1].Value;
                }

                // Extract issuer from payload
                var issMatch = Regex.Match(payloadJson, @"""iss""\s*:\s*""([^""]+)""");
                if (issMatch.Success)
                {
                    info.Issuer = issMatch.Groups[1].Value;

                    if (info.Issuer.Contains("auth.spacetimedb.com"))
                    {
                        info.Type = IdentityType.SSOAuthenticated;
                    }
                    else if (info.Issuer.Contains("localhost") || info.Issuer.StartsWith("http"))
                    {
                        info.Type = IdentityType.OfflineServerIssued;
                    }
                }

                // Extract expiration from payload
                var expMatch = Regex.Match(payloadJson, @"""exp""\s*:\s*(\d+|null)");
                if (expMatch.Success && expMatch.Groups[1].Value != "null")
                {
                    var expUnix = long.Parse(expMatch.Groups[1].Value);
                    info.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).DateTime;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerIdentityManager] Failed to parse token: {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// Decodes a Base64Url encoded string
        /// </summary>
        private static string DecodeBase64Url(string base64Url)
        {
            // Pad if necessary
            var padded = base64Url.PadRight(base64Url.Length + (4 - base64Url.Length % 4) % 4, '=');
            // Replace URL-safe characters with standard Base64 characters
            var base64 = padded.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Gets a user-friendly type name
        /// </summary>
        public string GetTypeName()
        {
            switch (Type)
            {
                case IdentityType.SSOAuthenticated:
                    return "SSO Authenticated, Ready to Publish";
                case IdentityType.OfflineServerIssued:
                    return "Offline/Server-Issued";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// Gets an icon for the identity type
        /// </summary>
        public string GetTypeIcon()
        {
            switch (Type)
            {
                case IdentityType.SSOAuthenticated:
                    return "🔒";
                case IdentityType.OfflineServerIssued:
                    return "⚠";
                default:
                    return "❓";
            }
        }

        /// <summary>
        /// Gets a description for the identity type
        /// </summary>
        public string GetTypeDescription()
        {
            switch (Type)
            {
                case IdentityType.SSOAuthenticated:
                    return "This identity is verified through SpacetimeDB SSO and can be recovered if lost.\n\nAlways publish using SSO if you have an Internet connection.";
                case IdentityType.OfflineServerIssued:
                    return "This identity is offline CLI server-issued and cannot be recovered if lost. Please login with SpacetimeDB SSO if you have an Internet connection.";
                default:
                    return "Unable to determine identity type.";
            }
        }

        /// <summary>
        /// Gets expiration info as a formatted string
        /// </summary>
        public string GetExpirationInfo()
        {
            if (ExpiresAt.HasValue)
            {
                return $"Expires: {ExpiresAt.Value:MMM dd, yyyy}";
            }
            else
            {
                return "Never expires";
            }
        }

        /// <summary>
        /// Saves the current identity type to persistent storage
        /// </summary>
        public static void SaveIdentityState(IdentityType type, string identity)
        {
            EditorPrefs.SetInt(EditorPrefsKeyIdentityType, (int)type);
            EditorPrefs.SetString(EditorPrefsKeyIdentity, identity ?? "");
            EditorPrefs.SetString(EditorPrefsKeyLastCheck, DateTime.Now.ToString("o"));
        }

        /// <summary>
        /// Gets the saved identity type from persistent storage
        /// </summary>
        public static IdentityType GetSavedIdentityType()
        {
            return (IdentityType)EditorPrefs.GetInt(EditorPrefsKeyIdentityType, (int)IdentityType.Unknown);
        }

        /// <summary>
        /// Gets the saved identity string from persistent storage
        /// </summary>
        public static string GetSavedIdentity()
        {
            return EditorPrefs.GetString(EditorPrefsKeyIdentity, "");
        }

        /// <summary>
        /// Gets when the identity was last checked
        /// </summary>
        public static DateTime? GetLastCheckTime()
        {
            string lastCheckStr = EditorPrefs.GetString(EditorPrefsKeyLastCheck, "");
            if (DateTime.TryParse(lastCheckStr, out DateTime lastCheck))
            {
                return lastCheck;
            }
            return null;
        }

        /// <summary>
        /// Clears all saved identity state
        /// </summary>
        public static void ClearSavedIdentityState()
        {
            EditorPrefs.DeleteKey(EditorPrefsKeyIdentityType);
            EditorPrefs.DeleteKey(EditorPrefsKeyIdentity);
            EditorPrefs.DeleteKey(EditorPrefsKeyLastCheck);
        }

        /// <summary>
        /// Fetches CLI identity using "spacetime login show --token"
        /// </summary>
        public static async Task<(string identity, string token, ServerIdentityManager info, string statusMessage)> FetchCliIdentityAsync(ServerManager serverManager, bool debugMode = false)
        {
            try
            {
                var result = await ExecuteServerCommandAsync(serverManager, "spacetime login show --token", debugMode);
                
                if (result.success && !string.IsNullOrEmpty(result.output))
                {
                    string output = result.output;
                    string cliIdentity = "";
                    string cliAuthToken = "";
                    
                    // Extract identity (first line after "logged in as")
                    var identityMatch = Regex.Match(output, @"logged in as\s+([a-fA-F0-9]+)");
                    if (identityMatch.Success)
                    {
                        cliIdentity = identityMatch.Groups[1].Value;
                        if (debugMode)
                            Debug.Log($"[ServerIdentityManager] Extracted CLI identity: {cliIdentity}");
                    }
                    
                    // Extract auth token (second line after "auth token ... is")
                    var tokenMatch = Regex.Match(output, @"auth token[^\n]*is\s+([^\s]+)", RegexOptions.Singleline);
                    if (tokenMatch.Success)
                    {
                        cliAuthToken = tokenMatch.Groups[1].Value.Trim();
                        if (debugMode)
                            Debug.Log($"[ServerIdentityManager] Extracted CLI auth token (length: {cliAuthToken.Length})");
                    }
                    
                    // Parse identity info from token
                    ServerIdentityManager info = null;
                    if (!string.IsNullOrEmpty(cliAuthToken) && !string.IsNullOrEmpty(cliIdentity))
                    {
                        info = ParseServerIdentityManager(cliAuthToken, cliIdentity);
                        
                        // Save identity state persistently
                        SaveIdentityState(info.Type, cliIdentity);
                        
                        if (debugMode)
                            Debug.Log($"[ServerIdentityManager] Parsed identity type: {info.Type}, Issuer: {info.Issuer}");
                    }
                    
                    string statusMessage = !string.IsNullOrEmpty(cliIdentity) 
                        ? "CLI identity loaded successfully" 
                        : "No CLI identity logged in";
                    
                    return (cliIdentity, cliAuthToken, info, statusMessage);
                }
                else
                {
                    string statusMessage = !string.IsNullOrEmpty(result.error) 
                        ? $"Error: {result.error}" 
                        : "Failed to fetch CLI identity";
                    
                    if (debugMode)
                        Debug.LogWarning($"[ServerIdentityManager] Failed to fetch CLI identity. Output: {result.output}, Error: {result.error}");
                    
                    return ("", "", null, statusMessage);
                }
            }
            catch (Exception ex)
            {
                if (debugMode)
                    Debug.LogError($"[ServerIdentityManager] Exception fetching CLI identity: {ex}");
                
                return ("", "", null, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches server database identity using "spacetime list"
        /// </summary>
        public static async Task<(string serverIdentity, string[] databaseIdentities, string statusMessage)> FetchServerIdentityAsync(ServerManager serverManager, bool debugMode = false)
        {
            try
            {
                var result = await ExecuteServerCommandAsync(serverManager, "spacetime list", debugMode);
                
                if (result.success && !string.IsNullOrEmpty(result.output))
                {
                    string output = result.output;
                    string serverIdentity = "";
                    string[] databaseIdentities = new string[0];
                    
                    // Extract the server identity from "Associated database identities for <identity>:"
                    var serverIdentityMatch = Regex.Match(output, @"Associated database identities for\s+([a-fA-F0-9]{64})");
                    if (serverIdentityMatch.Success)
                    {
                        serverIdentity = serverIdentityMatch.Groups[1].Value;
                        
                        if (debugMode)
                            Debug.Log($"[ServerIdentityManager] Extracted server identity: {serverIdentity}");
                    }
                    
                    // Extract all database identities (64-character hex strings, one per line)
                    // Match all lines that contain only whitespace and a 64-character hex string
                    var databaseMatches = Regex.Matches(output, @"^\s*([a-fA-F0-9]{64})\s*$", RegexOptions.Multiline);
                    if (databaseMatches.Count > 0)
                    {
                        databaseIdentities = new string[databaseMatches.Count];
                        for (int i = 0; i < databaseMatches.Count; i++)
                        {
                            databaseIdentities[i] = databaseMatches[i].Groups[1].Value;
                        }
                        
                        if (debugMode)
                            Debug.Log($"[ServerIdentityManager] Extracted {databaseIdentities.Length} database identities: {string.Join(", ", databaseIdentities)}");
                    }
                    
                    // Set status message
                    string statusMessage;
                    if (!string.IsNullOrEmpty(serverIdentity))
                    {
                        if (databaseIdentities.Length > 0)
                        {
                            statusMessage = $"Server identity loaded with {databaseIdentities.Length} database(s)";
                        }
                        else
                        {
                            statusMessage = "Server identity loaded (no databases published yet)";
                        }
                    }
                    else
                    {
                        statusMessage = "No server identity found - no databases published yet";
                    }
                    
                    return (serverIdentity, databaseIdentities, statusMessage);
                }
                else
                {
                    string statusMessage = !string.IsNullOrEmpty(result.error) 
                        ? $"Error: {result.error}" 
                        : "Failed to fetch server identity";
                    
                    if (debugMode)
                        Debug.LogWarning($"[ServerIdentityManager] Failed to fetch server identity. Output: {result.output}, Error: {result.error}");
                    
                    return ("", new string[0], statusMessage);
                }
            }
            catch (Exception ex)
            {
                if (debugMode)
                    Debug.LogError($"[ServerIdentityManager] Exception fetching server identity: {ex}");
                
                return ("", new string[0], $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes a server command using the appropriate processor (Docker or WSL)
        /// </summary>
        private static async Task<(string output, string error, bool success)> ExecuteServerCommandAsync(ServerManager serverManager, string command, bool debugMode = false)
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
                    Debug.LogError($"[ServerIdentityManager] Exception executing command: {ex}");
                return ("", ex.Message, false);
            }
        }
    }
}

// made by Mathias Toivonen at Northern Rogue Games
