using static DeathEvent.Configuration;
using static DeathEvent.DeathEvent;
using Newtonsoft.Json;

namespace DeathEvent;

#region 玩家数据管理方法
public class PlayerDatas
{
    [JsonProperty("死亡次数", Order = 0)]
    public int DeathCount { get; set; } = 0;
    [JsonProperty("队伍缓存", Order = 1)]
    public string? TeamCache { get; set; } = null;
    [JsonProperty("切换队伍时间", Order = 2)]
    public DateTime? SwitchTime { get; set; } = null;
}
#endregion

// 缓存数据类
public class CacheData
{
    [JsonProperty("玩家数据")]
    public Dictionary<string, PlayerDatas> PlayerData { get; set; } = new();
    [JsonProperty("队伍死亡次数")]
    public Dictionary<string, int> TeamDeathCount { get; set; } = new();

    public PlayerDatas GetData(string name)
    {
        if (!Config.DeathCache.PlayerData.TryGetValue(name, out var data))
        {
            data = new PlayerDatas();
            Config.DeathCache.PlayerData[name] = data;
        }
        return data;
    }

    public int GetTeamDeath(string name) => Config.DeathCache.TeamDeathCount.TryGetValue(name, out int count) ? count : 0;

    #region 检查切换队伍冷却
    public bool CheckSwitchCD(string name, PlayerDatas data)
    {
        // 如果在白名单中，不检查冷却
        if (Config.WhiteList.Contains(name)) return false;

        if (!data.SwitchTime.HasValue) return false;

        TimeSpan timeSpan = DateTime.Now - data.SwitchTime.Value;

        // 如果冷却已过期，移除冷却时间并返回false
        if (timeSpan.TotalSeconds >= Config.SwitchCD)
        {
            data.SwitchTime = null;
            WriteCache(); // 只写入缓存
            return false;
        }

        return true; // 默认返回true，表示仍在冷却中
    }
    #endregion

    #region 读取与写入缓存
    public void WriteCache() => Config.DeathCache.Write();
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
            TeamDeathCount = cache.TeamDeathCount ?? new();
        }
    }
    #endregion
}
