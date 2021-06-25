using System;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

public enum LensflareStyle
{
    Ghosting = 0,
    Hollywood = 1,
    Combined = 2
}

public enum TweakMode
{
    Simple = 0,
    Advanced = 1
}

[Serializable]
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Bloom and Flares")]
public class BloomAndFlares : PostEffectsBase
{
    public TweakMode tweakMode;
    public string bloomThisTag;
    public float sepBlurSpread;
    public float useSrcAlphaAsMask;
    public float bloomIntensity;
    public float bloomThreshhold;
    public int bloomBlurIterations;
    public bool lensflares;
    public int hollywoodFlareBlurIterations;
    public LensflareStyle lensflareMode;
    public float hollyStretchWidth;
    public float lensflareIntensity;
    public float lensflareThreshhold;
    public Color flareColorA;
    public Color flareColorB;
    public Color flareColorC;
    public Color flareColorD;
    public float blurWidth;

    // needed shaders & materials ...
    public Shader addAlphaHackShader;
    private Material _alphaAddMaterial;
    public Shader lensFlareShader;
    private Material _lensFlareMaterial;
    public Shader vignetteShader;
    private Material _vignetteMaterial;
    public Shader separableBlurShader;
    private Material _separableBlurMaterial;
    public Shader addBrightStuffOneOneShader;
    private Material _addBrightStuffBlendOneOneMaterial;
    public Shader hollywoodFlareBlurShader;
    private Material _hollywoodFlareBlurMaterial;
    public Shader hollywoodFlareStretchShader;
    private Material _hollywoodFlareStretchMaterial;
    public Shader brightPassFilterShader;
    private Material _brightPassFilterMaterial;

    public override void Start()
    {
        CreateMaterials();
        CheckSupport(false);
    }

    // @TODO group shaders into material passes
    public virtual void CreateMaterials()
    {
        _lensFlareMaterial = CheckShaderAndCreateMaterial(lensFlareShader, _lensFlareMaterial);
        _vignetteMaterial = CheckShaderAndCreateMaterial(vignetteShader, _vignetteMaterial);
        _separableBlurMaterial = CheckShaderAndCreateMaterial(separableBlurShader, _separableBlurMaterial);
        _addBrightStuffBlendOneOneMaterial = CheckShaderAndCreateMaterial(addBrightStuffOneOneShader, _addBrightStuffBlendOneOneMaterial);
        _hollywoodFlareBlurMaterial = CheckShaderAndCreateMaterial(hollywoodFlareBlurShader, _hollywoodFlareBlurMaterial);
        _hollywoodFlareStretchMaterial = CheckShaderAndCreateMaterial(hollywoodFlareStretchShader, _hollywoodFlareStretchMaterial);
        _brightPassFilterMaterial = CheckShaderAndCreateMaterial(brightPassFilterShader, _brightPassFilterMaterial);
        _alphaAddMaterial = CheckShaderAndCreateMaterial(addAlphaHackShader, _alphaAddMaterial);
    }

