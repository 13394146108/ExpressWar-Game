using System.Collections;
using UnityEngine;

/// <summary>
/// 快递出口：位置上原有的物体被玩家拿走（离开出生点）后，
/// 等待 RespawnDelay 秒，再随机生成一个 Prefab。
/// 用法：挂在一个空物体上，把 small / medium / large 三个 Prefab 拖进数组，
/// 场景里这个位置预先摆一个 box 作为"第一个"。
/// 注意：只有补货生成的新快递才随机功能类型，场景里预摆的快递保持普通。
/// </summary>
public class BoxSpawner : MonoBehaviour
{
    [Header("生成")]
    [Tooltip("可随机生成的预制体（小 / 中 / 大）")]
    public GameObject[] Prefabs;

    [Tooltip("物体离开出生点多远算被拿走（米）")]
    public float TakenDistance = 0.8f;

    [Tooltip("被拿走多久后补货（秒）")]
    public float RespawnDelay = 2f;

    [Header("功能类型随机（只作用于补货生成的新快递）")]
    [Tooltip("新生成的快递是否随机一个功能类型")]
    public bool RandomizeFeature = true;

    [Tooltip("各功能类型出现的权重：不用凑满 100，按比例算；填 0 = 永不出现")]
    public FeatureWeight[] FeatureWeights = new FeatureWeight[]
    {
        new FeatureWeight { Type = ParcelType.Normal,    Weight = 70f },
        new FeatureWeight { Type = ParcelType.Fragile,   Weight = 15f },
        new FeatureWeight { Type = ParcelType.Explosive, Weight = 15f },
    };

    /// <summary>权重表的一项：某个类型出现多少权重</summary>
    [System.Serializable]
    public class FeatureWeight
    {
        public ParcelType Type = ParcelType.Normal;

        [Min(0f)]
        public float Weight = 1f;
    }

    // 当前这个位置上的那一个
    private Transform current;
    private bool waiting;

    void Start()
    {
        // 场景里预先摆好的那个作为第一个（不随机功能，保持普通）
        current = transform.childCount > 0 ? transform.GetChild(0) : null;

        if (current != null)
            current.SetParent(null, true);   // 脱离父子关系，但保持世界位置
        else
            SpawnNow();
    }

    void Update()
    {
        if (waiting) return;

        // 被拿走 / 被销毁 → 开始补货倒计时
        if (current == null || IsTaken(current))
        {
            current = null;
            StartCoroutine(RespawnRoutine());
        }
    }

    /// <summary>离开出生点超过阈值就算被拿走（抓起、扔掉都会触发）</summary>
    private bool IsTaken(Transform t)
    {
        return Vector3.Distance(t.position, transform.position) > TakenDistance;
    }

    private IEnumerator RespawnRoutine()
    {
        waiting = true;

        yield return new WaitForSeconds(RespawnDelay);

        // 出生点被别的东西占着时先等着，避免叠在一起
        while (!IsSpotFree())
            yield return null;

        SpawnNow();
        waiting = false;
    }

    private void SpawnNow()
    {
        if (Prefabs == null || Prefabs.Length == 0) return;

        GameObject prefab = Prefabs[Random.Range(0, Prefabs.Length)];
        GameObject go = Instantiate(prefab, transform.position, transform.rotation);

        // 尺寸已经随机好了，这里再随机一个功能类型贴上去
        if (RandomizeFeature) ApplyRandomFeature(go);

        // 不设父物体：被拿走后不会留下挂在出口下的奇怪层级
        current = go.transform;
    }

    // ---------- 功能类型 ----------
    /// <summary>按权重随机一个类型，并写到快递的 ParcelFeature 上</summary>
    private void ApplyRandomFeature(GameObject go)
    {
        if (go == null) return;

        // prefab 上没挂就运行时补一个（挂了也能正常跑）
        ParcelFeature feature = go.GetComponent<ParcelFeature>();
        if (feature == null) feature = go.AddComponent<ParcelFeature>();

        feature.SetType(RollType());
    }

    /// <summary>按权重表掷一次类型</summary>
    private ParcelType RollType()
    {
        if (FeatureWeights == null || FeatureWeights.Length == 0)
            return ParcelType.Normal;

        float total = 0f;
        for (int i = 0; i < FeatureWeights.Length; i++)
        {
            if (FeatureWeights[i] != null && FeatureWeights[i].Weight > 0f)
                total += FeatureWeights[i].Weight;
        }
        if (total <= 0f) return ParcelType.Normal;

        float r = Random.Range(0f, total);
        for (int i = 0; i < FeatureWeights.Length; i++)
        {
            FeatureWeight w = FeatureWeights[i];
            if (w == null || w.Weight <= 0f) continue;

            if (r < w.Weight) return w.Type;
            r -= w.Weight;
        }

        return ParcelType.Normal;   // 浮点兜底
    }

    private bool IsSpotFree()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position, TakenDistance * 0.9f, ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider c in hits)
        {
            if (c.attachedRigidbody != null) return false;   // 有可动物体占位
        }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, TakenDistance);
    }
}

