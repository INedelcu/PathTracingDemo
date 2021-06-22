using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
    
// The CreateAssetMenu attribute lets you create instances of this class in the Unity Editor.
[CreateAssetMenu(menuName = "Rendering/RaytracingRenderPipelineAsset")]
public class RaytracingRenderPipelineAsset : RenderPipelineAsset
{
    [Range(1, 100)]
    public int bounceCountOpaque = 5;
    
    [Range(1, 100)]
    public int bounceCountTransparent = 8;

    public bool debugOutput = false;

    public RayTracingShader rayTracingShader = null;
    public RayTracingShader rayTracingShaderGBuffer = null;
    
    // replace with environment from Lighting window.
    public Cubemap envTexture = null;

    // Control instancing parameters (we replicate the entire scene for now)
    [Min(0)]
    public Vector2Int   instances = new Vector2Int(1, 1);
    public Vector2      instanceSpacing = new Vector2(1, 1);

    // Unity calls this method before rendering the first frame.
    // If a setting on the Render Pipeline Asset changes, Unity destroys the current Render Pipeline Instance and calls this method again before rendering the next frame.
    protected override RenderPipeline CreatePipeline()
    {
        // Instantiate the Render Pipeline that this custom SRP uses for rendering.
        return new RaytracingRenderPipelineInstance(this);
    }
}