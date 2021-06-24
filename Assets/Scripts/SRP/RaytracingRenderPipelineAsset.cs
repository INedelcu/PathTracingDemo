using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// The CreateAssetMenu attribute lets you create instances of this class in the Unity Editor.
[CreateAssetMenu(menuName = "Rendering/RaytracingRenderPipelineAsset")]
public class RaytracingRenderPipelineAsset : RenderPipelineAsset
{
    [Header("Pathtracing Settings")]
    [Range(1, 100)]
    public int bounceCountOpaque = 5;
    
    [Range(1, 100)]
    public int bounceCountTransparent = 8;

    [Range(1, 256)]
    public int sampleCount = 1;

    public bool debugOutput = false;

    public RayTracingShader rayTracingShader = null;
    public RayTracingShader rayTracingShaderGBuffer = null;    
    [Header("A-Trous Settings")]
    public bool EnableATrous = false;
    public ComputeShader aTrousShader = null;
    [Range(1, 10)]
    public int ATrousIterations = 5;
    [Range(0.001f, 10)]
    public float aTrousRadianceSigma = 1.0f;
    [Range(0.001f, 10)]
    public float aTrousNormalSigma = 0.1f;
    [Range(0.001f, 10)]
    public float aTrousDepthSigma = 0.1f;
    public bool RadianceStopping = false;
    public bool NormalStopping = false;
    public bool DepthStopping = false;

    [Header("Temporal Accumulation Settings")]
    public bool enableAccumulation = true;
    public bool useMotionVectors = true;
    [Range(0f, 1f)]
    public float alpha = 0.2f;
    public float speedAdaptation = 0.01f;

    [Header("Environment Settings")]
    // replace with environment from Lighting window.
    public Cubemap envTexture = null;

    // Unity calls this method before rendering the first frame.
    // If a setting on the Render Pipeline Asset changes, Unity destroys the current Render Pipeline Instance and calls this method again before rendering the next frame.
    protected override RenderPipeline CreatePipeline()
    {
        // Instantiate the Render Pipeline that this custom SRP uses for rendering.
        return new RaytracingRenderPipelineInstance(this);
    }
}