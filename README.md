# DeathEvent 共同死亡事件插件

- 作者: Kimi、羽学
- 出处: TShock官方群816771079
- 这是一个Tshock服务器插件，主要用于：有一个玩家死亡使全部在线玩家全部执行死亡,并提供重生补偿。

## 更新日志

```
v1.0.1
将补偿事件放到玩家复活后执行
```

## 配置
> 配置文件位置：tshock/共同死亡事件.json
```json
{
  "插件开关": true,
  "插件广播": true,
  "增加生命": 30,
  "增加魔力": 10,
  "给物品": true,
  "物品表": {
    "74": 1
  },
  "执行命令": [
    "/buff 8 360"
  ],
  "免疫名单": [
    "羽学",
    "Kimi"
  ]
}
```
## 反馈
- 优先发issued -> 共同维护的插件库：https://github.com/UnrealMultiple/TShockPlugin
- 次优先：TShock官方群：816771079
- 大概率看不到但是也可以：国内社区trhub.cn ，bbstr.net , tr.monika.love