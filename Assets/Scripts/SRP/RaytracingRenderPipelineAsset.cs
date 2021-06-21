using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
    
// The CreateAssetMenu attribute lets you create instances of this class in the Unity Editor.
[CreateAssetMenu(menuName = "Rendering/RaytracingRenderPipelineAsset")]
public class RaytracingRenderPipelineAsset : RenderPipelineAsset
{
    [Range(1, 100)]
    public uint bounceCountOpaque = 5;
    
    [Range(1, 100)]
    public uint bounceCountTransparent = 8;
    
    public RayTracingShader rayTracingShader = null;
    
    // replace with environment from Lighting window.
    public Cubemap envTexture = null;
    
    public RayTracingAccelerationStructure rayTracingAccelerationStructure = null;
    
    // Unity calls this method before rendering the first frame.
    // If a setting on the Render Pipeline Asset changes, Unity destroys the current Render Pipeline Instance and calls this method again before rendering the next frame.
    protected override RenderPipeline CreatePipeline()
    {
        CreateRayTracingAccelerationStructure();
            
        // Instantiate the Render Pipeline that this custom SRP uses for rendering.
        return new RaytracingRenderPipelineInstance(this);
    }

    protected override void OnDisable()
    {
        if (rayTracingAccelerationStructure != null)
        {
            rayTracingAccelerationStructure.Release();
            rayTracingAccelerationStructure = null;
        }
    }
    
    private void CreateRayTracingAccelerationStructure()
    {
        if (rayTracingAccelerationStructure == null)
        {
            RayTracingAccelerationStructure.RASSettings settings = new RayTracingAccelerationStructure.RASSettings();
            settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
            settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
            settings.layerMask = 255;

            rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);
        }
    }
}