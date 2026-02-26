using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace QuestLegoColorFinder
{
    public enum RuntimeHighlightMode
    {
        BasicStyling = 0,
        AdvancedCameraMask = 1
    }

    public enum HighlightStyle
    {
        BwExceptTarget = 0,
        GlowOverlay = 1
    }

    public enum LegoColorPreset
    {
        Red = 0,
        Blue = 1,
        Yellow = 2,
        Green = 3,
        Black = 4,
        White = 5
    }

    [Serializable]
    public struct ColorDetectionProfile
    {
        public LegoColorPreset preset;
        public string displayName;
        public Color accentColor;
        public Vector3 targetHsv;      // x=h, y=s, z=v (0..1)
        public Vector3 toleranceHsv;   // x=hTol, y=sTol, z=vTol
        public Vector2 saturationRange; // min,max
        public Vector2 valueRange;      // min,max
        public float chromaWeight;      // 1=chromatic, 0=mostly luminance
        public float maskSoftness;
        public float highlightBoost;

        public ColorDetectionProfile WithTargetHue(float hue01)
        {
            targetHsv.x = Mathf.Repeat(hue01, 1f);
            return this;
        }

        public ColorDetectionProfile WithTargetHsv(Vector3 hsv01)
        {
            targetHsv = new Vector3(Mathf.Repeat(hsv01.x, 1f), Mathf.Clamp01(hsv01.y), Mathf.Clamp01(hsv01.z));
            return this;
        }

        public ColorDetectionProfile WithTolerances(float hueTol, float satTol, float valTol)
        {
            toleranceHsv = new Vector3(Mathf.Clamp(hueTol, 0.001f, 0.5f), Mathf.Clamp01(satTol), Mathf.Clamp01(valTol));
            return this;
        }
    }

    public static class LegoColorProfiles
    {
        public static ColorDetectionProfile Get(LegoColorPreset preset)
        {
            switch (preset)
            {
                case LegoColorPreset.Red:
                    return CreateChromatic(preset, "Red", new Color(1f, 0.15f, 0.15f), 0.00f, 0.90f, 0.80f, 0.06f, 0.40f, 0.45f, 0.20f, 1.00f, 0.15f, 1.00f);
                case LegoColorPreset.Blue:
                    return CreateChromatic(preset, "Blue", new Color(0.20f, 0.55f, 1f), 0.60f, 0.85f, 0.75f, 0.06f, 0.35f, 0.45f, 0.15f, 1.00f, 0.15f, 1.00f);
                case LegoColorPreset.Yellow:
                    return CreateChromatic(preset, "Yellow", new Color(1f, 0.92f, 0.20f), 0.16f, 0.85f, 0.90f, 0.05f, 0.35f, 0.40f, 0.15f, 1.00f, 0.25f, 1.00f);
                case LegoColorPreset.Green:
                    return CreateChromatic(preset, "Green", new Color(0.25f, 0.95f, 0.35f), 0.33f, 0.80f, 0.65f, 0.06f, 0.35f, 0.45f, 0.15f, 1.00f, 0.15f, 0.95f);
                case LegoColorPreset.Black:
                    return new ColorDetectionProfile
                    {
                        preset = preset,
                        displayName = "Black",
                        accentColor = new Color(0.9f, 0.9f, 0.9f),
                        targetHsv = new Vector3(0f, 0.20f, 0.12f),
                        toleranceHsv = new Vector3(0.50f, 0.80f, 0.22f),
                        saturationRange = new Vector2(0.00f, 1.00f),
                        valueRange = new Vector2(0.00f, 0.28f),
                        chromaWeight = 0.15f,
                        maskSoftness = 0.06f,
                        highlightBoost = 1.6f
                    };
                case LegoColorPreset.White:
                    return new ColorDetectionProfile
                    {
                        preset = preset,
                        displayName = "White",
                        accentColor = new Color(1f, 1f, 1f),
                        targetHsv = new Vector3(0f, 0.05f, 0.92f),
                        toleranceHsv = new Vector3(0.50f, 0.25f, 0.20f),
                        saturationRange = new Vector2(0.00f, 0.30f),
                        valueRange = new Vector2(0.68f, 1.00f),
                        chromaWeight = 0.05f,
                        maskSoftness = 0.05f,
                        highlightBoost = 1.15f
                    };
                default:
                    return Get(LegoColorPreset.Red);
            }
        }

        private static ColorDetectionProfile CreateChromatic(
            LegoColorPreset preset,
            string displayName,
            Color accent,
            float h,
            float s,
            float v,
            float hTol,
            float sTol,
            float vTol,
            float satMin,
            float satMax,
            float valMin,
            float valMax)
        {
            return new ColorDetectionProfile
            {
                preset = preset,
                displayName = displayName,
                accentColor = accent,
                targetHsv = new Vector3(h, s, v),
                toleranceHsv = new Vector3(hTol, sTol, vTol),
                saturationRange = new Vector2(satMin, satMax),
                valueRange = new Vector2(valMin, valMax),
                chromaWeight = 1f,
                maskSoftness = 0.05f,
                highlightBoost = 1.45f
            };
        }
    }

    [DefaultExecutionOrder(-1000)]
    public sealed class AppBootstrap : MonoBehaviour
    {
        private const string BootstrapObjectName = "[QuestColorFinder]";
        private static bool s_created;

        private Camera _xrCamera;
        private GameObject _rigRoot;
        private PassthroughBasicStylingController _basicController;
        private PassthroughCameraMaskController _advancedController;
        private ColorSelectionUI _ui;

        private RuntimeHighlightMode _currentMode = RuntimeHighlightMode.BasicStyling;
        private HighlightStyle _currentStyle = HighlightStyle.BwExceptTarget;
        private ColorDetectionProfile _currentProfile = LegoColorProfiles.Get(LegoColorPreset.Red);
        private int _currentPresetIndex;

        private bool _cameraPermissionGranted;
        private string _statusMessage = "Initializing...";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (s_created || FindObjectOfType<AppBootstrap>() != null)
            {
                return;
            }

            GameObject go = new GameObject(BootstrapObjectName);
            DontDestroyOnLoad(go);
            go.AddComponent<AppBootstrap>();
            s_created = true;
        }

        private IEnumerator Start()
        {
            Application.targetFrameRate = 72;
            QualitySettings.vSyncCount = 0;

            EnsureRuntimeRig();
            EnsureControllers();
            EnsureUi();
            WireUiEvents();

            _basicController.Initialize(_rigRoot, _xrCamera);
            _advancedController.Initialize(_rigRoot, _xrCamera);
            _ui.Initialize(_xrCamera.transform);

            ApplyCurrentSelection();
            SetMode(RuntimeHighlightMode.BasicStyling, "Passthrough styling fallback active.");

            yield return StartCoroutine(InitializeModeSelectionRoutine());
        }

        private void OnDestroy()
        {
            if (_ui != null)
            {
                _ui.PresetSelected -= OnPresetSelected;
                _ui.StyleChanged -= OnStyleChanged;
                _ui.AdvancedPanelToggled -= OnAdvancedPanelToggled;
                _ui.ToleranceChanged -= OnToleranceChanged;
                _ui.CalibratePressed -= OnCalibratePressed;
            }

            s_created = false;
        }

        private void Update()
        {
            HandleQuickControls();
        }

        private IEnumerator InitializeModeSelectionRoutine()
        {
            _ui.SetStatusMessage("Requesting camera permission for Advanced Mode...");

            yield return StartCoroutine(RequestCameraPermissionsRoutine());

            if (_cameraPermissionGranted)
            {
                yield return StartCoroutine(_advancedController.TryStartAdvancedModeRoutine());
                if (_advancedController.IsAdvancedModeAvailable)
                {
                    SetMode(RuntimeHighlightMode.AdvancedCameraMask, "Advanced camera mask enabled.");
                    yield break;
                }

                SetMode(RuntimeHighlightMode.BasicStyling, "Camera access unavailable. Using Basic styling.");
                _ui.SetStatusMessage(_advancedController.LastFailureReason);
                yield break;
            }

            SetMode(RuntimeHighlightMode.BasicStyling, "Camera permission denied. Using Basic styling.");
            _ui.SetStatusMessage("Grant headset camera permission for true color masking.");
        }

        private void EnsureRuntimeRig()
        {
            _xrCamera = Camera.main;
            if (_xrCamera != null)
            {
                _rigRoot = _xrCamera.transform.root != null ? _xrCamera.transform.root.gameObject : _xrCamera.gameObject;
                return;
            }

            _rigRoot = new GameObject("XRRig");
            DontDestroyOnLoad(_rigRoot);

            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(_rigRoot.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            cameraGo.transform.localRotation = Quaternion.identity;

            _xrCamera = cameraGo.AddComponent<Camera>();
            _xrCamera.clearFlags = CameraClearFlags.SolidColor;
            _xrCamera.backgroundColor = Color.black;
            _xrCamera.nearClipPlane = 0.01f;
            _xrCamera.farClipPlane = 100f;
            _xrCamera.allowHDR = false;
            _xrCamera.allowMSAA = false;

            cameraGo.AddComponent<AudioListener>();
        }

        private void EnsureControllers()
        {
            _basicController = gameObject.GetComponent<PassthroughBasicStylingController>();
            if (_basicController == null)
            {
                _basicController = gameObject.AddComponent<PassthroughBasicStylingController>();
            }

            _advancedController = gameObject.GetComponent<PassthroughCameraMaskController>();
            if (_advancedController == null)
            {
                _advancedController = gameObject.AddComponent<PassthroughCameraMaskController>();
            }
        }

        private void EnsureUi()
        {
            _ui = gameObject.GetComponent<ColorSelectionUI>();
            if (_ui == null)
            {
                _ui = gameObject.AddComponent<ColorSelectionUI>();
            }
        }

        private void WireUiEvents()
        {
            _ui.PresetSelected -= OnPresetSelected;
            _ui.StyleChanged -= OnStyleChanged;
            _ui.AdvancedPanelToggled -= OnAdvancedPanelToggled;
            _ui.ToleranceChanged -= OnToleranceChanged;
            _ui.CalibratePressed -= OnCalibratePressed;

            _ui.PresetSelected += OnPresetSelected;
            _ui.StyleChanged += OnStyleChanged;
            _ui.AdvancedPanelToggled += OnAdvancedPanelToggled;
            _ui.ToleranceChanged += OnToleranceChanged;
            _ui.CalibratePressed += OnCalibratePressed;
        }

        private void ApplyCurrentSelection()
        {
            _basicController.SetProfile(_currentProfile);
            _basicController.SetStyle(_currentStyle);

            _advancedController.SetProfile(_currentProfile);
            _advancedController.SetStyle(_currentStyle);

            _ui.SetSelectedProfile(_currentProfile);
            _ui.SetSelectedStyle(_currentStyle);
            _ui.SetCalibrationEnabled(_currentMode == RuntimeHighlightMode.AdvancedCameraMask && _advancedController.IsAdvancedModeAvailable);
        }

        private void SetMode(RuntimeHighlightMode mode, string status)
        {
            _currentMode = mode;
            _statusMessage = status;

            _basicController.SetBasicPresentationActive(mode == RuntimeHighlightMode.BasicStyling);
            _advancedController.SetOverlayActive(mode == RuntimeHighlightMode.AdvancedCameraMask);

            _ui.SetModeIndicator(mode);
            _ui.SetStatusMessage(status);
            _ui.SetCalibrationEnabled(mode == RuntimeHighlightMode.AdvancedCameraMask && _advancedController.IsAdvancedModeAvailable);
            _ui.SetAdvancedControlsVisible(mode == RuntimeHighlightMode.AdvancedCameraMask);
        }

        private void OnPresetSelected(LegoColorPreset preset)
        {
            _currentPresetIndex = (int)preset;
            _currentProfile = LegoColorProfiles.Get(preset);
            ApplyCurrentSelection();
            _ui.SetStatusMessage(string.Format("Target color: {0}", _currentProfile.displayName));
        }

        private void OnStyleChanged(HighlightStyle style)
        {
            _currentStyle = style;
            ApplyCurrentSelection();
            _ui.SetStatusMessage(style == HighlightStyle.BwExceptTarget
                ? "Style 1 active: B/W except target."
                : "Style 2 active: Glow overlay.");
        }

        private void OnAdvancedPanelToggled(bool shown)
        {
            if (!shown)
            {
                _ui.SetStatusMessage(_statusMessage);
                return;
            }

            if (_currentMode != RuntimeHighlightMode.AdvancedCameraMask)
            {
                _ui.SetStatusMessage("Advanced thresholds apply only in Advanced Camera Mask mode.");
            }
        }

        private void OnToleranceChanged(float hueTolerance, float satTolerance, float valTolerance)
        {
            _currentProfile = _currentProfile.WithTolerances(hueTolerance, satTolerance, valTolerance);
            ApplyCurrentSelection();
        }

        private void OnCalibratePressed()
        {
            if (_currentMode != RuntimeHighlightMode.AdvancedCameraMask || !_advancedController.IsAdvancedModeAvailable)
            {
                _ui.SetStatusMessage("Calibration requires Advanced mode and camera access.");
                return;
            }

            StartCoroutine(CalibrateRoutine());
        }

        private IEnumerator CalibrateRoutine()
        {
            _ui.SetStatusMessage("Calibrating from center sample...");
            bool complete = false;
            bool success = false;
            Vector3 sampledHsv = Vector3.zero;
            string failure = string.Empty;

            yield return StartCoroutine(_advancedController.CalibrateCenterRoutine(
                hsv =>
                {
                    sampledHsv = hsv;
                    success = true;
                    complete = true;
                },
                reason =>
                {
                    failure = reason;
                    success = false;
                    complete = true;
                }));

            if (!complete || !success)
            {
                _ui.SetStatusMessage(string.IsNullOrEmpty(failure) ? "Calibration failed." : failure);
                yield break;
            }

            _currentProfile = _currentProfile.WithTargetHsv(sampledHsv);
            _currentProfile.toleranceHsv = new Vector3(
                Mathf.Clamp(_currentProfile.toleranceHsv.x, 0.03f, 0.12f),
                Mathf.Clamp(_currentProfile.toleranceHsv.y, 0.18f, 0.45f),
                Mathf.Clamp(_currentProfile.toleranceHsv.z, 0.18f, 0.45f));

            ApplyCurrentSelection();
            _ui.SetStatusMessage(string.Format("Calibrated hue={0:0.00}, sat={1:0.00}, val={2:0.00}",
                sampledHsv.x, sampledHsv.y, sampledHsv.z));
        }

        private void HandleQuickControls()
        {
            if (_ui == null || _xrCamera == null)
            {
                return;
            }

            if (_ui.IsPlacementMode())
            {
                _ui.RecenterInFrontOfHead(1.0f);
            }

            if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.JoystickButton0) || OvrInputBridge.GetDown("One"))
            {
                if (_ui.IsPlacementMode())
                {
                    _ui.SetPlacementMode(false);
                    _ui.SetStatusMessage("UI placed in world space.");
                    return;
                }

                _ui.ToggleVisible();
                if (_ui.IsVisible())
                {
                    _ui.RecenterInFrontOfHead(1.0f);
                    _ui.SetStatusMessage("UI shown. A/M hides, B/R recenters.");
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton1) || OvrInputBridge.GetDown("Two"))
            {
                _ui.SetVisible(true);
                _ui.SetPlacementMode(true);
                _ui.RecenterInFrontOfHead(1.0f);
                _ui.SetStatusMessage("Placement mode: move your head, then press A to place UI.");
            }

            if (Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.JoystickButton2) || OvrInputBridge.GetDown("Three"))
            {
                int count = Enum.GetValues(typeof(LegoColorPreset)).Length;
                _currentPresetIndex = (_currentPresetIndex + 1) % count;
                OnPresetSelected((LegoColorPreset)_currentPresetIndex);
            }

            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.JoystickButton3) || OvrInputBridge.GetDown("Four"))
            {
                HighlightStyle next = _currentStyle == HighlightStyle.BwExceptTarget ? HighlightStyle.GlowOverlay : HighlightStyle.BwExceptTarget;
                OnStyleChanged(next);
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton7) || OvrInputBridge.GetDown("Start"))
            {
                OnCalibratePressed();
            }

            if (Application.isEditor)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) { OnPresetSelected(LegoColorPreset.Red); }
                if (Input.GetKeyDown(KeyCode.Alpha2)) { OnPresetSelected(LegoColorPreset.Blue); }
                if (Input.GetKeyDown(KeyCode.Alpha3)) { OnPresetSelected(LegoColorPreset.Yellow); }
                if (Input.GetKeyDown(KeyCode.Alpha4)) { OnPresetSelected(LegoColorPreset.Green); }
                if (Input.GetKeyDown(KeyCode.Alpha5)) { OnPresetSelected(LegoColorPreset.Black); }
                if (Input.GetKeyDown(KeyCode.Alpha6)) { OnPresetSelected(LegoColorPreset.White); }
            }
        }

        private static class OvrInputBridge
        {
            private static bool s_triedInit;
            private static Type s_ovrInputType;
            private static Type s_buttonType;
            private static MethodInfo s_getDownButtonOnly;

            public static bool GetDown(string buttonName)
            {
                EnsureInit();
                if (s_ovrInputType == null || s_buttonType == null || s_getDownButtonOnly == null)
                {
                    return false;
                }

                try
                {
                    object button = Enum.Parse(s_buttonType, buttonName, true);
                    object result = s_getDownButtonOnly.Invoke(null, new[] { button });
                    return result is bool b && b;
                }
                catch
                {
                    return false;
                }
            }

            private static void EnsureInit()
            {
                if (s_triedInit)
                {
                    return;
                }

                s_triedInit = true;
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    try
                    {
                        Type t = assemblies[i].GetType("OVRInput", false);
                        if (t == null)
                        {
                            continue;
                        }

                        s_ovrInputType = t;
                        s_buttonType = t.GetNestedType("Button", BindingFlags.Public);
                        if (s_buttonType != null)
                        {
                            s_getDownButtonOnly = t.GetMethod("GetDown", BindingFlags.Public | BindingFlags.Static, null, new[] { s_buttonType }, null);
                        }
                        return;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private IEnumerator RequestCameraPermissionsRoutine()
        {
            _cameraPermissionGranted = false;

#if UNITY_ANDROID && !UNITY_EDITOR
            string[] permissions = new string[]
            {
                "android.permission.CAMERA",
                "horizonos.permission.HEADSET_CAMERA"
            };

            for (int i = 0; i < permissions.Length; i++)
            {
                string permission = permissions[i];
                bool alreadyGranted = false;
                try
                {
                    alreadyGranted = Permission.HasUserAuthorizedPermission(permission);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Permission check failed for " + permission + ": " + ex.Message);
                }

                if (alreadyGranted)
                {
                    _cameraPermissionGranted = true;
                    continue;
                }

                yield return StartCoroutine(RequestSinglePermissionRoutine(permission));

                try
                {
                    if (Permission.HasUserAuthorizedPermission(permission))
                    {
                        _cameraPermissionGranted = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Permission re-check failed for " + permission + ": " + ex.Message);
                }
            }
#else
            // Editor path: default to Basic mode to avoid webcam driver issues and black-screen overlays.
            // Real validation of passthrough/camera mask should happen on Quest hardware.
            _cameraPermissionGranted = false;
            yield return null;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator RequestSinglePermissionRoutine(string permission)
        {
            bool completed = false;

            PermissionCallbacks callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => completed = true;
            callbacks.PermissionDenied += _ => completed = true;
            callbacks.PermissionDeniedAndDontAskAgain += _ => completed = true;

            try
            {
                Permission.RequestUserPermission(permission, callbacks);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Permission request failed for " + permission + ": " + ex.Message);
                yield break;
            }

            float timeoutAt = Time.realtimeSinceStartup + 8f;
            while (!completed && Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }
        }
#endif
    }
}
