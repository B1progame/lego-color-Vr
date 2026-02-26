Shader "Hidden/QuestColorFinder/Composite"
{
    Properties
    {
        _SourceTex ("Source", 2D) = "black" {}
        _MaskTex ("Mask", 2D) = "black" {}
        _HighlightColor ("Highlight Color", Color) = (1,0.2,0.2,1)
        _HighlightBoost ("Highlight Boost", Float) = 1.4
        _Pulse ("Pulse", Float) = 0.5
        _StyleMode ("Style Mode", Float) = 0
        _GlowWidthPx ("Glow Width Px", Float) = 2
        _GlowIntensity ("Glow Intensity", Float) = 2.2
        _OutsideDesaturate ("Outside Desaturate", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _SourceTex;
            sampler2D _MaskTex;
            float4 _SourceTex_ST;
            float4 _MaskTex_TexelSize;
            float4 _HighlightColor;
            float _HighlightBoost;
            float _Pulse;
            float _StyleMode;
            float _GlowWidthPx;
            float _GlowIntensity;
            float _OutsideDesaturate;

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
                o.uv = TRANSFORM_TEX(v.uv, _SourceTex);
                return o;
            }

            float luminance(float3 c)
            {
                return dot(c, float3(0.2126, 0.7152, 0.0722));
            }

            float3 desaturate(float3 c, float amount)
            {
                float g = luminance(c);
                return lerp(c, g.xxx, saturate(amount));
            }

            float sampleMask(float2 uv)
            {
                return tex2D(_MaskTex, uv).r;
            }

            float glowEdge(float2 uv)
            {
                float2 texel = _MaskTex_TexelSize.xy * max(1.0, _GlowWidthPx);
                float c = sampleMask(uv);
                float n1 = sampleMask(uv + float2(texel.x, 0));
                float n2 = sampleMask(uv + float2(-texel.x, 0));
                float n3 = sampleMask(uv + float2(0, texel.y));
                float n4 = sampleMask(uv + float2(0, -texel.y));
                float n5 = sampleMask(uv + texel);
                float n6 = sampleMask(uv - texel);
                float maxNeighbor = max(max(max(n1, n2), max(n3, n4)), max(n5, n6));
                float edge = saturate(maxNeighbor - c + maxNeighbor * 0.25);
                edge = max(edge, saturate(c * 0.35));
                return edge;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float4 src = tex2D(_SourceTex, uv);
                float mask = sampleMask(uv);
                float edge = glowEdge(uv);

                if (_StyleMode < 0.5)
                {
                    float3 outside = desaturate(src.rgb, _OutsideDesaturate);
                    float3 insideBoost = saturate(src.rgb * _HighlightBoost + _HighlightColor.rgb * 0.14);
                    float softenedMask = smoothstep(0.10, 0.80, mask);
                    float3 outRgb = lerp(outside, insideBoost, softenedMask);
                    return float4(outRgb, 1.0);
                }
                else
                {
                    float pulse = lerp(0.75, 1.15, saturate(_Pulse));
                    float glow = saturate(edge * _GlowIntensity * pulse);
                    float3 glowColor = saturate(_HighlightColor.rgb * 1.35 + 0.15);
                    float alpha = saturate(glow);
                    return float4(glowColor * glow, alpha);
                }
            }
            ENDCG
        }
    }
}
