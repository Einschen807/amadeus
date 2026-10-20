namespace KurisuCompanion
{
    // 发送给模型的一条历史消息（role 为 user 或 assistant）。
    internal sealed class ChatTurn
    {
        public string Role;
        public string Text;
        public ChatTurn(string role, string text) { Role = role; Text = text; }
    }
}
