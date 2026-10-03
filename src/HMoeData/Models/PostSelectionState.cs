namespace HMoeData.Models;

public enum PostSelectionState
{
    /// <summary>
    /// 未选
    /// </summary>
    Unselected,
    
    /// <summary>
    /// 已选
    /// </summary>
    Selected,

    /// <summary>
    /// 取消选择（查看详情后取消）
    /// </summary>
    Deselected,

    /// <summary>
    /// 已删除（下载后删除）
    /// </summary>
    Deleted
}
