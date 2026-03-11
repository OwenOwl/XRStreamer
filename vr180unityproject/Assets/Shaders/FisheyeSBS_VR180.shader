Shader "Unlit/FisheyeSBS_VR180_ProjModes"
{
    Properties
    {
        _MainTex ("SBS Video", 2D) = "black" {}
        _FovDeg ("Fisheye FOV", Range(120, 220)) = 160
        _Radius ("Circle Radius", Range(0.3, 0.7)) = 0.52
        _CenterX ("Circle Center X", Range(0.0, 1.0)) = 0.50
        _CenterY ("Circle Center Y", Range(0.0, 1.0)) = 0.50
        _FlipY ("Flip Y", Float) = 1

        // 0=Equidistant, 1=Equisolid, 2=Stereographic, 3=Orthographic
        _ProjMode ("Projection Mode", Range(0, 3)) = 1
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
            float _ProjMode;

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

            bool IsInsideHalfUV(float2 uvHalf)
            {
                return uvHalf.x >= 0.0 && uvHalf.x <= 1.0 &&
                       uvHalf.y >= 0.0 && uvHalf.y <= 1.0;
            }

            float ModelRadius(float theta, float mode)
            {
                // Unnormalized fisheye radius law
                if (mode < 0.5)           return theta;                   // equidistant
                else if (mode < 1.5)      return 2.0 * sin(0.5 * theta);  // equisolid
                else if (mode < 2.5)      return 2.0 * tan(0.5 * theta);  // stereographic
                else                      return sin(theta);              // orthographic
            }

            float2 DirToFisheyeUV(float3 d)
            {
                d = normalize(d);

                // VR180 front hemisphere only
                if (d.z <= 0.0)
                    return float2(-1.0, -1.0);

                float theta = acos(saturate(d.z));
                float phi = atan2(d.y, d.x);

                float fovRad = radians(_FovDeg);
                float thetaMax = 0.5 * fovRad;

                // Normalize radius using the chosen model at thetaMax
                float rRaw = ModelRadius(theta, _ProjMode);
                float rMax = max(ModelRadius(thetaMax, _ProjMode), 1e-6);

                float rNorm = rRaw / rMax;
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
                float2 uvHalf = DirToFisheyeUV(d);

                if (!IsInsideHalfUV(uvHalf))
                    return fixed4(0,0,0,1);

                float eye = unity_StereoEyeIndex;
                float2 uv;
                uv.x = (uvHalf.x + eye) * 0.5;
                uv.y = uvHalf.y;

                return tex2D(_MainTex, saturate(uv));
            }
            ENDHLSL
        }
    }
}