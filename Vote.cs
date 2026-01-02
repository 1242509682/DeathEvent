using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using TShockAPI;

namespace DeathEvent;

internal static class Vote
{
    #region 投票数据结构
    public static ConcurrentDictionary<string, TeamVote> VoteData = new ConcurrentDictionary<string, TeamVote>();
    public class TeamVote
    {
        public string AppName { get; set; } = "";      // 申请人
        public int Team { get; set; } = -1;            // 目标队伍
        public DateTime Start { get; set; }            // 开始时间
        public int Time { get; set; } = 30;           // 投票时间(秒)
        public HashSet<string> Agree { get; set; } = new();   // 同意名单
        public HashSet<string> Against { get; set; } = new(); // 拒绝名单

        // 队伍成员总数(排除申请人) - 实时计算在线玩家
        public int Total => GetPlayers().Count;
        public int Remain => Time - (int)(DateTime.Now - Start).TotalSeconds;   // 剩余时间
        public bool IsEnd => Remain <= 0; // 是否结束
        public string Key => $"{AppName}|{Team}";   // 获取投票键

        // 获取队伍玩家列表（排除申请人）
        public List<TSPlayer> GetPlayers()
        {
            var plrs = new List<TSPlayer>();
            for (int i = 0; i < TShock.Players.Length; i++)
            {
                var p = TShock.Players[i];
                if (p != null && p.Active && p.Team == Team && p.Name != AppName)
                {
                    plrs.Add(p);
                }
            }
            return plrs;
        }

        // 获取投票统计信息
        public VoteStats GetStats()
        {
            var players = GetPlayers();
            int total = players.Count;
            int agree = Agree.Count;
            int against = Against.Count;
            double agreeRate = total > 0 ? (double)agree / total * 100 : 0;

            return new VoteStats
            {
                Total = total,
                Agree = agree,
                Against = against,
                AgreeRate = agreeRate,
                Players = players
            };
        }
    }

    public class VoteStats
    {
        public int Total { get; set; }
        public int Agree { get; set; }
        public int Against { get; set; }
        public double AgreeRate { get; set; }
        public List<TSPlayer> Players { get; set; } = new();
    }
    #endregion

    #region 公共方法
    // 获取投票数据
    public static TeamVote? Get(string appName, int team) => VoteData.TryGetValue($"{appName}|{team}", out var vote) ? vote : null;
    // 检查玩家是否有未结束的投票申请
    public static bool HasPending(string appName) => VoteData.Values.Any(v => v.AppName == appName && !v.IsEnd);
    // 检查队伍是否有未结束的投票
    public static bool HasTeam(int team) => VoteData.Values.Any(v => v.Team == team && !v.IsEnd);
    // 添加投票
    public static bool Add(TeamVote vote) => VoteData.TryAdd(vote.Key, vote);
    // 移除投票
    public static void Remove(string appName, int team) => VoteData.TryRemove($"{appName}|{team}", out _);
    #endregion

    #region 检查并处理超时投票
    public static void CheckTimeout()
    {
        // 使用列表记录需要处理的超时投票
        var ToProcess = new List<TeamVote>();

        // 一次遍历收集超时投票
        foreach (var vote in VoteData.Values)
        {
            if (vote.IsEnd)
            {
                ToProcess.Add(vote);
            }
        }

        // 批量处理超时投票
        foreach (var vote in ToProcess)
        {
            ProcessResult(vote);
        }
    }
    #endregion

    #region 投票操作(指令操作：/det y与n)
    public static void Action(TSPlayer plr, bool isAgree)
    {
        // 查找玩家当前队伍的投票
        var vote = VoteData.Values.FirstOrDefault(v => v.Team == plr.Team && !v.IsEnd && plr.Name != v.AppName);

        if (vote == null)
        {
            plr.SendMessage("当前没有可投票的申请", Color.Yellow);
            return;
        }

        // 检查是否已投票
        if (vote.Agree.Contains(plr.Name) || vote.Against.Contains(plr.Name))
        {
            ShowStatus(plr, vote);
            return;
        }

        // 执行投票
        if (isAgree)
        {
            vote.Agree.Add(plr.Name);
        }
        else
        {
            vote.Against.Add(plr.Name);
        }

        // 检查投票结果
        CheckResult(vote);
    }
    #endregion

    #region 检查投票结果
    private static void CheckResult(TeamVote vote)
    {
        var stats = vote.GetStats();
        int voted = stats.Agree + stats.Against;

        // 如果已经投票的人数达到当前在线成员数，立即结束投票
        if (voted >= stats.Total && stats.Total > 0)
        {
            ProcessResult(vote); // 处理投票结果
        }
        // 仍有未投票成员
        else if (voted < stats.Total)
        {
            SendMsg(vote); // 发送当前投票状态
        }
    }
    #endregion

    #region 处理投票结果（核心逻辑）
    private static void ProcessResult(TeamVote vote)
    {
        var stats = vote.GetStats();

        // 发送结果消息
        SendResult(vote, stats);

        // 投票通过
        if (stats.AgreeRate > 50)
        {
            var plr = TShock.Players.FirstOrDefault(p => p?.Name == vote.AppName);
            if (plr != null)
            {
                DeathEvent.SwitchTeam(plr, vote.Team, false);
            }
        }
        // 投票拒绝
        else
        {
            var plr = TShock.Players.FirstOrDefault(p => p?.Name == vote.AppName);
            plr?.SendMessage($"加入 {Data.GetTeamName(vote.Team)} 的申请被拒绝", Color.Red);
        }

        // 清理投票数据
        Remove(vote.AppName, vote.Team);
    }
    #endregion