    public virtual void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        CreateMaterials();
        // some objects should ignore the alpha threshhold limit,
        // so draw .a = 1 into the color buffer for those ...
        //
        // the drawing is scheduled here
        if (!string.IsNullOrEmpty(bloomThisTag) && (bloomThisTag != "Untagged"))
        {
            var gos = GameObject.FindGameObjectsWithTag(bloomThisTag);
            foreach (var go in gos)
            {
                if ((MeshFilter)go.GetComponent(typeof(MeshFilter)))
                {
                    var mesh = ((MeshFilter)go.GetComponent(typeof(MeshFilter))).sharedMesh;
                    _alphaAddMaterial.SetPass(0);
                    Graphics.DrawMeshNow(mesh, go.transform.localToWorldMatrix);
                }
            }
        }
        var halfRezColor = RenderTexture.GetTemporary((int)(source.width / 2f), (int)(source.height / 2f), 0);
        var quarterRezColor = RenderTexture.GetTemporary((int)(source.width / 4f), (int)(source.height / 4f), 0);
        var secondQuarterRezColor = RenderTexture.GetTemporary((int)(source.width / 4f), (int)(source.height / 4f), 0);
        var thirdQuarterRezColor = RenderTexture.GetTemporary((int)(source.width / 4f), (int)(source.height / 4f), 0);
        // at this point, we have massaged the alpha channel enough to start downsampling process for bloom
        Graphics.Blit(source, halfRezColor);
        Graphics.Blit(halfRezColor, quarterRezColor);
        RenderTexture.ReleaseTemporary(halfRezColor);
        // cut colors (threshholding)
        _brightPassFilterMaterial.SetVector("threshhold", new Vector4(bloomThreshhold, 1f / (1f - bloomThreshhold), 0f, 0f));
        _brightPassFilterMaterial.SetFloat("useSrcAlphaAsMask", useSrcAlphaAsMask);
        Graphics.Blit(quarterRezColor, secondQuarterRezColor, _brightPassFilterMaterial);
        // blurring
        if (bloomBlurIterations < 1)
        {
            bloomBlurIterations = 1;
        }
        Graphics.Blit(secondQuarterRezColor, quarterRezColor);
        var iter = 0;
        while (iter < bloomBlurIterations)
        {
            _separableBlurMaterial.SetVector("offsets", new Vector4(0f, (sepBlurSpread * 1f) / quarterRezColor.height, 0f, 0f));
            thirdQuarterRezColor.DiscardContents();
            Graphics.Blit(quarterRezColor, thirdQuarterRezColor, _separableBlurMaterial);
            _separableBlurMaterial.SetVector("offsets", new Vector4((sepBlurSpread * 1f) / quarterRezColor.width, 0f, 0f, 0f));
            quarterRezColor.DiscardContents();
            Graphics.Blit(thirdQuarterRezColor, quarterRezColor, _separableBlurMaterial);
            iter++;
        }
        Graphics.Blit(source, destination);
        if (lensflares)
        {
            // lens flare fun: cut some additional values
            // (yes, they will be cut on top of the already cut bloom values,
            //  so just optimize away if not really needed)
            _brightPassFilterMaterial.SetVector("threshhold", new Vector4(lensflareThreshhold, 1f / (1f - lensflareThreshhold), 0f, 0f));
            _brightPassFilterMaterial.SetFloat("useSrcAlphaAsMask", 0f);
            Graphics.Blit(secondQuarterRezColor, thirdQuarterRezColor, _brightPassFilterMaterial);
            if (lensflareMode == 0) // ghosting
            {
                // smooth out a little
                _separableBlurMaterial.SetVector("offsets", new Vector4(0f, (sepBlurSpread * 1f) / quarterRezColor.height, 0f, 0f));
                secondQuarterRezColor.DiscardContents();
                Graphics.Blit(thirdQuarterRezColor, secondQuarterRezColor, _separableBlurMaterial);
                _separableBlurMaterial.SetVector("offsets", new Vector4((sepBlurSpread * 1f) / quarterRezColor.width, 0f, 0f, 0f));
                thirdQuarterRezColor.DiscardContents();
                Graphics.Blit(secondQuarterRezColor, thirdQuarterRezColor, _separableBlurMaterial);
                // vignette for lens flares so that we don't notice any hard edges
                _vignetteMaterial.SetFloat("vignetteIntensity", 0.975f);
                Graphics.Blit(thirdQuarterRezColor, secondQuarterRezColor, _vignetteMaterial);
                // generating flares (_lensFlareMaterial has One One Blend)
                _lensFlareMaterial.SetVector("colorA", new Vector4(flareColorA.r, flareColorA.g, flareColorA.b, flareColorA.a) * lensflareIntensity);
                _lensFlareMaterial.SetVector("colorB", new Vector4(flareColorB.r, flareColorB.g, flareColorB.b, flareColorB.a) * lensflareIntensity);
                _lensFlareMaterial.SetVector("colorC", new Vector4(flareColorC.r, flareColorC.g, flareColorC.b, flareColorC.a) * lensflareIntensity);
                _lensFlareMaterial.SetVector("colorD", new Vector4(flareColorD.r, flareColorD.g, flareColorD.b, flareColorD.a) * lensflareIntensity);
                Graphics.Blit(secondQuarterRezColor, quarterRezColor, _lensFlareMaterial);
            }
            else
            {
                _hollywoodFlareBlurMaterial.SetVector("offsets", new Vector4(0f, (sepBlurSpread * 1f) / quarterRezColor.height, 0f, 0f));
                _hollywoodFlareBlurMaterial.SetTexture("_NonBlurredTex", quarterRezColor);
                _hollywoodFlareBlurMaterial.SetVector("tintColor", (new Vector4(flareColorA.r, flareColorA.g, flareColorA.b, flareColorA.a) * flareColorA.a) * lensflareIntensity);
                Graphics.Blit(thirdQuarterRezColor, secondQuarterRezColor, _hollywoodFlareBlurMaterial);
                _hollywoodFlareStretchMaterial.SetVector("offsets", new Vector4((sepBlurSpread * 1f) / quarterRezColor.width, 0f, 0f, 0f));
                _hollywoodFlareStretchMaterial.SetFloat("stretchWidth", hollyStretchWidth);
                Graphics.Blit(secondQuarterRezColor, thirdQuarterRezColor, _hollywoodFlareStretchMaterial);
                if (lensflareMode == (LensflareStyle)1) // hollywood flares
                {
                    var itera = 0;
                    while (itera < hollywoodFlareBlurIterations)
                    {
                        _separableBlurMaterial.SetVector("offsets", new Vector4((sepBlurSpread * 1f) / quarterRezColor.width, 0f, 0f, 0f));
                        secondQuarterRezColor.DiscardContents();
                        Graphics.Blit(thirdQuarterRezColor, secondQuarterRezColor, _separableBlurMaterial);
                        _separableBlurMaterial.SetVector("offsets", new Vector4((sepBlurSpread * 1f) / quarterRezColor.width, 0f, 0f, 0f));
                        thirdQuarterRezColor.DiscardContents();
                        Graphics.Blit(secondQuarterRezColor, thirdQuarterRezColor, _separableBlurMaterial);
                        itera++;
                    }
                    _addBrightStuffBlendOneOneMaterial.SetFloat("intensity", 1f);
                    Graphics.Blit(thirdQuarterRezColor, quarterRezColor, _addBrightStuffBlendOneOneMaterial);
                }
                else
                {
                    // 'both' (@NOTE: is weird, maybe just remove)
                    var ix = 0;
                    while (ix < hollywoodFlareBlurIterations)
                    {
                        _separableBlurMaterial.SetVector("offsets", new Vector4((sepBlurSpread * 1f) / quarterRezColor.width, 0f, 0f, 0f));
                        secondQuarterRezColor.DiscardContents();
                        Graphics.Blit(thirdQuarterRezColor, secondQuarterRezColor, _separableBlurMaterial);
                        _separableBlurMaterial.SetVector("offsets", new Vector4((sepBlurSpread * 1f) / quarterRezColor.width, 0f, 0f, 0f));
                        thirdQuarterRezColor.DiscardContents();
                        Graphics.Blit(secondQuarterRezColor, thirdQuarterRezColor, _separableBlurMaterial);
                        ix++;
                    }
                    // vignette for lens flares
                    _vignetteMaterial.SetFloat("vignetteIntensity", 1f);
                    Graphics.Blit(thirdQuarterRezColor, secondQuarterRezColor, _vignetteMaterial);
                    // creating the flares
                    // _lensFlareMaterial has One One Blend
                    _lensFlareMaterial.SetVector("colorA", (new Vector4(flareColorA.r, flareColorA.g, flareColorA.b, flareColorA.a) * flareColorA.a) * lensflareIntensity);
                    _lensFlareMaterial.SetVector("colorB", (new Vector4(flareColorB.r, flareColorB.g, flareColorB.b, flareColorB.a) * flareColorB.a) * lensflareIntensity);
                    _lensFlareMaterial.SetVector("colorC", (new Vector4(flareColorC.r, flareColorC.g, flareColorC.b, flareColorC.a) * flareColorC.a) * lensflareIntensity);
                    _lensFlareMaterial.SetVector("colorD", (new Vector4(flareColorD.r, flareColorD.g, flareColorD.b, flareColorD.a) * flareColorD.a) * lensflareIntensity);
                    Graphics.Blit(secondQuarterRezColor, thirdQuarterRezColor, _lensFlareMaterial);
                    _addBrightStuffBlendOneOneMaterial.SetFloat("intensity", 1f);
                    Graphics.Blit(thirdQuarterRezColor, quarterRezColor, _addBrightStuffBlendOneOneMaterial);
                }
            }
        }
        _addBrightStuffBlendOneOneMaterial.SetFloat("intensity", bloomIntensity);
        Graphics.Blit(quarterRezColor, destination, _addBrightStuffBlendOneOneMaterial);
        RenderTexture.ReleaseTemporary(quarterRezColor);
        RenderTexture.ReleaseTemporary(secondQuarterRezColor);
        RenderTexture.ReleaseTemporary(thirdQuarterRezColor);
    }

    public BloomAndFlares()
    {
        tweakMode = (TweakMode)1;
        sepBlurSpread = 1.5f;
        useSrcAlphaAsMask = 0.5f;
        bloomIntensity = 1f;
        bloomThreshhold = 0.4f;
        bloomBlurIterations = 3;
        lensflares = true;
        hollywoodFlareBlurIterations = 4;
        hollyStretchWidth = 2.5f;
        lensflareIntensity = 0.75f;
        lensflareThreshhold = 0.5f;
        flareColorA = new Color(0.4f, 0.4f, 0.8f, 0.75f);
        flareColorB = new Color(0.4f, 0.8f, 0.8f, 0.75f);
        flareColorC = new Color(0.8f, 0.4f, 0.8f, 0.75f);
        flareColorD = new Color(0.8f, 0.4f, 0f, 0.75f);
        blurWidth = 1f;
    }
}
