using System;

namespace KurisuCompanion
{
    // Only authored timelines from the white-coat ab model can be selected by the LLM.
    internal struct EmoteCue
    {
        public string Emotion;
        public string Gesture;
        public string ExpressionTimeline;
        public string GestureTimeline;

        public static EmoteCue FromSentence(BilingualSentence sentence)
        {
            string emotion = NormalizeEmotion(sentence?.Emotion, sentence?.Chinese);
            string gesture = NormalizeGesture(sentence?.Gesture);
            return new EmoteCue
            {
                Emotion = emotion,
                Gesture = gesture,
                ExpressionTimeline = Expression(emotion),
                GestureTimeline = GestureMotion(gesture)
            };
        }

        public static string Infer(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "neutral";
            if (Has(text, "生气", "可恶", "烦死", "混蛋")) return "angry";
            if (Has(text, "难过", "伤心", "遗憾", "想哭")) return "sad";
            if (Has(text, "担心", "小心", "没事吧")) return "worried";
            if (Has(text, "真的吗", "怎么会", "居然", "竟然")) return "surprised";
            if (Has(text, "笨蛋", "才不是", "别误会")) return "teasing";
            if (Has(text, "害羞", "脸红")) return "shy";
            if (Has(text, "太好了", "欢迎回来", "真开心", "高兴")) return "happy";
            return "neutral";
        }

        private static bool Has(string text, params string[] phrases)
        {
            foreach (string phrase in phrases)
                if (text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string NormalizeEmotion(string value, string text)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "neutral": case "happy": case "teasing": case "annoyed":
                case "angry": case "sad": case "worried": case "surprised":
                case "shy": case "serious": case "sleepy": return value.Trim().ToLowerInvariant();
                default: return Infer(text);
            }
        }

        private static string NormalizeGesture(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "nod": case "shake": case "sigh": case "think":
                case "look_away": case "laugh": case "surprise": case "blush":
                    return value.Trim().ToLowerInvariant();
                default: return "none";
            }
        }

        private static string Expression(string emotion)
        {
            switch (emotion)
            {
                case "happy": return "表情_笑う";
                case "teasing": return "表情_ニヤニヤ";
                case "annoyed": return "表情_むっとする";
                case "angry": return "表情_怒る";
                case "sad": return "表情_哀しい01";
                case "worried": return "表情_困る01";
                case "surprised": return "表情_驚き01";
                case "shy": return "表情_照れる";
                case "serious": return "表情_睨む";
                case "sleepy": return "表情_眠そう";
                default: return "表情_普通";
            }
        }

        private static string GestureMotion(string gesture)
        {
            switch (gesture)
            {
                case "nod": return "うん";
                case "shake": return "いやいや";
                case "sigh": return "ためいき";
                case "think": return "悩み";
                case "look_away": return "左向き";
                case "laugh": return "笑い";
                case "surprise": return "びっくり";
                case "blush": return "差分_頬染め";
                default: return null;
            }
        }
    }
}
