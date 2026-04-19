Shader "Unlit/FisheyeSBS_VR180"
{
    Properties
    {
        _MainTex ("SBS Video", 2D) = "black" {}
        _FovDeg ("Fisheye FOV", Range(120, 220)) = 160
        _SinHalfThetaMax ("[!] Sin(Fov/4)", Float) = 0.6427876096865
        _Radius ("Circle Radius", Range(0.3, 0.7)) = 0.52
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
            float _SinHalfThetaMax; // precomputed on CPU: sin(radians(_FovDeg) * 0.5)
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
                o.dir = v.vertex.xyz; // normalized once in frag after interpolation
                return o;
            }

            // d must already be normalized and d.z > 0 checked by caller
            float2 DirToFisheyeUV(float3 d)
            {
                float theta = acos(min(d.z, 1.0));
                float phi = atan2(d.y, d.x);

                // Equisolid projection: r = 2*sin(theta/2)
                float r = (sin(0.5 * theta) / max(_SinHalfThetaMax, 1e-6)) * _Radius;

                float sinPhi, cosPhi;
                sincos(phi, sinPhi, cosPhi);

                return float2(_CenterX + r * cosPhi,
                              _CenterY + r * sinPhi * _FlipY);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 d = normalize(i.dir);

                // VR180 front hemisphere only
                if (d.z <= 0.0)
                    return fixed4(0, 0, 0, 1);

                float2 uvHalf = DirToFisheyeUV(d);

                // Reject samples outside the fisheye circle (e.g. theta > thetaMax)
                if (!all(uvHalf == saturate(uvHalf)))
                    return fixed4(0, 0, 0, 1);

                float2 uv = float2((uvHalf.x + (float)unity_StereoEyeIndex) * 0.5, uvHalf.y);

                return tex2D(_MainTex, uv);
            }
            ENDHLSL
        }
    }
}