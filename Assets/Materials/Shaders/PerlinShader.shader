Shader "Custom/NewSurfaceShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        mapWidth ("Size", Range(0,1000)) = 255
        octaves("octaves", Range(0,10)) = 0
        scaleFactor("scale", Range(0.001, 20.0)) = 1.0
        persistence("persistence", Range(.0, 1.0)) = .5
        lacunarity("lacunarity", Range(1.0,3.0)) = 2.0
        warpStrength("warpStrength", Range(0.0, 5.0)) = 0.0 
        warpFrequency("warpFreq", Range(.0, 5.0)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
        };

        half _Glossiness;
        half _Metallic;
        int mapWidth;
        int octaves;
        float scaleFactor;
        float persistence;
        float lacunarity;
        float warpStrength;
        float warpFrequency;
        fixed4 _Color;

        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)


        float4 mod289(float4 x)
        {
          return x - floor(x / 289.0) * 289.0;
        }

        float4 permute(float4 x)
        {
          return mod289((x * 34.0 + 10.0)*x);
        }

        float4 taylorInvSqrt(float4 r)
        {
          return 1.79284291400159 - 0.85373472095314 * r;
        }

        float2 fade(float2 t) {
          return t*t*t*(t*(t*6.0-15.0)+10.0);
        }

        // Classic Perlin noise
        float perlin(float2 P)
        {
          float4 Pi = floor(P.xyxy) + float4(0.0, 0.0, 1.0, 1.0);
          float4 Pf = frac(P.xyxy) - float4(0.0, 0.0, 1.0, 1.0);
          Pi = mod289(Pi); // To avoid truncation effects in permutation
          float4 ix = Pi.xzxz;
          float4 iy = Pi.yyww;
          float4 fx = Pf.xzxz;
          float4 fy = Pf.yyww;

          float4 i = permute(permute(ix) + iy);

          float4 gx = frac(i * (1.0 / 41.0)) * 2.0 - 1.0 ;
          float4 gy = abs(gx) - 0.5 ;
          float4 tx = floor(gx + 0.5);
          gx = gx - tx;

          float2 g00 = float2(gx.x,gy.x);
          float2 g10 = float2(gx.y,gy.y);
          float2 g01 = float2(gx.z,gy.z);
          float2 g11 = float2(gx.w,gy.w);

          float4 norm = taylorInvSqrt(float4(dot(g00, g00), dot(g01, g01), dot(g10, g10), dot(g11, g11)));

          float n00 = norm.x * dot(g00, float2(fx.x, fy.x));
          float n10 = norm.y * dot(g10, float2(fx.y, fy.y));
          float n01 = norm.z * dot(g01, float2(fx.z, fy.z));
          float n11 = norm.w * dot(g11, float2(fx.w, fy.w));

          float2 fade_xy = fade(Pf.xy);
          float2 n_x = lerp(float2(n00, n01), float2(n10, n11), fade_xy.x);
          float n_xy = lerp(n_x.x, n_x.y, fade_xy.y);
          return 2.3 * n_xy;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Centered coordinate calculation (matches compute shader)
            float2 pos = (IN.uv_MainTex - 0.5) / scaleFactor;
            float c = 0;
            
            // Warp implementation
            float2 warp = 0;
            if(warpFrequency > 0) {
                float2 warpPos = pos * warpFrequency;
                warp = warpStrength * warpFrequency * float2(
                    perlin(warpPos),
                    perlin(warpPos + float2(100, 100))
                );
            }
            pos += warp;

            // Octave processing with simulated offset buffer
            float frequency = 1;
            float amplitude = 1;
            for(int i = 0; i < octaves; i++) {
                float2 offset = float2(i*12.9898, i*78.233); // Standard noise seed pattern
                float2 newPos = (pos - offset) * frequency;
                c += perlin(newPos) * amplitude;
                frequency *= lacunarity;
                amplitude *= persistence;
            }

            // Visual normalization matching compute range
            c = saturate(c * 0.5 + 0.5); 
            o.Albedo = _Color.rgb * c;
            
            // Standard PBR properties
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack "Diffuse"
}