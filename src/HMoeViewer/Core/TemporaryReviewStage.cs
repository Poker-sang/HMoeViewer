using System;

namespace HMoeViewer.Core;

/// <summary>
/// 保存在临时撤销历史数据库中的独立接受及进度标记，不写入原数据库。
/// 接受标记只影响对应阶段的筛选；已查看、已下载仅用于边框提示，不隐藏项目。标记可组合，不能按大小比较。
/// </summary>
[Flags]
public enum TemporaryReviewStage
{
    /// <summary>尚无任何临时接受或进度标记。</summary>
    None = 0,

    /// <summary>详情阶段已临时接受；从详情筛选隐藏，不影响下载后筛选。</summary>
    DetailAccepted = 1,

    /// <summary>下载阶段已临时接受；从下载后筛选隐藏，不影响详情筛选。</summary>
    DownloadAccepted = 2,

    /// <summary>两个阶段均已临时接受；初筛及永久状态页面仍显示符合其规则的项目。</summary>
    BothAccepted = DetailAccepted | DownloadAccepted,

    /// <summary>已在软件内显示正文或成功打开文章原网页；详情筛选用蓝框提示。</summary>
    Viewed = 4,

    /// <summary>至少一个下载链接已由服务确认成功发送到客户端；下载筛选用蓝框提示，不代表文件已完成下载。</summary>
    Downloaded = 8
}
