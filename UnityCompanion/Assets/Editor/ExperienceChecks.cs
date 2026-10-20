using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using KurisuCompanion;
using UnityEngine;

public static class ExperienceChecks
{
    public static void Run()
    {
        Assembly runtime = typeof(KurisuApp).Assembly;
        Type sessionType = runtime.GetType("KurisuCompanion.CompanionSession", true);
        Type entryType = runtime.GetType("KurisuCompanion.MemoryEntry", true);
        Type engine = runtime.GetType("KurisuCompanion.MemoryEngine", true);
        object session = Activator.CreateInstance(sessionType);
        IList memories = (IList)sessionType.GetField("memories").GetValue(session);
        DateTime now = DateTime.UtcNow;
        object low = MakeEntry(entryType, "记得一次游戏对话的详细情况", "记得一次游戏对话", "游戏对话", 40, now.AddDays(-20));
        object high = MakeEntry(entryType, "用户长期喜欢脑科学", "用户喜欢脑科学", "脑科学", 90, now.AddYears(-10));
        memories.Add(low); memories.Add(high);
        MethodInfo decay = engine.GetMethod("ApplyDecay", BindingFlags.Public | BindingFlags.Static);
        decay.Invoke(null, new object[] { session, now });
        Check((int)entryType.GetField("stage").GetValue(low) == 1 &&
              (string)entryType.GetField("text").GetValue(low) == "记得一次游戏对话", "first compression");
        decay.Invoke(null, new object[] { session, now.AddDays(20) });
        Check((int)entryType.GetField("stage").GetValue(low) == 2 &&
              (string)entryType.GetField("text").GetValue(low) == "游戏对话", "second compression");
        decay.Invoke(null, new object[] { session, now.AddDays(60) });
        Check(memories.Count == 1 && ReferenceEquals(memories[0], high), "low memory forgotten, high retained");

        Type suggestionType = runtime.GetType("KurisuCompanion.MemorySuggestion", true);
        Type setType = runtime.GetType("KurisuCompanion.MemorySuggestions", true);
        object candidate = Activator.CreateInstance(suggestionType);
        suggestionType.GetField("text").SetValue(candidate, "用户喜欢红茶");
        suggestionType.GetField("summary").SetValue(candidate, "喜欢红茶");
        suggestionType.GetField("gist").SetValue(candidate, "红茶");
        suggestionType.GetField("evidence").SetValue(candidate, "喜欢红茶");
        suggestionType.GetField("importance").SetValue(candidate, 65);
        object suggestions = Activator.CreateInstance(setType);
        Array items = Array.CreateInstance(suggestionType, 1);
        items.SetValue(candidate, 0);
        setType.GetField("items").SetValue(suggestions, items);
        MethodInfo merge = engine.GetMethod("Merge", BindingFlags.Public | BindingFlags.Static);
        Check((int)merge.Invoke(null, new object[] { session, suggestions, "我喜欢红茶。", now }) == 1 &&
              memories.Count == 2, "memory extracted from quoted user evidence");
        Check((int)merge.Invoke(null, new object[] { session, suggestions, "我喜欢咖啡。", now }) == 0 &&
              memories.Count == 2, "unsubstantiated memory rejected");

        Type capture = runtime.GetType("KurisuCompanion.ScreenCaptureService", true);
        string image = (string)capture.GetMethod("CapturePrimaryScreen", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        Check(image.StartsWith("data:image/jpeg;base64,") && image.Length > 30000, "Windows screen image payload");
        Debug.Log("KURISU_EXPERIENCE_CHECKS_OK");
    }

    public static void RunLiveMemory()
    {
        Assembly runtime = typeof(KurisuApp).Assembly;
        string key = ReadKey(runtime);
        if (string.IsNullOrWhiteSpace(key)) { Debug.Log("KURISU_MEMORY_LIVE_SKIPPED_NO_KEY"); return; }
        Type sessionType = runtime.GetType("KurisuCompanion.CompanionSession", true);
        Type suggestionType = runtime.GetType("KurisuCompanion.MemorySuggestions", true);
        Type client = runtime.GetType("KurisuCompanion.MemoryClient", true);
        object session = Activator.CreateInstance(sessionType);
        object suggestions = null;
        string error = null;
        Type callbackType = typeof(Action<>).MakeGenericType(suggestionType);
        Delegate success = Delegate.CreateDelegate(callbackType,
            new MemoryCallback(value => suggestions = value), typeof(MemoryCallback).GetMethod("Accept"));
        IEnumerator request = (IEnumerator)client.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { key, session, "我长期喜欢脑科学，尤其对记忆研究感兴趣。", success,
                (Action<string>)(value => error = value) });
        Pump(request);
        Check(error == null && suggestions != null, "live memory extraction: " + error);
        Array items = (Array)suggestionType.GetField("items").GetValue(suggestions);
        Check(items != null && items.Length > 0, "stable preference extracted");
        Type engine = runtime.GetType("KurisuCompanion.MemoryEngine", true);
        int merged = (int)engine.GetMethod("Merge", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { session, suggestions,
                "我长期喜欢脑科学，尤其对记忆研究感兴趣。", DateTime.UtcNow });
        Check(merged > 0, "live memory accepted with direct user evidence");
        Debug.Log("KURISU_MEMORY_LIVE_OK");
    }

