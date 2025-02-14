Shader "Custom/RealisticWater"
{
    Properties
    {
        _WaveLength ("WaveLength", float) = 0.5
        _Amplitude ("Amplitude", float) = 1.0
        _Speed ("Speed", float) = 1.0
        _Direction ("Direction", Vector) = (1, -1000, 0, -1000)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows vertex:vert

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        //sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
            float3 vertexPos;
            float3 worldPos;
        };

        fixed4 _Direction;
        half _Speed;
        half _Amplitude;
        half _WaveLength;

        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            float freq = 2.0f / _WaveLength;
            float phase = _Speed * freq;
            float2 direction = _Direction.xz;
            float time = _Time.y; 

            float2 worldPos = mul(unity_ObjectToWorld, v.vertex).xz;
            float height = _Amplitude * sin(dot(direction, worldPos) * freq + time * phase);

            v.vertex.y = height;
            o.worldPos.y = mul(unity_ObjectToWorld, v.vertex).y;
        }
        
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float height = IN.worldPos.y;
            o.Albedo = float3(0.0, 0.5 + height / 10.0, 1.0); 
            //o.Albedo = float3(0.0, 0, 1.0);
            o.Alpha = 1; 
        }
        ENDCG
    }
    FallBack "Diffuse"
}
