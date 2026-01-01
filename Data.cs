using TShockAPI;
using static DeathEvent.DeathEvent;
using System.Collections.Concurrent;

namespace DeathEvent;

internal static class Data
{
    #region 队伍数据结构
    public static ConcurrentDictionary<int, TeamDatas> TeamData = new ConcurrentDictionary<int, TeamDatas>();
    public class TeamDatas
    {
        public HashSet<string> Dead = new(); // 已死亡玩家名单
        public HashSet<string> Resp = new(); // 已复活玩家名单
        public string Cont = "";  // 补偿信息内容
        public string Exc = ""; // 超出服务器上限信息内容
        public DateTime CoolDown = DateTime.UtcNow; // 补偿冷却时间
    }
    #endregion

    #region 队伍数据管理方法
    public static TeamDatas GetTeamData(int teamId) => TeamData.GetOrAdd(teamId, _ => new TeamDatas());
    public static TeamDatas GetTeamData(TSPlayer plr) => GetTeamData(Config.Team ? plr.Team : -1);
    public static void ClearTeamData(int teamId, bool clearAll) => ClearTeamData(teamId, clearAll, null);
    public static void ClearTeamData(int teamId, string pName) => ClearTeamData(teamId, false, pName);
    public static void ClearTeamData(int teamId, bool clearAll = false, string? pName = null)
    {
        int Team = Config.Team ? teamId : -1;

        if (!TeamData.ContainsKey(Team)) return;

        var data = TeamData[Team];

        if (clearAll)
        {
            // 清理整个队伍数据
            data.Dead.Clear();
            data.Resp.Clear();
            data.Cont = "";
            data.Exc = "";
        }
        else if (!string.IsNullOrEmpty(pName))
        {
            // 只清理指定玩家数据
            data.Dead.Remove(pName);
            data.Resp.Remove(pName);
        }
    }

    private static readonly Dictionary<int, string> TeamNames = new()
    {
        { -1, "全体" },{ 0, "白队" },{ 1, "红队" },{ 2, "绿队" },{ 3, "蓝队" },{ 4, "黄队" },{ 5, "粉队" }
    };
    public static string GetTeamName(int teamId) => TeamNames.TryGetValue(teamId, out var name) ? name : "全体";
    public static int GetTeamId(string teamName) => TeamNames.FirstOrDefault(x => x.Value == teamName).Key;
    #endregion
}
