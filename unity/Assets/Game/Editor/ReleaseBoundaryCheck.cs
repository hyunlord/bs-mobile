using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEngine;
namespace Game.Editor
{
    public static class ReleaseBoundaryCheck
    {
        public static void Run()
        {
            var output=Path.Combine(FoundationBuild.RepoRoot,"artifacts/unity/release-scripts");
            if(Directory.Exists(output))Directory.Delete(output,true);
            Directory.CreateDirectory(output);
            var result=PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings {target=BuildTarget.Android,group=BuildTargetGroup.Android,options=ScriptCompilationOptions.None},output);
            if(result.assemblies==null||result.assemblies.Count==0)throw new BuildFailedException("Release script compilation produced no assemblies.");
            var app=Path.Combine(output,"Game.App.dll");
            if(!File.Exists(app))throw new BuildFailedException("Release App assembly missing.");
            Debug.Log("UNITY_RELEASE_SCRIPTS_COMPILED");
        }
    }
}
