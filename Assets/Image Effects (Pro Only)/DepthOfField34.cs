using System;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

public enum Dof34QualitySetting
{
    OnlyBackground = 1,
    BackgroundAndForeground = 2
}

public enum DofResolution
{
    High = 2,
    Medium = 3,
    Low = 4
}

[Serializable]
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Depth of Field (3.4)")]
public class DepthOfField34 : PostEffectsBase
{
    public Dof34QualitySetting quality;
    public DofResolution resolution;
    public bool simpleTweakMode;

    // simple tweak mode
    public float focalPoint;
    public float smoothness;

    // complex tweak mode
    public float focalZDistance;
    public float focalZStartCurve;
    public float focalZEndCurve;
    private float focalStartCurve;
    private float focalEndCurve;
    private float focalDistance01;
    public Transform objectFocus;
    public float focalSize;
    public int blurIterations;
    public float maxBlurSpread;
    public int foregroundBlurIterations;
    public float foregroundMaxBlurSpread;
    public float foregroundBlurExtrude;
    public Shader dofBlurShader;
    private Material dofBlurMaterial;
    public Shader dofShader;
    private Material dofMaterial;
    public bool visualize;
    private float widthOverHeight;
    private float oneOverBaseSize;
    public bool bokeh;
    public bool bokehSupport;
    public Shader bokehShader;
    public Texture2D bokehTexture;
    private Material bokehMaterial;
    public float bokehScale;
    public float bokehIntensity;
    public float bokehThreshhold;
    public float bokehBlendStrength;
    public int bokehDownsample;

    public virtual void CreateMaterials()
    {
        dofBlurMaterial = CheckShaderAndCreateMaterial(dofBlurShader, dofBlurMaterial);
        dofMaterial = CheckShaderAndCreateMaterial(dofShader, dofMaterial);
        bokehSupport = bokehShader.isSupported;
        if ((bokeh && bokehSupport) && bokehShader)
        {
            bokehMaterial = CheckShaderAndCreateMaterial(bokehShader, bokehMaterial);
        }
    }

    public override void Start()
    {
        CreateMaterials();
        CheckSupport(true);
    }

    public virtual void OnDisable()
    {
        Triangles.Cleanup();
    }

    public override void OnEnable()
    {
        GetComponent<Camera>().depthTextureMode = GetComponent<Camera>().depthTextureMode | DepthTextureMode.Depth;
    }

    public virtual float FocalDistance01(float worldDist)
    {
        return GetComponent<Camera>().WorldToViewportPoint(((worldDist - GetComponent<Camera>().nearClipPlane) * GetComponent<Camera>().transform.forward) + GetComponent<Camera>().transform.position).z / (GetComponent<Camera>().farClipPlane - GetComponent<Camera>().nearClipPlane);
    }

    public virtual int GetDividerBasedOnQuality()
    {
        var divider = 1;
        if (resolution == DofResolution.Medium)
        {
            divider = 2;
        }
        else
        {
            if (resolution == DofResolution.Low)
            {
                divider = 2;
            }
        }
        return divider;
    }

    public virtual int GetLowResolutionDividerBasedOnQuality(int baseDivider)
    {
        var lowTexDivider = baseDivider;
        if (resolution == DofResolution.High)
        {
            lowTexDivider = lowTexDivider * 2;
        }
        if (resolution == DofResolution.Low)
        {
            lowTexDivider = lowTexDivider * 2;
        }
        return lowTexDivider;
    }

