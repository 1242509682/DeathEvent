using Newtonsoft.Json;
using TShockAPI;

namespace DeathEvent;

internal class Configuration
{
    public static readonly string FilePath = Path.Combine(TShock.SavePath, "共同死亡事件.json");

    [JsonProperty("插件开关", Order = 0)]
    public bool Enabled { get; set; } = true;
    [JsonProperty("队伍模式", Order = 1)]
    public bool Team { get; set; } = true;
    [JsonProperty("队伍激励", Order = 2)]
    public bool Incentive { get; set; } = true;
    [JsonProperty("切换队伍冷却", Order = 3)]
    public int SwitchCD = 30;
    [JsonProperty("复活时间", Order = 4)]
    public int RespawnTimer = 3;
    [JsonProperty("补偿冷却", Order = 5)]
    public int CoolDowned { get; set; } = 180;
    [JsonProperty("补偿增加生命", Order = 6)]
    public int AddLifeAmount { get; set; } = 30;
    [JsonProperty("补偿增加魔力", Order = 7)]
    public int AddManaAmount { get; set; } = 10;
    [JsonProperty("同步最高生命者", Order = 8)]
    public bool SyncLifeMax { get; set; } = true;
    [JsonProperty("超出服务器上限", Order = 9)]
    public bool ExceMax { get; set; } = true;
    [JsonProperty("补偿物品", Order = 10)]
    public bool GiveItem { get; set; } = true;
    [JsonProperty("补偿物品表", Order = 11)]
    public Dictionary<int, int> ItemList { get; set; } = new Dictionary<int, int>();
    [JsonProperty("补偿执行命令", Order = 12)]
    public string[] DeathCommands { get; set; } = new string[0];
    [JsonProperty("免疫名单", Order = 13)]
    public List<string> WhiteList { get; set; } = new List<string>();
    [JsonProperty("个人死亡次数", Order = 14)]
    public Dictionary<string, int> DeathCount { get; set; } = new Dictionary<string, int>();
    [JsonProperty("队伍死亡次数", Order = 15)]
    public Dictionary<string, int> TeamDeathCount { get; set; } = new Dictionary<string, int>();
    [JsonProperty("玩家队伍缓存", Order = 16)]
    public Dictionary<string, string> BackTeam { get; set; } = new Dictionary<string, string>();

    #region 预设参数方法
    public void SetDefault()
    {
        WhiteList = new List<string>()
        {
            "羽学","Kimi",
        };

        ItemList = new Dictionary<int, int>()
        {
            { 74,1 },
        };

        DeathCommands = new string[]
        {
           "/buff 8 360",
        };
    }
    #endregion

    #region 读取与创建配置文件方法
    public void Write()
    {
        string json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(FilePath, json);
    }

    public static Configuration Read()
    {
        if (!File.Exists(FilePath))
        {
            var NewConfig = new Configuration();
            NewConfig.SetDefault();
            NewConfig.Write();
            return NewConfig;
        }
        else
        {
            string jsonContent = File.ReadAllText(FilePath);
            return JsonConvert.DeserializeObject<Configuration>(jsonContent)!;
        }
    }
    #endregion

    #region 统计死亡次数方法
    public int GetDeath(string name) => DeathCount.TryGetValue(name, out int count) ? count : 0;
    public void AddDeath(string name)
    {
        DeathCount.TryAdd(name, 0);
        DeathCount[name]++;
    }

    public int GetTeamDeath(string name) => TeamDeathCount.TryGetValue(name, out int count) ? count : 0;
    public void AddTeamDeath(string name)
    {
        TeamDeathCount.TryAdd(name, 0);
        TeamDeathCount[name]++;
    }
    #endregion
}