using System.Collections.Concurrent;
using Newtonsoft.Json;
using TShockAPI;
using static DeathEvent.Configuration;
using static DeathEvent.DeathEvent;

namespace DeathEvent;

// 缓存数据类
public class CacheData
{

    public static readonly string CachePath = Path.Combine(Paths, "死亡数据缓存.json");
    [JsonProperty("队伍数据", Order = 0)]
    public ConcurrentDictionary<string, TeamCache> TeamData { get; set; } = new();
    [JsonProperty("玩家数据", Order = 1)]
    public Dictionary<string, PlayerCache> PlayerData { get; set; } = new();

    #region 玩家缓存数据
    public class PlayerCache
    {
        [JsonProperty("锁定队伍", Order = -1)]
        public bool Lock { get; set; } = false;
        [JsonProperty("死亡次数", Order = 0)]
        public int DeathCount { get; set; } = 0;
        [JsonProperty("队伍缓存", Order = 1)]
        public string? TeamName { get; set; } = null;
        [JsonProperty("切换队伍时间", Order = 2)]
        public DateTime? SwitchTime { get; set; } = null;
        [JsonProperty("重生补偿时间", Order = 3)]
        public DateTime CoolDown { get; set; } = DateTime.Now;
    }
    #endregion

    #region 队伍缓存数据
    public class TeamCache
    {
        [JsonProperty("死亡次数", Order = -1)]
        public int DeathCount { get; set; } = 0;
        [JsonProperty("已死亡玩家名单", Order = 0)]
        public HashSet<string> Dead { get; set; } = new(); // 已死亡玩家名单
        [JsonProperty("已复活玩家名单", Order = 1)]
        public HashSet<string> Resp { get; set; } = new(); // 已复活玩家名单
    }
    #endregion

    #region 获取数据方法
    public TeamCache GetTeamData(int teamId) => TeamData.GetOrAdd(GetTeamName(teamId), _ => new TeamCache());
    public TeamCache GetTeamData(TSPlayer plr) => GetTeamData(Config.Team ? plr.Team : -1);
    public PlayerCache GetPlayerData(string name)
    {
        if (!PlayerData.TryGetValue(name, out var data))
        {
            data = new PlayerCache();
            PlayerData[name] = data;
        }

        return data;
    }
    #endregion

    #region 检查切换队伍冷却
    public bool CheckSwitchCD(TSPlayer plr, PlayerCache data)
    {
        if (Config.WhiteList.Contains(plr.Name) ||
            plr.HasPermission(CMDs.Admin)) return false;

        if (!data.SwitchTime.HasValue) return false;

        TimeSpan timeSpan = DateTime.Now - data.SwitchTime.Value;

        if (timeSpan.TotalSeconds >= Config.SwitchCD) return false;

        return true;
    }
    #endregion

    #region 队伍名称映射
    public static string GetTeamName(int teamId) => TeamNameMap.TryGetValue(teamId, out var name) ? name : "全体";
    public static string GetTeamCName(int teamId) => TeamColorMap.TryGetValue(teamId, out var name) ? name : "全体";
    private static readonly Dictionary<int, string> TeamNameMap = new()
    {
        { -1, "全体" },{ 0, "白队" },{ 1, "红队" },{ 2, "绿队" },{ 3, "蓝队" },{ 4, "黄队" },{ 5, "粉队" }
    };

    private static readonly Dictionary<int, string> TeamColorMap = new()
    {
        { 0, "[c/5ADECE:白队]" },{ 1, "[c/F56470:红队]" },
        { 2, "[c/74E25C:绿队]" },{ 3, "[c/5A9DDE:蓝队]" },
        { 4, "[c/FCF466:黄队]" },{ 5, "[c/E15BC2:粉队]" }
    };
    #endregion

    #region 队伍数据管理
    public static int GetTeamId(string teamName) => TeamNameMap.FirstOrDefault(x => x.Value == teamName).Key;
    public void ClearTeamData(int teamId, bool clearAll, string? pName = null)
    {
        int Team = Config.Team ? teamId : -1;
        var name = GetTeamName(teamId);
        if (!TeamData.ContainsKey(name)) return;

        var data = TeamData[name];

        if (clearAll)
        {
            data.Dead.Clear();
            data.Resp.Clear();
        }
        else if (!string.IsNullOrEmpty(pName))
        {
            data.Dead.Remove(pName);
            data.Resp.Remove(pName);
        }
    }
    #endregion

    #region 读取与写入缓存
    public void Write()
    {
        string json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(CachePath, json);
    }

    public void Read()
    {
        if (!File.Exists(CachePath))
        {
            Write();
        }
        else
        {
            string jsonContent = File.ReadAllText(CachePath);
            var cache = JsonConvert.DeserializeObject<CacheData>(jsonContent)!;
            PlayerData = cache.PlayerData ?? new();
            TeamData = cache.TeamData ?? new();
        }
    }
    #endregion
}