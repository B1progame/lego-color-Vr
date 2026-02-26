using System;
using System.Reflection;
using UnityEngine;

namespace QuestLegoColorFinder
{
    public sealed class PassthroughBasicStylingController : MonoBehaviour
    {
        private GameObject _rigRoot;
        private Camera _xrCamera;
        private readonly MetaPassthroughBridge _bridge = new MetaPassthroughBridge();

        private ColorDetectionProfile _profile = LegoColorProfiles.Get(LegoColorPreset.Red);
        private HighlightStyle _style = HighlightStyle.BwExceptTarget;
        private bool _basicPresentationActive = true;

        private void Update()
        {
            if (!_bridge.IsAvailable)
            {
                return;
            }

            if (!_basicPresentationActive)
            {
                return;
            }

            if (_style == HighlightStyle.GlowOverlay)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3.2f);
                Color pulseColor = Color.Lerp(_profile.accentColor * 0.55f, _profile.accentColor, pulse);
                _bridge.TrySetColor("edgeColor", pulseColor);
            }
        }

        public void Initialize(GameObject rigRoot, Camera xrCamera)
        {
            _rigRoot = rigRoot;
            _xrCamera = xrCamera;
            _bridge.Initialize(_rigRoot, _xrCamera);
            ApplyCurrentStyle();
        }

        public void SetProfile(ColorDetectionProfile profile)
        {
            _profile = profile;
            ApplyCurrentStyle();
        }

        public void SetStyle(HighlightStyle style)
        {
            _style = style;
            ApplyCurrentStyle();
        }

        public void SetBasicPresentationActive(bool active)
        {
            _basicPresentationActive = active;
            ApplyCurrentStyle();
        }

        private void ApplyCurrentStyle()
        {
            if (_xrCamera == null)
            {
                return;
            }

            _bridge.Initialize(_rigRoot, _xrCamera);
            _bridge.SetPassthroughEnabled(true);

            if (!_bridge.IsAvailable)
            {
                // Editor/no-SDK fallback visual cue.
                _xrCamera.clearFlags = CameraClearFlags.SolidColor;
                _xrCamera.backgroundColor = _basicPresentationActive
                    ? (_style == HighlightStyle.BwExceptTarget ? new Color(0.08f, 0.08f, 0.08f) : Color.black)
                    : Color.black;
                return;
            }

            _xrCamera.clearFlags = CameraClearFlags.SolidColor;
            _xrCamera.backgroundColor = Color.clear;

            if (!_basicPresentationActive)
            {
                ApplyNeutralPassthrough();
                return;
            }

            ApplyApproximateHighlightPassthrough();
        }

        private void ApplyNeutralPassthrough()
        {
            _bridge.TrySetBool("hidden", false);
            _bridge.TrySetFloat("textureOpacity", 1f);
            _bridge.TrySetBool("edgeRenderingEnabled", false);

            SetBestEffortFloat("colorMapEditorBrightness", 0f);
            SetBestEffortFloat("colorMapEditorContrast", 0f);
            SetBestEffortFloat("colorMapEditorPosterize", 0f);

            _bridge.TrySetColor("edgeColor", Color.black);
        }

        private void ApplyApproximateHighlightPassthrough()
        {
            _bridge.TrySetBool("hidden", false);
            _bridge.TrySetFloat("textureOpacity", 1f);
            _bridge.TrySetEnumByName("placement", "Overlay");
            _bridge.TrySetEnumByName("overlayType", "Underlay"); // ignored if unsupported

            if (_style == HighlightStyle.BwExceptTarget)
            {
                _bridge.TrySetBool("edgeRenderingEnabled", false);

                // Basic mode cannot segment by color. We push contrast/posterization and a subtle tint to make the selected color feel more prominent.
                SetBestEffortFloat("colorMapEditorBrightness", -0.02f);
                SetBestEffortFloat("colorMapEditorContrast", 0.35f);
                SetBestEffortFloat("colorMapEditorPosterize", 0.18f);

                _bridge.TrySetColor("edgeColor", _profile.accentColor * 0.2f);
                _bridge.TrySetColor("colorScale", Color.Lerp(Color.white, _profile.accentColor, 0.1f));
                _bridge.TrySetColor("colorOffset", new Color(-0.03f, -0.03f, -0.03f, 0f));
            }
            else
            {
                _bridge.TrySetBool("edgeRenderingEnabled", true);
                SetBestEffortFloat("colorMapEditorBrightness", 0.03f);
                SetBestEffortFloat("colorMapEditorContrast", 0.25f);
                SetBestEffortFloat("colorMapEditorPosterize", 0.08f);

                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 3.2f);
                _bridge.TrySetColor("edgeColor", Color.Lerp(_profile.accentColor * 0.5f, _profile.accentColor, pulse));
                _bridge.TrySetColor("colorScale", Color.white);
                _bridge.TrySetColor("colorOffset", Color.clear);
            }
        }

        private void SetBestEffortFloat(string name, float value)
        {
            _bridge.TrySetFloat(name, value);

            if (name == "colorMapEditorBrightness")
            {
                _bridge.TrySetFloat("brightness", value);
            }
            else if (name == "colorMapEditorContrast")
            {
                _bridge.TrySetFloat("contrast", value);
            }
            else if (name == "colorMapEditorPosterize")
            {
                _bridge.TrySetFloat("posterize", value);
            }
        }

        private sealed class MetaPassthroughBridge
        {
            private Component _ovrManager;
            private Component _passthroughLayer;
            private bool _initialized;
            private bool _loggedMissingSdk;

            public bool IsAvailable
            {
                get { return _passthroughLayer != null || _ovrManager != null; }
            }

            public void Initialize(GameObject rigRoot, Camera xrCamera)
            {
                if (_initialized && (_passthroughLayer != null || _ovrManager != null))
                {
                    return;
                }

                _initialized = true;

                Type ovrManagerType = FindType("OVRManager");
                Type passthroughType = FindType("OVRPassthroughLayer");

                if (ovrManagerType == null && passthroughType == null)
                {
                    if (!_loggedMissingSdk)
                    {
                        _loggedMissingSdk = true;
                        Debug.LogWarning("Meta XR passthrough types not found. Basic mode will use editor fallback visuals until Meta XR SDK packages are installed.");
                    }

                    return;
                }

                if (ovrManagerType != null && rigRoot != null)
                {
                    _ovrManager = GetOrAddComponent(rigRoot, ovrManagerType);
                }

                if (passthroughType != null)
                {
                    if (xrCamera != null)
                    {
                        _passthroughLayer = GetOrAddComponent(xrCamera.gameObject, passthroughType);
                    }

                    if (_passthroughLayer == null && rigRoot != null)
                    {
                        _passthroughLayer = GetOrAddComponent(rigRoot, passthroughType);
                    }
                }

                if (_passthroughLayer != null)
                {
                    TrySetBool("hidden", false);
                    TrySetFloat("textureOpacity", 1f);
                    TrySetEnumByName("projectionSurfaceType", "Reconstructed");
                }
            }

            public void SetPassthroughEnabled(bool enabled)
            {
                if (_ovrManager == null)
                {
                    return;
                }

                TrySetMemberValue(_ovrManager, "isInsightPassthroughEnabled", enabled);
                TrySetMemberValue(_ovrManager, "insightPassthroughEnabled", enabled);
            }

            public bool TrySetBool(string memberName, bool value)
            {
                if (_passthroughLayer == null)
                {
                    return false;
                }

                return TrySetMemberValue(_passthroughLayer, memberName, value);
            }

            public bool TrySetFloat(string memberName, float value)
            {
                if (_passthroughLayer == null)
                {
                    return false;
                }

                return TrySetMemberValue(_passthroughLayer, memberName, value);
            }

            public bool TrySetColor(string memberName, Color value)
            {
                if (_passthroughLayer == null)
                {
                    return false;
                }

                return TrySetMemberValue(_passthroughLayer, memberName, value);
            }

            public bool TrySetEnumByName(string memberName, string enumValueName)
            {
                if (_passthroughLayer == null)
                {
                    return false;
                }

                MemberInfo member = FindMember(_passthroughLayer.GetType(), memberName);
                if (member == null)
                {
                    return false;
                }

                Type enumType = GetMemberType(member);
                if (enumType == null || !enumType.IsEnum)
                {
                    return false;
                }

                try
                {
                    object parsed = Enum.Parse(enumType, enumValueName, true);
                    return SetMemberValue(_passthroughLayer, member, parsed);
                }
                catch
                {
                    return false;
                }
            }

            private static Component GetOrAddComponent(GameObject go, Type componentType)
            {
                Component existing = go.GetComponent(componentType);
                return existing != null ? existing : go.AddComponent(componentType);
            }

            private static Type FindType(string typeName)
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Assembly assembly = assemblies[i];
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
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

            private static bool TrySetMemberValue(object target, string memberName, object value)
            {
                if (target == null)
                {
                    return false;
                }

                MemberInfo member = FindMember(target.GetType(), memberName);
                if (member == null)
                {
                    return false;
                }

                return SetMemberValue(target, member, value);
            }

            private static MemberInfo FindMember(Type type, string memberName)
            {
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

                PropertyInfo property = type.GetProperty(memberName, Flags);
                if (property != null && property.CanWrite)
                {
                    return property;
                }

                FieldInfo field = type.GetField(memberName, Flags);
                if (field != null)
                {
                    return field;
                }

                return null;
            }

            private static bool SetMemberValue(object target, MemberInfo member, object value)
            {
                try
                {
                    if (member is PropertyInfo property)
                    {
                        object converted = ConvertValue(value, property.PropertyType);
                        if (converted == null && property.PropertyType.IsValueType)
                        {
                            return false;
                        }

                        property.SetValue(target, converted, null);
                        return true;
                    }

                    if (member is FieldInfo field)
                    {
                        object converted = ConvertValue(value, field.FieldType);
                        if (converted == null && field.FieldType.IsValueType)
                        {
                            return false;
                        }

                        field.SetValue(target, converted);
                        return true;
                    }
                }
                catch
                {
                    // Best-effort bridge; silently ignore unsupported members.
                }

                return false;
            }

            private static Type GetMemberType(MemberInfo member)
            {
                if (member is PropertyInfo property)
                {
                    return property.PropertyType;
                }

                if (member is FieldInfo field)
                {
                    return field.FieldType;
                }

                return null;
            }

            private static object ConvertValue(object value, Type targetType)
            {
                if (value == null)
                {
                    return null;
                }

                Type sourceType = value.GetType();
                if (targetType.IsAssignableFrom(sourceType))
                {
                    return value;
                }

                try
                {
                    if (targetType == typeof(float))
                    {
                        return Convert.ToSingle(value);
                    }

                    if (targetType == typeof(int))
                    {
                        return Convert.ToInt32(value);
                    }

                    if (targetType == typeof(bool))
                    {
                        return Convert.ToBoolean(value);
                    }

                    if (targetType == typeof(Color))
                    {
                        if (value is Color color)
                        {
                            return color;
                        }
                    }

                    if (targetType.IsEnum && value is string enumString)
                    {
                        return Enum.Parse(targetType, enumString, true);
                    }
                }
                catch
                {
                    return null;
                }

                return null;
            }
        }
    }
}
