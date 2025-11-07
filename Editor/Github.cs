using UnityEngine;
using UnityEditor;

namespace NorthernRogue.CCCP.Editor {

public static class Github
{
    // For the free github release of this asset we want to display this message at the welcome screen
    public static void WelcomeWindow()
    {
        EditorGUILayout.Space(30);

        string donationText = 
        "If you like this project, please consider buying\n the fully supported Asset Store version.\n\n";

        EditorGUILayout.LabelField(donationText, EditorStyles.centeredGreyMiniLabel, GUILayout.Height(50));
        
        EditorGUILayout.Space(-25);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Asset Store Version", GUILayout.Height(30), GUILayout.Width(200)))
        {
            Application.OpenURL("https://assetstore.unity.com/packages/tools/network/cosmos-cove-create-mmos-with-spacetimedb-330714");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }
} // Class
} // Namespace