using System.Collections.Concurrent;
using TShockAPI;
using static DeathEvent.DeathEvent;

namespace DeathEvent;

internal static class Data
{
    #region 数据结构
    public static ConcurrentDictionary<int, TeamDatas> MyData = new ConcurrentDictionary<int, TeamDatas>();
    public class TeamDatas
    {
        public HashSet<string> Dead = new HashSet<string>();
        public HashSet<string> Resp = new HashSet<string>();
        public string Cont = "";
        public string Exc = "";
        public DateTime CoolDown = DateTime.UtcNow;
    }
    #endregion

    #region 数据管理方法
    public static TeamDatas GetData(int teamId) => MyData.GetOrAdd(teamId, _ => new TeamDatas());
    public static TeamDatas GetData(TSPlayer plr) => GetData(Config.Team ? plr.Team : -1);
    public static HashSet<string> GetDead(TSPlayer plr) => GetData(plr).Dead;
    public static HashSet<string> GetResp(TSPlayer plr) => GetData(plr).Resp;
    public static string GetCont(TSPlayer plr) => GetData(plr).Cont;
    public static void SetCont(TSPlayer plr, string val) => GetData(plr).Cont = val;
    public static string GetExc(TSPlayer plr) => GetData(plr).Exc;
    public static void SetExc(TSPlayer plr, string val) => GetData(plr).Exc = val;
    public static DateTime GetCoolDown(TSPlayer plr) => GetData(plr).CoolDown;
    public static void SetCoolDown(TSPlayer plr) => GetData(plr).CoolDown = DateTime.UtcNow;
    public static void ClearData(TSPlayer plr)
    {
        int key = Config.Team ? plr.Team : -1;
        var data = GetData(key);

        // 清理队伍数据
        data.Dead.Clear();
        data.Resp.Clear();
        data.Cont = "";
        data.Exc = "";
    }

    private static readonly Dictionary<int, string> TeamNames = new()
    {
        { 0, "白队" }, { 1, "红队" }, { 2, "绿队" },
        { 3, "蓝队" }, { 4, "黄队" }, { 5, "粉队" }
    };
    public static string GetTeamName(int teamId) => TeamNames.TryGetValue(teamId, out var name) ? name : "未知队伍";
    public static int GetTeamId(string teamName) => TeamNames.FirstOrDefault(x => x.Value == teamName).Key;
    #endregion

    #region 队伍切换冷却管理
    public static Dictionary<string, DateTime> SwitchCD = new Dictionary<string, DateTime>();
    public static DateTime GetSwitch(string name) => SwitchCD[name];
    public static void SetSwitch(string name) => SwitchCD[name] = DateTime.UtcNow;
    public static bool CheckSwitch(string name)
    {
        if (!SwitchCD.ContainsKey(name))
            return false;

        TimeSpan timeSpan = DateTime.UtcNow - SwitchCD[name];

        // 如果冷却已过期，移除数据并返回false
        if (timeSpan.TotalSeconds >= Config.SwitchCD)
        {
            SwitchCD.Remove(name);
            return false;
        }

        return true; // 冷却中
    }
    #endregion
}
