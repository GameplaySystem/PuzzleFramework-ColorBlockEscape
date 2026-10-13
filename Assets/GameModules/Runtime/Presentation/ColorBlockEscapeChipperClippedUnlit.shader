Shader "ColorBlockEscape/ChipperClippedUnlit"
{
    Properties
    {
        _BaseColor("Color", Color) = (1, 1, 1, 1)
        _ClipPlane("World Clip Plane", Vector) = (0, 1, 0, 0)
        _ClipEnabled("Clip Enabled", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _ClipPlane;
                float _ClipEnabled;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                if (_ClipEnabled > 0.5)
                {
                    float signedDistance = dot(float4(input.positionWS, 1.0), _ClipPlane);
                    clip(-signedDistance);
                }
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