    #region 发送最终结果
    private static void SendResult(TeamVote vote, VoteStats stats)
    {
        var teamName = Data.GetTeamName(vote.Team);
        string msg = $"\n投票结束！{teamName}申请结果:\n" +
                     $"同意:[c/32CD32:{stats.Agree}/{stats.Total}] " +
                     $"({stats.AgreeRate:F1}%)\n";

        msg += stats.AgreeRate > 50 ? "结果: [c/32CD32:通过]" : "结果: [c/FF4500:不通过]";

        // 发送给所有相关玩家
        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            if (p != null && p.Active)
            {
                if((p.Name == vote.AppName || p.Team == vote.Team))
                p.SendMessage(msg, Tool.color);
            }
        }
    }
    #endregion

    #region 显示投票状态（用于/det v指令）
    public static void ShowStatus(TSPlayer plr, TeamVote vote)
    {
        var stats = vote.GetStats();
        var teamName = Data.GetTeamName(vote.Team);

        // 检查投票状态
        string info = plr.Name == vote.AppName ? "（您是申请人）"
            : vote.Agree.Contains(plr.Name) ? "（您已投：同意）"
            : vote.Against.Contains(plr.Name) ? "（您已投：反对）"
            : "（您未投票）";

        string msg = $"\n{teamName}投票 {info}\n" +
                     $"申请人: [c/508DC8:{vote.AppName}]\n" +
                     $"同意: [c/32CD32:{stats.Agree}/{stats.Total}] 反对: [c/FF4500:{stats.Against}/{stats.Total}]\n" +
                     $"同意率: [c/FFD700:{stats.AgreeRate:F1}%] 剩余: [c/00CED1:{vote.Remain}秒]\n";

        // 显示投票情况
        var voted = new List<string>();
        var notVoted = new List<string>();

        foreach (var p in stats.Players)
        {
            if (vote.Agree.Contains(p.Name))
                voted.Add($"[c/32CD32:{p.Name}]");
            else if (vote.Against.Contains(p.Name))
                voted.Add($"[c/FF4500:{p.Name}]");
            else
                notVoted.Add(p.Name);
        }

        if (voted.Count > 0)
            msg += $"已投票: {string.Join("、", voted)}\n";

        if (notVoted.Count > 0)
            msg += $"未投票: [c/888888:{string.Join("、", notVoted)}]";

        plr.SendMessage(msg, Tool.color);
    }
    #endregion

    #region 发送投票信息(用于投票后,投票还未结束前发送)
    private static void SendMsg(TeamVote vote)
    {
        var stats = vote.GetStats();
        var teamName = Data.GetTeamName(vote.Team);
        string msg = $"\n{teamName}投票: [c/508DC8:{vote.AppName}]申请加入\n" +
                     $"同意:[c/32CD32:{stats.Agree}/{stats.Total}], 拒绝:[c/FF4500:{stats.Against}/{stats.Total}]\n" +
                     $"同意率:[c/FFD700:{stats.AgreeRate:F1}%], 剩余:[c/00CED1:{vote.Remain}秒]\n" +
                     "使用 /det [c/5A9CDE:y]同意, [c/F4636F:n]拒绝, [c/5ADED3:v]查看详情\n";

        int voted = stats.Agree + stats.Against;
        int remaining = stats.Total - voted;
        if (remaining > 0)
            msg += $"还需投票: [c/FCF567:{remaining}]人";

        // 发送给目标队伍成员
        foreach (var p in stats.Players)
        {
            if (p != null && p.Active)
            {
                p.SendMessage(msg, Tool.color);
            }
        }
    }
    #endregion

    #region 清理玩家投票数据
    public static void Clear(string plrName)
    {
        // 单次遍历完成所有操作
        foreach (var kv in VoteData)
        {
            var vote = kv.Value;

            // 1. 检查是否是申请人
            if (vote.AppName == plrName)
            {
                if (!vote.IsEnd)
                {
                    NotifyCancel(vote, $"申请人 {plrName} 已离开");
                }
                
                Remove(vote.AppName, vote.Team); // 先通知再移除
                continue; // 已移除，不需要检查投票记录
            }

            // 2. 检查是否是投票者
            if (vote.Agree.Remove(plrName) || vote.Against.Remove(plrName))
            {
                if (!vote.IsEnd)
                {
                    CheckResult(vote);
                }
            }
        }
    }
    #endregion

    #region 清理所有投票数据（用于服务器重置）
    public static void ClearAll()
    {
        // 通知所有未结束的投票
        foreach (var vote in VoteData.Values)
        {
            if (!vote.IsEnd)
            {
                NotifyCancel(vote, "服务器重置");
            }
        }

        // 清空所有投票数据
        VoteData.Clear();
    }
    #endregion

    #region 通知投票取消
    private static void NotifyCancel(TeamVote vote, string reason)
    {
        try
        {
            var teamName = Data.GetTeamName(vote.Team);
            string msg = $"{teamName}投票已取消: {reason}";

            // 通知申请者（如果在线）
            var app = TShock.Players.FirstOrDefault(p => p?.Name == vote.AppName);
            if (app != null) app.SendMessage(msg, Tool.color);

            // 通知目标队伍成员
            var stats = vote.GetStats();
            foreach (var p in stats.Players)
            {
                if (p == null || !p.Active) continue;
                p.SendMessage(msg, Tool.color);
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"通知投票取消时发生错误: {ex.Message}");
        }
    }
    #endregion

}