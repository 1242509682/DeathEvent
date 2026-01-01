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
        public int Total => GetTeamPlayers().Count;
        public int Remain => Time - (int)(DateTime.Now - Start).TotalSeconds;   // 剩余时间
        public bool IsEnd => Remain <= 0; // 是否结束
        public string GetKey() => $"{AppName}|{Team}";   // 获取投票键

        // 获取队伍玩家列表（排除申请人）
        public List<TSPlayer> GetTeamPlayers()
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
            var teamPlayers = GetTeamPlayers();
            int total = teamPlayers.Count;
            int agree = Agree.Count;
            int against = Against.Count;
            double agreeRate = total > 0 ? (double)agree / total * 100 : 0;

            return new VoteStats
            {
                Total = total,
                Agree = agree,
                Against = against,
                AgreeRate = agreeRate,
                TeamPlayers = teamPlayers
            };
        }
    }

    public class VoteStats
    {
        public int Total { get; set; }
        public int Agree { get; set; }
        public int Against { get; set; }
        public double AgreeRate { get; set; }
        public List<TSPlayer> TeamPlayers { get; set; } = new();
    }
    #endregion

    #region 公共方法
    // 获取投票数据
    public static TeamVote? GetVote(string appName, int team) => VoteData.TryGetValue($"{appName}|{team}", out var vote) ? vote : null;
    // 检查玩家是否有未结束的投票申请
    public static bool HasPendingVote(string appName) => VoteData.Values.Any(v => v.AppName == appName && !v.IsEnd);
    // 检查队伍是否有未结束的投票
    public static bool HasTeamVote(int team) => VoteData.Values.Any(v => v.Team == team && !v.IsEnd);
    // 添加投票
    public static bool AddVote(TeamVote vote) => VoteData.TryAdd(vote.GetKey(), vote);
    // 移除投票
    public static void RemoveVote(string appName, int team) => VoteData.TryRemove($"{appName}|{team}", out _);
    #endregion

    #region 检查并处理超时投票
    public static void CheckVoteTime()
    {
        // 处理所有超时的投票
        var votes = VoteData.Values.Where(v => v.IsEnd).ToList();
        foreach (var vote in votes)
        {
            ProcessVoteEnd(vote);
        }
    }
    #endregion

    #region 投票操作(指令操作：/det y与n)
    public static void VoteAction(TSPlayer plr, bool isAgree)
    {
        // 查找玩家当前队伍的投票
        var vote = VoteData.Values.FirstOrDefault(v =>
            v.Team == plr.Team && !v.IsEnd && plr.Name != v.AppName);

        if (vote == null)
        {
            plr.SendMessage("当前没有可投票的申请", Color.Yellow);
            return;
        }

        // 检查是否已投票
        if (vote.Agree.Contains(plr.Name) || vote.Against.Contains(plr.Name))
        {
            ShowVoteStatus(plr, vote);
            return;
        }

        // 执行投票
        if (isAgree)
        {
            vote.Agree.Add(plr.Name);
            plr.SendMessage("已同意申请", Tool.color);
        }
        else
        {
            vote.Against.Add(plr.Name);
            plr.SendMessage("已拒绝申请", Tool.color);
        }
        // 检查投票是否达到全员投票
        var stats = vote.GetStats();
        int voted = stats.Agree + stats.Against;

        // 如果还没有达到全员投票，发送投票状态更新
        if (voted < stats.Total)
        {
            SendVoteMsg(vote);
        }

        CheckVoteResult(vote);
    }
    #endregion

    #region 显示投票状态
    public static void ShowVoteStatus(TSPlayer plr, TeamVote vote)
    {
        var stats = vote.GetStats();
        var teamName = Data.GetTeamName(vote.Team);

        // 检查玩家是否已投票
        string info = GetVoteInfo(plr.Name, vote);
        string msg = BuildMessage(vote, stats, teamName, info, true);

        // 显示已投票成员名单
        var (on, un) = GetVotedPlayers(vote, stats.TeamPlayers);
        if (on.Count > 0)
        {
            msg += $"已投票成员: {string.Join("、", on)}\n";
        }

        // 显示未投票成员
        if (un.Count > 0)
        {
            msg += $"未投票: [c/888888:{string.Join("、", un)}]";
        }

        plr.SendMessage(msg, Tool.color);
    }

    private static (List<string> handled, List<string> unhandled) GetVotedPlayers(TeamVote vote, List<TSPlayer> TeamPlr)
    {
        var handled = new List<string>();
        var unhandled = new List<string>();

        foreach (var p in TeamPlr)
        {
            if (vote.Agree.Contains(p.Name))
            {
                handled.Add($"[c/32CD32:{p.Name}]");
            }
            else if (vote.Against.Contains(p.Name))
            {
                handled.Add($"[c/FF4500:{p.Name}]");
            }
            else
            {
                unhandled.Add(p.Name);
            }
        }

        return (handled, unhandled);
    }

    private static string GetVoteInfo(string plrName, TeamVote vote)
    {
        if (vote.Agree.Contains(plrName))
            return "（您已投：同意）";
        if (vote.Against.Contains(plrName))
            return "（您已投：反对）";
        return "（您未投票）";
    }
    #endregion

    #region 构建投票消息
    private static string BuildMessage(TeamVote vote, VoteStats stats, string teamName, string info = "", bool Details = false)
    {
        string msg;
        if (Details)
        {
            msg = $"{teamName}投票 {info}\n" +
                  $"申请人: [c/508DC8:{vote.AppName}]\n" +
                  $"同意: [c/32CD32:{stats.Agree}/{stats.Total}] 反对: [c/FF4500:{stats.Against}/{stats.Total}]\n" +
                  $"同意率: [c/FFD700:{stats.AgreeRate:F1}%] 剩余时间: [c/00CED1:{vote.Remain}秒]\n";
        }
        else
        {
            msg = $"{teamName}投票: [c/508DC8:{vote.AppName}]申请加入\n" +
                  $"同意:[c/32CD32:{stats.Agree}/{stats.Total}], 拒绝:[c/FF4500:{stats.Against}/{stats.Total}]\n" +
                  $"同意率:[c/FFD700:{stats.AgreeRate:F1}%], 剩余:[c/00CED1:{vote.Remain}秒]\n" +
                  "使用 /det [c/5A9CDE:y] 同意, /det [c/F4636F:n] 拒绝, /det [c/5ADED3:v] 查看详情";

            int voted = stats.Agree + stats.Against;
            int remaining = stats.Total - voted;
            if (remaining > 0)
            {
                msg += $"还需投票: [c/FCF567:{remaining}]人";
            }
        }

        return msg;
    }
    #endregion

    #region 发送投票信息
    private static void SendVoteMsg(TeamVote vote)
    {
        var stats = vote.GetStats();
        var teamName = Data.GetTeamName(vote.Team);
        string msg = BuildMessage(vote, stats, teamName);

        // 发送给目标队伍成员
        foreach (var p in stats.TeamPlayers)
        {
            if (p != null && p.Active)
            {
                p.SendMessage(msg, Tool.color);
            }
        }
    }
    #endregion

    #region 检查投票结果
    private static void CheckVoteResult(TeamVote vote)
    {
        var stats = vote.GetStats();
        int voted = stats.Agree + stats.Against;

        // 如果已经投票的人数达到当前在线成员数，立即结束投票
        if (voted >= stats.Total && stats.Total > 0)
        {
            SendFinalResult(vote, stats);
            ProcessVoteEnd(vote);
        }
    }

    private static void SendFinalResult(TeamVote vote, VoteStats stats)
    {
        var teamName = Data.GetTeamName(vote.Team);
        string finalMsg = $"\n投票结束！{teamName}申请结果:\n" +
                          $"同意:[c/32CD32:{stats.Agree}/{stats.Total}] " +
                          $"({stats.AgreeRate:F1}%)\n";

        finalMsg += stats.AgreeRate > 50 ? "结果: [c/32CD32:通过]" : "结果: [c/FF4500:不通过]";

        // 发送给所有相关玩家
        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            if (p != null && p.Active &&
                (p.Name == vote.AppName || p.Team == vote.Team))
            {
                p.SendMessage(finalMsg, Tool.color);
            }
        }
    }
    #endregion

    #region 投票结果处理
    public static void ProcessVoteEnd(TeamVote vote)
    {
        var stats = vote.GetStats();

        // 超过50%同意
        if (stats.AgreeRate > 50)
        {
            var plr = TShock.Players.FirstOrDefault(p => p?.Name == vote.AppName);
            if (plr != null)
            {
                Data.ClearTeamData(plr.Team, plr.Name); // 从旧队伍移除玩家数据
                plr.SetTeam(vote.Team);
                var teamName = Data.GetTeamName(vote.Team);
                TSPlayer.All.SendMessage($"[c/508DC8:{plr.Name}] 已加入 {teamName}", Color.White);

                // 更新缓存
                var pdata = DeathEvent.Cache.GetData(plr.Name);
                pdata.SwitchTime = DateTime.Now;
                pdata.TeamCache = teamName;
                DeathEvent.Cache.Write();

                // 获取新队伍数据,并提示队伍死亡次数
                var newData = Data.GetTeamData(vote.Team);
                if (newData.Dead.Count > 0)
                    plr.SendMessage($"{teamName}已有{newData.Dead.Count}名队员死亡", Tool.color);

                // 显示新队伍补偿冷却信息s
                TimeSpan CoolTime = DateTime.UtcNow - newData.CoolDown;
                double CoolRemain = DeathEvent.Config.CoolDowned - CoolTime.TotalSeconds;
                if (CoolRemain > 0)
                    plr.SendMessage($"{teamName}补尝冷却剩余: [c/508DC8:{CoolRemain:f2}]秒", Tool.color);
            }
        }
        else
        {
            var plr = TShock.Players.FirstOrDefault(p => p?.Name == vote.AppName);
            plr?.SendMessage($"加入 {Data.GetTeamName(vote.Team)} 的申请被拒绝", Color.Red);
        }

        // 清理投票数据
        RemoveVote(vote.AppName, vote.Team);
    }
    #endregion

    #region 清理玩家投票数据
    public static void ClearVotes(string plrName)
    {
        // 1. 清理玩家作为申请人的投票
        var appKeys = VoteData.Where(kv => kv.Value.AppName == plrName)
                              .Select(kv => kv.Key)
                              .ToList();

        foreach (var key in appKeys)
        {
            VoteData.TryRemove(key, out _);
        }

        // 2. 清理玩家作为投票者的记录
        foreach (var vote in VoteData.Values)
        {
            vote.Agree.Remove(plrName);
            vote.Against.Remove(plrName);

            // 如果投票已经结束，立即处理结果
            if (vote.IsEnd)
            {
                ProcessVoteEnd(vote);
            }
        }
    }

    public static void ClearTeamVotes(string plrName, int teamId)
    {
        // 清理该玩家在指定队伍中的投票记录
        foreach (var vote in VoteData.Values)
        {
            if (vote.Team == teamId)
            {
                vote.Agree.Remove(plrName);
                vote.Against.Remove(plrName);

                if (vote.IsEnd)
                {
                    ProcessVoteEnd(vote);
                }
            }
        }
    }
    #endregion
}