    public static void RunLiveVision()
    {
        Assembly runtime = typeof(KurisuApp).Assembly;
        string key = ReadKey(runtime);
        if (string.IsNullOrWhiteSpace(key)) { Debug.Log("KURISU_VISION_LIVE_SKIPPED_NO_KEY"); return; }
        Type client = runtime.GetType("KurisuCompanion.AgentClient", true);
        Type chatTurn = runtime.GetType("KurisuCompanion.ChatTurn", true);
        object history = Activator.CreateInstance(typeof(List<>).MakeGenericType(chatTurn));
        bool captured = false;
        string answer = null, error = null;
        Func<string, string, Action<bool>, IEnumerator> approval = (title, details, decide) => Reject(decide);
        IEnumerator request = (IEnumerator)client.GetMethod("Send", BindingFlags.Public | BindingFlags.Static).Invoke(null,
            new object[] { key, "你是牧濑红莉栖。用中文回答。", "", history,
                "你看看我的电脑屏幕里面有什么，简短描述。", null, approval,
                (Action<string>)(value => { if (value != null && value.Contains("capture_screen")) captured = true; }),
                (Action<string>)(value => answer = value), (Action<string>)(value => error = value), null, null, null });
        Pump(request);
        Check(error == null && captured && !string.IsNullOrWhiteSpace(answer), "live screenshot model loop: " + error);
        Debug.Log("KURISU_VISION_LIVE_OK");
    }

    private static object MakeEntry(Type type, string text, string summary, string gist, int importance, DateTime seen)
    {
        object entry = Activator.CreateInstance(type);
        type.GetField("id").SetValue(entry, Guid.NewGuid().ToString("N"));
        type.GetField("text").SetValue(entry, text);
        type.GetField("summary").SetValue(entry, summary);
        type.GetField("gist").SetValue(entry, gist);
        type.GetField("importance").SetValue(entry, importance);
        type.GetField("lastSeenUtc").SetValue(entry, seen.ToString("o"));
        return entry;
    }

    private static string ReadKey(Assembly runtime)
    {
        return (string)runtime.GetType("KurisuCompanion.KeyVault", true)
            .GetMethod("Read", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
    }

    private sealed class MemoryCallback
    {
        private readonly Action<object> onValue;
        public MemoryCallback(Action<object> onValue) { this.onValue = onValue; }
        public void Accept(object value) { onValue(value); }
    }

    private static IEnumerator Reject(Action<bool> decide) { decide(false); yield break; }

    private static void Pump(IEnumerator iterator)
    {
        while (iterator.MoveNext())
        {
            if (iterator.Current is IEnumerator nested) Pump(nested);
            else if (iterator.Current is AsyncOperation operation)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(120);
                while (!operation.isDone)
                {
                    if (DateTime.UtcNow > deadline) throw new Exception("network timeout");
                    Thread.Sleep(20);
                }
            }
            else Thread.Sleep(20);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Experience check failed: " + name);
    }
}
