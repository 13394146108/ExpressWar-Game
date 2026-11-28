using UnityEngine;

/// <summary>
/// 顾客订单的桌面透明预览：一个半透明方块 + 12 条描边。
/// 由 CustomerOrder 在运行时创建，不挂到场景物体上，也不参与物理。
/// </summary>
public sealed class OrderGhostPreview
{
    private GameObject root;
    private GameObject outlineRoot;
    private Material fillMaterial;
    private Material outlineMaterial;

    /// <summary>在桌面落点显示预览</summary>
    public void Show(Transform table, OrderSize size, Color color, float alpha, float outlineWidth)
    {
        Clear();
        if (table == null) return;

        Vector3 cubeSize = SizeOf(size);
        Vector3 center = table.position;

        Collider tableCollider = table.GetComponentInChildren<Collider>();
        if (tableCollider != null)
        {
            Bounds b = tableCollider.bounds;
            center = new Vector3(b.center.x, b.max.y + cubeSize.y * 0.5f + 0.005f, b.center.z);
        }
        else
        {
            center += Vector3.up * (cubeSize.y * 0.5f + 0.005f);
        }

        root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "OrderGhostPreview";
        root.transform.SetPositionAndRotation(center, table.rotation);
        root.transform.localScale = cubeSize;

        Collider cubeCollider = root.GetComponent<Collider>();
        if (cubeCollider != null)
        {
            cubeCollider.enabled = false;   // 先禁用，Destroy 到帧末才真正生效
            Object.Destroy(cubeCollider);
        }

        Renderer cubeRenderer = root.GetComponent<Renderer>();
        if (cubeRenderer != null)
        {
            fillMaterial = CreateFillMaterial(color, alpha);
            if (fillMaterial != null) cubeRenderer.sharedMaterial = fillMaterial;

            cubeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cubeRenderer.receiveShadows = false;
        }

        outlineRoot = new GameObject("OrderGhostOutline");
        outlineRoot.transform.SetPositionAndRotation(center, table.rotation);

        BuildOutline(cubeSize, color, outlineWidth);
    }

    /// <summary>暂时隐藏，不销毁</summary>
    public void Hide()
    {
        if (root != null) root.SetActive(false);
        if (outlineRoot != null) outlineRoot.SetActive(false);
    }

    /// <summary>彻底销毁预览和临时材质</summary>
    public void Clear()
    {
        if (root != null) Object.Destroy(root);
        if (outlineRoot != null) Object.Destroy(outlineRoot);
        if (fillMaterial != null) Object.Destroy(fillMaterial);
        if (outlineMaterial != null) Object.Destroy(outlineMaterial);

        root = null;
        outlineRoot = null;
        fillMaterial = null;
        outlineMaterial = null;
    }

    private void BuildOutline(Vector3 cubeSize, Color color, float width)
    {
        outlineMaterial = CreateLineMaterial(color);
        if (outlineMaterial == null) return;

        // 立方体 8 个角（独立根节点不缩放，所以直接按实际尺寸算世界坐标）
        Vector3 h = cubeSize * 0.5f;
        Vector3[] corners =
        {
            new Vector3(-h.x, -h.y, -h.z), // 0
            new Vector3( h.x, -h.y, -h.z), // 1
            new Vector3(-h.x,  h.y, -h.z), // 2
            new Vector3( h.x,  h.y, -h.z), // 3
            new Vector3(-h.x, -h.y,  h.z), // 4
            new Vector3( h.x, -h.y,  h.z), // 5
            new Vector3(-h.x,  h.y,  h.z), // 6
            new Vector3( h.x,  h.y,  h.z), // 7
        };

        // 立方体的 12 条边
        int[,] edges =
        {
            { 0, 1 }, { 1, 3 }, { 3, 2 }, { 2, 0 },   // 后面
            { 4, 5 }, { 5, 7 }, { 7, 6 }, { 6, 4 },   // 前面
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },   // 竖向
        };

        float lineWidth = Mathf.Max(0.002f, width);
        Color lineColor = new Color(color.r, color.g, color.b, 1f);

        for (int i = 0; i < edges.GetLength(0); i++)
        {
            GameObject edge = new GameObject("Edge");
            edge.transform.SetParent(outlineRoot.transform, false);

            LineRenderer line = edge.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, corners[edges[i, 0]]);
            line.SetPosition(1, corners[edges[i, 1]]);

            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;

            line.startColor = lineColor;
            line.endColor = lineColor;
            line.sharedMaterial = outlineMaterial;

            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
        }
    }

    private static Material CreateFillMaterial(Color color, float alpha)
    {
        // 必须用带 _Color 的 Shader，否则 Material.color 无效，表面会保持白色。
        // Sprites/Default 支持 _Color + 透明，并会正确乘上立方体表面的顶点色。
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");

        if (shader == null)
        {
            Debug.LogWarning("[OrderGhost] 找不到透明材质 Shader，桌面预览将不显示");
            return null;
        }

        Material material = new Material(shader);
        Color tint = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));

        if (material.HasProperty("_Color"))
            material.color = tint;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", tint);

        return material;
    }

    private static Material CreateLineMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        bool useVertexColor = shader != null;

        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
        {
            Debug.LogWarning("[OrderGhost] 找不到描边材质 Shader，桌面预览描边将不显示");
            return null;
        }

        Material material = new Material(shader);
        if (material.HasProperty("_Color"))
        {
            // Sprites/Default 会把材质颜色和 LineRenderer 顶点颜色相乘，
            // 所以材质保持白色；回退 Shader 则直接用订单颜色。
            material.color = useVertexColor
                ? Color.white
                : new Color(color.r, color.g, color.b, 1f);
        }

        return material;
    }

    /// <summary>订单尺寸和三种快递预制体的实际尺寸保持一致</summary>
    private static Vector3 SizeOf(OrderSize size)
    {
        if (size == OrderSize.Medium) return new Vector3(0.4f, 0.3f, 0.3f);
        if (size == OrderSize.Large) return new Vector3(0.6f, 0.4f, 0.4f);
        return new Vector3(0.2f, 0.2f, 0.2f);
    }
}
