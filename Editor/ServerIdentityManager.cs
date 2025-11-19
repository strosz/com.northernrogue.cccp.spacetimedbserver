using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

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
                    return "SSO Authenticated";
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
                    return "This identity is verified through SpacetimeDB SSO and can be recovered if lost.";
                case IdentityType.OfflineServerIssued:
                    return "This identity is server-issued and cannot be recovered if lost. Please login with SpacetimeDB SSO.";
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
    }
}

// made by Mathias Toivonen at Northern Rogue Games
