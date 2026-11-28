using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 掉落惩罚：Tag 为 CanGrab 的快递直接碰到名为 Ground 的地面后，
/// 连续停留超过 DestroyDelay 秒就消失。中途离开地面会重新计时。
///
/// 用法：把本脚本挂到场景里任意物体上（例如 Ground 或一个空的管理物体），
/// 不需要手动拖引用；它会自己找 CanGrab 快递和 Ground。
/// </summary>
public class Punish : MonoBehaviour
{
    [Header("惩罚规则")]
    [Tooltip("在 Ground 上连续停留多久后消失（秒）")]
    [Min(0f)] public float DestroyDelay = 4f;

    [Tooltip("地面物体的名字（场景里那个正方体）")]
    public string GroundObjectName = "Ground";

    private const string GrabTag = "CanGrab";

    private readonly Dictionary<Rigidbody, float> groundTimers = new Dictionary<Rigidbody, float>();
    private readonly Dictionary<Rigidbody, ParcelGroundWatcher> watchers = new Dictionary<Rigidbody, ParcelGroundWatcher>();
    private readonly HashSet<Rigidbody> activeBodies = new HashSet<Rigidbody>();
    private readonly List<Rigidbody> expiredBodies = new List<Rigidbody>();

    void Update()
    {
        float dt = Time.deltaTime;
        activeBodies.Clear();

        // 每帧重新扫描，BoxSpawner 运行时新生成的快递也能自动纳入判定
        GameObject[] parcels = GameObject.FindGameObjectsWithTag(GrabTag);
        for (int i = 0; i < parcels.Length; i++)
        {
            Rigidbody rb = parcels[i].GetComponent<Rigidbody>();
            if (rb == null) continue;

            activeBodies.Add(rb);

            ParcelGroundWatcher watcher = GetWatcher(rb);

            // 被拿着（或放在货架上）时是 kinematic，不算落地
            if (rb.isKinematic)
            {
                watcher.Clear();
                groundTimers.Remove(rb);
                continue;
            }

            if (!watcher.IsTouchingGround)
            {
                groundTimers.Remove(rb);   // 离开地面：重新计时
                continue;
            }

            if (!groundTimers.TryGetValue(rb, out float timer)) timer = 0f;
            timer += dt;

            if (timer >= DestroyDelay)
            {
                groundTimers.Remove(rb);
                Debug.Log($"[Punish] {rb.name} 在 {GroundObjectName} 上停留超过 {DestroyDelay:0.#} 秒，已销毁", this);
                Destroy(rb.gameObject);
                continue;
            }

            groundTimers[rb] = timer;
        }

        // 清掉已经被别处销毁 / 不再带 CanGrab 标签的快递计时和监听器
        if (groundTimers.Count > 0 || watchers.Count > 0)
        {
            expiredBodies.Clear();

            foreach (Rigidbody rb in groundTimers.Keys)
                if (!activeBodies.Contains(rb)) expiredBodies.Add(rb);

            foreach (Rigidbody rb in watchers.Keys)
                if (!activeBodies.Contains(rb) && !expiredBodies.Contains(rb)) expiredBodies.Add(rb);

            for (int i = 0; i < expiredBodies.Count; i++)
            {
                groundTimers.Remove(expiredBodies[i]);
                watchers.Remove(expiredBodies[i]);
            }
        }
    }

    /// <summary>给快递取（或补建）一个落地监听器；同一个刚体只挂一个</summary>
    private ParcelGroundWatcher GetWatcher(Rigidbody rb)
    {
        if (!watchers.TryGetValue(rb, out ParcelGroundWatcher watcher) || watcher == null)
        {
            watcher = rb.gameObject.AddComponent<ParcelGroundWatcher>();
            watchers[rb] = watcher;
        }

        watcher.GroundName = GroundObjectName;
        return watcher;
    }
}

/// <summary>
/// 自动挂在快递上，用碰撞回调记录它当前是否直接压在 Ground 上。
/// 由 Punish 在运行时添加，不需要在预制体里手动挂。
/// </summary>
[DisallowMultipleComponent]
public class ParcelGroundWatcher : MonoBehaviour
{
    public string GroundName = "Ground";

    private int groundContacts;

    public bool IsTouchingGround => groundContacts > 0;

    void OnCollisionEnter(Collision collision)
    {
        if (IsGround(collision)) groundContacts++;
    }

    void OnCollisionStay(Collision collision)
    {
        if (IsGround(collision) && groundContacts == 0) groundContacts = 1;
    }

    void OnCollisionExit(Collision collision)
    {
        if (IsGround(collision)) groundContacts = Mathf.Max(0, groundContacts - 1);
    }

    public void Clear() => groundContacts = 0;

    private bool IsGround(Collision collision)
    {
        if (collision == null) return false;

        GameObject go = collision.gameObject;
        if (go == null) return false;
        if (go.name == GroundName) return true;

        Transform parent = go.transform.parent;
        return parent != null && parent.name == GroundName;
    }
}
