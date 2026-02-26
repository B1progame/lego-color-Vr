using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QuestLegoColorFinder
{
    public sealed class ColorSelectionUI : MonoBehaviour
    {
        public event Action<LegoColorPreset> PresetSelected;
        public event Action<HighlightStyle> StyleChanged;
        public event Action<bool> AdvancedPanelToggled;
        public event Action<float, float, float> ToleranceChanged;
        public event Action CalibratePressed;

        private bool _initialized;
        private bool _suppressCallbacks;
        private bool _advancedPanelExpanded;
        private bool _isVisible = true;
        private bool _placementMode;

        private Canvas _canvas;
        private GameObject _canvasRoot;
        private GameObject _rootPanel;
        private GameObject _advancedSectionRoot;
        private GameObject _advancedPanel;

        private Text _titleText;
        private Text _modeText;
        private Text _statusText;
        private Text _advancedHintText;
        private Button _advancedToggleButton;
        private Text _advancedToggleButtonText;
        private Button _calibrateButton;

        private Slider _hueTolSlider;
        private Slider _satTolSlider;
        private Slider _valTolSlider;
        private Text _hueTolValueText;
        private Text _satTolValueText;
        private Text _valTolValueText;

        private HighlightStyle _selectedStyle;
        private LegoColorPreset _selectedPreset;

        private readonly Dictionary<LegoColorPreset, Button> _presetButtons = new Dictionary<LegoColorPreset, Button>();
        private readonly Dictionary<HighlightStyle, Button> _styleButtons = new Dictionary<HighlightStyle, Button>();
        private readonly Dictionary<Button, Color> _buttonBaseColors = new Dictionary<Button, Color>();

        private Font _font;
        private Transform _headTransform;

        public void Initialize(Transform attachToCamera)
        {
            if (_initialized)
            {
                _headTransform = attachToCamera;
                return;
            }

            _initialized = true;
            _headTransform = attachToCamera;
            _font = LoadBuiltinUiFont();

            EnsureEventSystem();
            BuildCanvas();
            BuildUiHierarchy();
            RecenterInFrontOfHead(1.0f);

            UpdateStyleButtonVisuals();
            UpdatePresetButtonVisuals();
            SetModeIndicator(RuntimeHighlightMode.BasicStyling);
            SetStatusMessage("Choose a color, then enable Advanced mode by granting camera permission.");
            SetAdvancedControlsVisible(false);
            SetCalibrationEnabled(false);
            SetVisible(true);
        }

        public void SetModeIndicator(RuntimeHighlightMode mode)
        {
            if (_modeText == null)
            {
                return;
            }

            _modeText.text = mode == RuntimeHighlightMode.AdvancedCameraMask
                ? "Mode: Advanced (Camera Mask)"
                : "Mode: Basic (Styling)";
            _modeText.color = mode == RuntimeHighlightMode.AdvancedCameraMask
                ? new Color(0.68f, 1f, 0.68f)
                : new Color(1f, 0.92f, 0.62f);
        }

        public void SetStatusMessage(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = "Status: " + message;
            }
        }

        public void SetAdvancedControlsVisible(bool visible)
        {
            if (_advancedSectionRoot != null)
            {
                _advancedSectionRoot.SetActive(visible);
            }

            if (!visible)
            {
                SetAdvancedPanelExpanded(false);
            }
        }

        public void SetCalibrationEnabled(bool enabled)
        {
            if (_calibrateButton != null)
            {
                _calibrateButton.interactable = enabled;
            }

            if (_advancedHintText != null)
            {
                _advancedHintText.text = enabled
                    ? "Calibrate samples the center of the camera image and updates the target hue."
                    : "Advanced color mask unavailable. Grant camera permission and ensure MRUK PCA is available.";
            }
        }

        public void SetSelectedProfile(ColorDetectionProfile profile)
        {
            _selectedPreset = profile.preset;
            UpdatePresetButtonVisuals();

            if (_titleText != null)
            {
                _titleText.text = "LEGO Color Finder  [" + profile.displayName + "]";
            }

            _suppressCallbacks = true;
            if (_hueTolSlider != null) _hueTolSlider.value = profile.toleranceHsv.x;
            if (_satTolSlider != null) _satTolSlider.value = profile.toleranceHsv.y;
            if (_valTolSlider != null) _valTolSlider.value = profile.toleranceHsv.z;
            _suppressCallbacks = false;

            UpdateSliderValueTexts();
        }

        public void SetSelectedStyle(HighlightStyle style)
        {
            _selectedStyle = style;
            UpdateStyleButtonVisuals();
        }

        public void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (_canvasRoot != null)
            {
                _canvasRoot.SetActive(visible);
            }
        }

        public bool IsVisible()
        {
            return _isVisible;
        }

        public bool IsPlacementMode()
        {
            return _placementMode;
        }

        public void ToggleVisible()
        {
            SetVisible(!_isVisible);
        }

        public void SetPlacementMode(bool enabled)
        {
            _placementMode = enabled;
        }

        public void RecenterInFrontOfHead(float distanceMeters)
        {
            if (_canvasRoot == null || _headTransform == null)
            {
                return;
            }

            Vector3 forward = _headTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            Vector3 targetPos = _headTransform.position + forward * Mathf.Max(0.4f, distanceMeters);
            targetPos.y = Mathf.Clamp(_headTransform.position.y - 0.08f, 0.75f, 1.8f);
            _canvasRoot.transform.position = targetPos;

            Vector3 lookPos = _headTransform.position;
            lookPos.y = targetPos.y;
            Vector3 dir = (lookPos - targetPos).normalized;
            if (dir.sqrMagnitude < 0.001f)
            {
                dir = -forward;
            }

            // World-space UI can appear mirrored/inverted on some XR setups depending on canvas facing.
            // We orient toward the user, then rotate 180° so the canvas front faces the camera consistently.
            _canvasRoot.transform.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
        }

        private void BuildCanvas()
        {
            _canvasRoot = new GameObject("ColorFinderUIRoot");
            _canvasRoot.layer = 5;
            _canvasRoot.transform.position = Vector3.zero;
            _canvasRoot.transform.rotation = Quaternion.identity;
            _canvasRoot.transform.localScale = new Vector3(0.0012f, 0.0012f, 0.0012f);

            GameObject canvasGo = new GameObject("ColorFinderCanvas");
            canvasGo.layer = 5; // UI
            canvasGo.transform.SetParent(_canvasRoot.transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = Camera.main;
            _canvas.pixelPerfect = false;
            RectTransform canvasRt = _canvas.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(760f, 1080f);

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            scaler.referencePixelsPerUnit = 100f;

            canvasGo.AddComponent<GraphicRaycaster>();
            TryAddMetaUiRaycaster(canvasGo);
        }

        private void BuildUiHierarchy()
        {
            _rootPanel = CreatePanel("Panel", _canvas.transform, new Color(0.04f, 0.05f, 0.06f, 0.83f));
            RectTransform panelRt = _rootPanel.GetComponent<RectTransform>();
            panelRt.sizeDelta = new Vector2(700f, 980f);

            VerticalLayoutGroup panelLayout = _rootPanel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(22, 22, 18, 18);
            panelLayout.spacing = 12f;
            panelLayout.childControlHeight = false;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childForceExpandWidth = true;

            _rootPanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _titleText = CreateText("Title", _rootPanel.transform, "LEGO Color Finder", 34, FontStyle.Bold, TextAnchor.MiddleLeft);
            LayoutElement titleLayout = _titleText.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 42f;

            _modeText = CreateText("Mode", _rootPanel.transform, "Mode: Basic (Styling)", 24, FontStyle.Bold, TextAnchor.MiddleLeft);
            LayoutElement modeLayout = _modeText.gameObject.AddComponent<LayoutElement>();
            modeLayout.preferredHeight = 32f;

            _statusText = CreateText("Status", _rootPanel.transform, "Status: Initializing...", 20, FontStyle.Normal, TextAnchor.UpperLeft);
            _statusText.color = new Color(0.88f, 0.92f, 0.96f);
            LayoutElement statusLayout = _statusText.gameObject.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 74f;

            CreateSectionHeader(_rootPanel.transform, "Target Color");
            BuildColorButtons(_rootPanel.transform);

            CreateSectionHeader(_rootPanel.transform, "Highlight Style");
            BuildStyleButtons(_rootPanel.transform);

            _advancedSectionRoot = new GameObject("AdvancedSection", typeof(RectTransform));
            _advancedSectionRoot.transform.SetParent(_rootPanel.transform, false);
            VerticalLayoutGroup advancedSectionLayout = _advancedSectionRoot.AddComponent<VerticalLayoutGroup>();
            advancedSectionLayout.spacing = 10f;
            advancedSectionLayout.childControlHeight = false;
            advancedSectionLayout.childControlWidth = true;
            advancedSectionLayout.childForceExpandHeight = false;
            advancedSectionLayout.childForceExpandWidth = true;
            _advancedSectionRoot.AddComponent<LayoutElement>();

            _advancedToggleButton = CreateButton(_advancedSectionRoot.transform, "Advanced settings", new Color(0.20f, 0.28f, 0.38f), 26, 72f);
            _advancedToggleButtonText = _advancedToggleButton.GetComponentInChildren<Text>();
            _advancedToggleButton.onClick.AddListener(() =>
            {
                SetAdvancedPanelExpanded(!_advancedPanelExpanded);
                if (AdvancedPanelToggled != null)
                {
                    AdvancedPanelToggled(_advancedPanelExpanded);
                }
            });

            _advancedPanel = CreatePanel("AdvancedPanel", _advancedSectionRoot.transform, new Color(0.09f, 0.11f, 0.13f, 0.9f));
            VerticalLayoutGroup advancedPanelLayout = _advancedPanel.AddComponent<VerticalLayoutGroup>();
            advancedPanelLayout.padding = new RectOffset(16, 16, 12, 12);
            advancedPanelLayout.spacing = 8f;
            advancedPanelLayout.childControlHeight = false;
            advancedPanelLayout.childControlWidth = true;
            advancedPanelLayout.childForceExpandHeight = false;
            advancedPanelLayout.childForceExpandWidth = true;
            _advancedPanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _hueTolSlider = CreateSliderRow(_advancedPanel.transform, "Hue tolerance", 0.01f, 0.20f, 0.06f, out _hueTolValueText, "HueTol");
            _satTolSlider = CreateSliderRow(_advancedPanel.transform, "Sat tolerance", 0.05f, 1.00f, 0.35f, out _satTolValueText, "SatTol");
            _valTolSlider = CreateSliderRow(_advancedPanel.transform, "Val tolerance", 0.05f, 1.00f, 0.45f, out _valTolValueText, "ValTol");

            _hueTolSlider.onValueChanged.AddListener(_ => OnToleranceSliderChanged());
            _satTolSlider.onValueChanged.AddListener(_ => OnToleranceSliderChanged());
            _valTolSlider.onValueChanged.AddListener(_ => OnToleranceSliderChanged());

            _calibrateButton = CreateButton(_advancedPanel.transform, "CALIBRATE (CENTER SAMPLE)", new Color(0.08f, 0.52f, 0.22f), 28, 92f);
            _calibrateButton.onClick.AddListener(() =>
            {
                if (CalibratePressed != null)
                {
                    CalibratePressed();
                }
            });

            _advancedHintText = CreateText("AdvancedHint", _advancedPanel.transform,
                "Advanced mask requires headset camera permission and MRUK PassthroughCameraAccess.",
                18, FontStyle.Normal, TextAnchor.UpperLeft);
            _advancedHintText.color = new Color(0.78f, 0.84f, 0.90f);
            LayoutElement hintLayout = _advancedHintText.gameObject.AddComponent<LayoutElement>();
            hintLayout.preferredHeight = 78f;

            SetAdvancedPanelExpanded(false);
        }

        private void BuildColorButtons(Transform parent)
        {
            GameObject grid = new GameObject("ColorButtons", typeof(RectTransform));
            grid.transform.SetParent(parent, false);
            GridLayoutGroup gridLayout = grid.AddComponent<GridLayoutGroup>();
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 3;
            gridLayout.cellSize = new Vector2(206f, 70f);
            gridLayout.spacing = new Vector2(8f, 8f);
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayout.childAlignment = TextAnchor.MiddleCenter;

            LayoutElement layout = grid.AddComponent<LayoutElement>();
            layout.preferredHeight = 152f;

            CreatePresetButton(grid.transform, LegoColorPreset.Red, "Red", LegoColorProfiles.Get(LegoColorPreset.Red).accentColor);
            CreatePresetButton(grid.transform, LegoColorPreset.Blue, "Blue", LegoColorProfiles.Get(LegoColorPreset.Blue).accentColor);
            CreatePresetButton(grid.transform, LegoColorPreset.Yellow, "Yellow", LegoColorProfiles.Get(LegoColorPreset.Yellow).accentColor);
            CreatePresetButton(grid.transform, LegoColorPreset.Green, "Green", LegoColorProfiles.Get(LegoColorPreset.Green).accentColor);
            CreatePresetButton(grid.transform, LegoColorPreset.Black, "Black", new Color(0.2f, 0.2f, 0.2f));
            CreatePresetButton(grid.transform, LegoColorPreset.White, "White", new Color(0.95f, 0.95f, 0.95f));
        }

        private void CreatePresetButton(Transform parent, LegoColorPreset preset, string label, Color baseColor)
        {
            Button button = CreateButton(parent, label, baseColor, 24, 70f);
            button.onClick.AddListener(() =>
            {
                _selectedPreset = preset;
                UpdatePresetButtonVisuals();
                if (PresetSelected != null)
                {
                    PresetSelected(preset);
                }
            });

            _presetButtons[preset] = button;
            _buttonBaseColors[button] = baseColor;
        }

        private void BuildStyleButtons(Transform parent)
        {
            GameObject row = new GameObject("StyleButtons", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childControlHeight = false;
            h.childControlWidth = true;
            h.childForceExpandHeight = false;
            h.childForceExpandWidth = true;
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 78f;

            Button bwButton = CreateButton(row.transform, "Style 1: B/W except target", new Color(0.22f, 0.22f, 0.22f), 21, 74f);
            bwButton.onClick.AddListener(() =>
            {
                _selectedStyle = HighlightStyle.BwExceptTarget;
                UpdateStyleButtonVisuals();
                if (StyleChanged != null)
                {
                    StyleChanged(HighlightStyle.BwExceptTarget);
                }
            });

            Button glowButton = CreateButton(row.transform, "Style 2: Glow overlay", new Color(0.16f, 0.24f, 0.36f), 21, 74f);
            glowButton.onClick.AddListener(() =>
            {
                _selectedStyle = HighlightStyle.GlowOverlay;
                UpdateStyleButtonVisuals();
                if (StyleChanged != null)
                {
                    StyleChanged(HighlightStyle.GlowOverlay);
                }
            });

            _styleButtons[HighlightStyle.BwExceptTarget] = bwButton;
            _styleButtons[HighlightStyle.GlowOverlay] = glowButton;
            _buttonBaseColors[bwButton] = new Color(0.22f, 0.22f, 0.22f);
            _buttonBaseColors[glowButton] = new Color(0.16f, 0.24f, 0.36f);
        }

        private void OnToleranceSliderChanged()
        {
            UpdateSliderValueTexts();

            if (_suppressCallbacks)
            {
                return;
            }

            if (ToleranceChanged != null)
            {
                ToleranceChanged(_hueTolSlider.value, _satTolSlider.value, _valTolSlider.value);
            }
        }

        private void UpdateSliderValueTexts()
        {
            if (_hueTolValueText != null) _hueTolValueText.text = _hueTolSlider.value.ToString("0.00");
            if (_satTolValueText != null) _satTolValueText.text = _satTolSlider.value.ToString("0.00");
            if (_valTolValueText != null) _valTolValueText.text = _valTolSlider.value.ToString("0.00");
        }

        private void SetAdvancedPanelExpanded(bool expanded)
        {
            _advancedPanelExpanded = expanded;
            if (_advancedPanel != null)
            {
                _advancedPanel.SetActive(expanded);
            }

            if (_advancedToggleButtonText != null)
            {
                _advancedToggleButtonText.text = expanded ? "Hide advanced settings" : "Advanced settings";
            }
        }

        private void UpdatePresetButtonVisuals()
        {
            foreach (KeyValuePair<LegoColorPreset, Button> pair in _presetButtons)
            {
                Button button = pair.Value;
                if (button == null)
                {
                    continue;
                }

                Image image = button.GetComponent<Image>();
                Text text = button.GetComponentInChildren<Text>();
                bool selected = pair.Key == _selectedPreset;

                Color baseColor = Color.gray;
                if (_buttonBaseColors.ContainsKey(button))
                {
                    baseColor = _buttonBaseColors[button];
                }
                else if (image != null)
                {
                    baseColor = image.color;
                }
                Color normal = selected
                    ? Color.Lerp(baseColor, Color.white, 0.35f)
                    : Color.Lerp(baseColor, Color.black, 0.08f);

                if (image != null)
                {
                    image.color = normal;
                    image.raycastTarget = true;
                }

                ColorBlock cb = button.colors;
                cb.normalColor = normal;
                cb.highlightedColor = Color.Lerp(normal, Color.white, 0.10f);
                cb.pressedColor = Color.Lerp(normal, Color.black, 0.15f);
                cb.selectedColor = cb.highlightedColor;
                cb.disabledColor = new Color(0.25f, 0.25f, 0.25f, 0.5f);
                button.colors = cb;

                if (text != null)
                {
                    bool lightButton = (pair.Key == LegoColorPreset.White || pair.Key == LegoColorPreset.Yellow);
                    text.color = lightButton ? new Color(0.05f, 0.05f, 0.05f) : Color.white;
                    if (selected)
                    {
                        text.fontStyle = FontStyle.Bold;
                    }
                    else
                    {
                        text.fontStyle = FontStyle.Normal;
                    }
                }
            }
        }

        private void UpdateStyleButtonVisuals()
        {
            foreach (KeyValuePair<HighlightStyle, Button> pair in _styleButtons)
            {
                Button button = pair.Value;
                if (button == null)
                {
                    continue;
                }

                Image image = button.GetComponent<Image>();
                Text text = button.GetComponentInChildren<Text>();
                bool selected = pair.Key == _selectedStyle;
                Color baseColor = _buttonBaseColors.ContainsKey(button)
                    ? _buttonBaseColors[button]
                    : new Color(0.2f, 0.2f, 0.2f);
                Color normal = selected ? Color.Lerp(baseColor, new Color(0.35f, 0.55f, 0.95f), 0.35f) : baseColor;

                if (image != null)
                {
                    image.color = normal;
                }

                ColorBlock cb = button.colors;
                cb.normalColor = normal;
                cb.highlightedColor = Color.Lerp(normal, Color.white, 0.12f);
                cb.pressedColor = Color.Lerp(normal, Color.black, 0.15f);
                cb.selectedColor = cb.highlightedColor;
                button.colors = cb;

                if (text != null)
                {
                    text.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                    text.color = Color.white;
                }
            }
        }

        private void CreateSectionHeader(Transform parent, string label)
        {
            Text header = CreateText("Section_" + label, parent, label, 22, FontStyle.Bold, TextAnchor.MiddleLeft);
            header.color = new Color(0.84f, 0.92f, 1f);
            LayoutElement layout = header.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 28f;
        }

        private Slider CreateSliderRow(
            Transform parent,
            string label,
            float min,
            float max,
            float defaultValue,
            out Text valueText,
            string objectName)
        {
            GameObject row = new GameObject(objectName + "Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childControlHeight = false;
            h.childControlWidth = false;
            h.childForceExpandHeight = false;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 56f;

            Text labelText = CreateText(objectName + "Label", row.transform, label, 20, FontStyle.Normal, TextAnchor.MiddleLeft);
            RectTransform labelRt = labelText.rectTransform;
            labelRt.sizeDelta = new Vector2(220f, 52f);
            LayoutElement labelLE = labelText.gameObject.AddComponent<LayoutElement>();
            labelLE.preferredWidth = 220f;
            labelLE.preferredHeight = 52f;

            Slider slider = CreateSlider(row.transform, min, max, defaultValue, objectName);
            LayoutElement sliderLE = slider.gameObject.AddComponent<LayoutElement>();
            sliderLE.preferredWidth = 320f;
            sliderLE.preferredHeight = 40f;

            valueText = CreateText(objectName + "Value", row.transform, defaultValue.ToString("0.00"), 20, FontStyle.Bold, TextAnchor.MiddleRight);
            LayoutElement valueLE = valueText.gameObject.AddComponent<LayoutElement>();
            valueLE.preferredWidth = 72f;
            valueLE.preferredHeight = 52f;

            return slider;
        }

        private Slider CreateSlider(Transform parent, float min, float max, float value, string name)
        {
            GameObject sliderGo = new GameObject(name + "Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
            sliderGo.transform.SetParent(parent, false);
            Image background = sliderGo.GetComponent<Image>();
            background.color = new Color(0.15f, 0.17f, 0.19f, 1f);
            RectTransform sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.sizeDelta = new Vector2(320f, 40f);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            RectTransform fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRt.offsetMin = new Vector2(10f, 0f);
            fillAreaRt.offsetMax = new Vector2(-10f, 0f);

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImage = fill.GetComponent<Image>();
            fillImage.color = new Color(0.28f, 0.62f, 0.95f, 1f);

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGo.transform, false);
            RectTransform handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = new Vector2(0f, 0f);
            handleAreaRt.anchorMax = new Vector2(1f, 1f);
            handleAreaRt.offsetMin = new Vector2(10f, 0f);
            handleAreaRt.offsetMax = new Vector2(-10f, 0f);

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            RectTransform handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(18f, 34f);

            Slider slider = sliderGo.GetComponent<Slider>();
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.wholeNumbers = false;

            sliderGo.GetComponent<RectTransform>().anchoredPosition3D = Vector3.zero;
            return slider;
        }

        private Button CreateButton(Transform parent, string label, Color baseColor, int fontSize, float height)
        {
            GameObject buttonGo = new GameObject(label.Replace(" ", "") + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(parent, false);

            RectTransform rect = buttonGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100f, height);

            Image image = buttonGo.GetComponent<Image>();
            image.color = baseColor;

            Button button = buttonGo.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.10f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.15f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.25f, 0.25f, 0.25f, 0.35f);
            button.colors = colors;

            GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(buttonGo.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 4f);
            textRt.offsetMax = new Vector2(-8f, -4f);

            Text text = textGo.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = label;
            text.color = Color.white;

            return button;
        }

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            return go;
        }

        private Text CreateText(string name, Transform parent, string value, int fontSize, FontStyle fontStyle, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 28f);

            Text text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.text = value;
            return text;
        }

        private void EnsureEventSystem()
        {
            EventSystem current = EventSystem.current;
            GameObject eventSystemGo;
            if (current == null)
            {
                eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
            }
            else
            {
                eventSystemGo = current.gameObject;
            }

            TryAddMetaUiInputModule(eventSystemGo);

            if (eventSystemGo.GetComponent<BaseInputModule>() == null)
            {
                eventSystemGo.AddComponent<StandaloneInputModule>();
            }
        }

        private void TryAddMetaUiInputModule(GameObject eventSystemGo)
        {
            Type ovrInputModuleType = FindTypeByName("OVRInputModule");
            if (ovrInputModuleType == null)
            {
                return;
            }

            if (eventSystemGo.GetComponent(ovrInputModuleType) != null)
            {
                return;
            }

            try
            {
                eventSystemGo.AddComponent(ovrInputModuleType);
            }
            catch
            {
                // Ignore if API signature differs.
            }
        }

        private void TryAddMetaUiRaycaster(GameObject canvasGo)
        {
            Type ovrRaycasterType = FindTypeByName("OVRRaycaster");
            if (ovrRaycasterType == null)
            {
                return;
            }

            if (canvasGo.GetComponent(ovrRaycasterType) != null)
            {
                return;
            }

            try
            {
                canvasGo.AddComponent(ovrRaycasterType);
            }
            catch
            {
                // Ignore if unavailable in this SDK version.
            }
        }

        private static Type FindTypeByName(string typeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                if (types == null)
                {
                    continue;
                }

                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type == null)
                    {
                        continue;
                    }

                    if (string.Equals(type.Name, typeName, StringComparison.Ordinal))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static Font LoadBuiltinUiFont()
        {
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
                try
                {
                    return Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
