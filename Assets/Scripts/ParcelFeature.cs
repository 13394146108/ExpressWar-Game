using UnityEngine;

/// <summary>快递的功能类型</summary>
public enum ParcelType
{
    Normal = 0,     // 普通：现在的黄色快递，没有功能
    Fragile = 1,    // 易碎：蓝色，功能后续补
    Explosive = 2,  // 易爆：红色，功能后续补
}

/// <summary>
/// 给快递挂上「功能类型」。挂在快递根节点上（和 GrabbableItem 同一个物体）。
/// 尺寸随机由 BoxSpawner 负责，这里只管功能类型和它的表现。
/// </summary>
[DisallowMultipleComponent]
public class ParcelFeature : MonoBehaviour
{
    [Header("功能类型")]
    [SerializeField] private ParcelType type = ParcelType.Normal;

    public ParcelType Type => type;
    public bool IsSpecial => type != ParcelType.Normal;

    [Header("外观（只对易碎 / 易爆生效，普通保持原材质）")]
    [SerializeField] private Color fragileColor = new Color(0.15f, 0.45f, 1f);   // 蓝
    [SerializeField] private Color explosiveColor = new Color(1f, 0.15f, 0.15f); // 红

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

    private void ApplyVisual()
    {
        Color? tint = null;
        if (type == ParcelType.Fragile) tint = fragileColor;
        else if (type == ParcelType.Explosive) tint = explosiveColor;

        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;

            block.Clear();                 // 普通类型 => 空 block => 恢复 box.mat 原色
            if (tint.HasValue)
            {
                block.SetColor(ColorId, tint.Value);
                block.SetColor(BaseColorId, tint.Value);
            }
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
