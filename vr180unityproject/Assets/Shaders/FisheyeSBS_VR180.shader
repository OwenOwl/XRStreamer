Shader "Unlit/FisheyeSBS_VR180"
{
    Properties
    {
        _MainTex ("SBS Video", 2D) = "black" {}
        _FovDeg ("Fisheye FOV", Range(120, 220)) = 180
        _Radius ("Circle Radius", Range(0.3, 0.5)) = 0.48
        _CenterX ("Circle Center X", Range(0.0, 1.0)) = 0.50
        _CenterY ("Circle Center Y", Range(0.0, 1.0)) = 0.50
        _FlipY ("Flip Y", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Front
        ZWrite On
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _FovDeg;
            float _Radius;
            float _CenterX;
            float _CenterY;
            float _FlipY;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(v.vertex.xyz);
                return o;
            }

            float2 DirToFisheyeUV(float3 d)
            {
                float theta = acos(saturate(d.z));
                theta = min(theta, 0.5 * UNITY_PI);

                float phi = atan2(d.y, d.x);

                float fovRad = radians(_FovDeg);
                float rNorm = theta / (0.5 * fovRad);

                float r = rNorm * _Radius;
                float2 uv;
                uv.x = _CenterX + r * cos(phi);
                uv.y = _CenterY + r * sin(phi) * _FlipY;

                return uv;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 d = normalize(i.dir);

                if (d.z < 0.0)
                    return fixed4(0,0,0,1);

                float2 uvHalf = DirToFisheyeUV(d);

                float eye = unity_StereoEyeIndex;
                float2 uv;
                uv.x = (uvHalf.x + eye) * 0.5;
                uv.y = uvHalf.y;

                uv = saturate(uv);  
                return tex2D(_MainTex, uv);
            }
            ENDHLSL
        }
    }
}