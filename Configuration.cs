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
    [JsonProperty("共同死亡复活时间", Order = 2)]
    public int RespawnTimer = 5;
    [JsonProperty("补偿广播", Order = 3)]
    public bool Broadcast { get; set; } = true;
    [JsonProperty("补偿冷却", Order = 4)]
    public int CoolDowned { get; set; } = 60;
    [JsonProperty("增加生命", Order = 5)]
    public int AddLifeAmount { get; set; } = 30;
    [JsonProperty("增加魔力", Order = 6)]
    public int AddManaAmount { get; set; } = 10;
    [JsonProperty("同步最高生命者", Order = 7)]
    public bool SyncLifeMax { get; set; } = true;
    [JsonProperty("超出服务器上限", Order = 8)]
    public bool ExceMax { get; set; } = true;
    [JsonProperty("给物品", Order = 9)]
    public bool GiveItem { get; set; } = true;
    [JsonProperty("物品表", Order = 10)]
    public Dictionary<int, int> ItemList { get; set; } = new Dictionary<int, int>();
    [JsonProperty("执行命令", Order = 11)]
    public string[] DeathCommands { get; set; } = new string[0];
    [JsonProperty("免疫名单", Order = 12)]
    public List<string> WhiteList { get; set; } = new List<string>();

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
}