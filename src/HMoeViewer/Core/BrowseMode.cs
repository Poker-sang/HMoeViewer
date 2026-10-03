using HMoeData.Models;

namespace HMoeViewer.Core;

/// <summary>
/// 浏览页面的显示规则。初筛显示所有项目，详情和下载只在已选项目中排除对应阶段临时接受的项目。
/// 后四个模式只按原数据库的永久选择状态过滤，
/// 不受正文或下载链接缓存、临时接受标记影响；所有模式仍应用搜索条件。
/// </summary>
public enum BrowseMode
{
    /// <summary>初次筛选：显示所有项目，不排除任何永久状态或临时接受标记。</summary>
    Brief,

    /// <summary>详情筛选：只显示永久状态为 <see cref="PostSelectionState.Selected" /> 且未在详情阶段临时接受的项目。</summary>
    Detail,

    /// <summary>
    /// 下载后筛选：只显示永久状态为 <see cref="PostSelectionState.Selected" /> 且未在下载阶段临时接受的项目。
    /// 不要求先在详情阶段接受；详情临时接受不影响此页面。
    /// </summary>
    Download,

    /// <summary>未选择：只显示原数据库永久状态为 <see cref="PostSelectionState.Unselected" /> 的项目，不受临时接受标记影响。</summary>
    Unselected,

    /// <summary>取消：只显示原数据库永久状态为 <see cref="PostSelectionState.Deselected" /> 的项目，不受临时接受标记影响。</summary>
    Deselected,

    /// <summary>已删：只显示原数据库永久状态为 <see cref="PostSelectionState.Deleted" /> 的项目，不受临时接受标记影响。</summary>
    Deleted,

    /// <summary>选择：只显示原数据库永久状态为 <see cref="PostSelectionState.Selected" /> 的全部项目，不受临时接受标记影响。</summary>
    Selected
}
