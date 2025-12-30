using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using System.Text;
using System.Text.RegularExpressions;
using TShockAPI;

namespace DeathEvent;

internal class Tool
{
    #region 逐行渐变色方法
    public static void GradMess(TSPlayer plr, string mess)
    {
        // 处理空值或空字符串
        if (string.IsNullOrEmpty(mess))
            return;

        var lines = mess.Split('\n');
        var GradMess = new StringBuilder();
        var start = new Color(166, 213, 234);
        var end = new Color(245, 247, 175);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (!string.IsNullOrEmpty(line))
            {
                float ratio = (float)i / (lines.Length - 1);
                var gradColor = Color.Lerp(start, end, ratio);
                string colorHex = $"{gradColor.R:X2}{gradColor.G:X2}{gradColor.B:X2}";

                // 检查是否已有颜色标签
                if (line.Contains("[c/"))
                {
                    // 已有颜色标签，只处理图标
                    GradMess.AppendLine(ReplaceIconsOnly(line));
                }
                else
                {
                    // 没有颜色标签，处理渐变和图标
                    GradMess.AppendLine(ProcessLine(line, colorHex));
                }
            }
            else
            {
                // 空行保留
                GradMess.AppendLine();
            }
        }

        plr.SendMessage(GradMess.ToString(), 240, 250, 150);
    }

    // 处理单行渐变和图标
    private static string ProcessLine(string line, string colorHex)
    {
        var result = new StringBuilder();
        int index = 0;
        int length = line.Length;
        int charCount = 0;

        // 先计算需要渐变的总字符数（排除图标标签）
        for (int i = 0; i < length; i++)
        {
            if (line[i] == '[' && i + 1 < length && line[i + 1] == 'i')
            {
                // 跳过整个图标标签
                int end = line.IndexOf(']', i);
                if (end != -1)
                {
                    i = end;
                    continue;
                }
            }
            charCount++;
        }

        // 重置索引，开始处理
        for (int i = 0; i < length; i++)
        {
            char c = line[i];

            // 检查是否是图标标签 [i:xxx] 或 [i/s数量:xxx]
            if (c == '[' && i + 1 < length && line[i + 1] == 'i')
            {
                int end = line.IndexOf(']', i);
                if (end != -1)
                {
                    string tag = line.Substring(i, end - i + 1);

                    // 解析物品图标标签
                    if (TryParseItemTag(tag, out string iconTag))
                    {
                        result.Append(iconTag);
                    }
                    else
                    {
                        result.Append(tag); // 无效标签保留原样
                    }

                    i = end; // 跳过整个标签
                }
                else
                {
                    // 不完整的标签，按普通字符处理
                    result.Append($"[c/{colorHex}:{c}]");
                    charCount++;
                }
            }
            else
            {
                // 普通字符，使用渐变色
                result.Append($"[c/{colorHex}:{c}]");
                charCount++;
            }
        }

        return result.ToString();
    }
    #endregion

    #region 渐变着色方法 + 物品图标解析
    public static string TextGradient(string text)
    {
        // 处理空值或空字符串
        if (string.IsNullOrEmpty(text))
            return text;

        // 如果文本中已包含 [c/xxx:] 自定义颜色标签，则不做渐变，只替换图标
        if (text.Contains("[c/"))
        {
            return ReplaceIconsOnly(text);
        }

        var name = new StringBuilder();
        int length = text.Length;
        int gradientIndex = 0; // 渐变索引，排除换行符

        // 首先计算需要渐变的总字符数（排除换行符和图标标签）
        int gradientCharCount = 0;
        for (int i = 0; i < length; i++)
        {
            // 检查是否是图标标签 [i:xxx] 或 [i/s数量:xxx]
            if (text[i] == '[' && i + 2 < length && text[i + 1] == 'i')
            {
                // 跳过整个图标标签
                int end = text.IndexOf(']', i);
                if (end != -1)
                {
                    i = end;
                    continue;
                }
            }
            else if (text[i] != '\n' && text[i] != '\r')
            {
                gradientCharCount++;
            }
        }

        // 重置索引
        for (int i = 0; i < length; i++)
        {
            char c = text[i];

            // 处理换行符 - 直接保留
            if (c == '\n' || c == '\r')
            {
                name.Append(c);
                continue;
            }

            // 检查是否是图标标签 [i:xxx] 或 [i/s数量:xxx]
            if (c == '[' && i + 1 < length && text[i + 1] == 'i')
            {
                int end = text.IndexOf(']', i);
                if (end != -1)
                {
                    string tag = text.Substring(i, end - i + 1);

                    // 解析物品图标标签
                    if (TryParseItemTag(tag, out string iconTag))
                    {
                        name.Append(iconTag);
                    }
                    else
                    {
                        name.Append(tag); // 无效标签保留原样
                    }

                    i = end; // 跳过整个标签
                }
                else
                {
                    name.Append(c);
                    gradientIndex++;
                }
            }
            else
            {
                // 渐变颜色计算，排除换行符
                var start = new Color(166, 213, 234);
                var endColor = new Color(245, 247, 175);
                float ratio = gradientCharCount <= 1 ? 0.5f : (float)gradientIndex / (gradientCharCount - 1);
                var color = Color.Lerp(start, endColor, ratio);

                name.Append($"[c/{color.Hex3()}:{c}]");
                gradientIndex++;
            }
        }

        return name.ToString();
    }

    // 解析物品图标标签
    private static bool TryParseItemTag(string tag, out string result)
    {
        result = tag;

        // 匹配 [i:物品ID] 格式
        var match1 = Regex.Match(tag, @"^\[i:(\d+)\]$");
        if (match1.Success)
        {
            if (int.TryParse(match1.Groups[1].Value, out int itemID))
            {
                result = ItemIcon(itemID);
                return true;
            }
        }

        // 匹配 [i/s数量:物品ID] 格式
        var match2 = Regex.Match(tag, @"^\[i/s(\d+):(\d+)\]$");
        if (match2.Success)
        {
            if (int.TryParse(match2.Groups[2].Value, out int itemID))
            {
                int stack = int.Parse(match2.Groups[1].Value);
                result = ItemIconStack(itemID, stack);
                return true;
            }
        }

        return false;
    }
    #endregion

    #region 只替换图标，不做渐变
    public static string ReplaceIconsOnly(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new StringBuilder();
        int index = 0;
        int length = text.Length;

        while (index < length)
        {
            char c = text[index];

            // 检查是否是图标标签 [i:xxx] 或 [i/s数量:xxx]
            if (c == '[' && index + 1 < length && text[index + 1] == 'i')
            {
                int end = text.IndexOf(']', index);
                if (end != -1)
                {
                    string tag = text.Substring(index, end - index + 1);

                    if (TryParseItemTag(tag, out string iconTag))
                    {
                        result.Append(iconTag);
                    }
                    else
                    {
                        result.Append(tag);
                    }

                    index = end + 1;
                }
                else
                {
                    result.Append(c);
                    index++;
                }
            }
            else
            {
                result.Append(c);
                index++;
            }
        }

        return result.ToString();
    }
    #endregion

    #region 优化广播消息（处理换行符）
    public static string BroadcastGradient(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // 按行分割，每行单独处理渐变
        var lines = text.Split('\n');
        var result = new StringBuilder();

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];

            // 去除可能的回车符
            line = line.TrimEnd('\r');

            if (!string.IsNullOrEmpty(line))
            {
                // 对每一行进行渐变处理
                result.Append(TextGradient(line));
            }

            // 添加换行符（除了最后一行）
            if (lineIndex < lines.Length - 1)
            {
                result.Append('\n');
            }
        }

        return result.ToString();
    }
    #endregion

    #region 返回物品图标方法
    // 方法：ItemIcon，根据给定的物品对象返回插入物品图标的格式化字符串
    public static string ItemIcon(Item item)
    {
        return ItemIcon(item.type);
    }

    // 方法：ItemIcon，根据给定的物品ID返回插入物品图标的格式化字符串
    public static string ItemIcon(ItemID itemID)
    {
        return ItemIcon(itemID);
    }

    // 方法：ItemIcon，根据给定的物品整型ID返回插入物品图标的格式化字符串
    public static string ItemIcon(int itemID)
    {
        return $"[i:{itemID}]";
    }

    // 方法：ItemIconStack，返回带数量的物品图标
    public static string ItemIconStack(int itemID, int stack)
    {
        return $"[i/s{stack}:{itemID}]";
    }
    #endregion
}