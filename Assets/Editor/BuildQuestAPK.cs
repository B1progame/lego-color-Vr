#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestLegoColorFinder.Editor
{
    public static class BuildQuestAPK
    {
        private const string DefaultOutputPath = "Builds/QuestColorFinder.apk";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tools/Quest Color Finder/Apply Recommended Project Settings")]
        public static void ApplyRecommendedProjectSettingsMenu()
        {
            ApplyRecommendedProjectSettings();
            Debug.Log("Quest Color Finder: recommended project settings applied.");
        }

        [MenuItem("Tools/Quest Color Finder/Build APK")]
        public static void BuildFromMenu()
        {
            string path = EditorUtility.SaveFilePanel("Build Quest APK", "Builds", "QuestColorFinder", "apk");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            BuildInternal(path);
        }

        public static void BuildFromCommandLine()
        {
            string outputPath = GetCommandLineArg("--outputPath");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), DefaultOutputPath);
            }

            BuildInternal(outputPath);
        }

        private static void BuildInternal(string outputPath)
        {
            if (!File.Exists(MainScenePath))
            {
                throw new FileNotFoundException("Main scene not found.", MainScenePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? "Builds");

            ApplyRecommendedProjectSettings();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainScenePath, true)
            };

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                target = BuildTarget.Android,
                locationPathName = outputPath,
                options = BuildOptions.None
            };

            Debug.Log("Quest Color Finder: starting Android APK build -> " + outputPath);

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new Exception(string.Format(
                    "Build failed. Result={0}, Errors={1}, Output={2}",
                    summary.result,
                    summary.totalErrors,
                    summary.outputPath));
            }

            Debug.Log(string.Format(
                "Quest Color Finder build succeeded. Size={0} MB, Time={1}s, Output={2}",
                (summary.totalSize / (1024f * 1024f)).ToString("0.0"),
                summary.totalTime.TotalSeconds.ToString("0.0"),
                summary.outputPath));
        }

        private static void ApplyRecommendedProjectSettings()
        {
            EnsureAndroidBuildTarget();

            PlayerSettings.productName = "Quest LEGO Color Finder";
            PlayerSettings.companyName = "Local";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.local.questlegocolorfinder");
            PlayerSettings.bundleVersion = "1.0.0";

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.MTRendering = true;
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Medium);

            PlayerSettings.stripEngineCode = true;
            PlayerSettings.gcIncremental = true;
            PlayerSettings.runInBackground = true;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

            TryConfigureActiveInputHandlingForOpenXR();
            TryConfigureDefineSymbols();
            TryLogOpenXRReminder();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureAndroidBuildTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                return;
            }

            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            if (!switched)
            {
                throw new Exception("Failed to switch active build target to Android. Install Android Build Support in Unity Hub.");
            }
        }

        private static void TryConfigureDefineSymbols()
        {
            string current = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android);
            string symbol = "QUEST_COLOR_FINDER";
            if (!current.Split(';').Contains(symbol))
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, string.IsNullOrEmpty(current) ? symbol : current + ";" + symbol);
            }
        }

        private static void TryConfigureActiveInputHandlingForOpenXR()
        {
            // OpenXR validation requires "Input System Package (New)" on many Unity 2022.x configurations.
            // Use reflection so this file compiles across patch versions where the API surface may differ.
            try
            {
                PropertyInfo prop = typeof(PlayerSettings).GetProperty(
                    "activeInputHandler",
                    BindingFlags.Public | BindingFlags.Static);

                if (prop != null && prop.CanWrite)
                {
                    Type enumType = prop.PropertyType;
                    if (enumType != null && enumType.IsEnum)
                    {
                        string[] preferredNames = { "InputSystemPackage", "NewInputSystem", "InputSystemPackageNew" };
                        object enumValue = null;

                        foreach (string name in preferredNames)
                        {
                            if (!Enum.GetNames(enumType).Contains(name))
                            {
                                continue;
                            }

                            enumValue = Enum.Parse(enumType, name);
                            break;
                        }

                        if (enumValue == null)
                        {
                            // Common enum layout is Old=0, New=1, Both=2. Prefer index 1 if present.
                            Array values = Enum.GetValues(enumType);
                            if (values.Length > 1)
                            {
                                enumValue = values.GetValue(1);
                            }
                        }

                        if (enumValue != null)
                        {
                            prop.SetValue(null, enumValue, null);
                            Debug.Log("Quest Color Finder: set Active Input Handling to Input System Package (New) for OpenXR.");
                            return;
                        }
                    }
                }

                // Older internal API fallback.
                MethodInfo setInt = typeof(PlayerSettings).GetMethod(
                    "SetPropertyInt",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(int), typeof(BuildTargetGroup) },
                    null);

                if (setInt != null)
                {
                    // "activeInputHandler" value 1 typically maps to New Input System only.
                    setInt.Invoke(null, new object[] { "activeInputHandler", 1, BuildTargetGroup.Android });
                    Debug.Log("Quest Color Finder: attempted to set Active Input Handling to New Input System via internal API.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Quest Color Finder: could not auto-set Active Input Handling for OpenXR. Set it manually to 'Input System Package (New)'. " + ex.Message);
            }
        }

        private static void TryLogOpenXRReminder()
        {
            // Keeping this as a reminder instead of hard reflection writes, because OpenXR feature APIs move between package versions.
            Debug.Log(
                "Quest Color Finder: Verify OpenXR is enabled for Android and Meta Quest support features are turned on. " +
                "Also ensure Meta XR All-in-One SDK + MRUK are installed from the Meta registry.");
        }

        private static string GetCommandLineArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    return null;
                }

                return args[i + 1];
            }

            return null;
        }
    }
}
#endif
