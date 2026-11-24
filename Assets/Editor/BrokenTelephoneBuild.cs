using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BrokenTelephone.EditorTools
{
    /// <summary>
    /// Web release settings + one-click WebGL build into ../WebBuild (served by Server/server.js).
    /// Adapted from Unity's "Use C# code to enable optimization settings" (Web optimization manual).
    /// </summary>
    public static class BrokenTelephoneBuild
    {
        const string OutputDir = "WebBuild";
        // Assets/WebGLTemplates/FullWindow: canvas fills the whole browser window and follows resizes.
        const string Template = "PROJECT:FullWindow";

        [MenuItem("Broken Telephone/Apply Web Release Settings")]
        public static void ApplyWebReleaseSettings()
        {
            var target = NamedBuildTarget.WebGL;
            PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.High);
            PlayerSettings.SetApiCompatibilityLevel(target, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.stripUnusedMeshComponents = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.template = Template;
            PlayerSettings.WebGL.dataCaching = true;
            // server.js sends Content-Encoding: br, so no JS decompressor is needed (saves ~150 KB + startup time).
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            // Explicitly-thrown only (not None): JsonUtility/IO failures stay catchable instead of aborting the page.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            // UI-only game: small initial heap avoids a large upfront allocation on mobile Safari.
            PlayerSettings.WebGL.initialMemorySize = 32;
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;

            // Use the cheapest URP quality level ("Mobile" in the URP template) as the Web default.
            int mobile = System.Array.IndexOf(QualitySettings.names, "Mobile");
            if (mobile >= 0) QualitySettings.SetQualityLevel(mobile, true);

            AssetDatabase.SaveAssets();

            Debug.Log("[BT] Web release settings applied and saved:\n"
                + $"  il2cppCodeGeneration = {PlayerSettings.GetIl2CppCodeGeneration(target)}\n"
                + $"  managedStrippingLevel = {PlayerSettings.GetManagedStrippingLevel(target)}\n"
                + $"  apiCompatibilityLevel = {PlayerSettings.GetApiCompatibilityLevel(target)}\n"
                + $"  stripEngineCode = {PlayerSettings.stripEngineCode}\n"
                + $"  compressionFormat = {PlayerSettings.WebGL.compressionFormat}\n"
                + $"  decompressionFallback = {PlayerSettings.WebGL.decompressionFallback}\n"
                + $"  exceptionSupport = {PlayerSettings.WebGL.exceptionSupport}\n"
                + $"  debugSymbolMode = {PlayerSettings.WebGL.debugSymbolMode}\n"
                + $"  dataCaching = {PlayerSettings.WebGL.dataCaching}\n"
                + $"  memoryGrowthMode = {PlayerSettings.WebGL.memoryGrowthMode}, initial = {PlayerSettings.WebGL.initialMemorySize} MB\n"
                + $"  codeOptimization = {UnityEditor.WebGL.UserBuildSettings.codeOptimization} (per-machine, stored in Library/)\n"
                + $"  template = {PlayerSettings.WebGL.template}\n"
                + $"  quality = {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
        }

        [MenuItem("Broken Telephone/Build WebGL")]
        public static void BuildWebGL()
        {
            ApplyWebReleaseSettings();
            var scenes = new[] { "Assets/Scenes/SampleScene.unity" };
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputDir));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[BT] WebGL build {s.result}: {s.totalSize / 1024f / 1024f:F2} MB in {s.totalTime.TotalSeconds:F0}s -> {output}\n"
                + "Run it:  cd Server && npm install && npm start   then open http://localhost:8080");
        }

        [MenuItem("Broken Telephone/Development Build WebGL (fast)")]
        public static void BuildWebGLDev()
        {
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputDir));
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.BuildTimes;
            PlayerSettings.WebGL.template = Template;
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/SampleScene.unity" }, output, BuildTarget.WebGL, BuildOptions.Development);
            Debug.Log($"[BT] Dev WebGL build {report.summary.result} -> {output}");
        }
    }
}
