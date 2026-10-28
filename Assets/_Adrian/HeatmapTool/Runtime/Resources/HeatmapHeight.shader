Shader "Hidden/HeatmapTool/Height"
{
    Properties
    {
        _HideAbove ("Hide Above", Float) = 100000
        _HeightMin ("Height Min", Float) = 0
        _HeightRange ("Height Range", Float) = 1
    }

    SubShader
    {
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _HideAbove;
            float _HeightMin;
            float _HeightRange;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float height : TEXCOORD0;
            };

            v2f vert (float4 vertex : POSITION)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, vertex);
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.height = world.y;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                clip(_HideAbove - i.height);
                float v = 1.0 + floor(saturate((i.height - _HeightMin) / _HeightRange) * 16777214.0 + 0.5);
                float r = floor(v / 65536.0);
                float g = floor((v - r * 65536.0) / 256.0);
                float b = v - r * 65536.0 - g * 256.0;
                return float4(r, g, b, 255.0) / 255.0;
            }
            ENDCG
        }
    }
}
