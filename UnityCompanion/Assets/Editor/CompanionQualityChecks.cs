using System;
using System.Collections.Generic;
using System.Linq;
using KurisuCompanion;
using UnityEngine;

// 离线检查：记忆检索、三级路由、角色一致性、双语流逐句过滤、指标统计。不访问网络、不读写用户数据。
// 运行：Unity -batchmode -quit -projectPath UnityCompanion -executeMethod CompanionQualityChecks.Run
public static class CompanionQualityChecks
{
    public static void Run()
    {
        Metrics.ResetForTests();
        MemoryRetrievalCases();
        RoutingCases();
        PersonaCases();
        SafetyCases();
        MetricsCases();
        Debug.Log("KURISU_QUALITY_CHECKS_OK");
    }

    private static void MemoryRetrievalCases()
    {
        DateTime now = DateTime.UtcNow;
        var memories = new List<MemoryEntry>
        {
            Entry("用户希望被称为阿宇", 90, now.AddDays(-200)),
            Entry("用户喜欢喝红茶，不加糖", 60, now.AddDays(-3)),
            Entry("用户养了一只叫小黑的猫", 55, now.AddDays(-40)),
            Entry("用户在准备考研数学", 65, now.AddDays(-10)),
        };
        for (int i = 0; i < 6; i++) memories.Add(Entry("用户关注第" + i + "支股票的市场行情", 75, now.AddDays(-1)));

        var cat = MemoryRetrieval.Select(memories, "我家猫今天又把杯子打翻了", now, 3).Select(m => m.text).ToList();
        Check(cat.Contains("用户养了一只叫小黑的猫"), "topic-relevant memory beats higher-importance noise: " + string.Join(" / ", cat));
        Check(cat.Contains("用户希望被称为阿宇"), "core memory keeps its slot");
        var study = MemoryRetrieval.Select(memories, "数学真题好难，考研还来得及吗", now, 3).Select(m => m.text).ToList();
        Check(study[0] == "用户在准备考研数学", "most relevant memory ranked first: " + string.Join(" / ", study));
        var idle = MemoryRetrieval.Select(memories, null, now, 3).Select(m => m.text).ToList();
        Check(idle.Contains("用户希望被称为阿宇") && idle.Count == 3, "without query falls back to importance and recency");

        var corpus = memories.Select(m => m.text).ToList();
        float rare = MemoryRetrieval.Relevance("猫", "用户养了一只叫小黑的猫", corpus);
        float common = MemoryRetrieval.Relevance("关", "用户关注第1支股票的市场行情", corpus);
        Check(rare > common, "IDF favours rare terms (" + rare.ToString("F2") + " > " + common.ToString("F2") + ")");
        Check(MemoryRetrieval.Relevance("今天天气不错", "用户在准备考研数学", corpus) == 0, "unrelated text scores zero");

        var session = new CompanionSession { memories = memories };
        string context = session.Context("小黑最近不爱吃饭");
        Check(context.Contains("小黑的猫") && context.Contains("[自动记忆]"), "session context uses query retrieval");
    }

    private static void RoutingCases()
    {
        Check(AgentRouter.Decide("帮我打开桌面上的论文文件") == RouteDecision.Computer, "explicit computer request");
        Check(AgentRouter.Decide("帮我写一个小游戏并运行验证") == RouteDecision.Codex, "explicit codex request");
        Check(AgentRouter.Decide("今天过得怎么样？") == RouteDecision.Chat, "plain chat");
        Check(AgentRouter.Decide("我刚刚打开了一个文件") == RouteDecision.Chat, "narrative stays chat");
        Check(AgentRouter.Decide("为什么这个网页一直白屏，你看看") == RouteDecision.Ambiguous, "question word with target goes to classifier");
        Check(AgentRouter.Decide("那个论文的结论靠谱吗") == RouteDecision.Ambiguous, "target without verb goes to classifier");
        Check(IntentClassifier.Parse(Response("{\\\"intent\\\":\\\"computer\\\"}")) == RouteDecision.Computer, "classifier computer");
        Check(IntentClassifier.Parse(Response("{\\\"intent\\\":\\\"codex\\\"}")) == RouteDecision.Codex, "classifier codex");
        Check(IntentClassifier.Parse(Response("{\\\"intent\\\":\\\"maybe\\\"}")) == RouteDecision.Chat, "unknown intent falls back to chat");
        Check(IntentClassifier.Parse("not json") == RouteDecision.Chat, "broken response falls back to chat");
    }

