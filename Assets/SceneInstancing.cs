using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SceneInstancing : MonoBehaviour
{
    // Control instancing parameters (we replicate the entire scene for now)
    [Min(0)]
    public Vector2Int instances = new Vector2Int(1, 1);
    public Vector2 instanceSpacing = new Vector2(1, 1);

    List<MeshRenderer> instancedRenderes = new List<MeshRenderer>(1024);

    // Start is called before the first frame update
    void Start()
    {
        bool useInstancing = !instances.Equals(new Vector2Int(1, 1));

        if (useInstancing)
        {
            // TODO: instancing will not work properly when changing scenes! For now if instancing is active, we have to close the editor and open it again when changing scenes. 
            var rendererArray = UnityEngine.GameObject.FindObjectsOfType<MeshRenderer>();

            for (var i = 0; i < rendererArray.Length; i++)
            {
                for (var j = 0; j < instances.x; j++)
                {
                    for (var k = 0; k < instances.y; k++)
                    {
                        var instance = Instantiate(rendererArray[i]);
                        instance.hideFlags = HideFlags.HideAndDontSave;
                        instance.gameObject.hideFlags = HideFlags.HideAndDontSave;
                        instance.gameObject.transform.position = rendererArray[i].transform.position + new Vector3(j * instanceSpacing.x, 0, k * instanceSpacing.y);
                        // Note: this will probably not work on every scene.
                        instance.gameObject.transform.localScale = rendererArray[i].transform.lossyScale;

                        instancedRenderes.Add(instance);
                    }
                }
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnDestroy()
    {
        for(int i=0; i< instancedRenderes.Count; ++i)
        {
            DestroyImmediate(instancedRenderes[i]);
        }
        instancedRenderes.Clear();
    }
}
