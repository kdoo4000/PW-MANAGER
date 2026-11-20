Shader "Hidden/PWManager/BeltMaterial"
{
    Properties { _MainTex ("Part", 2D) = "white" {} _Mask ("Material masks", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Mask;
            float4 _Metal, _Strap;
            float _HasMask, _MetalChanged, _StrapChanged, _White;
            float4 frag(v2f_img input) : SV_Target
            {
                float4 part = tex2D(_MainTex, input.uv);
                float3 mask = tex2D(_Mask, input.uv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                    part.rgb = LinearToGammaSpace(part.rgb);
                #endif
                bool gold = part.r > 35.0/255 && part.g > 25.0/255 && part.r >= part.g*.96 && part.b < part.g*.85;
                if (_HasMask > .5 && _White > .5 && mask.b > .5) part.rgb = 1;
                else if (_HasMask > .5 && mask.g > .5 && _StrapChanged > .5)
                {
                    float shade = _White > .5 ? 1 : clamp(dot(part.rgb, float3(1,1,1))*255/114, .72, 1.3);
                    part.rgb = _Strap.rgb * shade;
                }
                else if (_MetalChanged > .5 && (_HasMask > .5 ? mask.r > .5 : gold))
                {
                    float luma = dot(part.rgb, float3(.2126,.7152,.0722));
                    part.rgb = luma < .74 ? _Metal.rgb*luma/.74 : _Metal.rgb+(1-_Metal.rgb)*(luma-.74)/.26;
                }
                #ifndef UNITY_COLORSPACE_GAMMA
                    part.rgb = GammaToLinearSpace(part.rgb);
                #endif
                return part;
            }
            ENDCG
        }
    }
}
