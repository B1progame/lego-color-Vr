using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace QuestLegoColorFinder
{
    public sealed class PassthroughCameraMaskController : MonoBehaviour
    {
        private const float OverlayDistance = 2.0f;
        private const int MaskDownsample = 2;

        private GameObject _rigRoot;
        private Camera _xrCamera;

        private GameObject _overlayQuad;
        private MeshRenderer _overlayRenderer;
        private Material _colorMaskMaterial;
        private Material _compositeMaterial;
        private RenderTexture _maskRenderTexture;

        private ICameraSourceProvider _cameraSourceProvider;
        private bool _overlayRequested;
        private bool _isAdvancedModeAvailable;

        private ColorDetectionProfile _profile = LegoColorProfiles.Get(LegoColorPreset.Red);
        private HighlightStyle _style = HighlightStyle.BwExceptTarget;
        private float _pulse01;

        public bool IsAdvancedModeAvailable
        {
            get { return _isAdvancedModeAvailable && _cameraSourceProvider != null; }
        }

        public string LastFailureReason { get; private set; }

        public void Initialize(GameObject rigRoot, Camera xrCamera)
        {
            _rigRoot = rigRoot;
            _xrCamera = xrCamera;

            EnsureOverlayObjects();
            EnsureMaterials();
            SetOverlayActive(false);
            PushShaderParameters();
        }

        private void OnDisable()
        {
            SetOverlayActive(false);
        }

        private void OnDestroy()
        {
            if (_maskRenderTexture != null)
            {
                _maskRenderTexture.Release();
                Destroy(_maskRenderTexture);
                _maskRenderTexture = null;
            }

            if (_cameraSourceProvider != null)
            {
                _cameraSourceProvider.Dispose();
                _cameraSourceProvider = null;
            }

            if (_colorMaskMaterial != null)
            {
                Destroy(_colorMaskMaterial);
                _colorMaskMaterial = null;
            }

            if (_compositeMaterial != null)
            {
                Destroy(_compositeMaterial);
                _compositeMaterial = null;
            }
        }

        private void Update()
        {
            if (_cameraSourceProvider != null)
            {
                _cameraSourceProvider.Tick();
            }

            UpdateOverlayVisibility();

            if (!IsAdvancedModeAvailable || !_overlayRequested || _overlayRenderer == null || !_overlayRenderer.enabled)
            {
                return;
            }

            Texture sourceTexture = _cameraSourceProvider.CurrentTexture;
            if (sourceTexture == null || sourceTexture.width <= 0 || sourceTexture.height <= 0)
            {
                return;
            }

            EnsureMaskRenderTexture(sourceTexture.width, sourceTexture.height);

            _pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
            PushShaderParameters();

            _colorMaskMaterial.SetTexture("_MainTex", sourceTexture);
            Graphics.Blit(sourceTexture, _maskRenderTexture, _colorMaskMaterial, 0);

            _compositeMaterial.SetTexture("_SourceTex", sourceTexture);
            _compositeMaterial.SetTexture("_MaskTex", _maskRenderTexture);

            UpdateOverlayQuadTransform();
        }

        public void SetOverlayActive(bool active)
        {
            _overlayRequested = active;
            UpdateOverlayVisibility();
        }

        public void SetProfile(ColorDetectionProfile profile)
        {
            _profile = profile;
            PushShaderParameters();
        }

        public void SetStyle(HighlightStyle style)
        {
            _style = style;
            PushShaderParameters();
        }

        public IEnumerator TryStartAdvancedModeRoutine()
        {
            LastFailureReason = string.Empty;
            _isAdvancedModeAvailable = false;

            DisposeCurrentProvider();
            EnsureMaterials();

            if (_colorMaskMaterial == null || _compositeMaterial == null)
            {
                LastFailureReason = "Advanced shaders were not found. Ensure Assets/Shaders/ColorMask.shader and Assets/Shaders/Composite.shader compile successfully.";
                yield break;
            }

            List<ICameraSourceProvider> candidates = new List<ICameraSourceProvider>();
            candidates.Add(new MrukReflectionCameraSourceProvider());

#if UNITY_EDITOR
            candidates.Add(new WebCamTextureCameraSourceProvider());
#endif

            for (int i = 0; i < candidates.Count; i++)
            {
                ICameraSourceProvider candidate = candidates[i];
                string initReason;
                if (!candidate.Initialize(this, _rigRoot, _xrCamera, out initReason))
                {
                    LastFailureReason = initReason;
                    candidate.Dispose();
                    continue;
                }

                float timeoutAt = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < timeoutAt)
                {
                    candidate.Tick();
                    Texture tex = candidate.CurrentTexture;
                    if (tex != null && tex.width > 0 && tex.height > 0)
                    {
                        _cameraSourceProvider = candidate;
                        _isAdvancedModeAvailable = true;
                        LastFailureReason = string.Empty;
                        PushShaderParameters();
                        UpdateOverlayVisibility();
                        yield break;
                    }

                    yield return null;
                }

                LastFailureReason = string.IsNullOrEmpty(initReason)
                    ? (candidate.Name + " started but no camera texture became available.")
                    : initReason;
                candidate.Dispose();
            }

            _isAdvancedModeAvailable = false;

            if (string.IsNullOrEmpty(LastFailureReason))
            {
                LastFailureReason = "Advanced mode unavailable: MRUK PassthroughCameraAccess not found or no camera texture stream.";
            }
        }

        public IEnumerator CalibrateCenterRoutine(Action<Vector3> onSuccess, Action<string> onFailure)
        {
            if (!IsAdvancedModeAvailable || _cameraSourceProvider == null)
            {
                if (onFailure != null)
                {
                    onFailure("Advanced mode is not active.");
                }

                yield break;
            }

            Texture sourceTexture = _cameraSourceProvider.CurrentTexture;
            if (sourceTexture == null || sourceTexture.width <= 0 || sourceTexture.height <= 0)
            {
                if (onFailure != null)
                {
                    onFailure("Camera texture is not ready for calibration.");
                }

                yield break;
            }

            const int sampleRtSize = 64;
            const int centerPatchRadius = 8;

            RenderTexture tempRt = new RenderTexture(sampleRtSize, sampleRtSize, 0, RenderTextureFormat.ARGB32);
            tempRt.useMipMap = false;
            tempRt.autoGenerateMips = false;
            tempRt.Create();

            Texture2D cpuReadback = new Texture2D(sampleRtSize, sampleRtSize, TextureFormat.RGB24, false, false);

            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(sourceTexture, tempRt);
            yield return new WaitForEndOfFrame();

            try
            {
                RenderTexture.active = tempRt;
                cpuReadback.ReadPixels(new Rect(0, 0, sampleRtSize, sampleRtSize), 0, 0, false);
                cpuReadback.Apply(false, false);

                Color accum = Color.black;
                int count = 0;
                int cx = sampleRtSize / 2;
                int cy = sampleRtSize / 2;
                for (int y = cy - centerPatchRadius; y < cy + centerPatchRadius; y++)
                {
                    for (int x = cx - centerPatchRadius; x < cx + centerPatchRadius; x++)
                    {
                        accum += cpuReadback.GetPixel(x, y);
                        count++;
                    }
                }

                if (count <= 0)
                {
                    if (onFailure != null)
                    {
                        onFailure("Calibration sample was empty.");
                    }

                    yield break;
                }

                Color avg = accum / count;
                float h;
                float s;
                float v;
                Color.RGBToHSV(avg, out h, out s, out v);

                if (onSuccess != null)
                {
                    onSuccess(new Vector3(h, s, v));
                }
            }
            catch (Exception ex)
            {
                if (onFailure != null)
                {
                    onFailure("Calibration failed: " + ex.Message);
                }
            }
            finally
            {
                RenderTexture.active = prev;
                if (tempRt != null)
                {
                    tempRt.Release();
                    Destroy(tempRt);
                }

                if (cpuReadback != null)
                {
                    Destroy(cpuReadback);
                }
            }
        }

        private void EnsureOverlayObjects()
        {
            if (_xrCamera == null || _overlayQuad != null)
            {
                return;
            }

            _overlayQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _overlayQuad.name = "AdvancedMaskOverlayQuad";
            _overlayQuad.transform.SetParent(_xrCamera.transform, false);
            _overlayQuad.transform.localPosition = new Vector3(0f, 0f, OverlayDistance);
            _overlayQuad.transform.localRotation = Quaternion.identity;
            _overlayQuad.transform.localScale = Vector3.one;

            Collider col = _overlayQuad.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            _overlayRenderer = _overlayQuad.GetComponent<MeshRenderer>();
            if (_overlayRenderer != null)
            {
                _overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _overlayRenderer.receiveShadows = false;
                _overlayRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                _overlayRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            UpdateOverlayQuadTransform();
        }

        private void EnsureMaterials()
        {
            if (_colorMaskMaterial == null)
            {
                Shader colorMaskShader = Shader.Find("Hidden/QuestColorFinder/ColorMask");
                if (colorMaskShader != null)
                {
                    _colorMaskMaterial = new Material(colorMaskShader) { name = "ColorMaskRuntimeMat" };
                }
            }

            if (_compositeMaterial == null)
            {
                Shader compositeShader = Shader.Find("Hidden/QuestColorFinder/Composite");
                if (compositeShader != null)
                {
                    _compositeMaterial = new Material(compositeShader) { name = "CompositeRuntimeMat" };
                }
            }

            if (_overlayRenderer != null && _compositeMaterial != null)
            {
                _overlayRenderer.sharedMaterial = _compositeMaterial;
            }
        }

        private void UpdateOverlayVisibility()
        {
            bool shouldShow = _overlayRequested && IsAdvancedModeAvailable && _compositeMaterial != null && _colorMaskMaterial != null;
            if (_overlayQuad != null)
            {
                _overlayQuad.SetActive(shouldShow);
            }

            if (_overlayRenderer != null)
            {
                _overlayRenderer.enabled = shouldShow;
            }
        }

        private void EnsureMaskRenderTexture(int sourceWidth, int sourceHeight)
        {
            int width = Mathf.Max(64, sourceWidth / MaskDownsample);
            int height = Mathf.Max(64, sourceHeight / MaskDownsample);

            if (_maskRenderTexture != null && _maskRenderTexture.width == width && _maskRenderTexture.height == height)
            {
                return;
            }

            if (_maskRenderTexture != null)
            {
                _maskRenderTexture.Release();
                Destroy(_maskRenderTexture);
            }

            RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
                ? RenderTextureFormat.R8
                : RenderTextureFormat.ARGB32;
            _maskRenderTexture = new RenderTexture(width, height, 0, format);
            _maskRenderTexture.name = "QuestColorFinderMaskRT";
            _maskRenderTexture.useMipMap = false;
            _maskRenderTexture.autoGenerateMips = false;
            _maskRenderTexture.wrapMode = TextureWrapMode.Clamp;
            _maskRenderTexture.filterMode = FilterMode.Bilinear;
            _maskRenderTexture.Create();

            if (_compositeMaterial != null)
            {
                _compositeMaterial.SetTexture("_MaskTex", _maskRenderTexture);
            }
        }

        private void PushShaderParameters()
        {
            if (_colorMaskMaterial != null)
            {
                _colorMaskMaterial.SetVector("_TargetHSV", new Vector4(_profile.targetHsv.x, _profile.targetHsv.y, _profile.targetHsv.z, 0f));
                _colorMaskMaterial.SetVector("_Tolerance", new Vector4(
                    Mathf.Clamp(_profile.toleranceHsv.x, 0.001f, 0.5f),
                    Mathf.Clamp01(_profile.toleranceHsv.y),
                    Mathf.Clamp01(_profile.toleranceHsv.z),
                    0f));
                _colorMaskMaterial.SetVector("_SatRange", new Vector4(
                    Mathf.Clamp01(_profile.saturationRange.x),
                    Mathf.Clamp01(_profile.saturationRange.y),
                    0f,
                    0f));
                _colorMaskMaterial.SetVector("_ValRange", new Vector4(
                    Mathf.Clamp01(_profile.valueRange.x),
                    Mathf.Clamp01(_profile.valueRange.y),
                    0f,
                    0f));
                _colorMaskMaterial.SetFloat("_ChromaWeight", Mathf.Clamp01(_profile.chromaWeight));
                _colorMaskMaterial.SetFloat("_MaskSoftness", Mathf.Clamp(_profile.maskSoftness, 0.001f, 0.25f));
            }

            if (_compositeMaterial != null)
            {
                _compositeMaterial.SetVector("_TargetHSV", new Vector4(_profile.targetHsv.x, _profile.targetHsv.y, _profile.targetHsv.z, 0f));
                _compositeMaterial.SetColor("_HighlightColor", _profile.accentColor);
                _compositeMaterial.SetFloat("_HighlightBoost", Mathf.Max(0.5f, _profile.highlightBoost));
                _compositeMaterial.SetFloat("_Pulse", _pulse01);
                _compositeMaterial.SetFloat("_StyleMode", _style == HighlightStyle.BwExceptTarget ? 0f : 1f);
                _compositeMaterial.SetFloat("_GlowWidthPx", _style == HighlightStyle.GlowOverlay ? 2.0f : 1.0f);
                _compositeMaterial.SetFloat("_GlowIntensity", _style == HighlightStyle.GlowOverlay ? 2.2f : 1.3f);
                _compositeMaterial.SetFloat("_OutsideDesaturate", _style == HighlightStyle.BwExceptTarget ? 1f : 0f);
            }
        }

        private void UpdateOverlayQuadTransform()
        {
            if (_xrCamera == null || _overlayQuad == null)
            {
                return;
            }

            float distance = OverlayDistance;
            float fovRad = _xrCamera.fieldOfView * Mathf.Deg2Rad;
            float height = 2f * distance * Mathf.Tan(fovRad * 0.5f);
            float width = height * Mathf.Max(0.01f, _xrCamera.aspect);

            _overlayQuad.transform.localPosition = new Vector3(0f, 0f, distance);
            _overlayQuad.transform.localRotation = Quaternion.identity;
            _overlayQuad.transform.localScale = new Vector3(width, height, 1f);
        }

        private void DisposeCurrentProvider()
        {
            if (_cameraSourceProvider == null)
            {
                return;
            }

            _cameraSourceProvider.Dispose();
            _cameraSourceProvider = null;
        }

        private interface ICameraSourceProvider : IDisposable
        {
            string Name { get; }
            Texture CurrentTexture { get; }
            bool Initialize(MonoBehaviour host, GameObject rigRoot, Camera xrCamera, out string reason);
            void Tick();
        }

        private sealed class WebCamTextureCameraSourceProvider : ICameraSourceProvider
        {
            private WebCamTexture _webCamTexture;
            public string Name { get { return "WebCamTexture (Editor fallback)"; } }
            public Texture CurrentTexture { get { return _webCamTexture; } }

            public bool Initialize(MonoBehaviour host, GameObject rigRoot, Camera xrCamera, out string reason)
            {
                reason = string.Empty;
                WebCamDevice[] devices;
                try
                {
                    devices = WebCamTexture.devices;
                }
                catch (Exception ex)
                {
                    reason = "Failed to query editor webcams: " + ex.Message;
                    return false;
                }

                if (devices == null || devices.Length == 0)
                {
                    reason = "No editor webcam found for Advanced-mode simulation.";
                    return false;
                }

                try
                {
                    _webCamTexture = new WebCamTexture(devices[0].name, 1280, 720, 30);
                    _webCamTexture.Play();
                }
                catch (Exception ex)
                {
                    reason = "Editor webcam fallback failed to start: " + ex.Message;
                    if (_webCamTexture != null)
                    {
                        try
                        {
                            if (_webCamTexture.isPlaying)
                            {
                                _webCamTexture.Stop();
                            }
                        }
                        catch
                        {
                        }

                        UnityEngine.Object.Destroy(_webCamTexture);
                        _webCamTexture = null;
                    }

                    return false;
                }

                return true;
            }

            public void Tick()
            {
            }

            public void Dispose()
            {
                if (_webCamTexture != null)
                {
                    if (_webCamTexture.isPlaying)
                    {
                        _webCamTexture.Stop();
                    }

                    UnityEngine.Object.Destroy(_webCamTexture);
                    _webCamTexture = null;
                }
            }
        }

        private sealed class MrukReflectionCameraSourceProvider : ICameraSourceProvider
        {
            private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            private MonoBehaviour _host;
            private Component _cameraAccessComponent;
            private readonly List<MemberInfo> _textureMembers = new List<MemberInfo>();
            private bool _triedDiscoverMembers;

            public string Name { get { return "MRUK PassthroughCameraAccess"; } }

            public Texture CurrentTexture
            {
                get
                {
                    if (_cameraAccessComponent == null)
                    {
                        return null;
                    }

                    if (!_triedDiscoverMembers)
                    {
                        DiscoverTextureMembers();
                    }

                    for (int i = 0; i < _textureMembers.Count; i++)
                    {
                        Texture tex = ReadTexture(_textureMembers[i]);
                        if (tex != null && tex.width > 0 && tex.height > 0)
                        {
                            return tex;
                        }
                    }

                    return null;
                }
            }

            public bool Initialize(MonoBehaviour host, GameObject rigRoot, Camera xrCamera, out string reason)
            {
                _host = host;
                reason = string.Empty;

                Type componentType = FindPassthroughCameraAccessType();
                if (componentType == null)
                {
                    reason = "MRUK PassthroughCameraAccess component type not found. Install Meta MR Utility Kit.";
                    return false;
                }

                _cameraAccessComponent = host.GetComponent(componentType);
                if (_cameraAccessComponent == null)
                {
                    _cameraAccessComponent = host.gameObject.AddComponent(componentType);
                }

                if (_cameraAccessComponent == null)
                {
                    reason = "Failed to add MRUK PassthroughCameraAccess component.";
                    return false;
                }

                TrySetBool("enabled", true);
                TrySetBool("autoStart", true);
                TrySetBool("requestPermissionsOnStart", false);
                TrySetBool("autoRequestPermission", false);

                TryInvokeFirst(
                    "Initialize",
                    "RequestPermission",
                    "RequestPermissions",
                    "StartCameraAccess",
                    "StartCamera",
                    "StartCapture",
                    "StartPassthroughCamera",
                    "Begin");

                return true;
            }

            public void Tick()
            {
                if (_cameraAccessComponent == null)
                {
                    return;
                }

                // Some versions expose explicit update/poll methods; invoke opportunistically.
                TryInvokeFirst(
                    "Poll",
                    "UpdateFrame",
                    "Refresh",
                    "Tick");
            }

            public void Dispose()
            {
                if (_cameraAccessComponent != null)
                {
                    TryInvokeFirst(
                        "StopCameraAccess",
                        "StopCamera",
                        "StopCapture",
                        "End");

                    if (_host != null)
                    {
                        UnityEngine.Object.Destroy(_cameraAccessComponent);
                    }
                }

                _cameraAccessComponent = null;
                _textureMembers.Clear();
                _triedDiscoverMembers = false;
                _host = null;
            }

            private void DiscoverTextureMembers()
            {
                _triedDiscoverMembers = true;
                _textureMembers.Clear();

                if (_cameraAccessComponent == null)
                {
                    return;
                }

                Type type = _cameraAccessComponent.GetType();

                PropertyInfo[] properties = type.GetProperties(Flags);
                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo property = properties[i];
                    if (!property.CanRead)
                    {
                        continue;
                    }

                    if (typeof(Texture).IsAssignableFrom(property.PropertyType))
                    {
                        _textureMembers.Add(property);
                    }
                }

                FieldInfo[] fields = type.GetFields(Flags);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (typeof(Texture).IsAssignableFrom(field.FieldType))
                    {
                        _textureMembers.Add(field);
                    }
                }

                _textureMembers.Sort(CompareTextureMemberPriority);
            }

            private static int CompareTextureMemberPriority(MemberInfo a, MemberInfo b)
            {
                return GetScore(a.Name).CompareTo(GetScore(b.Name));

                int GetScore(string n)
                {
                    string name = n.ToLowerInvariant();
                    int score = 100;
                    if (name.Contains("left")) score -= 25;
                    if (name.Contains("color")) score -= 20;
                    if (name.Contains("camera")) score -= 10;
                    if (name.Contains("texture")) score -= 5;
                    return score;
                }
            }

            private Texture ReadTexture(MemberInfo member)
            {
                try
                {
                    object value;
                    if (member is PropertyInfo property)
                    {
                        value = property.GetValue(_cameraAccessComponent, null);
                    }
                    else if (member is FieldInfo field)
                    {
                        value = field.GetValue(_cameraAccessComponent);
                    }
                    else
                    {
                        return null;
                    }

                    return value as Texture;
                }
                catch
                {
                    return null;
                }
            }

            private bool TrySetBool(string name, bool value)
            {
                if (_cameraAccessComponent == null)
                {
                    return false;
                }

                if (string.Equals(name, "enabled", StringComparison.OrdinalIgnoreCase))
                {
                    Behaviour behaviour = _cameraAccessComponent as Behaviour;
                    if (behaviour != null)
                    {
                        behaviour.enabled = value;
                        return true;
                    }
                }

                PropertyInfo property = _cameraAccessComponent.GetType().GetProperty(name, Flags);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                {
                    try
                    {
                        property.SetValue(_cameraAccessComponent, value, null);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                FieldInfo field = _cameraAccessComponent.GetType().GetField(name, Flags);
                if (field != null && field.FieldType == typeof(bool))
                {
                    try
                    {
                        field.SetValue(_cameraAccessComponent, value);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                return false;
            }

            private void TryInvokeFirst(params string[] methodNames)
            {
                if (_cameraAccessComponent == null)
                {
                    return;
                }

                Type type = _cameraAccessComponent.GetType();
                for (int i = 0; i < methodNames.Length; i++)
                {
                    MethodInfo method = type.GetMethod(methodNames[i], Flags, null, Type.EmptyTypes, null);
                    if (method == null)
                    {
                        continue;
                    }

                    try
                    {
                        method.Invoke(_cameraAccessComponent, null);
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }

            private static Type FindPassthroughCameraAccessType()
            {
                Type exact = Type.GetType("Meta.XR.MRUtilityKit.PassthroughCameraAccess, Meta.XR.MRUtilityKit");
                if (exact != null)
                {
                    return exact;
                }

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

                        if (typeof(Component).IsAssignableFrom(type) &&
                            type.Name.IndexOf("PassthroughCameraAccess", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return type;
                        }
                    }
                }

                return null;
            }
        }
    }
}
