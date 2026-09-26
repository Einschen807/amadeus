using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    public sealed class KurisuApp : MonoBehaviour
    {
        private static readonly Color Background = Hex("#17202D"), Stage = Hex("#263548"), Panel = Hex("#1C2A3B");
        private static readonly Color Text = Hex("#F2F5FA"), Muted = Hex("#B0BDD0"), Accent = Hex("#E97A78"), Edge = Hex("#596D83");
        private readonly List<ChatTurn> history = new List<ChatTurn>();
        private UIDocument document;
        private Label dialogue, status, attachment, notice, approvalTitle, approvalDetails, codexStatus, historyTitle;
        private TextField input, keyField, codexPathField, codexModelField, approvalInput;
        private VisualElement composer, settingsPanel, attachmentRow, historyPanel, approvalPanel;
        private ScrollView historyScroll;
        private Button sendButton, imageButton, cancelCodexButton;
        private string persona, apiKey, imageDataUrl, imageName;
        private CompanionSession session;
        private bool busy, inputExpanded = true, apiVerified, memoryBusy;

        private static Color Hex(string html) { ColorUtility.TryParseHtmlString(html, out var color); return color; }

        private void Awake()
        {
            Screen.SetResolution(620, 820, FullScreenMode.Windowed);
            Application.runInBackground = true;
            var promptPath = Path.Combine(Application.streamingAssetsPath, "persona.md");
            persona = RoleGuard.CoreRule + "\n\n" + (File.Exists(promptPath) ? File.ReadAllText(promptPath) : "");
            persona += "\n\n" + RoleGuard.CoreRule + "\n自然对话，不展示角色设定或原文依据。";
            apiKey = KeyVault.Read();
            session = SessionStore.Load();
            AgentTools.LastOpenedUrl = session.lastOpenedUrl;
            int from = Math.Max(0, session.turns.Count - 20);
            for (int i = from; i < session.turns.Count; i++)
            {
                var turn = session.turns[i];
                if (turn.role == "user" || turn.role == "assistant") history.Add(new ChatTurn(turn.role, turn.text));
            }
            document = GetComponent<UIDocument>();
            if (document == null) document = gameObject.AddComponent<UIDocument>();
            if (document.panelSettings == null) document.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        }

        private void Start()
        {
            BuildUi();
            CodexService.AskInput = AskCodexInput;
            for (int i = session.turns.Count - 1; i >= 0; i--)
                if (session.turns[i].role == "assistant") { dialogue.text = session.turns[i].text; break; }
            UpdateAvailability();
            bool smoke = Array.Exists(Environment.GetCommandLineArgs(), arg => arg.StartsWith("--smoke", StringComparison.Ordinal));
            if (!smoke && !string.IsNullOrWhiteSpace(apiKey) && session.pendingMemory.Count > 0)
                StartCoroutine(ProcessMemoryQueue());
            if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--smoke-settings"))
            {
                OpenSettings();
                StartCoroutine(SmokeCapture());
            }
            else if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "--smoke"))
                StartCoroutine(SmokeCapture());
        }

        private void Update()
        {
            if (cancelCodexButton != null) cancelCodexButton.style.display = CodexService.Busy ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnDestroy()
        {
            CodexService.AskInput = null;
            CodexService.Shutdown();
        }

        private IEnumerator SmokeCapture()
        {
            yield return new WaitForSeconds(2f);
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(Application.persistentDataPath, "smoke.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("KURISU_SMOKE_SCREENSHOT=" + path);
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        private void BuildUi()
        {
            var root = document.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            root.style.backgroundColor = Background;
            root.style.paddingLeft = root.style.paddingRight = 14;
            root.style.paddingTop = root.style.paddingBottom = 12;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 9;
            root.Add(header);
            var identity = new VisualElement(); header.Add(identity);
            var title = MakeLabel("AMΔDEUS  /  牧濑红莉栖", 20, Text); title.style.unityFontStyleAndWeight = FontStyle.Bold; identity.Add(title);
            status = MakeLabel("", 11, Muted); status.style.marginTop = 3; identity.Add(status);
            var historyButton = MakeButton("记录", OpenHistory); header.Add(historyButton);
            var settingsButton = MakeButton("⚙ 设置", OpenSettings); header.Add(settingsButton);

            var stage = new VisualElement();
            stage.style.flexGrow = 1; stage.style.flexShrink = 1;
            stage.style.minHeight = 300; stage.style.backgroundColor = Stage;
            stage.style.overflow = Overflow.Hidden;
            stage.style.borderTopLeftRadius = stage.style.borderTopRightRadius = 12;
            stage.style.borderBottomLeftRadius = stage.style.borderBottomRightRadius = 12;
            SetBorder(stage, Edge); root.Add(stage);
            var stageLabel = MakeLabel("LAB MEM 004  /  KURISU MAKISE", 11, Muted);
            stageLabel.style.position = Position.Absolute; stageLabel.style.top = 15; stageLabel.style.left = 16; stage.Add(stageLabel);
            var portrait = new Image { image = Resources.Load<Texture2D>("kurisu"), scaleMode = ScaleMode.ScaleToFit };
            portrait.style.flexGrow = 1; portrait.style.marginTop = 16; portrait.style.marginBottom = 0;
            portrait.style.alignSelf = Align.Center; portrait.style.width = new Length(95, LengthUnit.Percent);
            stage.Add(portrait);

            var speech = new VisualElement();
            speech.style.position = Position.Absolute;
            speech.style.left = 0; speech.style.right = 0; speech.style.bottom = 0;
            speech.style.backgroundColor = new Color(0.04f, 0.055f, 0.07f, 0.76f);
            speech.style.paddingLeft = speech.style.paddingRight = 16;
            speech.style.paddingTop = 11; speech.style.paddingBottom = 12;
            speech.style.height = 110;
            speech.style.borderTopWidth = 1; speech.style.borderTopColor = new Color(0.78f, 0.62f, 0.48f, 0.65f);
            stage.Add(speech);
            var name = MakeLabel("牧濑红莉栖", 13, Accent); name.style.unityFontStyleAndWeight = FontStyle.Bold; name.style.marginBottom = 6; speech.Add(name);
            var speechScroll = new ScrollView(ScrollViewMode.Vertical); speechScroll.style.maxHeight = 82; speech.Add(speechScroll);
            dialogue = MakeLabel("", 18, Text); dialogue.style.whiteSpace = WhiteSpace.Normal; speechScroll.Add(dialogue);

            var toggle = MakeButton("收起输入", null);
            toggle.style.alignSelf = Align.FlexEnd; toggle.style.marginTop = 7; toggle.style.marginBottom = 7; root.Add(toggle);
            toggle.clicked += () =>
            {
                inputExpanded = !inputExpanded;
                composer.style.display = inputExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                toggle.text = inputExpanded ? "收起输入" : "展开输入";
            };
            composer = new VisualElement();
            composer.style.backgroundColor = Panel; composer.style.paddingTop = composer.style.paddingBottom = 9;
            composer.style.paddingLeft = composer.style.paddingRight = 9;
            composer.style.borderTopLeftRadius = composer.style.borderTopRightRadius = 9;
            composer.style.borderBottomLeftRadius = composer.style.borderBottomRightRadius = 9;
            SetBorder(composer, Edge); root.Add(composer);
            var inputRow = new VisualElement(); inputRow.style.flexDirection = FlexDirection.Row;
            inputRow.style.alignItems = Align.Center; composer.Add(inputRow);
            imageButton = MakeButton("＋", PickImage);
            imageButton.style.fontSize = 25; imageButton.style.width = 48; imageButton.style.height = 58;
            imageButton.style.marginRight = 7; inputRow.Add(imageButton);
            input = new TextField { multiline = true }; input.style.minHeight = 58; input.style.maxHeight = 115;
            input.style.flexGrow = 1; input.style.color = Hex("#1E2837"); inputRow.Add(input);
            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return && !evt.shiftKey)
                { evt.StopImmediatePropagation(); Send(); }
            }, TrickleDown.TrickleDown);
            sendButton = MakeButton("发送", Send); sendButton.style.backgroundColor = Accent;
            sendButton.style.height = 58; sendButton.style.marginLeft = 7; sendButton.style.marginRight = 0;
            inputRow.Add(sendButton);
            attachmentRow = new VisualElement(); attachmentRow.style.flexDirection = FlexDirection.Row;
            attachmentRow.style.alignItems = Align.Center; attachmentRow.style.marginTop = 6;
            attachmentRow.style.display = DisplayStyle.None; composer.Add(attachmentRow);
            attachment = MakeLabel("", 11, Muted); attachment.style.flexGrow = 1; attachmentRow.Add(attachment);
            var clearButton = MakeButton("×", () => { imageDataUrl = imageName = null; UpdateAvailability(); });
            clearButton.style.marginRight = 0; attachmentRow.Add(clearButton);

            notice = MakeLabel("", 11, Accent); notice.style.whiteSpace = WhiteSpace.Normal;
            notice.style.marginTop = 5; root.Add(notice);
            cancelCodexButton = MakeButton("停止当前任务", () => CodexService.Cancel());
            cancelCodexButton.style.alignSelf = Align.FlexEnd;
            cancelCodexButton.style.display = DisplayStyle.None; root.Add(cancelCodexButton);

            BuildSettings(root);
            BuildHistory(root);
            BuildApproval(root);
        }

        private void BuildSettings(VisualElement root)
        {
            settingsPanel = new VisualElement();
            settingsPanel.style.position = Position.Absolute;
            settingsPanel.style.left = settingsPanel.style.right = 0;
            settingsPanel.style.top = settingsPanel.style.bottom = 0;
            settingsPanel.style.backgroundColor = new Color(0.04f, 0.07f, 0.11f, 0.96f);
            settingsPanel.style.paddingLeft = settingsPanel.style.paddingRight = 30;
            settingsPanel.style.paddingTop = 35; settingsPanel.style.display = DisplayStyle.None;
            root.Add(settingsPanel);
            var content = new ScrollView(ScrollViewMode.Vertical); content.style.flexGrow = 1;
            content.style.paddingBottom = 20; settingsPanel.Add(content);
            content.Add(MakeLabel("DEEPSEEK  /  API 设置", 22, Text));
            var note = MakeLabel("聊天必须配置 DeepSeek API Key。日常对话、图片理解和游戏截图分析统一使用 deepseek-flash。", 14, Muted);
            note.style.whiteSpace = WhiteSpace.Normal; note.style.marginTop = 16; note.style.marginBottom = 15; content.Add(note);
            var keyLabel = MakeLabel("API Key", 14, Text); keyLabel.style.marginBottom = 6; content.Add(keyLabel);
            keyField = new TextField { isPasswordField = true }; keyField.style.color = Hex("#1E2837"); content.Add(keyField);
            var privacy = MakeLabel("密钥通过 Windows 当前用户的 DPAPI 加密后保存在本机。程序会从对话自动提取长期记忆；短期记忆会逐步压缩、遗忘。", 12, Muted);
            privacy.style.whiteSpace = WhiteSpace.Normal; privacy.style.marginTop = 12; content.Add(privacy);
            var codexHeader = MakeLabel("CODEX  /  复杂任务", 20, Text); codexHeader.style.marginTop = 22; content.Add(codexHeader);
            var codexNote = MakeLabel("复杂任务自动委托给本机 Codex。ChatGPT 登录使用账号额度；API 登录按 API 计费。", 13, Muted);
            codexNote.style.whiteSpace = WhiteSpace.Normal; codexNote.style.marginTop = 8; content.Add(codexNote);
            content.Add(MakeLabel("Codex 路径（留空自动查找）", 13, Text));
            codexPathField = new TextField(); codexPathField.style.color = Hex("#1E2837"); content.Add(codexPathField);
            content.Add(MakeLabel("执行模型（留空使用 Codex 默认配置）", 13, Text));
            codexModelField = new TextField(); codexModelField.style.color = Hex("#1E2837"); content.Add(codexModelField);
            codexStatus = MakeLabel("尚未检查", 12, Muted); codexStatus.style.whiteSpace = WhiteSpace.Normal; content.Add(codexStatus);
            var codexButtons = new VisualElement(); codexButtons.style.flexDirection = FlexDirection.Row; codexButtons.style.marginTop = 8; content.Add(codexButtons);
            codexButtons.Add(MakeButton("检查连接", () => StartCoroutine(CodexSettingsAction(false))));
            codexButtons.Add(MakeButton("登录 Codex", () => StartCoroutine(CodexSettingsAction(true))));
            var workspaceButton = MakeButton("打开 Agent 工作目录", () =>
            {
                try
                {
                    Directory.CreateDirectory(AgentTools.WorkspacePath);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = AgentTools.WorkspacePath, UseShellExecute = true
                    });
                }
                catch (Exception e) { notice.text = "无法打开 Agent 工作目录：" + e.Message; }
            });
            workspaceButton.style.marginTop = 12; content.Add(workspaceButton);
            var buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row; buttons.style.marginTop = 22; content.Add(buttons);
            buttons.Add(MakeButton("取消", () => settingsPanel.style.display = DisplayStyle.None));
            var save = MakeButton("保存", () =>
            {
                try
                {
                    string oldKey = apiKey;
                    CodexService.SavePreferences(codexPathField.value, codexModelField.value);
                    KeyVault.Save(keyField.value);
                    apiKey = KeyVault.Read();
                    if (apiKey != oldKey) apiVerified = false;
                    settingsPanel.style.display = DisplayStyle.None;
                    notice.text = "设置已保存。";
                    UpdateAvailability();
                    if (!memoryBusy && session.pendingMemory.Count > 0) StartCoroutine(ProcessMemoryQueue());
                }
                catch (Exception e) { codexStatus.text = "保存设置失败：" + e.Message; }
            }); save.style.backgroundColor = Accent; buttons.Add(save);
        }

        private void OpenSettings()
        {
            keyField.value = apiKey;
            codexPathField.value = CodexService.Preferences.executable;
            codexModelField.value = CodexService.Preferences.model;
            codexStatus.text = CodexService.ConnectionStatus;
            settingsPanel.style.display = DisplayStyle.Flex;
        }

        private IEnumerator CodexSettingsAction(bool login)
        {
            string error = null;
            try { CodexService.SavePreferences(codexPathField.value, codexModelField.value); }
            catch (Exception e) { error = e.Message; }
            if (error != null) { codexStatus.text = error; yield break; }
            codexStatus.text = "正在连接……";
            if (login) yield return CodexService.Login(value => codexStatus.text = value);
            else yield return CodexService.CheckConnection(value => codexStatus.text = value);
        }

        private void BuildHistory(VisualElement root)
        {
            historyPanel = new VisualElement();
            historyPanel.style.position = Position.Absolute;
            historyPanel.style.left = historyPanel.style.right = 0;
            historyPanel.style.top = historyPanel.style.bottom = 0;
            historyPanel.style.paddingLeft = historyPanel.style.paddingRight = 20;
            historyPanel.style.paddingTop = historyPanel.style.paddingBottom = 20;
            historyPanel.style.backgroundColor = new Color(0.04f, 0.07f, 0.11f, 0.97f);
            historyPanel.style.display = DisplayStyle.None;
            root.Add(historyPanel);
            var bar = new VisualElement(); bar.style.flexDirection = FlexDirection.Row;
            bar.style.justifyContent = Justify.SpaceBetween; historyPanel.Add(bar);
            historyTitle = MakeLabel("对话记录", 20, Text); bar.Add(historyTitle);
            bar.Add(MakeButton("对话", OpenHistory));
            bar.Add(MakeButton("任务", OpenCodexHistory));
            bar.Add(MakeButton("关闭", () => historyPanel.style.display = DisplayStyle.None));
            historyScroll = new ScrollView(ScrollViewMode.Vertical); historyScroll.style.flexGrow = 1;
            historyScroll.style.marginTop = 12; historyPanel.Add(historyScroll);
        }

        private void OpenHistory()
        {
            historyTitle.text = "对话记录";
            historyScroll.Clear();
            foreach (var turn in session.turns)
            {
                string label = turn.role == "assistant" ? "牧濑红莉栖" : "你";
                var heading = MakeLabel(label + "  " + turn.time, 11, turn.role == "assistant" ? Accent : Muted);
                heading.style.marginTop = 12; historyScroll.Add(heading);
                var content = MakeLabel(turn.text, 15, Text); content.style.whiteSpace = WhiteSpace.Normal;
                historyScroll.Add(content);
            }
            historyPanel.style.display = DisplayStyle.Flex;
            historyScroll.schedule.Execute(() => historyScroll.scrollOffset = new Vector2(0, historyScroll.contentContainer.layout.height));
        }

        private void OpenCodexHistory()
        {
            historyTitle.text = "Codex 任务"; historyScroll.Clear();
            var records = CodexService.Records;
            if (records.Count == 0) historyScroll.Add(MakeLabel("还没有委托过 Codex 任务。", 14, Muted));
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var task = records[i];
                var title = MakeLabel(task.updated + "  ·  " + task.status, 13, Accent); title.style.marginTop = 12; historyScroll.Add(title);
                var body = MakeLabel(task.task + "\n目录：" + task.directory + "\n任务 ID：" + task.threadId + "\n\n" + task.result, 14, Text);
                body.style.whiteSpace = WhiteSpace.Normal; body.style.marginBottom = 12; historyScroll.Add(body);
            }
            historyPanel.style.display = DisplayStyle.Flex;
        }

        private void BuildApproval(VisualElement root)
        {
            approvalPanel = new VisualElement();
            approvalPanel.style.position = Position.Absolute;
            approvalPanel.style.left = approvalPanel.style.right = 0;
            approvalPanel.style.top = approvalPanel.style.bottom = 0;
            approvalPanel.style.paddingLeft = approvalPanel.style.paddingRight = 24;
            approvalPanel.style.paddingTop = approvalPanel.style.paddingBottom = 30;
            approvalPanel.style.backgroundColor = new Color(0.035f, 0.06f, 0.09f, 0.98f);
            approvalPanel.style.display = DisplayStyle.None;
            root.Add(approvalPanel);
            approvalTitle = MakeLabel("", 20, Accent); approvalPanel.Add(approvalTitle);
            var intro = MakeLabel("请核对下面的操作，再决定是否允许。拒绝后她会收到拒绝结果并继续回答。", 13, Muted);
            intro.style.whiteSpace = WhiteSpace.Normal; intro.style.marginTop = 10; approvalPanel.Add(intro);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1; scroll.style.marginTop = 16; approvalPanel.Add(scroll);
            approvalDetails = MakeLabel("", 14, Text);
            approvalDetails.style.whiteSpace = WhiteSpace.Normal; scroll.Add(approvalDetails);
            approvalInput = new TextField { multiline = true }; approvalInput.style.minHeight = 70;
            approvalInput.style.color = Hex("#1E2837"); approvalInput.style.display = DisplayStyle.None; approvalPanel.Add(approvalInput);
            var buttons = new VisualElement(); buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 15; approvalPanel.Add(buttons);
            buttons.Add(MakeButton("拒绝", () => approvalDecision = false));
            var allow = MakeButton("允许执行", () => approvalDecision = true);
            allow.style.backgroundColor = Accent; buttons.Add(allow);
            buttons.Add(MakeButton("停止任务", () => { CodexService.Cancel(); approvalDecision = false; }));
        }

        private bool? approvalDecision;

        private IEnumerator AskApproval(string title, string details, Action<bool> onDecision)
        {
            approvalInput.style.display = DisplayStyle.None;
            approvalDecision = null;
            approvalTitle.text = title;
            approvalDetails.text = details;
            approvalPanel.style.display = DisplayStyle.Flex;
            while (!approvalDecision.HasValue && !(CodexService.Busy && CodexService.CancelRequested)) yield return null;
            approvalPanel.style.display = DisplayStyle.None;
            onDecision(approvalDecision ?? false);
        }

        private IEnumerator AskCodexInput(string question, Action<string> onAnswer)
        {
            approvalDecision = null; approvalTitle.text = "任务需要补充信息"; approvalDetails.text = question;
            approvalInput.value = ""; approvalInput.style.display = DisplayStyle.Flex;
            approvalPanel.style.display = DisplayStyle.Flex; approvalInput.Focus();
            while (!approvalDecision.HasValue && !CodexService.CancelRequested) yield return null;
            string answer = approvalDecision == true ? approvalInput.value : "";
            approvalPanel.style.display = DisplayStyle.None; approvalInput.style.display = DisplayStyle.None;
            onAnswer(answer);
        }

        private void UpdateAvailability()
        {
            bool ready = !string.IsNullOrWhiteSpace(apiKey) && !busy;
            sendButton.SetEnabled(ready); imageButton.SetEnabled(!busy); input.SetEnabled(ready);
            status.text = busy ? "deepseek-flash · 处理中…" : ready ?
                (apiVerified ? "deepseek-flash · 最近请求成功" : "deepseek-flash · 已配置") : "API 未配置";
            attachment.text = imageName == null ? "" : imageName;
            attachmentRow.style.display = imageName == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void PickImage()
        {
            if (busy) return;
            Debug.Log("KURISU_IMAGE_PICK_CLICK");
            try
            {
                string path = NativeImagePicker.Pick();
                Debug.Log(string.IsNullOrEmpty(path) ? "KURISU_IMAGE_PICK_CANCEL" : "KURISU_IMAGE_PICK_SELECTED");
                if (string.IsNullOrEmpty(path)) return;
                imageDataUrl = imageName = null;
                imageDataUrl = ImageAttachment.Prepare(path);
                imageName = Path.GetFileName(path); notice.text = ""; UpdateAvailability();
            }
            catch (Exception e)
            {
                Debug.LogWarning("KURISU_IMAGE_PICK_ERROR " + e.GetType().Name + " " + e.Message);
                UpdateAvailability(); notice.text = "选择或读取图片失败：" + e.Message;
            }
        }

        private void Send()
        {
            if (busy || string.IsNullOrWhiteSpace(apiKey)) return;
            string userText = input.value.Trim();
            if (userText.Length == 0 && imageDataUrl == null) return;
            if (userText.Length == 0) userText = "请看这张图片，说说你观察到了什么。";
            StartCoroutine(SendRoutine(userText, imageDataUrl, imageName));
        }

        private IEnumerator SendRoutine(string userText, string image, string attachedName)
        {
            string previousDialogue = dialogue.text;
            busy = true; dialogue.text = "让我看看……"; notice.text = ""; UpdateAvailability();
            string answer = null, error = null;
            yield return AgentClient.Send(apiKey, persona, session.Context(), history, userText, image,
                AskApproval, progress => { dialogue.text = progress; status.text = CodexService.Busy ? "Codex · 正在执行任务" : "deepseek-flash · 处理中…"; },
                reply => answer = reply, reason => error = reason);
            if (error != null)
            {
                apiVerified = false;
                dialogue.text = previousDialogue;
                notice.text = "回复失败：" + error + " 输入和附件已保留，可重试。";
            }
            else
            {
                string savedUserText = image == null ? userText : userText + " [附有一张图片：" + attachedName + "]";
                history.Add(new ChatTurn("user", savedUserText));
                history.Add(new ChatTurn("assistant", answer));
                while (history.Count > 20) history.RemoveRange(0, 2);
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                session.turns.Add(new SavedTurn { role = "user", text = savedUserText, time = now });
                session.turns.Add(new SavedTurn { role = "assistant", text = answer, time = now });
                if (session.turns.Count > 500) session.turns.RemoveRange(0, session.turns.Count - 500);
                session.pendingMemory.Add(userText.Substring(0, Math.Min(2000, userText.Length)));
                if (session.pendingMemory.Count > 30) session.pendingMemory.RemoveAt(0);
                session.lastOpenedUrl = AgentTools.LastOpenedUrl ?? "";
                try { SessionStore.Save(session); }
                catch (Exception e) { notice.text = "对话已显示，但保存记录失败：" + e.Message; }
                apiVerified = true;
                dialogue.text = answer; input.value = ""; imageDataUrl = imageName = null;
                if (!memoryBusy) StartCoroutine(ProcessMemoryQueue());
            }
            if (error != null && session.lastOpenedUrl != AgentTools.LastOpenedUrl)
            {
                session.lastOpenedUrl = AgentTools.LastOpenedUrl ?? "";
                try { SessionStore.Save(session); } catch (Exception e) { Debug.LogWarning("无法保存最近网页：" + e.Message); }
            }
            busy = false; UpdateAvailability();
        }

        private IEnumerator ProcessMemoryQueue()
        {
            if (memoryBusy || string.IsNullOrWhiteSpace(apiKey)) yield break;
            memoryBusy = true;
            try
            {
                while (session.pendingMemory.Count > 0)
                {
                    string source = session.pendingMemory[0];
                    MemorySuggestions suggestions = null;
                    string failure = null;
                    yield return MemoryClient.Extract(apiKey, session, source,
                        value => suggestions = value, value => failure = value);
                    if (failure != null)
                    {
                        Debug.LogWarning("自动记忆本轮延后：" + failure);
                        break;
                    }
                    MemoryEngine.Merge(session, suggestions, source, DateTime.UtcNow);
                    MemoryEngine.ApplyDecay(session, DateTime.UtcNow);
                    session.pendingMemory.RemoveAt(0);
                    try { SessionStore.Save(session); }
                    catch (Exception e) { Debug.LogWarning("自动记忆保存失败：" + e.Message); break; }
                }
            }
            finally { memoryBusy = false; }
        }

        private static Label MakeLabel(string content, int size, Color color)
        {
            var label = new Label(content); label.style.fontSize = size; label.style.color = color; return label;
        }

        private static Button MakeButton(string content, Action action)
        {
            var button = new Button(action) { text = content };
            button.style.backgroundColor = new Color(0.22f, 0.29f, 0.39f);
            button.style.color = Text; button.style.paddingLeft = button.style.paddingRight = 12;
            button.style.paddingTop = button.style.paddingBottom = 7;
            button.style.marginRight = 6; return button;
        }

        private static void SetBorder(VisualElement element, Color color)
        {
            element.style.borderTopWidth = element.style.borderRightWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = 1;
            element.style.borderTopColor = element.style.borderRightColor = element.style.borderBottomColor = element.style.borderLeftColor = color;
        }
    }
}
