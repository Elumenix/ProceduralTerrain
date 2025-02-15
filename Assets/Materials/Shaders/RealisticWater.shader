Shader "Custom/RealisticWater"
{
    Properties
    {
        _WaveLength ("WaveLength", float) = 0.5
        _Amplitude ("Amplitude", float) = 1.0
        _Speed ("Speed", float) = 1.0
        _Direction ("Direction", Range(0.0, 360.0)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0


        struct Input
        {
            float3 vertexPos;
            float3 worldPos;
            float3 worldNormal;
            float3 worldTangent;
        };

        float _Direction;
        half _Speed;
        half _Amplitude;
        half _WaveLength;

        
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            // Initialize wave variables
            float freq = 2.0f / _WaveLength;
            float phase = _Speed * freq;
            float2 direction = float2(cos(radians(_Direction)), sin(radians(_Direction)));
            float time = _Time.y; 

            // Early calculations
            float2 worldPos = mul(unity_ObjectToWorld, v.vertex).xz;
            float innerResult = dot(direction, worldPos) * freq + time * phase;
            
            // Assigning vertex heights
            float height = _Amplitude * sin(innerResult);
            v.vertex.y = height;
            o.worldPos.y = mul(unity_ObjectToWorld, v.vertex).y;
            
            // BiTangent
            float dHeight_dx = _Amplitude * cos(innerResult) * direction.x * freq;
            float3 biTangent = normalize(float3(1.0, dHeight_dx, 0.0));

            // Tangent 
            float dHeight_dy = _Amplitude * cos(innerResult) * direction.y * freq;
            o.worldTangent = normalize(float3(0, dHeight_dy, 1));
            v.tangent = float4(o.worldTangent, 1);

            // Normal
            o.worldNormal = normalize(cross(o.worldTangent, biTangent));
            v.normal = o.worldNormal;
        }
        
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float height = IN.worldPos.y;
            o.Albedo = float3(0.0, 0.5 + height / 10.0, 1.0);
            //o.Albedo = float3(IN.worldNormal.x, 0, IN.worldNormal.z);
            //o.Albedo = float3(0.0, 0, 1.0);
            o.Alpha = 1; 
        }
        ENDCG
    }
    FallBack "Diffuse"
}
