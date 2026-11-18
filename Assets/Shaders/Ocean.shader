// Océano estilizado para URP: olas por suma de senos (mismas que OceanWaves.cs),
// normales por píxel, reflejo de cielo con Fresnel, especular del sol, espuma en crestas y sombras.
Shader "OnePiece/Ocean"
{
    Properties
    {
        _DeepColor     ("Color profundo", Color) = (0.015, 0.11, 0.24, 1)
        _ShallowColor  ("Color cresta", Color)   = (0.04, 0.42, 0.52, 1)
        _HorizonColor  ("Cielo horizonte", Color) = (0.78, 0.88, 0.97, 1)
        _ZenithColor   ("Cielo cenit", Color)     = (0.22, 0.48, 0.85, 1)
        _FoamColor     ("Espuma", Color)          = (0.95, 0.98, 1, 1)
        _Smoothness    ("Suavidad", Range(0, 1))  = 0.9
        _SpecIntensity ("Brillo del sol", Range(0, 10)) = 4
        _FoamAmount    ("Cantidad de espuma", Range(0, 1)) = 0.3
        _RippleStrength("Ondas pequeñas", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor, _ShallowColor, _HorizonColor, _ZenithColor, _FoamColor;
            half _Smoothness, _SpecIntensity, _FoamAmount, _RippleStrength;
        CBUFFER_END

        // Globales escritas por OceanWaves.cs: xy = dirección, z = amplitud, w = longitud de onda
        float4 _OP_Waves[4];
        float  _OP_WaveTime;

        void SampleWaves(float2 p, out float h, out float2 grad, out float ampSum)
        {
            h = 0; grad = 0; ampSum = 0.0001;
            [unroll] for (int i = 0; i < 4; i++)
            {
                float4 w = _OP_Waves[i];
                float2 d = normalize(w.xy + float2(1e-5, 0));
                float k = 6.2831853 / max(w.w, 0.01);
                float c = sqrt(9.81 * k);
                float ph = k * dot(d, p) - c * _OP_WaveTime;
                h += w.z * sin(ph);
                grad += w.z * k * cos(ph) * d;
                ampSum += w.z;
            }
        }

        float3 DisplacedWorldPos(float3 positionOS)
        {
            float3 ws = TransformObjectToWorld(positionOS);
            float h, a; float2 g;
            SampleWaves(ws.xz, h, g, a);
            ws.y += h;
            return ws;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  fogCoord   : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = DisplacedWorldPos(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            float Fbm(float2 p)
            {
                float s = 0, a = 0.5;
                [unroll] for (int i = 0; i < 4; i++) { s += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            // Ondulaciones finas solo en la normal (no desplazan geometría)
            float2 RippleGrad(float2 p, float t)
            {
                const float3 R[4] = { float3( 0.8, 0.6, 3.7), float3(-0.5, 0.86, 2.3),
                                      float3( 0.2,-0.98, 1.45), float3(-0.9,-0.3, 0.9) };
                float2 g = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float k = 6.2831853 / R[i].z;
                    float c = sqrt(9.81 * k);
                    float ph = k * dot(R[i].xy, p) - c * t;
                    g += 0.06 * R[i].z * k * cos(ph) * R[i].xy * 0.25;
                }
                return g;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 pos = i.positionWS;
                float h, ampSum; float2 grad;
                SampleWaves(pos.xz, h, grad, ampSum);

                float3 camPos = GetCameraPositionWS();
                float dist = distance(camPos, pos);
                float detailFade = saturate(1 - dist / 220.0);
                grad += RippleGrad(pos.xz, _OP_WaveTime) * _RippleStrength * detailFade;

                float3 N = normalize(float3(-grad.x, 1, -grad.y));
                float3 V = normalize(camPos - pos);

                Light ml = GetMainLight(TransformWorldToShadowCoord(pos));
                float shadow = lerp(1, ml.shadowAttenuation, 0.75);
                float3 L = ml.direction;

                float hn = saturate(h / (2 * ampSum) + 0.5);           // 0 valle, 1 cresta
                float NdotL = saturate(dot(N, L));
                float3 ambient = SampleSH(N);

                // Cuerpo del agua + dispersión subsuperficial en las crestas a contraluz
                float3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, hn * hn * 0.85);
                float sss = pow(saturate(dot(V, -L) * 0.5 + 0.5), 3) * hn * hn;
                float3 water = body * (ambient * 0.9 + NdotL * ml.color * shadow * 0.35)
                             + _ShallowColor.rgb * sss * ml.color * 0.9;

                // Reflejo del cielo
                float fres = 0.02 + 0.98 * pow(1 - saturate(dot(N, V)), 5);
                float3 R = reflect(-V, N);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(R.y), 0.45));
                float3 col = lerp(water, sky, saturate(fres * 0.9));

                // Sol
                float3 H = normalize(L + V);
                float specPow = exp2(_Smoothness * 10 + 1);
                col += pow(saturate(dot(N, H)), specPow) * ml.color * shadow * _SpecIntensity
                       * (specPow + 8) / 25.0 * 0.15;

                // Espuma en crestas
                float2 drift = float2(_OP_WaveTime * 0.35, _OP_WaveTime * 0.12);
                float n = Fbm(pos.xz * 0.28 + drift) * 0.65 + Fbm(pos.xz * 1.3 - drift * 1.7) * 0.35;
                float crest = saturate((hn - (1 - _FoamAmount * 0.7)) * 3.5);
                // espuma en hebras: bandas finas de ruido en vez de manchas sólidas
                float streak = 1 - abs(Fbm(pos.xz * 0.6 + drift * 2.0) * 2 - 1);
                float foam = smoothstep(0.55, 0.9, crest * (0.45 + 0.55 * n) + streak * crest * 0.5)
                           * 0.85 * (0.4 + 0.6 * detailFade);
                float3 foamCol = _FoamColor.rgb * (ambient + NdotL * ml.color * shadow * 0.8);
                col = lerp(col, foamCol, foam);

                col = MixFog(col, i.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            float4 vertD(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformWorldToHClip(DisplacedWorldPos(positionOS.xyz));
            }
            half fragD() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
