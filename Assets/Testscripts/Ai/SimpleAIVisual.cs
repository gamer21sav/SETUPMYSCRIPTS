using System;
using System.Reflection;
using UnityEngine;

public class SimpleAIVisual : MonoBehaviour
{
    // ─── PUBLIC SETTINGS ────────────────────────────────

    [Header("Layout")]
    public float groundOffset = 0.05f;   // lifts shapes off the floor to avoid flicker
    public int segments = 48;            // smoothness of circles and arcs

    [Header("Colors (alpha = fill opacity)")]
    public Color attackColor = new Color(0f, 0.4f, 1f, 0.15f);
    public Color detectColor = new Color(1f, 0f, 0f, 0.12f);
    public Color sightColor = new Color(1f, 1f, 1f, 0.08f);
    public Color fovColor = new Color(1f, 1f, 0f, 0.20f);
    public Color roamColor = new Color(0f, 1f, 0f, 0.10f);

    // ─── PRIVATE STATE ──────────────────────────────────

    private Component ai;

    // ─── STARTUP ────────────────────────────────────────

    void Start()
    {
        ai = GetComponent("SimpleAI");
        if (ai == null)
        {
            Debug.LogError($"{name}: SimpleAI component not found. Ensure the enemy AI script is attached to this object.", this);
            enabled = false;
            return;
        }

        Vector3 offset = new Vector3(0f, groundOffset, 0f);

        // These follow the enemy as it moves and turns
        MakeShape("AttackRange", BuildSector(GetFloat("attackrange", "attackRange"), 360f), attackColor, transform, offset);
        MakeShape("DetectRange", BuildSector(GetFloat("detectrange", "detectRange"), 360f), detectColor, transform, offset);
        MakeShape("SightRange", BuildSector(GetFloat("sightRange"), 360f), sightColor, transform, offset);
        MakeShape("FieldOfView", BuildSector(GetFloat("sightRange"), GetFloat("fieldOfView", "fieldofview")), fovColor, transform, offset);

        // Roam ring stays at the spawn point (home), so it is not parented to the enemy
        Vector3 home = transform.position + offset;
        MakeShape("RoamRange", BuildSector(GetFloat("roamRadius", "roamradius"), 360f), roamColor, null, home);
    }

    // ─── HELPERS ────────────────────────────────────────

    float GetFloat(params string[] names)
    {
        foreach (string name in names)
        {
            FieldInfo field = ai.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float))
            {
                return (float)field.GetValue(ai);
            }

            PropertyInfo property = ai.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(float))
            {
                return (float)property.GetValue(ai, null);
            }
        }

        string requestedNames = string.Join(" or ", names);
        Debug.LogError($"{name}: Unable to find a float field/property named {requestedNames} on {ai.GetType().Name}.", this);
        return 0f;
    }

    // Builds a flat fan. 360 = full circle. Otherwise a pie slice centered on +Z.
    Mesh BuildSector(float radius, float fullAngle)
    {
        Vector3[] verts = new Vector3[segments + 2];
        int[] tris = new int[segments * 3];

        verts[0] = Vector3.zero;
        float start = -fullAngle * 0.5f;

        for (int i = 0; i <= segments; i++)
        {
            float a = start + fullAngle * i / segments;
            verts[i + 1] = Quaternion.Euler(0f, a, 0f) * Vector3.forward * radius;
        }

        for (int i = 0; i < segments; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i + 2;
        }

        Mesh mesh = new Mesh();
        mesh.name = "SightShape";
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    GameObject MakeShape(string shapeName, Mesh mesh, Color color, Transform parent, Vector3 position)
    {
        GameObject go = new GameObject(shapeName);
        go.transform.SetParent(parent, false);
        go.transform.position = parent == null ? position : parent.TransformPoint(position);
        go.transform.rotation = parent == null ? Quaternion.identity : parent.rotation;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Sprites/Default");   // transparent, works in built-in and URP
        if (shader == null)
        {
            Debug.LogError($"{name}: Sprites/Default shader not found.", this);
            return go;
        }
        mr.sharedMaterial = new Material(shader) { color = color };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        return go;
    }
}