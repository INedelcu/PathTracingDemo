using System;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

public enum DofQualitySetting
{
    Low = 0,
    Medium = 1,
    High = 2
}

[Serializable]
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Depth of Field")]
public class DepthOfField : PostEffectsBase
{
    public DofQualitySetting quality;
    public float divider;
    public float focalZDistance;
    public float focalStartCurve;
    public float focalEndCurve;
    public float focalZStart;
    public float focalZEnd;
    private float _focalDistance01;
    private float _focalStart01;
    private float _focalEnd01;
    public float focalFalloff;
    public Transform objectFocus;
    public float focalSize;
    public bool enableBokeh;
    public float bokehThreshhold;
    public float bokehFalloff;
    public float noiseAmount;
    public int blurIterations;
    public float blurSpread;
    public int foregroundBlurIterations;
    public float foregroundBlurSpread;
    public float foregroundBlurWeight;
    public Shader weightedBlurShader;
    private Material _weightedBlurMaterial;
    public Shader preDofShader;
    private Material _preDofMaterial;
    public Shader blurShader;
    private Material _blurMaterial;

    public virtual void CreateMaterials()
    {
        _weightedBlurMaterial = CheckShaderAndCreateMaterial(weightedBlurShader, _weightedBlurMaterial);
        _blurMaterial = CheckShaderAndCreateMaterial(blurShader, _blurMaterial);
        _preDofMaterial = CheckShaderAndCreateMaterial(preDofShader, _preDofMaterial);
    }

    public override void Start()
    {
        CreateMaterials();
        CheckSupport(true);
    }

    public override void OnEnable()
    {
        GetComponent<Camera>().depthTextureMode = GetComponent<Camera>().depthTextureMode | DepthTextureMode.Depth;
    }

