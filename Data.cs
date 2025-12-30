using TShockAPI;
using static DeathEvent.DeathEvent;

namespace DeathEvent;

internal static class Data
{
    #region 数据结构
    public static Dictionary<int, TeamDatas> MyData = new Dictionary<int, TeamDatas>();
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
    public static TeamDatas GetData(int teamId)
    {
        if (!MyData.ContainsKey(teamId))
            MyData[teamId] = new TeamDatas();
        return MyData[teamId];
    }
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
        if (MyData.ContainsKey(key))
        {
            var data = MyData[key];
            data.Cont = "";
            data.Exc = "";
        }
    }

    public static string GetTeamName(int teamId)
    {
        return teamId switch
        {
            0 => "[c/54D1C2:白队]",
            1 => "[c/F4626F:红队]",
            2 => "[c/FCD665:绿队]",
            3 => "[c/599CDE:蓝队]",
            4 => "[c/D8F161:黄队]",
            5 => "[c/E25BC0:粉队]",
            _ => "未知队伍"
        };
    }
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
