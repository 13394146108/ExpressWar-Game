using UnityEngine;

/// <summary>
/// 可搬运物体的尺寸 / 重量。挂在所有打了 CanGrab 标签的物体上。
/// 玩家总承重为 3，所以：大=3 独占，中+小=3，小+小+小=3。
/// </summary>
public class GrabbableItem : MonoBehaviour
{
    public enum ItemSize
    {
        Small = 1,   // 小
        Medium = 2,  // 中
        Large = 3    // 大
    }

    [Tooltip("小 = 1 / 中 = 2 / 大 = 3")]
    public ItemSize Size = ItemSize.Small;

    /// <summary>重量（等于尺寸等级），与玩家承重相减来判定还能不能拿</summary>
    public int Weight => (int)Size;
}
