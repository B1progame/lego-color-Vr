#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QuestLegoColorFinder.Editor
{
    public class QuestColorFinderMetaSetupTool : EditorWindow
    {
        private const string CameraRigBlockId = "e47682b9-c270-40b1-b16d-90b627a5ce1b";
        private const string ControllerTrackingBlockId = "5817f7c0-f2a5-45f9-a5ca-64264e0166e8";
        private const string HandTrackingBlockId = "8b26b298-7bf4-490e-b245-a039c0184303";

        private bool _busy;
        private Vector2 _scroll;
        private string _lastStatus = "Ready.";

        [MenuItem("Tools/Quest Color Finder/Meta XR Auto Setup Tool")]
        public static void ShowWindow()
        {
            QuestColorFinderMetaSetupTool window = GetWindow<QuestColorFinderMetaSetupTool>("Quest Meta Setup");
            window.minSize = new Vector2(520f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Quest Color Finder - Meta XR Setup Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Automates the parts we can safely script for your installed Meta XR SDK version:\n" +
                "- Quest build settings\n" +
                "- Meta Building Blocks: Camera Rig, Controller Tracking, Hand Tracking\n" +
                "- Opens the remaining settings pages for manual fixes (OpenXR validation, Hand Tracking V2)\n\n" +
                "Note: Ray/UI interaction Building Blocks may not be available in your currently installed package set.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(_busy))
            {
                if (GUILayout.Button("1) Apply Recommended Quest Build Settings", GUILayout.Height(32f)))
                {
                    RunSafe(() =>
                    {
                        BuildQuestAPK.ApplyRecommendedProjectSettingsMenu();
                        _lastStatus = "Applied Quest build settings.";
                    });
                }

                if (GUILayout.Button("2) Auto-Install Meta XR Scene Blocks (Camera Rig + Controller + Hand)", GUILayout.Height(36f)))
                {
                    _ = AutoInstallSceneBlocksAsync();
                }

                if (GUILayout.Button("3) Open Project Validation / Settings Windows", GUILayout.Height(32f)))
                {
                    OpenProjectSettingsPages();
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_lastStatus, MessageType.None);

            EditorGUILayout.Space(8);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("What This Tool Still Cannot Guarantee", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("- Hand Tracking V2 selection (Meta XR setting UI varies by version)");
            EditorGUILayout.LabelField("- OpenXR validation fixes that require Unity restart");
            EditorGUILayout.LabelField("- Ray/UI interaction blocks if they are not included in installed packages");
            EditorGUILayout.LabelField("- Passthrough visualization in Editor (test on Quest hardware)");

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("After Running This Tool", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Open Meta XR Project Setup and apply remaining Recommended items");
            EditorGUILayout.LabelField("2. Enable Hand Tracking V2 in Meta XR settings (if shown)");
            EditorGUILayout.LabelField("3. Open OpenXR > Project Validation and Fix All");
            EditorGUILayout.LabelField("4. Build and install APK to Quest");
            EditorGUILayout.EndScrollView();
        }

        private async Task AutoInstallSceneBlocksAsync()
        {
            _busy = true;
            _lastStatus = "Installing Building Blocks into the active scene...";
            Repaint();

            try
            {
                EnsureSceneOpenAndSavedPrompt();

                List<string> results = new List<string>();
                await InstallBlockAsync(CameraRigBlockId, "Camera Rig", results);
                await InstallBlockAsync(ControllerTrackingBlockId, "Controller Tracking", results);
                await InstallBlockAsync(HandTrackingBlockId, "Hand Tracking", results);

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();

                _lastStatus = "Auto-setup finished.\n" + string.Join("\n", results);
                Debug.Log("[QuestColorFinder] Meta XR auto setup finished.\n" + string.Join("\n", results));
            }
            catch (Exception ex)
            {
                _lastStatus = "Auto-setup failed: " + ex.Message + "\nCheck Console for details.";
                Debug.LogError("[QuestColorFinder] Meta XR auto setup failed\n" + ex);
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }

        private static void EnsureSceneOpenAndSavedPrompt()
        {
            if (!EditorSceneManager.GetActiveScene().isDirty)
            {
                return;
            }

            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        private static void OpenProjectSettingsPages()
        {
            SettingsService.OpenProjectSettings("Project/XR Plug-in Management");
            SettingsService.OpenProjectSettings("Project/XR Plug-in Management/OpenXR");
            SettingsService.OpenProjectSettings("Project/XR Plug-in Management/OpenXR/Project Validation");
            SettingsService.OpenProjectSettings("Project/Meta XR");
        }

        private async Task InstallBlockAsync(string blockId, string label, List<string> results)
        {
            object blockData = GetMetaBlockData(blockId);
            if (blockData == null)
            {
                results.Add("- " + label + ": block not found in installed Meta XR package set.");
                return;
            }

            MethodInfo addToProject = blockData.GetType().GetMethod(
                "AddToProject",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(GameObject), typeof(Action) },
                null);

            if (addToProject == null)
            {
                results.Add("- " + label + ": installer API not found.");
                return;
            }

            try
            {
                object taskObj = addToProject.Invoke(blockData, new object[] { null, null });
                if (taskObj is Task task)
                {
                    await task;
                }
                else
                {
                    // Some versions may return void/async-void. Allow one editor tick.
                    await Task.Yield();
                }

                results.Add("- " + label + ": installed or already present.");
            }
            catch (TargetInvocationException tie)
            {
                string msg = tie.InnerException != null ? tie.InnerException.Message : tie.Message;
                string lower = msg != null ? msg.ToLowerInvariant() : string.Empty;
                // Singleton/already-present cases bubble up as exceptions in some BB versions.
                if (lower.Contains("singleton") && lower.Contains("already present"))
                {
                    results.Add("- " + label + ": already installed.");
                }
                else
                {
                    results.Add("- " + label + ": failed (" + msg + ")");
                }
            }
            catch (Exception ex)
            {
                results.Add("- " + label + ": failed (" + ex.Message + ")");
            }
        }

        private static object GetMetaBlockData(string blockId)
        {
            Type utilsType = FindType("Meta.XR.BuildingBlocks.Editor.Utils");
            if (utilsType == null)
            {
                return null;
            }

            MethodInfo getBlockData = utilsType.GetMethod(
                "GetBlockData",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (getBlockData == null)
            {
                return null;
            }

            return getBlockData.Invoke(null, new object[] { blockId });
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = assembly.GetType(fullName, false);
                    if (t != null)
                    {
                        return t;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private void RunSafe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _lastStatus = ex.Message;
                Debug.LogException(ex);
            }
        }
    }
}
#endif
