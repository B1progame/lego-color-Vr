Shader "Hidden/QuestColorFinder/ColorMask"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _TargetHSV ("Target HSV", Vector) = (0, 0.9, 0.8, 0)
        _Tolerance ("Tolerance HSV", Vector) = (0.06, 0.35, 0.45, 0)
        _SatRange ("Saturation Range", Vector) = (0.15, 1, 0, 0)
        _ValRange ("Value Range", Vector) = (0.15, 1, 0, 0)
        _ChromaWeight ("Chroma Weight", Float) = 1
        _MaskSoftness ("Mask Softness", Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _TargetHSV;
            float4 _Tolerance;
            float4 _SatRange;
            float4 _ValRange;
            float _ChromaWeight;
            float _MaskSoftness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1e-6;
                float h = abs(q.z + (q.w - q.y) / (6.0 * d + e));
                float s = d / (q.x + e);
                float v = q.x;
                return float3(frac(h), saturate(s), saturate(v));
            }

            float remapTolerance(float delta, float tol, float softness)
            {
                float t = max(1e-4, tol);
                float s = max(1e-4, softness);
                return 1.0 - smoothstep(t, t + s, delta);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 rgb = tex2D(_MainTex, i.uv).rgb;
                float3 hsv = rgb2hsv(rgb);

                float hueDelta = abs(hsv.x - _TargetHSV.x);
                hueDelta = min(hueDelta, 1.0 - hueDelta);
                float hueScore = remapTolerance(hueDelta, _Tolerance.x, _MaskSoftness);
                float satScore = remapTolerance(abs(hsv.y - _TargetHSV.y), _Tolerance.y, _MaskSoftness);
                float valScore = remapTolerance(abs(hsv.z - _TargetHSV.z), _Tolerance.z, _MaskSoftness);

                float satGate = smoothstep(_SatRange.x - _MaskSoftness, _SatRange.x + _MaskSoftness, hsv.y) *
                                (1.0 - smoothstep(_SatRange.y - _MaskSoftness, _SatRange.y + _MaskSoftness, hsv.y));
                float valGate = smoothstep(_ValRange.x - _MaskSoftness, _ValRange.x + _MaskSoftness, hsv.z) *
                                (1.0 - smoothstep(_ValRange.y - _MaskSoftness, _ValRange.y + _MaskSoftness, hsv.z));

                float chromaScore = hueScore * satScore * valScore;
                float lumaLikeScore = satScore * valScore;
                float score = lerp(lumaLikeScore, chromaScore, saturate(_ChromaWeight));
                float mask = saturate(score * satGate * valGate);

                return fixed4(mask, mask, mask, mask);
            }
            ENDCG
        }
    }
}
