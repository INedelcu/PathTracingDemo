using System;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

public enum SunShaftsResolution
{
    Low = 0,
    Normal = 1,
    High = 2
}

public enum ShaftsScreenBlendMode
{
    Screen = 0,
    Add = 1
}

[Serializable]
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Rendering/Sun Shafts")]
public class SunShafts : PostEffectsBase
{
    public SunShaftsResolution resolution;
    public ShaftsScreenBlendMode screenBlendMode;
    public Transform sunTransform;
    public int radialBlurIterations;
    public Color sunColor;
    public Color sunThreshold;
    public float sunShaftBlurRadius;
    public float sunShaftIntensity;
    public float maxRadius;
    public bool useDepthTexture;
    public Shader sunShaftsShader;
    private Material sunShaftsMaterial;
    public Shader simpleClearShader;
    private Material simpleClearMaterial;

    public override bool CheckResources()
    {
        CheckSupport(useDepthTexture);
        sunShaftsMaterial = CheckShaderAndCreateMaterial(sunShaftsShader, sunShaftsMaterial);
        simpleClearMaterial = CheckShaderAndCreateMaterial(simpleClearShader, simpleClearMaterial);
        if (!isSupported)
        {
            ReportAutoDisable();
        }
        return isSupported;
    }

    public virtual void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (CheckResources() == false)
        {
            Graphics.Blit(source, destination);
            return;
        }
        // we actually need to check this every frame
        if (useDepthTexture)
        {
            ((Camera)GetComponent(typeof(Camera))).depthTextureMode = ((Camera)GetComponent(typeof(Camera))).depthTextureMode | DepthTextureMode.Depth;
        }
        var divider = 4;
        if (resolution == SunShaftsResolution.Normal)
        {
            divider = 2;
        }
        else
        {
            if (resolution == SunShaftsResolution.High)
            {
                divider = 1;
            }
        }
        var v = Vector3.one * 0.5f;
        if (sunTransform)
        {
            v = ((Camera)GetComponent(typeof(Camera))).WorldToViewportPoint(sunTransform.position);
        }
        else
        {
            v = new Vector3(0.5f, 0.5f, 0f);
        }
        var rtW = source.width / divider;
        var rtH = source.height / divider;
        RenderTexture lrColorB = null;
        var lrDepthBuffer = RenderTexture.GetTemporary(rtW, rtH, 0);
        // mask out everything except the skybox
        // we have 2 methods, one of which requires depth buffer support, the other one is just comparing images
        sunShaftsMaterial.SetVector("_BlurRadius4", new Vector4(1f, 1f, 0f, 0f) * sunShaftBlurRadius);
        sunShaftsMaterial.SetVector("_SunPosition", new Vector4(v.x, v.y, v.z, maxRadius));
        sunShaftsMaterial.SetVector("_SunThreshold", sunThreshold);
        if (!useDepthTexture)
        {
            var format = ((Camera)GetComponent(typeof(Camera))).allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
            var tmpBuffer = RenderTexture.GetTemporary(source.width, source.height, 0, format);
            RenderTexture.active = tmpBuffer;
            GL.ClearWithSkybox(false, (Camera)GetComponent(typeof(Camera)));
            sunShaftsMaterial.SetTexture("_Skybox", tmpBuffer);
            Graphics.Blit(source, lrDepthBuffer, sunShaftsMaterial, 3);
            RenderTexture.ReleaseTemporary(tmpBuffer);
        }
        else
        {
            Graphics.Blit(source, lrDepthBuffer, sunShaftsMaterial, 2);
        }
        // paint a small black small border to get rid of clamping problems
        DrawBorder(lrDepthBuffer, simpleClearMaterial);
        // radial blur:
        radialBlurIterations = Mathf.Clamp(radialBlurIterations, 1, 4);
        var ofs = sunShaftBlurRadius * (1f / 768f);
        sunShaftsMaterial.SetVector("_BlurRadius4", new Vector4(ofs, ofs, 0f, 0f));
        sunShaftsMaterial.SetVector("_SunPosition", new Vector4(v.x, v.y, v.z, maxRadius));
        var it2 = 0;
        while (it2 < radialBlurIterations)
        {
            // each iteration takes 2 * 6 samples
            // we update _BlurRadius each time to cheaply get a very smooth look
            lrColorB = RenderTexture.GetTemporary(rtW, rtH, 0);
            Graphics.Blit(lrDepthBuffer, lrColorB, sunShaftsMaterial, 1);
            RenderTexture.ReleaseTemporary(lrDepthBuffer);
            ofs = (sunShaftBlurRadius * (((it2 * 2f) + 1f) * 6f)) / 768f;
            sunShaftsMaterial.SetVector("_BlurRadius4", new Vector4(ofs, ofs, 0f, 0f));
            lrDepthBuffer = RenderTexture.GetTemporary(rtW, rtH, 0);
            Graphics.Blit(lrColorB, lrDepthBuffer, sunShaftsMaterial, 1);
            RenderTexture.ReleaseTemporary(lrColorB);
            ofs = (sunShaftBlurRadius * (((it2 * 2f) + 2f) * 6f)) / 768f;
            sunShaftsMaterial.SetVector("_BlurRadius4", new Vector4(ofs, ofs, 0f, 0f));
            it2++;
        }
        // put together:
        if (v.z >= 0f)
        {
            sunShaftsMaterial.SetVector("_SunColor", new Vector4(sunColor.r, sunColor.g, sunColor.b, sunColor.a) * sunShaftIntensity);
        }
        else
        {
            sunShaftsMaterial.SetVector("_SunColor", Vector4.zero); // no backprojection !
        }
        sunShaftsMaterial.SetTexture("_ColorBuffer", lrDepthBuffer);
        Graphics.Blit(source, destination, sunShaftsMaterial, screenBlendMode == ShaftsScreenBlendMode.Screen ? 0 : 4);
        RenderTexture.ReleaseTemporary(lrDepthBuffer);
    }

    public SunShafts()
    {
        resolution = SunShaftsResolution.Normal;
        screenBlendMode = ShaftsScreenBlendMode.Screen;
        radialBlurIterations = 2;
        sunColor = Color.white;
        sunThreshold = new Color(0.87f, 0.74f, 0.65f);
        sunShaftBlurRadius = 2.5f;
        sunShaftIntensity = 1.15f;
        maxRadius = 0.75f;
        useDepthTexture = true;
    }
}
