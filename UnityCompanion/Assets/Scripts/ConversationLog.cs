using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KurisuCompanion
{
    // 一轮对话落地后的全部状态变化：模型上下文窗口、持久化记录、待提取的长期记忆。
    // 从 KurisuApp 中拆出，界面层只需要调用 Record 与 ProcessMemoryQueue。
    internal sealed class ConversationLog
    {
        public const int HistoryMessages = 20, SavedTurnLimit = 500, PendingMemoryLimit = 30, MemorySourceChars = 2000;
        private bool memoryBusy;

        public CompanionSession Session { get; }
        // 发送给模型的最近十轮问答；同一个列表实例也交给陪伴模式使用。
        public List<ChatTurn> History { get; } = new List<ChatTurn>();
        public bool HasPendingMemory => Session.pendingMemory.Count > 0;

        public ConversationLog(CompanionSession session)
        {
            Session = session;
            for (int i = Math.Max(0, session.turns.Count - HistoryMessages); i < session.turns.Count; i++)
            {
                var turn = session.turns[i];
                if (turn.role == "user" || turn.role == "assistant") History.Add(new ChatTurn(turn.role, turn.text));
            }
        }

        public string Context(string query = null) => Session.Context(query);

        // memorySource 为空时不进入记忆提取（例如固定的停止回应）。保存失败时抛出，由调用方提示用户。
        public void Record(string userText, string assistantText, string memorySource)
        {
            History.Add(new ChatTurn("user", userText));
            History.Add(new ChatTurn("assistant", assistantText));
            while (History.Count > HistoryMessages) History.RemoveRange(0, 2);
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Session.turns.Add(new SavedTurn { role = "user", text = userText, time = now });
            Session.turns.Add(new SavedTurn { role = "assistant", text = assistantText, time = now });
            if (Session.turns.Count > SavedTurnLimit) Session.turns.RemoveRange(0, Session.turns.Count - SavedTurnLimit);
            if (!string.IsNullOrWhiteSpace(memorySource))
            {
                Session.pendingMemory.Add(memorySource.Substring(0, Math.Min(MemorySourceChars, memorySource.Length)));
                if (Session.pendingMemory.Count > PendingMemoryLimit) Session.pendingMemory.RemoveAt(0);
            }
            Session.lastOpenedUrl = AgentTools.LastOpenedUrl ?? "";
            SessionStore.Save(Session);
        }

        // 回复失败时仍保留 Agent 刚打开的网页，便于"读一下刚才的页面"。
        public void SaveLastOpenedUrl()
        {
            if (Session.lastOpenedUrl == AgentTools.LastOpenedUrl) return;
            Session.lastOpenedUrl = AgentTools.LastOpenedUrl ?? "";
            try { SessionStore.Save(Session); } catch (Exception e) { Debug.LogWarning("无法保存最近网页：" + e.Message); }
        }

        // 后台逐条提取长期记忆；失败时保留队列，下次对话或保存设置后重试。
        public IEnumerator ProcessMemoryQueue(string apiKey)
        {
            if (memoryBusy || string.IsNullOrWhiteSpace(apiKey)) yield break;
            memoryBusy = true;
            try
            {
                while (Session.pendingMemory.Count > 0)
                {
                    string source = Session.pendingMemory[0];
                    MemorySuggestions suggestions = null;
                    string failure = null;
                    yield return MemoryClient.Extract(apiKey, Session, source,
                        value => suggestions = value, value => failure = value);
                    if (failure != null)
                    {
                        Debug.LogWarning("自动记忆本轮延后：" + failure);
                        break;
                    }
                    MemoryEngine.Merge(Session, suggestions, source, DateTime.UtcNow);
                    MemoryEngine.ApplyDecay(Session, DateTime.UtcNow);
                    Session.pendingMemory.RemoveAt(0);
                    try { SessionStore.Save(Session); }
                    catch (Exception e) { Debug.LogWarning("自动记忆保存失败：" + e.Message); break; }
                }
            }
            finally { memoryBusy = false; }
        }
    }
}
