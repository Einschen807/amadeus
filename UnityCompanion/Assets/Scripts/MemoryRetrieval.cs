using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KurisuCompanion
{
    // 按"与本轮话题的相关度 + 重要度 + 时效"选出进入提示词的记忆。
    // 相关度用 BM25：词项为中文单字与相邻二字，IDF 直接在用户自己的记忆库内统计——
    // "猫""考研"这类只出现在少数记忆里的字词权重高，"喜欢"这类到处都有的字词权重低。
    // 不依赖向量接口：零额外请求、零延迟，几十到几百条记忆的规模下足够用。
    internal static class MemoryRetrieval
    {
        public const float RelevanceWeight = .55f, ImportanceWeight = .30f, RecencyWeight = .15f;
        public const int CoreImportance = 80, CoreSlots = 8;
        private const double RecencyDays = 30, K1 = 1.2, B = .75, BigramBoost = 1.5, Saturation = 3.0;
        private const string StopChars = "的了是在我你他她它们吗呢吧啊呀哦嗯和与及就都也还很这那个有没不要会对说把被让给";

        private sealed class Doc
        {
            public MemoryEntry Memory;
            public Dictionary<string, int> Terms;
            public int Length;
            public float Score;
        }

        public static List<MemoryEntry> Select(IEnumerable<MemoryEntry> memories, string query, DateTime utcNow, int limit)
        {
            var docs = memories.Where(m => m != null && !string.IsNullOrWhiteSpace(m.text))
                .Select(m => { var terms = Terms(m.text); return new Doc { Memory = m, Terms = terms, Length = terms.Values.Sum() }; })
                .ToList();
            var relevance = Relevances(docs, Terms(query));
            foreach (var doc in docs) doc.Score = Combine(relevance[doc], doc.Memory, utcNow);
            var ranked = docs.OrderByDescending(d => d.Score).ThenByDescending(d => d.Memory.mentions).ToList();
            // 明确且长期重要的事实（如称呼）始终保留少量名额，避免被一时的话题挤掉。
            var chosen = ranked.Where(d => d.Memory.importance >= CoreImportance).Take(Math.Min(CoreSlots, limit)).ToList();
            foreach (var doc in ranked)
            {
                if (chosen.Count >= limit) break;
                if (!chosen.Contains(doc)) chosen.Add(doc);
            }
            return chosen.OrderByDescending(d => d.Score).Select(d => d.Memory).ToList();
        }

        // 单条记忆相对一个查询的相关度（0–1），供检查与调试使用。
        public static float Relevance(string query, string memory, IEnumerable<string> corpus = null)
        {
            var docs = (corpus ?? new[] { memory }).Append(memory)
                .Select(text => { var terms = Terms(text); return new Doc { Terms = terms, Length = terms.Values.Sum() }; }).ToList();
            return Relevances(docs, Terms(query))[docs[docs.Count - 1]];
        }

        private static Dictionary<Doc, float> Relevances(List<Doc> docs, Dictionary<string, int> query)
        {
            var result = docs.ToDictionary(d => d, d => 0f);
            if (docs.Count == 0 || query.Count == 0) return result;
            double averageLength = Math.Max(1, docs.Average(d => d.Length));
            foreach (string term in query.Keys)
            {
                int df = docs.Count(d => d.Terms.ContainsKey(term));
                if (df == 0) continue;
                double idf = Math.Log(1 + (docs.Count - df + .5) / (df + .5));
                double boost = term.Length >= 2 ? BigramBoost : 1;
                foreach (var doc in docs)
                {
                    if (!doc.Terms.TryGetValue(term, out int tf)) continue;
                    double norm = tf + K1 * (1 - B + B * doc.Length / averageLength);
                    result[doc] += (float)(boost * idf * tf * (K1 + 1) / norm);
                }
            }
            // 一个罕见二字词命中约为 3 分，即视为强相关；把原始分压到 0–1。
            foreach (var doc in docs) result[doc] = (float)Math.Min(1, result[doc] / Saturation);
            return result;
        }

        private static float Combine(float relevance, MemoryEntry memory, DateTime utcNow)
        {
            float importance = Math.Max(0, Math.Min(100, memory.importance)) / 100f;
            float recency = 0;
            if (DateTime.TryParse(memory.lastSeenUtc, null, DateTimeStyles.RoundtripKind, out DateTime seen))
                recency = (float)Math.Exp(-Math.Max(0, (utcNow - seen.ToUniversalTime()).TotalDays) / RecencyDays);
            return RelevanceWeight * relevance + ImportanceWeight * importance + RecencyWeight * recency;
        }

        // 词项：非停用字的中文单字、相邻二字，以及英文/数字单词。"用户"出现在几乎每条记忆里，先去掉。
        public static Dictionary<string, int> Terms(string text)
        {
            var terms = new Dictionary<string, int>();
            if (string.IsNullOrWhiteSpace(text)) return terms;
            text = text.ToLowerInvariant().Replace("用户", " ");
            var word = new StringBuilder();
            char previous = '\0';
            foreach (char c in text + " ")
            {
                if (IsLatin(c)) { word.Append(c); previous = '\0'; continue; }
                if (word.Length >= 2) Add(terms, word.ToString());
                word.Clear();
                if (IsCjk(c) && StopChars.IndexOf(c) < 0)
                {
                    Add(terms, c.ToString());
                    if (previous != '\0') Add(terms, new string(new[] { previous, c }));
                    previous = c;
                }
                else previous = '\0';
            }
            return terms;
        }

        private static void Add(Dictionary<string, int> terms, string term) =>
            terms[term] = terms.TryGetValue(term, out int count) ? count + 1 : 1;

        private static bool IsCjk(char c) => c >= '一' && c <= '鿿' || c >= '぀' && c <= 'ヿ';
        private static bool IsLatin(char c) => c >= 'a' && c <= 'z' || c >= '0' && c <= '9';
    }
}
