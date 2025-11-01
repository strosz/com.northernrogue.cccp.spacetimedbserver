using UnityEngine;
using UnityEditor;

namespace NorthernRogue.CCCP.Editor {

public static class AssetStore
{
    // For the Asset Store release of this asset we want to display this message at the welcome screen
    public static void WelcomeWindow()
    {
        EditorGUILayout.Space(30);

        string assetStoreText = 
        "Thank you for purchasing this asset\nfrom the Unity Asset Store!\n\n";

        EditorGUILayout.LabelField(assetStoreText, EditorStyles.centeredGreyMiniLabel, GUILayout.Height(50));
        
        EditorGUILayout.Space(-25);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Request Support", GUILayout.Height(30), GUILayout.Width(200)))
        {
            OpenSupportPDF();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private static void OpenSupportPDF()
    {
        try
        {           
            // Get package info for direct path access (most efficient)
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(System.Reflection.Assembly.GetExecutingAssembly());
            if (packageInfo != null)
            {
                string pdfPath = System.IO.Path.Combine(packageInfo.assetPath, "Support.pdf");
                
                if (System.IO.File.Exists(pdfPath))
                {
                    // Use the system's file association to open the PDF (works on Windows, macOS, and Linux)
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = pdfPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                else
                {
                    Debug.LogWarning("Support.pdf not found at expected location: " + pdfPath);
                }
            }
            else
            {
                Debug.LogWarning("Could not find the package information needed in order to open Support.pdf.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Failed to open Support.pdf: " + ex.Message);
        }
    }
} // Class
} // Namespace