    public virtual void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        CreateMaterials();
        // update needed focal & rt size parameter
        bokeh = bokeh && bokehSupport;
        var blurForeground = quality > Dof34QualitySetting.OnlyBackground;
        var focal01Size = focalSize;
        if (simpleTweakMode)
        {
            if (objectFocus)
            {
                focalDistance01 = GetComponent<Camera>().WorldToViewportPoint(objectFocus.position).z / GetComponent<Camera>().farClipPlane;
            }
            else
            {
                focalDistance01 = FocalDistance01(focalPoint);
            }
            var curve = focalDistance01 * smoothness;
            focalStartCurve = curve;
            focalEndCurve = curve;
            focal01Size = 0.005f;
            blurForeground = blurForeground && (focalPoint > (GetComponent<Camera>().nearClipPlane + Mathf.Epsilon));
        }
        else
        {
            if (objectFocus)
            {
                var vpPoint = GetComponent<Camera>().WorldToViewportPoint(objectFocus.position);
                vpPoint.z = vpPoint.z / GetComponent<Camera>().farClipPlane;
                focalDistance01 = vpPoint.z;
            }
            else
            {
                focalDistance01 = FocalDistance01(focalZDistance);
            }
            focalStartCurve = focalZStartCurve;
            focalEndCurve = focalZEndCurve;
            blurForeground = blurForeground && (focalPoint > (GetComponent<Camera>().nearClipPlane + Mathf.Epsilon));
        }
        widthOverHeight = (1f * source.width) / (1f * source.height);
        oneOverBaseSize = 1f / 512f;
        //  we use the alpha channel for storing the COC which also means that
        //  unfortunately, alpha based image effects such as sun shafts, bloom or glow
        //  might not work as expected if stacked after this image effect
        dofMaterial.SetFloat("_ForegroundBlurExtrude", foregroundBlurExtrude);
        dofMaterial.SetVector("_CurveParams", new Vector4(simpleTweakMode ? 1f / focalStartCurve : focalStartCurve, simpleTweakMode ? 1f / focalEndCurve : focalEndCurve, focal01Size * 0.5f, focalDistance01));
        dofMaterial.SetVector("_InvRenderTargetSize", new Vector4(1f / (1f * source.width), 1f / (1f * source.height), 0f, 0f));
        // needed render textures
        var divider = GetDividerBasedOnQuality();
        var lowTexDivider = GetLowResolutionDividerBasedOnQuality(divider);
        RenderTexture foregroundTexture = null;
        RenderTexture foregroundDefocus = null;
        if (blurForeground)
        {
            foregroundTexture = RenderTexture.GetTemporary(source.width, source.height, 0);
            foregroundDefocus = RenderTexture.GetTemporary(source.width / divider, source.height / divider, 0);
        }
        var mediumTexture = RenderTexture.GetTemporary(source.width / divider, source.height / divider, 0);
        var backgroundDefocus = RenderTexture.GetTemporary(source.width / divider, source.height / divider, 0);
        var lowTexture = RenderTexture.GetTemporary(source.width / lowTexDivider, source.height / lowTexDivider, 0);
        RenderTexture bokehSource = null;
        RenderTexture bokehSource2 = null;
        if (bokeh)
        {
            bokehSource = RenderTexture.GetTemporary(source.width / (lowTexDivider * bokehDownsample), source.height / (lowTexDivider * bokehDownsample), 0);
            bokehSource2 = RenderTexture.GetTemporary(source.width / (lowTexDivider * bokehDownsample), source.height / (lowTexDivider * bokehDownsample), 0);
        }
        // just to make sure:
        source.filterMode = FilterMode.Bilinear;
        if (foregroundTexture)
        {
            foregroundTexture.filterMode = FilterMode.Bilinear;
            foregroundDefocus.filterMode = FilterMode.Bilinear;
        }
        backgroundDefocus.filterMode = FilterMode.Bilinear;
        mediumTexture.filterMode = FilterMode.Bilinear;
        lowTexture.filterMode = FilterMode.Bilinear;
        if (bokeh)
        {
            bokehSource.filterMode = FilterMode.Bilinear;
            bokehSource2.filterMode = FilterMode.Bilinear;
        }
        // blur foreground if needed
        if (blurForeground)
        {
            // foreground handling comes first (coc -> alpha channel)
            Graphics.Blit(source, foregroundTexture, dofMaterial, 5);
            // better downsample and blur (shouldn't be weighted)
            Graphics.Blit(foregroundTexture, mediumTexture, dofMaterial, 6);
            Blur(mediumTexture, mediumTexture, 1, 1, foregroundMaxBlurSpread);
            if (bokehSource)
            {
                dofMaterial.SetVector("_InvRenderTargetSize", new Vector4(1f / (1f * bokehSource.width), 1f / (1f * bokehSource.height), 0f, 0f));
                Graphics.Blit(mediumTexture, bokehSource, dofMaterial, 6);
            }
            Blur(mediumTexture, lowTexture, foregroundBlurIterations, 1, foregroundMaxBlurSpread);
            // some final FG calculations can be performed in low resolution:
            dofBlurMaterial.SetTexture("_TapLow", lowTexture);
            dofBlurMaterial.SetTexture("_TapMedium", mediumTexture);
            Graphics.Blit(null, foregroundDefocus, dofBlurMaterial, 3);
            // background (coc -> alpha channel)
            // @NOTE: this is safe, we are not sampling from "source"
            Graphics.Blit(source, source, dofMaterial, 3);
            // better downsample (should actually be weighted for higher quality)
            Graphics.Blit(source, mediumTexture, dofMaterial, 6);
        }
        else
        {
            // @NOTE: this is safe, we are not sampling from "source"
            Graphics.Blit(source, source, dofMaterial, 3);
            // better downsample (could actually be weighted for higher quality)
            Graphics.Blit(source, mediumTexture, dofMaterial, 6);
        }
        // blur background
        Blur(mediumTexture, mediumTexture, 1, 0, maxBlurSpread);
        if (bokehSource)
        {
            if (!blurForeground)
            {
                dofMaterial.SetVector("_InvRenderTargetSize", new Vector4(1f / (1f * bokehSource.width), 1f / (1f * bokehSource.height), 0f, 0f));
                Graphics.Blit(mediumTexture, bokehSource2, dofMaterial, 6);
            }
            else
            {
                dofMaterial.SetTexture("_TapMedium", mediumTexture);
                Graphics.Blit(bokehSource, bokehSource2, dofMaterial, 8);
            }
        }
        Blur(mediumTexture, lowTexture, blurIterations, 0, maxBlurSpread);
        dofBlurMaterial.SetTexture("_TapLow", lowTexture);
        dofBlurMaterial.SetTexture("_TapMedium", mediumTexture);
        Graphics.Blit(null, backgroundDefocus, dofBlurMaterial, 3);
        if (bokeh && bokehMaterial)
        {
            Mesh[] meshes = Triangles.GetMeshes(bokehSource.width, bokehSource.height);
            GL.PushMatrix();
            GL.LoadIdentity();
            RenderTexture.active = bokehSource;
            GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));
            bokehMaterial.SetTexture("_Source", bokehSource2);//blurForeground ? mediumTexture : backgroundDefocus);
            bokehMaterial.SetVector("_ArScale", new Vector4(1f, (mediumTexture.width * 1f) / (mediumTexture.height * 1f), 1f, 1f));
            bokehMaterial.SetFloat("_Scale", bokehScale * 0.01f);
            bokehMaterial.SetFloat("_Intensity", bokehIntensity * 0.01f);
            bokehMaterial.SetFloat("_Threshhold", bokehThreshhold);
            bokehMaterial.SetTexture("_MainTex", bokehTexture);
            bokehMaterial.SetPass(0);
            foreach (var m in meshes)
            {
                if (m)
                {
                    Graphics.DrawMeshNow(m, Matrix4x4.identity);
                }
            }
            GL.PopMatrix();
            // blend bokeh result into low resolution texture(s)
            dofMaterial.SetFloat("_BlendStrength", bokehBlendStrength);
            Graphics.Blit(bokehSource, backgroundDefocus, dofMaterial, 9);
            if (blurForeground)
            {
                Graphics.Blit(bokehSource, foregroundDefocus, dofMaterial, 9);
            }
        }
        dofMaterial.SetTexture("_TapLowForeground", blurForeground ? foregroundDefocus : null);
        dofMaterial.SetTexture("_TapLowBackground", backgroundDefocus);
        dofMaterial.SetTexture("_TapMedium", mediumTexture); // needed for debugging/visualization
        // defocus for background
        Graphics.Blit(source, blurForeground ? foregroundTexture : destination, dofMaterial, visualize ? 2 : 0);
        // defocus for foreground
        if (blurForeground)
        {
            Graphics.Blit(foregroundTexture, destination, dofMaterial, visualize ? 1 : 4);
        }
        if (foregroundTexture)
        {
            RenderTexture.ReleaseTemporary(foregroundTexture);
        }
        if (foregroundDefocus)
        {
            RenderTexture.ReleaseTemporary(foregroundDefocus);
        }
        RenderTexture.ReleaseTemporary(backgroundDefocus);
        RenderTexture.ReleaseTemporary(mediumTexture);
        RenderTexture.ReleaseTemporary(lowTexture);
        if (bokehSource)
        {
            RenderTexture.ReleaseTemporary(bokehSource);
        }
        if (bokehSource2)
        {
            RenderTexture.ReleaseTemporary(bokehSource2);
        }
    }

    public virtual void Blur(RenderTexture from, RenderTexture to, int iterations, int blurPass, float spread)
    {
        var tmp = RenderTexture.GetTemporary(to.width, to.height);
        if (iterations > 1)
        {
            BlurHex(from, to, iterations / 2, blurPass, spread, tmp);
        }
        var it = 0;
        while (it < (iterations % 2))
        {
            dofBlurMaterial.SetVector("offsets", new Vector4(0f, spread * oneOverBaseSize, 0f, 0f));
            Graphics.Blit((it == 0) && (iterations <= 1) ? from : to, tmp, dofBlurMaterial, blurPass);
            dofBlurMaterial.SetVector("offsets", new Vector4((spread / widthOverHeight) * oneOverBaseSize, 0f, 0f, 0f));
            Graphics.Blit(tmp, to, dofBlurMaterial, blurPass);
            it++;
        }
        RenderTexture.ReleaseTemporary(tmp);
    }

    public virtual void BlurHex(RenderTexture from, RenderTexture to, int iterations, int blurPass, float spread, RenderTexture tmp)
    {
        var it = 0;
        while (it < iterations)
        {
            dofBlurMaterial.SetVector("offsets", new Vector4(0f, spread * oneOverBaseSize, 0f, 0f));
            Graphics.Blit(it == 0 ? from : to, tmp, dofBlurMaterial, blurPass);
            dofBlurMaterial.SetVector("offsets", new Vector4((spread / widthOverHeight) * oneOverBaseSize, 0f, 0f, 0f));
            Graphics.Blit(tmp, to, dofBlurMaterial, blurPass);
            dofBlurMaterial.SetVector("offsets", new Vector4((spread / widthOverHeight) * oneOverBaseSize, spread * oneOverBaseSize, 0f, 0f));
            Graphics.Blit(to, tmp, dofBlurMaterial, blurPass);
            dofBlurMaterial.SetVector("offsets", new Vector4((spread / widthOverHeight) * oneOverBaseSize, -spread * oneOverBaseSize, 0f, 0f));
            Graphics.Blit(tmp, to, dofBlurMaterial, blurPass);
            it++;
        }
    }

    public DepthOfField34()
    {
        quality = Dof34QualitySetting.OnlyBackground;
        resolution = DofResolution.Medium;
        simpleTweakMode = true;
        focalPoint = 0.2f;
        smoothness = 2f;
        focalZStartCurve = 1f;
        focalZEndCurve = 1f;
        focalStartCurve = 1.2f;
        focalEndCurve = 1.2f;
        focalDistance01 = 0.1f;
        focalSize = 0.0025f;
        blurIterations = 1;
        maxBlurSpread = 1.375f;
        foregroundBlurIterations = 1;
        foregroundMaxBlurSpread = 1.375f;
        foregroundBlurExtrude = 1.65f;
        widthOverHeight = 1.25f;
        oneOverBaseSize = 1f / 512f;
        bokehSupport = true;
        bokehScale = 6f;
        bokehIntensity = 5.1251265f;
        bokehThreshhold = 0.65f;
        bokehBlendStrength = 0.875f;
        bokehDownsample = 1;
    }
}
