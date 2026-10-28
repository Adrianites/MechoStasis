Shader "Hidden/HeatmapTool/Height"
{
    Properties
    {
        _HideAbove ("Hide Above", Float) = 100000
        _HeightBase ("Height Base", Float) = 0
        _Marker ("Corner Marker", Float) = 0
    }

    SubShader
    {
        Pass
        {
            // No depth test, so the result doesn't depend on the platform's depth conventions. Red keeps the
            // highest surface below the cut and green the highest top above it (Max); alpha counts the surfaces
            // above the cut (Add).
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One, One One
            BlendOp Max, Add

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _HideAbove;
            float _HeightBase;
            float _Marker;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float height : TEXCOORD0;
                float up : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.height = world.y;
                // only the sign matters, so the world normal isn't normalised (a mesh without normals counts as facing up)
                o.up = mul(v.normal, (float3x3)unity_WorldToObject).y;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                if (_Marker > 0.5) return float4(1e20, 0, 0, 0);
                // Above the cut, a surface facing up counts +1 and one facing down -1. A positive total means the cut
                // passes through something solid (a tall rock, a mountain, a wall), whose top is the highest surface
                // facing up; a closed roof above the floor cancels out.
                if (i.height > _HideAbove)
                    return i.up >= 0 ? float4(0, i.height - _HideAbove, 0, 1) : float4(0, 0, 0, -1);
                // stored above a base below everything the camera can see, so 0 still means "nothing here"
                return float4(i.height - _HeightBase, 0, 0, 0);
            }
            ENDCG
        }
    }
}