    private static void PersonaCases()
    {
        Check(RoleGuard.IsOutOfCharacter("作为一个 AI 助手，我无法拥有情感。"), "whitespace variant detected");
        Check(RoleGuard.IsOutOfCharacter("我是一个大语言模型，没有个人经历。"), "language-model self introduction");
        Check(RoleGuard.IsOutOfCharacter("我是由某公司开发的智能助手。"), "vendor self introduction");
        Check(!RoleGuard.IsOutOfCharacter("这是一个 AI 程序，但我可以帮你把问题理清楚。"), "honest identity still allowed");
        Check(!RoleGuard.IsOutOfCharacter("等等，你这说法有个问题。"), "ordinary in-character line");

        var played = new List<BilingualSentence>();
        var buffer = new BilingualBuffer(played.Add);
        buffer.Push("{\"sentences\":[{\"ja\":\"そうね。\",\"zh\":\"作为一个AI助手，我会尽力帮你。\"},{\"ja\":\"続けて。\",\"zh\":\"你继续说。\"}]}");
        buffer.Finish();
        Check(buffer.Error == null && buffer.Count == 1 && buffer.Dropped == 1 && buffer.Chinese == "你继续说。", "out-of-character sentence dropped, rest kept");
        Check(played.Count == 2 && played[0].Invalid && !played[1].Invalid, "dropped sentence is never played");
    }

    private static void SafetyCases()
    {
        foreach (var (text, expected) in EvalCases.Safety)
            Check(SafetyGuard.Assess(text) == expected, "risk level for \"" + text + "\" should be " + expected + ", got " + SafetyGuard.Assess(text));
        Check(SafetyGuard.Instruction(RiskLevel.Crisis).Contains(SafetyGuard.Hotline), "crisis instruction names the hotline");
        Check(SafetyGuard.Instruction(RiskLevel.None) == "", "no instruction without risk");
        Check(SafetyGuard.MentionsHelp(SafetyGuard.HelpLine().Chinese), "fallback help line is recognised as help");
    }

    private static void MetricsCases()
    {
        var values = new List<float> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Check(Math.Abs(Metrics.Percentile(values, .5f) - 5.5f) < 1e-4, "median interpolates");
        Check(Math.Abs(Metrics.Percentile(values, .9f) - 9.1f) < 1e-4, "p90 interpolates");
        Check(float.IsNaN(Metrics.Percentile(new List<float>(), .5f)), "empty percentile is NaN");
        for (int i = 0; i < Metrics.Window + 50; i++) Metrics.Sample("voice.caption_seconds", i / 100f);
        Check(Metrics.Values("voice.caption_seconds").Count == Metrics.Window, "rolling window bounded");
        Metrics.Count("chat.turns", 10);
        Metrics.Count("persona.ooc_sentence_dropped");
        string summary = Metrics.Summary();
        Check(summary.Contains("首句字幕") && summary.Contains("P90") && summary.Contains("10 轮中拦截"), "summary text: " + summary);
        Metrics.ResetForTests();
    }

    private static MemoryEntry Entry(string text, int importance, DateTime seen) => new MemoryEntry
    {
        id = Guid.NewGuid().ToString("N"), text = text, importance = importance, mentions = 1,
        lastSeenUtc = seen.ToUniversalTime().ToString("o")
    };

    private static string Response(string escapedContent) =>
        "{\"choices\":[{\"message\":{\"content\":\"" + escapedContent + "\"}}]}";

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Quality check failed: " + name);
    }
}
