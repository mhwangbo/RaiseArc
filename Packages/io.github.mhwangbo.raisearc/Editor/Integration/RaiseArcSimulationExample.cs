using System.IO;
using PrincessStudio.Unity;
using UnityEditor;
using UnityEngine;

namespace RaiseArc.Editor
{
    public static class SimulationExample
    {
        public static void Create()
        {
            var project = new UnityProjectCodec().FromJson(File.ReadAllText("Assets/PrincessStudio/Samples/Data/SimulationExample.json"));
            if (!AssetDatabase.IsValidFolder("Assets/RaiseArcSimulationSample")) AssetDatabase.CreateFolder("Assets", "RaiseArcSimulationSample");
            var asset = ScriptableObject.CreateInstance<GameProjectAsset>(); asset.Write(project);
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath("Assets/RaiseArcSimulationSample/SimulationExample.asset"));
            AssetDatabase.SaveAssetIfDirty(asset); Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
        }
    }
}