    public virtual void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // create materials if needed
        CreateMaterials();
        source.filterMode = FilterMode.Bilinear;
        // source.filterMode = FilterMode.Point;
        // determine area of focus
        if (objectFocus)
        {
            var vpPoint = GetComponent<Camera>().WorldToViewportPoint(objectFocus.position);
            vpPoint.z = vpPoint.z / GetComponent<Camera>().farClipPlane;
            _focalDistance01 = vpPoint.z;
        }
        else
        {
            _focalDistance01 = GetComponent<Camera>().WorldToViewportPoint((focalZDistance * GetComponent<Camera>().transform.forward) + GetComponent<Camera>().transform.position).z / GetComponent<Camera>().farClipPlane;
        }
        if (focalZEnd > GetComponent<Camera>().farClipPlane)
        {
            focalZEnd = GetComponent<Camera>().farClipPlane;
        }
        _focalStart01 = GetComponent<Camera>().WorldToViewportPoint((focalZStart * GetComponent<Camera>().transform.forward) + GetComponent<Camera>().transform.position).z / GetComponent<Camera>().farClipPlane;
        _focalEnd01 = GetComponent<Camera>().WorldToViewportPoint((focalZEnd * GetComponent<Camera>().transform.forward) + GetComponent<Camera>().transform.position).z / GetComponent<Camera>().farClipPlane;
        if (_focalDistance01 < _focalStart01)
        {
            _focalDistance01 = _focalStart01 + Mathf.Epsilon;
        }
        if (_focalEnd01 < _focalStart01)
        {
            _focalEnd01 = _focalStart01 + Mathf.Epsilon;
        }
        // NOTE:
        //  we use the alpha channel for storing the COC which also means that
        //  unfortunately, alpha based image effects such as sun shafts, bloom or glow
        //  might not work as expected if placed *after* this image effect
        _preDofMaterial.SetFloat("focalDistance01", _focalDistance01);
        _preDofMaterial.SetFloat("focalFalloff", focalFalloff);
        _preDofMaterial.SetFloat("focalStart01", _focalStart01);
        _preDofMaterial.SetFloat("focalEnd01", _focalEnd01);
        _preDofMaterial.SetFloat("focalSize", focalSize * 0.5f);
        _preDofMaterial.SetFloat("_ForegroundBlurWeight", foregroundBlurWeight);
        _preDofMaterial.SetVector("_CurveParams", new Vector4(focalStartCurve, focalEndCurve, 0f, 0f));
        var fgBokehFalloff = -bokehFalloff / (1f * foregroundBlurIterations);
        _preDofMaterial.SetVector("_BokehThreshhold", new Vector4(bokehThreshhold, (1f / (1f - bokehThreshhold)) * (1f - fgBokehFalloff), fgBokehFalloff, noiseAmount));
        _preDofMaterial.SetVector("_InvRenderTargetSize", new Vector4(1f / (1f * source.width), 1f / (1f * source.height), 0f, 0f));
        var fgSource = RenderTexture.GetTemporary(source.width, source.height, 0);
        var oneEightUnblurredBg = RenderTexture.GetTemporary((int)(source.width / divider), (int)(source.height / divider), 0);
        var oneEight = RenderTexture.GetTemporary((int)(source.width / divider), (int)(source.height / divider), 0);
        var oneEight2 = RenderTexture.GetTemporary((int)(source.width / divider), (int)(source.height / divider), 0);
        var oneEightTmp = RenderTexture.GetTemporary((int)(source.width / divider), (int)(source.height / divider), 0);
        fgSource.filterMode = FilterMode.Bilinear;
        oneEight.filterMode = FilterMode.Bilinear;
        oneEight2.filterMode = FilterMode.Bilinear;
        oneEightTmp.filterMode = FilterMode.Bilinear;
        oneEightUnblurredBg.filterMode = FilterMode.Bilinear;
        if (quality >= DofQualitySetting.High)
        {
            // COC (foreground)
            Graphics.Blit(source, fgSource, _preDofMaterial, 11);
            // better downsample (shouldn't be weighted)
            Graphics.Blit(fgSource, oneEight, _preDofMaterial, 12);
            // foreground defocus
            if (foregroundBlurIterations < 1)
            {
                foregroundBlurIterations = 1;
            }
            var fgBlurPass = enableBokeh ? 9 : 6;
            var it33 = 0;
            while (it33 < foregroundBlurIterations)
            {
                _preDofMaterial.SetVector("_Vh", new Vector4(foregroundBlurSpread, 0f, 0, 0));
                Graphics.Blit(oneEight, oneEightTmp, _preDofMaterial, fgBlurPass);
                _preDofMaterial.SetVector("_Vh", new Vector4(0f, foregroundBlurSpread, 0, 0));
                Graphics.Blit(oneEightTmp, oneEight, _preDofMaterial, fgBlurPass);
                if (enableBokeh)
                {
                    _preDofMaterial.SetVector("_Vh", new Vector4(foregroundBlurSpread, -foregroundBlurSpread, 0, 0));
                    Graphics.Blit(oneEight, oneEightTmp, _preDofMaterial, fgBlurPass);
                    _preDofMaterial.SetVector("_Vh", new Vector4(-foregroundBlurSpread, -foregroundBlurSpread, 0, 0));
                    Graphics.Blit(oneEightTmp, oneEight, _preDofMaterial, fgBlurPass);
                }
                it33++;
            }
            // COC (background), where is my MRT!?
            Graphics.Blit(source, source, _preDofMaterial, 4);
            // better downsample
            Graphics.Blit(source, oneEightUnblurredBg, _preDofMaterial, 12);
        }
        else
        {
            // medium & low quality
            // calculate COC for BG & FG at the same time
            Graphics.Blit(source, source, _preDofMaterial, 3);
            // better downsample (should actually be weighted)
            Graphics.Blit(source, oneEightUnblurredBg, _preDofMaterial, 12);
        }
        if (blurIterations < 1)
        {
            blurIterations = 1;
        }
        var bgBokehFalloff = -bokehFalloff / (1f * blurIterations);
        _weightedBlurMaterial.SetVector("_Threshhold", new Vector4(bokehThreshhold, (1f / (1f - bokehThreshhold)) * (1f - bgBokehFalloff), bgBokehFalloff, noiseAmount));
        if (quality >= DofQualitySetting.Medium)
        {
            //  blur background a little
            _weightedBlurMaterial.SetVector("offsets", new Vector4(0f, (blurSpread * 1.5f) / source.height, 0f, 0f));
            Graphics.Blit(oneEightUnblurredBg, oneEightTmp, _weightedBlurMaterial, 1);
            _weightedBlurMaterial.SetVector("offsets", new Vector4((blurSpread * 1.5f) / source.width, 0f, 0f, 0f));
            Graphics.Blit(oneEightTmp, oneEightUnblurredBg, _weightedBlurMaterial, 1);
            var bgBlurPass = enableBokeh ? 0 : 1;
            // blur and evtly bokeh'ify background
            var it = 0;
            while (it < blurIterations)
            {
                _weightedBlurMaterial.SetVector("offsets", new Vector4(0f, blurSpread / source.height, 0f, 0f));
                Graphics.Blit(it == 0 ? oneEightUnblurredBg : oneEight2, oneEightTmp, _weightedBlurMaterial, bgBlurPass);
                _weightedBlurMaterial.SetVector("offsets", new Vector4(blurSpread / source.width, 0f, 0f, 0f));
                Graphics.Blit(oneEightTmp, oneEight2, _weightedBlurMaterial, bgBlurPass);
                if (enableBokeh)
                {
                    _weightedBlurMaterial.SetVector("offsets", new Vector4(blurSpread / source.width, blurSpread / source.height, 0f, 0f));
                    Graphics.Blit(oneEight2, oneEightTmp, _weightedBlurMaterial, bgBlurPass);
                    _weightedBlurMaterial.SetVector("offsets", new Vector4(blurSpread / source.width, -blurSpread / source.height, 0f, 0f));
                    Graphics.Blit(oneEightTmp, oneEight2, _weightedBlurMaterial, bgBlurPass);
                }
                it++;
            }
        }
        else
        {
            // @TODO: do noise properly as soon as we have nice MRT support
            /*
            if(enableNoise) {
                _weightedBlurMaterial.SetVector ("offsets", Vector4 (0.0, (blurSpread*1.5)/source.height, 0.0,0.0));
                Graphics.Blit (oneEight2, oneEightTmp, _weightedBlurMaterial,1);
                _weightedBlurMaterial.SetVector ("offsets", Vector4 ((blurSpread*1.5)/source.width,  0.0,0.0,0.0));
                Graphics.Blit (oneEightTmp, oneEightUnsharp, _weightedBlurMaterial,1);
            }
            */
            //Graphics.Blit ( oneEight2, oneEightTmp, _weightedBlurMaterial,4);
            //Graphics.Blit ( oneEightTmp, oneEight2, _weightedBlurMaterial,4);
            // on low quality, we don't care about borders or bokeh's, let's just blur it and
            // hope for the best contrast =)
            var it = 0;
            while (it < blurIterations)
            {
                _blurMaterial.SetVector("offsets", new Vector4(0f, blurSpread / source.height, 0f, 0f));
                Graphics.Blit(it == 0 ? oneEightUnblurredBg : oneEight2, oneEightTmp, _blurMaterial);
                _blurMaterial.SetVector("offsets", new Vector4(blurSpread / source.width, 0f, 0f, 0f));
                Graphics.Blit(oneEightTmp, oneEight2, _blurMaterial);
                it++;
            }
        }
        // Almost done ... all we need to do now is to generate the very
        // final image based on defocused foreground and background as well
        // as the generated COC values
        var fgBlurNeeded = (_focalDistance01 > 0f) && (focalStartCurve > 0f);
        _preDofMaterial.SetTexture("_FgLowRez", oneEight);
        _preDofMaterial.SetTexture("_BgLowRez", oneEight2); // can also be fg *and* bg blur in the case of medium/low quality
        _preDofMaterial.SetTexture("_BgUnblurredTex", oneEightUnblurredBg);
        _weightedBlurMaterial.SetTexture("_TapLow", oneEight2);
        _weightedBlurMaterial.SetTexture("_TapMedium", oneEightUnblurredBg);
        // some final BG calculations can be performed in low resolution: do it now
        Graphics.Blit(oneEight2, oneEight2, _weightedBlurMaterial, 3);
        // final BG calculations
        if (quality > DofQualitySetting.Medium)
        {
            Graphics.Blit(source, fgBlurNeeded ? fgSource : destination, _preDofMaterial, 0);
        }
        else
        {
            if (quality == DofQualitySetting.Medium)
            {
                Graphics.Blit(source, destination, _preDofMaterial, 2);
            }
            else
            {
                if (quality == DofQualitySetting.Low)
                {
                    Graphics.Blit(source, destination, _preDofMaterial, 1);
                }
            }
        }
        // final FG calculations
        if ((quality > DofQualitySetting.Medium) && fgBlurNeeded)
        {
            Graphics.Blit(fgSource, oneEightUnblurredBg, _preDofMaterial, 12);
            Graphics.Blit(fgSource, destination, _preDofMaterial, 10); // FG BLUR
        }
        RenderTexture.ReleaseTemporary(fgSource);
        RenderTexture.ReleaseTemporary(oneEight);
        RenderTexture.ReleaseTemporary(oneEight2);
        RenderTexture.ReleaseTemporary(oneEightTmp);
        RenderTexture.ReleaseTemporary(oneEightUnblurredBg);
    }

    public DepthOfField()
    {
        quality = DofQualitySetting.High;
        divider = 2f;
        focalStartCurve = 1.175f;
        focalEndCurve = 1.1f;
        focalZEnd = 10000f;
        _focalDistance01 = 0.1f;
        _focalEnd01 = 1f;
        focalFalloff = 1f;
        focalSize = 0.075f;
        enableBokeh = true;
        bokehThreshhold = 0.2f;
        bokehFalloff = 0.2f;
        noiseAmount = 1.5f;
        blurIterations = 1;
        blurSpread = 1.35f;
        foregroundBlurIterations = 1;
        foregroundBlurSpread = 1f;
        foregroundBlurWeight = 1f;
    }
}
