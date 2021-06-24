#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

public class PathTracingSimpleShaderGUI : ShaderGUI
{
    private static class Styles
    {
        public static GUIContent albedoText = EditorGUIUtility.TrTextContent("Albedo", "Albedo (RGB)");
        public static GUIContent normalText = EditorGUIUtility.TrTextContent("Normal", "Normal (RGB)");
        public static GUIContent emissionText = EditorGUIUtility.TrTextContent("Color", "Emission (RGB)");
    }

    MaterialEditor m_MaterialEditor;

    MaterialProperty albedoTex = null;
    MaterialProperty albedoColor = null;
    MaterialProperty normalTex = null;
    MaterialProperty metalicValue = null;
    MaterialProperty emissionState = null;
    MaterialProperty emissionTex = null;
    MaterialProperty emissionColor = null;
    MaterialProperty specularColor = null;
    MaterialProperty smoothnessValue = null;
    MaterialProperty iorValue = null;
    MaterialProperty cutoutState = null;
    MaterialProperty cutoffValue = null;


    bool firstTimeApply = true;

    public void FindProperties(MaterialProperty[] props)
    {
        albedoTex = FindProperty("_MainTex", props);
        albedoColor = FindProperty("_Color", props);

        normalTex = FindProperty("_NormalMap", props);

        metalicValue = FindProperty("_Metallic", props);

        emissionState = FindProperty("_Emission", props);
        emissionTex = FindProperty("_EmissionTex", props);
        emissionColor = FindProperty("_EmissionColor", props);

        specularColor = FindProperty("_SpecularColor", props);

        smoothnessValue = FindProperty("_Smoothness", props);

        iorValue = FindProperty("_IOR", props);

        cutoutState = FindProperty("_Cutout", props);
        cutoffValue = FindProperty("_Cutoff", props);

    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        FindProperties(properties);

        m_MaterialEditor = materialEditor;

        Material material = materialEditor.target as Material;

        if (firstTimeApply)
        {
            MaterialChanged(material);
            firstTimeApply = false;
        }

        ShaderPropertiesGUI(material);
    }

    static void SetKeyword(Material m, string keyword, bool state)
    {
        if (state)
            m.EnableKeyword(keyword);
        else
            m.DisableKeyword(keyword);
    }

    void SetMaterialKeywords(Material m)
    {
        SetKeyword(m, "_EMISSION", (emissionState.floatValue != 0.0f));
        SetKeyword(m, "GEOM_TYPE_LEAF", (cutoutState.floatValue != 0.0f));
    }

    void MaterialChanged(Material material)
    {   
        SetMaterialKeywords(material);
    }

    public void ShaderPropertiesGUI(Material material)
    {
        EditorGUIUtility.labelWidth = 0f;

        bool showEmissionSettings = false;
        bool isCutout = false;

        EditorGUI.BeginChangeCheck();
        {
            m_MaterialEditor.TexturePropertySingleLine(Styles.albedoText, albedoTex, albedoColor);           

            EditorGUI.indentLevel = 1;
            m_MaterialEditor.TextureScaleOffsetProperty(albedoTex);        
            EditorGUI.indentLevel = 0;

            m_MaterialEditor.TexturePropertySingleLine(Styles.normalText, normalTex);

            EditorGUI.indentLevel = 1;
            m_MaterialEditor.TextureScaleOffsetProperty(normalTex);
            EditorGUI.indentLevel = 0;

            m_MaterialEditor.ColorProperty(specularColor, "Specular Color");
            m_MaterialEditor.RangeProperty(metalicValue, "Metallic");
            m_MaterialEditor.RangeProperty(smoothnessValue, "Smoothness");
            m_MaterialEditor.RangeProperty(iorValue, "Index Of Refraction");

            showEmissionSettings = (emissionState.floatValue != 0.0f);
            
            EditorGUI.showMixedValue = emissionState.hasMixedValue;

            showEmissionSettings = EditorGUILayout.Toggle("Emission", showEmissionSettings);

            EditorGUI.showMixedValue = false;

            if (showEmissionSettings)
            {
                m_MaterialEditor.TexturePropertyWithHDRColor(Styles.emissionText, emissionTex, emissionColor, false);

                EditorGUI.indentLevel = 1;

                m_MaterialEditor.TextureScaleOffsetProperty(emissionTex);

                EditorGUI.indentLevel = 0;

                bool hadEmissionTexture = emissionTex.textureValue != null;

                float brightness = emissionColor.colorValue.maxColorComponent;
                if (emissionTex.textureValue != null && !hadEmissionTexture && brightness <= 0f)
                    emissionColor.colorValue = Color.white;
            }

            isCutout = (cutoutState.floatValue != 0.0f);

            EditorGUI.showMixedValue = cutoutState.hasMixedValue;

            isCutout = EditorGUILayout.Toggle("IsCutout", isCutout);

            if (isCutout)
            {
                EditorGUI.indentLevel = 1;
                m_MaterialEditor.RangeProperty(cutoffValue, "Alpha Cutoff");
                EditorGUI.indentLevel = 0;
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            emissionState.floatValue = showEmissionSettings ? 1.0f : 0.0f;
            cutoutState.floatValue = isCutout ? 1.0f : 0.0f;

            MaterialChanged(material);
        }
    }
}

#endif