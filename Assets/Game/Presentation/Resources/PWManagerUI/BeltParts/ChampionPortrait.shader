Shader "Hidden/PWManager/ChampionPortrait"
{
    Properties { _MainTex ("Background", 2D) = "black" {} _Layer ("Layer", 2D) = "black" {} _FaceMask ("Face", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Layer, _FaceMask;
            float4 _Pose, _Fold, _BeltSize, _Edge;
            float _Mode, _Front;
            float4 frag(v2f_img input) : SV_Target
            {
                float4 background = tex2D(_MainTex,input.uv), layer = 0;
                if (_Mode > .5)
                {
                    layer = tex2D(_Layer,input.uv);
                    if (_Mode > 1.5) layer.a *= tex2D(_FaceMask,input.uv).a;
                }
                else
                {
                    float2 delta = float2(input.uv.x*1568,(1-input.uv.y)*1411)-_Pose.xy;
                    float cosine=cos(_Pose.w), sine=sin(_Pose.w);
                    float2 beltPosition=float2(cosine*delta.x+sine*delta.y,-sine*delta.x+cosine*delta.y)/_Pose.z+_BeltSize.zw;
                    if (_Front < .5)
                    {
                        float divisor=1+_Fold.y*_Fold.y,a=(_Fold.y*_Fold.y-1)/divisor,b=2*_Fold.y/divisor;
                        float2 local=beltPosition-float2(_Fold.x,_BeltSize.y*.5);
                        beltPosition=float2(a*local.x+b*local.y,b*local.x-a*local.y)+float2(_Fold.x,_BeltSize.y*.5);
                    }
                    if (beltPosition.x>=0 && beltPosition.y>=0 && beltPosition.x<_BeltSize.x && beltPosition.y<_BeltSize.y)
                    {
                        float t=beltPosition.y/_BeltSize.y, distance=max(0,min(4,min(beltPosition.y-_Fold.z,_Fold.w-beltPosition.y)));
                        float corner=4-sqrt(max(0,16-(4-distance)*(4-distance)));
                        float seam=_Fold.x+_Fold.y*(beltPosition.y-_BeltSize.y*.5)+(_Front>.5?1:-1)*(8*t*(1-t)+corner);
                        if ((_Front>.5 && beltPosition.x>=seam)||(_Front<.5 && beltPosition.x<=seam))
                        {
                            layer=tex2D(_Layer,float2(beltPosition.x/_BeltSize.x,1-beltPosition.y/_BeltSize.y));
                            float edge=abs(beltPosition.x-seam);
                            float3 dark=float3(.078,.078,.078), highlight=_Edge.rgb;
                            #ifndef UNITY_COLORSPACE_GAMMA
                                dark=GammaToLinearSpace(dark); highlight=GammaToLinearSpace(highlight);
                            #endif
                            if (edge<3.5) layer.rgb=dark;
                            else if (abs(edge-4.5)<.75) layer.rgb=highlight;
                        }
                    }
                }
                float alpha=layer.a+background.a*(1-layer.a);
                return float4((layer.rgb*layer.a+background.rgb*background.a*(1-layer.a))/max(alpha,.00001),alpha);
            }
            ENDCG
        }
    }
}
