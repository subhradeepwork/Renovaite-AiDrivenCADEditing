Shader "Custom/ShadedWireframeTransparent"
{
    Properties
    {
        _MainColor ("Main Color (Transparent)", Color) = (1,1,1,0.3)
        _WireColor ("Wire Color (Solid)", Color) = (0,0,0,1)
        _Thickness ("Line Thickness", Range(0.1,5)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType"="Transparent" }
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2g
            {
                float4 pos : POSITION;
            };

            struct g2f
            {
                float4 pos : SV_POSITION;
                float3 bary : TEXCOORD0;
            };

            v2g vert(appdata v)
            {
                v2g o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            [maxvertexcount(3)]
            void geom(triangle v2g input[3], inout TriangleStream<g2f> triStream)
            {
                float3 baryCoords[3] = { float3(1,0,0), float3(0,1,0), float3(0,0,1) };

                for (int i = 0; i < 3; i++)
                {
                    g2f o;
                    o.pos = input[i].pos;
                    o.bary = baryCoords[i];
                    triStream.Append(o);
                }
            }

            float edgeFactor(float3 bary, float thickness)
            {
                float3 d = fwidth(bary);
                float3 a3 = smoothstep(0.0, thickness * d, bary);
                return min(min(a3.x, a3.y), a3.z);
            }

            fixed4 _MainColor;
            fixed4 _WireColor;
            float _Thickness;

            fixed4 frag(g2f i) : SV_Target
            {
                float edge = edgeFactor(i.bary, _Thickness);
                // edge = 0 → wireframe, edge = 1 → center
                fixed4 color = lerp(_WireColor, _MainColor, edge);
                return color;
            }
            ENDCG
        }
    }
}
