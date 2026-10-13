Shader "Hidden/PWManager/PortraitComposite"
{
    Properties { _MainTex ("Base", 2D) = "black" {} _Layer ("Layer", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Layer;
            float4 _Tint;
            float _Mode, _Skin;
            float3 materialTint(float3 rgb)
            {
                float x = dot(rgb, float3(.2126, .7152, .0722));
                if (x <= .05) return x;
                float shade = min(1, .3 + .7 * sqrt(max(0, (x - .05) / .45)));
                return x <= .5 ? _Tint.rgb * shade : _Tint.rgb + (1 - _Tint.rgb) * (x - .5) * .6;
            }
            float4 frag(v2f_img input) : SV_Target
            {
                float4 background = tex2D(_MainTex, input.uv);
                float4 layer = tex2D(_Layer, input.uv);
                #ifndef UNITY_COLORSPACE_GAMMA
                    layer.rgb = LinearToGammaSpace(layer.rgb);
                #endif
                if (_Mode == 1) layer.rgb = materialTint(layer.rgb);
                if (_Mode == 2) layer.rgb = saturate(dot(layer.rgb, float3(.2126, .7152, .0722)) * _Tint.rgb / (88.457 / 255));
                if (_Mode == 3) layer.rgb = _Tint.rgb;
                if (_Mode == 4)
                {
                    if (_Skin < 100)
                    {
                        float3 rgb = layer.rgb * _Skin / 100;
                        float gray = dot(rgb, float3(.2126, .7152, .0722));
                        layer.rgb = lerp(gray.xxx, rgb, 1 - (100 - _Skin) * .003);
                    }
                    else if (_Skin > 100)
                    {
                        float warmth = (_Skin - 100) / 50;
                        float3 exponent = pow(100 / _Skin, 2.5) * float3(1, 1 + .14 * warmth, 1 - .28 * warmth);
                        float3 blend = smoothstep(.1, .2, layer.rgb);
                        layer.rgb = lerp(layer.rgb, pow(max(layer.rgb, .00001), exponent), blend);
                    }
                }
                #ifndef UNITY_COLORSPACE_GAMMA
                    layer.rgb = GammaToLinearSpace(layer.rgb);
                #endif
                float alpha = layer.a + background.a * (1 - layer.a);
                float3 rgb = (layer.rgb * layer.a + background.rgb * background.a * (1 - layer.a)) / max(alpha, .00001);
                return float4(rgb, alpha);
            }
            ENDCG
        }
    }
}
