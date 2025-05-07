using UnityEngine;
using UnityEditor;

namespace NorthernRogue.CCCP.Editor {

public static class Github
{
    public static void WelcomeWindow()
    {
        EditorGUILayout.Space(30);

        string donationText = 
        "If you like this project,\nplease consider buying me a coffee.\n\n";

        EditorGUILayout.LabelField(donationText, EditorStyles.centeredGreyMiniLabel, GUILayout.Height(50));
        
        EditorGUILayout.Space(-25);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Buy me a coffee", GUILayout.Height(30), GUILayout.Width(200)))
        {
            Application.OpenURL("https://ko-fi.com/northernrogue");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }
} // Class
} // Namespace