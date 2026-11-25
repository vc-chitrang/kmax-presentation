// Inside-out backdrop sphere for the presentation scene (URP, unlit).
// Samples a 6-sided sky (same six textures and face layout as Unity's Skybox/6 Sided) by the direction from the sphere's
// centre, not from the camera. Because the Kmax rig camera sits off-centre inside the sphere, its narrow frustum covers a
// wider part of the sky than a real skybox would, so the sky no longer looks zoomed in.
Shader "KmaxPresentation/BackdropSphere" {
    Properties {
        _Tint ("Tint", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation (yaw, degrees)", Range(0, 360)) = 0
        [NoScaleOffset] _FrontTex ("Front [+Z]", 2D) = "grey" {}
        [NoScaleOffset] _BackTex ("Back [-Z]", 2D) = "grey" {}
        [NoScaleOffset] _LeftTex ("Left [+X]", 2D) = "grey" {}
        [NoScaleOffset] _RightTex ("Right [-X]", 2D) = "grey" {}
        [NoScaleOffset] _UpTex ("Up [+Y]", 2D) = "grey" {}
        [NoScaleOffset] _DownTex ("Down [-Y]", 2D) = "grey" {}
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Front
        // Writes depth so URP's skybox pass (drawn where depth is still at the far plane) does not paint over the sphere.
        ZWrite On

        Pass {
            Name "BackdropUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FrontTex); SAMPLER(sampler_FrontTex);
            TEXTURE2D(_BackTex); SAMPLER(sampler_BackTex);
            TEXTURE2D(_LeftTex); SAMPLER(sampler_LeftTex);
            TEXTURE2D(_RightTex); SAMPLER(sampler_RightTex);
            TEXTURE2D(_UpTex); SAMPLER(sampler_UpTex);
            TEXTURE2D(_DownTex); SAMPLER(sampler_DownTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Exposure;
                float _Rotation;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RotateAroundY(float3 direction, float degrees) {
                float radians = degrees * PI / 180.0;
                float s = sin(radians);
                float c = cos(radians);
                return float3(c * direction.x - s * direction.z, direction.y, s * direction.x + c * direction.z);
            }

            Varyings Vert(Attributes input) {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionOS = RotateAroundY(input.positionOS.xyz, _Rotation);
                return output;
            }

            // Face layout of Unity's Skybox/6 Sided: each face is the view from the centre along that axis, top of the image up
            // (for Up the top of the image faces -Z, for Down it faces +Z).
            half3 SampleSky(float3 d) {
                float3 a = abs(d);
                float2 uv;
                half3 color;
                if (a.z >= a.x && a.z >= a.y) {
                    if (d.z > 0.0) {
                        uv = float2(d.x / a.z, d.y / a.z);
                        color = SAMPLE_TEXTURE2D(_FrontTex, sampler_FrontTex, uv * 0.5 + 0.5).rgb;
                    } else {
                        uv = float2(-d.x / a.z, d.y / a.z);
                        color = SAMPLE_TEXTURE2D(_BackTex, sampler_BackTex, uv * 0.5 + 0.5).rgb;
                    }
                } else if (a.x >= a.y) {
                    if (d.x > 0.0) {
                        uv = float2(-d.z / a.x, d.y / a.x);
                        color = SAMPLE_TEXTURE2D(_LeftTex, sampler_LeftTex, uv * 0.5 + 0.5).rgb;
                    } else {
                        uv = float2(d.z / a.x, d.y / a.x);
                        color = SAMPLE_TEXTURE2D(_RightTex, sampler_RightTex, uv * 0.5 + 0.5).rgb;
                    }
                } else {
                    if (d.y > 0.0) {
                        uv = float2(d.x / a.y, -d.z / a.y);
                        color = SAMPLE_TEXTURE2D(_UpTex, sampler_UpTex, uv * 0.5 + 0.5).rgb;
                    } else {
                        uv = float2(d.x / a.y, d.z / a.y);
                        color = SAMPLE_TEXTURE2D(_DownTex, sampler_DownTex, uv * 0.5 + 0.5).rgb;
                    }
                }
                return color;
            }

            half4 Frag(Varyings input) : SV_Target {
                float3 direction = normalize(input.directionOS);
                // Same tint scale as Unity's skybox shaders (tint 0.5 = no change): 2 in gamma space, 4.5948 in linear space.
                #if defined(UNITY_COLORSPACE_GAMMA)
                    half tintScale = 2.0;
                #else
                    half tintScale = 4.59479380;
                #endif
                half3 color = SampleSky(direction) * _Tint.rgb * tintScale * _Exposure;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
