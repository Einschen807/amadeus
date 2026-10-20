using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KurisuCompanion
{
    // 本机体验指标：延迟采样（保留最近 200 个，算 P50/P90）与事件计数。
    // 只记录耗时和次数，不记录任何对话内容；测试与批处理模式不写盘，避免污染真实数据。
    internal static class Metrics
    {
        public const int Window = 200;
        private const float SaveInterval = 10f;

        [Serializable] private sealed class Series { public string name; public List<float> values = new List<float>(); }
        [Serializable] private sealed class Counter { public string name; public int value; }
        [Serializable] private sealed class Store
        {
            public int version = 1;
            public string since;
            public List<Series> samples = new List<Series>();
            public List<Counter> counters = new List<Counter>();
        }

        // 设置页与 README 展示用的中文名称；未列出的指标仍会记录。
        private static readonly (string key, string label)[] Labels =
        {
            ("llm.first_token_seconds", "模型首个字"),
            ("voice.caption_seconds", "首句字幕"),
            ("voice.first_audio_seconds", "首句出声"),
            ("voice.tts_segment_seconds", "单段合成"),
            ("router.classifier_seconds", "意图分类"),
        };

        private static readonly object gate = new object();
        private static Store store;
        private static string path;
        private static bool persist, dirty;
        private static float lastSave;

        public static void Sample(string name, float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0) return;
            lock (gate)
            {
                var series = Ensure().samples.Find(item => item.name == name);
                if (series == null) { series = new Series { name = name }; store.samples.Add(series); }
                series.values.Add((float)Math.Round(seconds, 3));
                if (series.values.Count > Window) series.values.RemoveRange(0, series.values.Count - Window);
                dirty = true;
            }
            SaveSoon();
        }

        public static void Count(string name, int amount = 1)
        {
            lock (gate)
            {
                var counter = Ensure().counters.Find(item => item.name == name);
                if (counter == null) { counter = new Counter { name = name }; store.counters.Add(counter); }
                counter.value += amount;
                dirty = true;
            }
            SaveSoon();
        }

        public static int CountOf(string name)
        {
            lock (gate) return Ensure().counters.Find(item => item.name == name)?.value ?? 0;
        }

        public static IReadOnlyList<float> Values(string name)
        {
            lock (gate) return Ensure().samples.Find(item => item.name == name)?.values.ToList() ?? new List<float>();
        }

        // 线性插值分位数，p 取 0–1。
        public static float Percentile(IReadOnlyList<float> values, float p)
        {
            if (values == null || values.Count == 0) return float.NaN;
            var sorted = values.OrderBy(v => v).ToList();
            float rank = Mathf.Clamp01(p) * (sorted.Count - 1);
            int low = (int)Math.Floor(rank), high = (int)Math.Ceiling(rank);
            return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
        }

        public static string Summary()
        {
            var text = new StringBuilder();
            lock (gate)
            {
                Ensure();
                foreach (var (key, label) in Labels)
                {
                    var values = store.samples.Find(item => item.name == key)?.values;
                    if (values == null || values.Count == 0) continue;
                    text.Append(label).Append("  P50 ").Append(Percentile(values, .5f).ToString("F2"))
                        .Append("s · P90 ").Append(Percentile(values, .9f).ToString("F2"))
                        .Append("s（n=").Append(values.Count).Append("）\n");
                }
                int accepted = Get("memory.accepted"), rejected = Get("memory.rejected_evidence");
                if (accepted + rejected > 0)
                    text.Append("记忆候选  采纳 ").Append(accepted).Append(" · 无原话依据被拒 ").Append(rejected).Append('\n');
                int turns = Get("chat.turns"), ooc = Get("persona.ooc_sentence_dropped") + Get("persona.ooc_reply");
                if (turns > 0)
                    text.Append("角色一致性  ").Append(turns).Append(" 轮中拦截 ").Append(ooc)
                        .Append(" 次 · 重试 ").Append(Get("persona.retry")).Append(" 次\n");
                int rule = Get("router.rule_computer") + Get("router.rule_codex"), ambiguous = Get("router.rule_ambiguous");
                if (rule + ambiguous > 0)
                    text.Append("任务路由  规则直判 ").Append(rule).Append(" · 交给分类 ").Append(ambiguous)
                        .Append(" · 分类超时回落 ").Append(Get("router.classifier_fallback")).Append('\n');
                if (text.Length == 0) return "还没有统计数据。正常对话几轮后再来看。";
                text.Append("统计自 ").Append(store.since);
            }
            return text.ToString();
        }

        public static void Flush()
        {
            lock (gate)
            {
                if (!persist || !dirty || store == null) return;
                try
                {
                    string temporary = path + ".tmp";
                    File.WriteAllText(temporary, JsonUtility.ToJson(store, true));
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    dirty = false;
                }
                catch (Exception e) { Debug.LogWarning("METRICS_SAVE_FAILED " + e.Message); }
            }
            lastSave = Time.realtimeSinceStartup;
        }

        // 供编辑器检查使用：清空内存中的数据且不写盘。
        internal static void ResetForTests()
        {
            lock (gate) { store = new Store { since = DateTime.Now.ToString("yyyy-MM-dd HH:mm") }; persist = false; dirty = false; }
        }

        private static int Get(string name) => store.counters.Find(item => item.name == name)?.value ?? 0;

        private static void SaveSoon()
        {
            if (persist && Time.realtimeSinceStartup - lastSave > SaveInterval) Flush();
        }

        private static Store Ensure()
        {
            if (store != null) return store;
            persist = !Application.isBatchMode;
            if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg.StartsWith("--smoke", StringComparison.Ordinal)))
                persist = false;
            path = Path.Combine(Application.persistentDataPath, "metrics.json");
            try { if (persist && File.Exists(path)) store = JsonUtility.FromJson<Store>(File.ReadAllText(path)); }
            catch (Exception e) { Debug.LogWarning("METRICS_LOAD_FAILED " + e.Message); }
            if (store == null || store.version != 1) store = new Store { since = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            if (store.samples == null) store.samples = new List<Series>();
            if (store.counters == null) store.counters = new List<Counter>();
            return store;
        }
    }
}
