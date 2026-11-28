using UnityEngine;

/// <summary>快递的功能类型</summary>
public enum ParcelType
{
    Normal = 0,     // 普通：绿色 (0, 255, 0)
    Fragile = 1,    // 易碎：蓝色 (0, 0, 255)
    Explosive = 2,  // 易爆：红色 (255, 0, 0)
}

/// <summary>
/// 给快递挂上「功能类型」和颜色。挂在快递根节点上（和 GrabbableItem 同一个物体）。
/// 普通 / 易碎 / 易爆分别对应绿 / 蓝 / 红；
/// Compose 合成出来的快递会通过 SetMixedColor 覆盖成混合后的颜色。
/// </summary>
[DisallowMultipleComponent]
public class ParcelFeature : MonoBehaviour
{
    private static readonly Color RedColor = new Color(1f, 0f, 0f);     // 255, 0, 0
    private static readonly Color GreenColor = new Color(0f, 1f, 0f);   // 0, 255, 0
    private static readonly Color BlueColor = new Color(0f, 0f, 1f);    // 0, 0, 255

    [Header("功能类型")]
    [SerializeField] private ParcelType type = ParcelType.Normal;

    public ParcelType Type => type;
    public bool IsSpecial => type != ParcelType.Normal;

    private bool hasMixedColor;
    private Color mixedColor;

    /// <summary>当前实际颜色：混合过的快递优先用混合色，否则按红/绿/蓝类型取色</summary>
    public Color CurrentColor
    {
        get
        {
            if (hasMixedColor) return mixedColor;
            if (type == ParcelType.Fragile) return BlueColor;
            if (type == ParcelType.Explosive) return RedColor;
            return GreenColor;
        }
    }

    /// <summary>是否已经被 Compose 刷成混合色（用于判断还能不能查纯色混合表）</summary>
    public bool HasMixedColor => hasMixedColor;

    [Header("易碎（预留：先填 0 表示不判定，等你要做功能时再配数值）")]
    [Tooltip("受到超过这个冲量就判定为「摔坏」")]
    [Min(0f)][SerializeField] private float fragileImpulseThreshold = 0f;

    [Header("易爆（预留）")]
    [Tooltip("受到超过这个冲量就判定为「引爆」")]
    [Min(0f)][SerializeField] private float explosiveImpulseThreshold = 0f;

    [Tooltip("爆炸影响半径（米）")]
    [Min(0f)][SerializeField] private float explosiveRadius = 2f;

    [Tooltip("爆炸推力")]
    [Min(0f)][SerializeField] private float explosiveForce = 6f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");     // 内置管线 Standard
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP 兼容用

    void Start()
    {
        // 也在这里刷一次：如果组件是直接挂在 prefab 上并在 Inspector 里配了类型，
        // 运行起来也会正确染色
        ApplyVisual();
    }

    /// <summary>由 BoxSpawner 在生成时调用：设定功能类型并立刻更新外观</summary>
    public void SetType(ParcelType newType)
    {
        type = newType;
        ApplyVisual();
    }

    /// <summary>由 Compose 调用：给合成结果刷上混合出来的颜色</summary>
    public void SetMixedColor(Color color)
    {
        hasMixedColor = true;
        mixedColor = color;
        ApplyVisual();
    }

    private void ApplyVisual()
    {
        Color tint = CurrentColor;
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;

            block.Clear();
            block.SetColor(ColorId, tint);
            block.SetColor(BaseColorId, tint);
            r.SetPropertyBlock(block);
        }
    }

    //// ---------- 功能钩子（现在是空壳，后面往里填） ----------
    //void OnCollisionEnter(Collision collision)
    //{
    //    if (type == ParcelType.Normal) return;      // 普通快递不参与判定

    //    float impulse = collision.impulse.magnitude;

    //    if (type == ParcelType.Fragile) OnFragileImpact(impulse, collision);
    //    else if (type == ParcelType.Explosive) OnExplosiveImpact(impulse, collision);
    //}

    ///// <summary>易碎：摔/砸的冲量超过阈值时触发</summary>
    //private void OnFragileImpact(float impulse, Collision collision)
    //{
    //    if (fragileImpulseThreshold <= 0f) return;  // 还没配阈值 → 暂时不触发
    //    if (impulse < fragileImpulseThreshold) return;

    //    // TODO: 碎裂表现（换破碎模型 / 生成碎片 / 播放音效）、给投放方扣分等
    //    Debug.Log($"[ParcelFeature] 易碎快递摔坏了，冲量 {impulse:F1}", this);
    //}

    ///// <summary>易爆：冲量超过阈值时引爆</summary>
    //private void OnExplosiveImpact(float impulse, Collision collision)
    //{
    //    if (explosiveImpulseThreshold <= 0f) return;
    //    if (impulse < explosiveImpulseThreshold) return;

    //    Explode();
    //}

    ///// <summary>爆炸：先做成通用版本，后续再细化伤害/得分</summary>
    //public void Explode()
    //{
    //    Collider[] hits = Physics.OverlapSphere(
    //        transform.position, explosiveRadius, ~0, QueryTriggerInteraction.Ignore);

    //    foreach (Collider c in hits)
    //    {
    //        Rigidbody rb = c.attachedRigidbody;
    //        if (rb == null || rb.isKinematic) continue;      // 不推静止/被拿着的物体
    //        if (rb == GetComponent<Rigidbody>()) continue;   // 不推自己

    //        rb.AddExplosionForce(explosiveForce, transform.position, explosiveRadius);
    //    }

    //    // TODO: 爆炸特效 / 音效 / 销毁自身 / 结算分数
    //    Debug.Log("[ParcelFeature] 易爆快递爆炸", this);
    //}
}
