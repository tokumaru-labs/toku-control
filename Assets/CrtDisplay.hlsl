// Shader Model 2 keeps WPF's software-rendering fallback available.
sampler2D source : register(s0);
float2 texelSize : register(c0);
float bloom : register(c1);
float rgbShift : register(c2);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 center = tex2D(source, uv);
    float2 shift = float2(texelSize.x * rgbShift, 0);
    float3 color = float3(tex2D(source, uv + shift).r, center.g, tex2D(source, uv - shift).b);
    float2 dx = float2(texelSize.x * 1.5, 0);
    float2 dy = float2(0, texelSize.y * 1.5);
    float3 nearby = (center.rgb + tex2D(source, uv + dx).rgb + tex2D(source, uv - dx).rgb
                    + tex2D(source, uv + dy).rgb + tex2D(source, uv - dy).rgb) * 0.2;
    color += max(nearby - 0.12, 0) * bloom;
    // WPF expects premultiplied alpha. Do not paint into transparent pixels.
    return float4(min(saturate(color), center.a), center.a);
}
