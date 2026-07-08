#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
public class PathTracingSimpleGlassShaderGUI : ShaderGUI
{
    MaterialEditor m_MaterialEditor;

    MaterialProperty iorValue = null;
    MaterialProperty roughnessValue = null;
    MaterialProperty colorValue = null;
    MaterialProperty extinctionValue = null;
    MaterialProperty flatShadingState = null;
    MaterialProperty normalMapTex = null;
    MaterialProperty normalMapScale = null;

    bool firstTimeApply = true;

    public void FindProperties(MaterialProperty[] props)
    {
        iorValue = FindProperty("_IOR", props);
        roughnessValue = FindProperty("_Roughness", props);
        colorValue = FindProperty("_Color", props);
        extinctionValue = FindProperty("_ExtinctionCoefficient", props);
        flatShadingState = FindProperty("_FlatShading", props);
        normalMapTex = FindProperty("_NormalMapTex", props);
        normalMapScale = FindProperty("_NormalMapScale", props);
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
        SetKeyword(m, "FLAT_SHADING_ON", (flatShadingState.floatValue != 0.0f));
        SetKeyword(m, "DOUBLE_SIDED_ON", true);
        // Keyword follows the assigned map, so there is no enable toggle.
        SetKeyword(m, "NORMAL_MAP_ON", m.GetTexture("_NormalMapTex") != null);
    }

    void MaterialChanged(Material material)
    {
        SetMaterialKeywords(material);
    }

    public void ShaderPropertiesGUI(Material material)
    {
        EditorGUIUtility.labelWidth = 0f;

        bool flatShading = false;

        EditorGUI.BeginChangeCheck();
        {
            flatShading = (flatShadingState.floatValue != 0.0f);

            m_MaterialEditor.RangeProperty(iorValue, "Index Of Refraction");

            m_MaterialEditor.ColorProperty(colorValue, "Color");

            m_MaterialEditor.RangeProperty(extinctionValue, "Extinction Coefficient");

            m_MaterialEditor.RangeProperty(roughnessValue, "Roughness");

            m_MaterialEditor.TexturePropertySingleLine(EditorGUIUtility.TrTextContent("Normal Map", "Tangent space normal map"), normalMapTex, normalMapTex.textureValue != null ? normalMapScale : null);

            if (normalMapTex.textureValue != null)
            {
                EditorGUI.indentLevel = 1;
                m_MaterialEditor.TextureScaleOffsetProperty(normalMapTex);
                EditorGUI.indentLevel = 0;
            }

            EditorGUI.showMixedValue = flatShadingState.hasMixedValue;

            flatShading = EditorGUILayout.Toggle("Flat Shading", flatShading);

            EditorGUI.showMixedValue = false;
        }

        if (EditorGUI.EndChangeCheck())
        {
            flatShadingState.floatValue = flatShading ? 1.0f : 0.0f;

            MaterialChanged(material);
        }
    }
}

#endif