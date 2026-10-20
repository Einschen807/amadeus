using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using T = KurisuCompanion.AmadeusTheme;

namespace KurisuCompanion
{
    // 界面构建：主画面、设置、记录、审批弹窗。只负责控件与展示，不含对话流程。
    public sealed partial class KurisuApp
    {
        private const float NoticeSeconds = 7f;
        private Label metricsLabel;
        private Button historyTab, tasksTab, stopTaskButton, settingsButton;
        private string noticeShown;
        private float noticeSince;

        private void BuildUi()
        {
            var root = document.rootVisualElement;
            root.Clear(); root.style.flexGrow = 1; root.style.backgroundColor = Color.black;
            status = T.TextLabel("", 13, T.Text); status.name = "service-status";
            microphoneState = T.TextLabel("语音输入：关闭", 13, T.Text);
            microphoneHint = T.TextLabel("", 12, T.Muted); microphoneHint.name = "microphone-status";

            var stage = new VisualElement { name = "portrait-stage" };
            stage.style.flexGrow = 1; stage.style.flexShrink = 1; stage.style.minHeight = 100;
            stage.style.backgroundColor = Color.black; stage.style.overflow = Overflow.Hidden; root.Add(stage);
            backgroundEffect = new FlowingCircuitBackground(); stage.Add(backgroundEffect);
            portraitViewport = new PortraitViewport(Resources.Load<Texture2D>("kurisu"));
            portraitViewport.style.opacity = 0f;
            stage.Add(portraitViewport);
            bubble = new SpeechBubble(); stage.Add(bubble);
            dialogue = bubble.Caption;
            stage.Add(new PhoneFrameOverlay());
            settingsButton = IconButton(new ChromeIcon(true), "设置", OpenSettings);
            settingsButton.name = "settings-toggle"; settingsButton.style.position = Position.Absolute;
            settingsButton.style.top = 26; settingsButton.style.right = 16; root.Add(settingsButton);
            var dragHandle = new VisualElement { name = "window-drag-handle" };
            dragHandle.style.position = Position.Absolute;
            dragHandle.style.left = dragHandle.style.right = 110;
            dragHandle.style.top = 0; dragHandle.style.height = 19;
            dragHandle.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) phoneWindow.BeginDrag(); });
            root.Add(dragHandle);
            root.Add(WindowButton("window-minimize", "−", 39, () => phoneWindow.Minimize()));
            root.Add(WindowButton("window-close", "×", 10, () => Application.Quit()));

            microphoneIcon = new MicrophoneIcon();
            microphoneButton = IconButton(microphoneIcon, "开启语音输入", ToggleVoiceMode);
            microphoneButton.name = "microphone-toggle"; microphoneButton.style.position = Position.Absolute;
            microphoneButton.style.right = 25; microphoneButton.style.bottom = 26; root.Add(microphoneButton);
            textIcon = new TextInputIcon();
            textIcon.Active = false;
            textButton = IconButton(textIcon, "显示文本输入框", () => SetTextVisible(!input.Visible));
            textButton.name = "text-toggle"; textButton.style.position = Position.Absolute;
            textButton.style.right = 77; textButton.style.bottom = 26; root.Add(textButton);
            input = new DetachedInputWindow();
            input.Send = Send;
            input.Clear = () => { imageDataUrl = imageName = attachedText = null; UpdateAvailability(); };
            input.Drop = AcceptDroppedFiles;
            input.Problem = message => notice.text = message;

            BuildNotice(root);
            cancelCodexButton = T.Ghost("停止当前任务", () => CodexService.Cancel());
            cancelCodexButton.style.marginTop = 8; cancelCodexButton.style.alignSelf = Align.FlexStart;
            cancelCodexButton.style.display = DisplayStyle.None;
            BuildSettings(root); BuildHistory(root); BuildApproval(root);
            BuildDropOverlay(root);
        }

        // 顶部提示条：左侧红色信号条，几秒后自动收起。
        private void BuildNotice(VisualElement root)
        {
            notice = T.TextLabel("", 13, T.Text); notice.name = "transient-notice";
            notice.style.position = Position.Absolute;
            notice.style.left = 24; notice.style.right = 72; notice.style.top = 28;
            notice.style.paddingLeft = 12; notice.style.paddingRight = 12;
            notice.style.paddingTop = 8; notice.style.paddingBottom = 9;
            notice.style.backgroundColor = new Color(T.Surface.r, T.Surface.g, T.Surface.b, .92f);
            notice.style.borderLeftWidth = 2; notice.style.borderLeftColor = T.Highlight;
            notice.style.display = DisplayStyle.None;
            notice.pickingMode = PickingMode.Ignore;
            root.Add(notice);
        }

        private void TickNotice()
        {
            if (notice.text != noticeShown) { noticeShown = notice.text; noticeSince = Time.unscaledTime; }
            else if (!string.IsNullOrEmpty(noticeShown) && Time.unscaledTime - noticeSince > NoticeSeconds) notice.text = "";
            notice.style.display = string.IsNullOrEmpty(notice.text) ? DisplayStyle.None : DisplayStyle.Flex;
            // 面板打开时收起右上角齿轮，避免透过半透明面板露出来。
            bool sheetOpen = settingsPanel.style.display == DisplayStyle.Flex || historyPanel.style.display == DisplayStyle.Flex ||
                             approvalPanel.style.display == DisplayStyle.Flex;
            settingsButton.style.display = sheetOpen ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void BuildDropOverlay(VisualElement root)
        {
            dropOverlay = new VisualElement { name = "file-drop-overlay", pickingMode = PickingMode.Ignore };
            dropOverlay.style.position = Position.Absolute;
            dropOverlay.style.left = dropOverlay.style.right = 12;
            dropOverlay.style.top = 18; dropOverlay.style.bottom = 11;
            dropOverlay.style.backgroundColor = new Color(T.Void.r, T.Void.g, T.Void.b, .78f);
            dropOverlay.style.alignItems = Align.Center; dropOverlay.style.justifyContent = Justify.Center;
            dropOverlay.style.display = DisplayStyle.None;
            // 四角取景框，呼应识别 / 接收数据的意象。
            dropOverlay.generateVisualContent += context =>
            {
                var p = context.painter2D;
                float w = dropOverlay.contentRect.width, h = dropOverlay.contentRect.height, m = 26, arm = 34;
                p.strokeColor = T.Highlight; p.lineWidth = 2;
                foreach (var (x, y, dx, dy) in new[] { (m, m, 1, 1), (w - m, m, -1, 1), (m, h - m, 1, -1), (w - m, h - m, -1, -1) })
                {
                    p.BeginPath();
                    p.MoveTo(new Vector2(x + dx * arm, y)); p.LineTo(new Vector2(x, y)); p.LineTo(new Vector2(x, y + dy * arm));
                    p.Stroke();
                }
            };
            root.Add(dropOverlay);
            dropOverlay.Add(T.Caps("INCOMING  DATA", T.Highlight));
            var title = T.TextLabel("松开以添加附件", 22, T.Text, true); title.style.marginTop = 8; dropOverlay.Add(title);
            var hint = T.TextLabel("图片 PNG / JPEG · 文本与代码文件", 12, T.Muted); hint.style.marginTop = 8; dropOverlay.Add(hint);
        }

        private static Button IconButton(VisualElement icon, string tooltip, Action action) {
            var button = new Button(action) { tooltip = tooltip };
            button.style.width = 44; button.style.height = 48; button.style.flexShrink = 0;
            button.style.backgroundColor = Color.clear;
            button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            button.style.marginTop = button.style.marginBottom = button.style.marginLeft = button.style.marginRight = 0;
            button.style.paddingTop = button.style.paddingBottom = button.style.paddingLeft = button.style.paddingRight = 0;
            button.style.alignItems = Align.Center; button.style.justifyContent = Justify.Center; button.Add(icon);
            button.RegisterCallback<PointerEnterEvent>(_ => icon.style.opacity = .7f);
            button.RegisterCallback<PointerLeaveEvent>(_ => icon.style.opacity = 1);
            return button;
        }

        private static Button WindowButton(string elementName, string symbol, float right, Action action) {
            var button = new Button(action) { name = elementName, text = symbol };
            button.style.position = Position.Absolute;
            button.style.top = 0; button.style.right = right;
            button.style.width = 25; button.style.height = 18;
            button.style.backgroundColor = Color.clear;
            button.style.color = new Color(.95f, .85f, .78f);
            button.style.fontSize = 15;
            button.style.borderTopWidth = button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            button.style.paddingTop = button.style.paddingBottom = 0;
            button.style.paddingLeft = button.style.paddingRight = 0;
            button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = new Color(.55f, .18f, .12f, .75f));
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = Color.clear);
            return button;
        }

        private void BuildSettings(VisualElement root)
        {
            settingsPanel = T.Sheet("settings-panel", "设置", "CONFIG", CloseSettings);
            root.Add(settingsPanel);
            var content = T.Scroll(); settingsPanel.Add(content);

            content.Add(T.Section("01", "对话", "CONVERSATION"));
            companionToggle = T.Switch("陪伴模式", false);
            companionToggle.name = "companion-mode-toggle";
            companionToggle.RegisterValueChangedCallback(evt => companionManager?.SetActive(evt.newValue));
            content.Add(companionToggle);
            content.Add(T.TextLabel("开启后，屏幕有变化时会把缩小的截图发送给 DeepSeek 分析。", 12, T.Muted));
            var records = T.Row(12); content.Add(records);
            var history = T.Ghost("对话记录", OpenHistory); history.name = "history-in-settings"; records.Add(history);
            records.Add(T.Ghost("任务记录", OpenCodexHistory));

            content.Add(T.Section("02", "状态", "STATUS"));
            content.Add(status); content.Add(microphoneState); content.Add(microphoneHint); content.Add(cancelCodexButton);
            microphoneState.style.marginTop = 6;

            content.Add(T.Section("03", "模型", "DEEPSEEK"));
            keyField = T.Field(content, "API Key", "", "sk-…", true);
            var keyNote = T.TextLabel("密钥经 Windows DPAPI 加密，只保存在本机。", 12, T.Muted); keyNote.style.marginTop = 6; content.Add(keyNote);

            BuildVoiceSettings(content);

            content.Add(T.Section("05", "复杂任务", "CODEX"));
            codexPathField = T.Field(content, "Codex 路径", "", "留空自动查找");
            codexModelField = T.Field(content, "执行模型", "", "留空使用 Codex 默认");
            codexStatus = T.TextLabel("尚未检查", 12, T.Muted); codexStatus.style.marginTop = 8; content.Add(codexStatus);
            var codexButtons = T.Row(10); content.Add(codexButtons);
            codexButtons.Add(T.Ghost("检查连接", () => StartCoroutine(CodexSettingsAction(false))));
            codexButtons.Add(T.Ghost("登录", () => StartCoroutine(CodexSettingsAction(true))));
            codexButtons.Add(T.Ghost("工作目录", OpenWorkspace));

            content.Add(T.Section("06", "体验指标", "METRICS"));
            metricsLabel = T.TextLabel("", 12, T.Text); metricsLabel.name = "metrics-summary";
            metricsLabel.style.unityParagraphSpacing = 4;
            content.Add(metricsLabel);
            var spacer = new VisualElement(); spacer.style.height = 12; content.Add(spacer);

            settingsPanel.Add(T.Rule());
            var footer = T.Row(12); footer.style.flexWrap = Wrap.NoWrap; footer.style.justifyContent = Justify.FlexEnd;
            footer.style.flexShrink = 0; settingsPanel.Add(footer);
            footer.Add(T.Ghost("取消", CloseSettings));
            var save = T.Primary("保存", SaveSettings); save.style.marginRight = 0; save.style.minWidth = 88; footer.Add(save);
        }

        private void CloseSettings() => settingsPanel.style.display = DisplayStyle.None;

        private void SaveSettings()
        {
            try
            {
                SaveVoiceSettings();
                string oldKey = apiKey;
                CodexService.SavePreferences(codexPathField.value, codexModelField.value);
                KeyVault.Save(keyField.value);
                apiKey = KeyVault.Read();
                if (apiKey != oldKey) apiVerified = false;
                CloseSettings();
                notice.text = "设置已保存。";
                UpdateAvailability();
                if (conversation.HasPendingMemory) StartCoroutine(conversation.ProcessMemoryQueue(apiKey));
            }
            catch (Exception e) { codexStatus.text = "保存设置失败：" + e.Message; }
        }

        private void OpenWorkspace()
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
        }

        private void OpenSettings()
        {
            keyField.value = apiKey;
            codexPathField.value = CodexService.Preferences.executable;
            codexModelField.value = CodexService.Preferences.model;
            codexStatus.text = CodexService.ConnectionStatus;
            metricsLabel.text = Metrics.Summary();
            settingsPanel.Q<ScrollView>().scrollOffset = Vector2.zero;
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
            historyPanel = T.Sheet("history-panel", "对话记录", "ARCHIVE", () => historyPanel.style.display = DisplayStyle.None);
            root.Add(historyPanel);
            historyTitle = historyPanel.Q<Label>("history-panel-title");
            var tabs = T.Row(12); tabs.style.flexShrink = 0; historyPanel.Add(tabs);
            historyTab = T.Ghost("对话", OpenHistory); tabs.Add(historyTab);
            tasksTab = T.Ghost("任务", OpenCodexHistory); tabs.Add(tasksTab);
            historyScroll = T.Scroll(); historyScroll.style.marginTop = 12; historyPanel.Add(historyScroll);
        }

        private void SelectHistoryTab(bool conversationTab)
        {
            foreach (var (tab, active) in new[] { (historyTab, conversationTab), (tasksTab, !conversationTab) })
            {
                tab.style.color = active ? T.Text : T.Muted;
                tab.style.borderBottomColor = active ? T.Highlight : T.Line;
                tab.style.borderBottomWidth = active ? 2 : 1;
            }
        }

        private void OpenHistory() => OpenHistory(session.turns);

        private void OpenHistory(IEnumerable<SavedTurn> turns)
        {
            historyTitle.text = "对话记录";
            SelectHistoryTab(true);
            historyScroll.Clear();
            int count = 0;
            foreach (var turn in turns)
            {
                bool kurisu = turn.role == "assistant";
                var card = T.Card(kurisu ? T.Highlight : T.Muted);
                card.style.marginTop = 8;
                if (!kurisu) { card.style.backgroundColor = new Color(1, 1, 1, .025f); card.style.marginLeft = 36; }
                else card.style.marginRight = 36;
                var heading = T.Row(0); heading.style.flexWrap = Wrap.NoWrap; heading.style.justifyContent = Justify.SpaceBetween;
                heading.Add(T.TextLabel(kurisu ? "牧濑红莉栖" : "你", 11, kurisu ? T.Highlight : T.Muted, true));
                heading.Add(T.TextLabel(turn.time, 10, T.Muted));
                card.Add(heading);
                var body = T.TextLabel(turn.text, 14, T.Text); body.style.marginTop = 4; card.Add(body);
                historyScroll.Add(card);
                count++;
            }
            if (count == 0) historyScroll.Add(T.TextLabel("还没有对话。", 13, T.Muted));
            historyPanel.style.display = DisplayStyle.Flex;
            historyScroll.schedule.Execute(() => historyScroll.scrollOffset = new Vector2(0, historyScroll.contentContainer.layout.height));
        }

        private void OpenCodexHistory()
        {
            historyTitle.text = "任务记录";
            SelectHistoryTab(false);
            historyScroll.Clear();
            var records = CodexService.Records;
            if (records.Count == 0) historyScroll.Add(T.TextLabel("还没有委托过 Codex 任务。", 13, T.Muted));
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var task = records[i];
                var card = T.Card(T.Signal); card.style.marginTop = 8;
                var heading = T.Row(0); heading.style.flexWrap = Wrap.NoWrap; heading.style.justifyContent = Justify.SpaceBetween;
                heading.Add(T.TextLabel(task.status, 11, T.Signal, true));
                heading.Add(T.TextLabel(task.updated, 10, T.Muted));
                card.Add(heading);
                var title = T.TextLabel(task.task, 14, T.Text); title.style.marginTop = 4; card.Add(title);
                var meta = T.TextLabel(task.directory + (string.IsNullOrEmpty(task.threadId) ? "" : "\n" + task.threadId), 11, T.Muted);
                meta.style.marginTop = 4; card.Add(meta);
                if (!string.IsNullOrWhiteSpace(task.result))
                {
                    var result = T.TextLabel(task.result, 12, T.Text); result.style.marginTop = 8; card.Add(result);
                }
                historyScroll.Add(card);
            }
            historyPanel.style.display = DisplayStyle.Flex;
        }

        private void BuildApproval(VisualElement root)
        {
            approvalPanel = T.Sheet("approval-panel", "", "AUTHORIZATION", null);
            root.Add(approvalPanel);
            approvalTitle = approvalPanel.Q<Label>("approval-panel-title");
            approvalTitle.style.color = T.Highlight;
            var scroll = T.Scroll(); scroll.style.marginTop = 14; approvalPanel.Add(scroll);
            var block = T.Card(T.Highlight); block.style.backgroundColor = T.FieldFill; scroll.Add(block);
            approvalDetails = T.TextLabel("", 13, T.Text); block.Add(approvalDetails);
            var inputHolder = new VisualElement(); inputHolder.style.flexShrink = 0; approvalPanel.Add(inputHolder);
            approvalInput = T.Field(inputHolder, "你的回答", "");
            approvalInput.multiline = true;
            approvalInput.Q(className: TextField.inputUssClassName).style.minHeight = 72;
            inputHolder.style.display = DisplayStyle.None;
            approvalPanel.Add(T.Rule());
            var buttons = T.Row(12); buttons.style.flexWrap = Wrap.NoWrap; buttons.style.flexShrink = 0; approvalPanel.Add(buttons);
            stopTaskButton = T.Ghost("停止任务", () => { CodexService.Cancel(); approvalDecision = false; });
            stopTaskButton.style.color = T.Muted;
            buttons.Add(stopTaskButton);
            var gap = new VisualElement(); gap.style.flexGrow = 1; buttons.Add(gap);
            buttons.Add(T.Ghost("拒绝", () => approvalDecision = false));
            var allow = T.Primary("允许", () => approvalDecision = true); allow.style.marginRight = 0; allow.style.minWidth = 88; buttons.Add(allow);
        }

        private bool? approvalDecision;

        private void ShowApprovalPanel(string title, string details, bool askForInput)
        {
            approvalDecision = null;
            approvalTitle.text = title;
            approvalDetails.text = details;
            approvalInput.parent.style.display = askForInput ? DisplayStyle.Flex : DisplayStyle.None;
            stopTaskButton.style.display = CodexService.Busy ? DisplayStyle.Flex : DisplayStyle.None;
            approvalPanel.style.display = DisplayStyle.Flex;
        }

        private void HideApprovalPanel()
        {
            approvalPanel.style.display = DisplayStyle.None;
            approvalInput.parent.style.display = DisplayStyle.None;
        }

        private IEnumerator AskApproval(string title, string details, Action<bool> onDecision)
        {
            ShowApprovalPanel(title, details, false);
            while (!approvalDecision.HasValue && !(CodexService.Busy && CodexService.CancelRequested)) yield return null;
            HideApprovalPanel();
            onDecision(approvalDecision ?? false);
        }

        private IEnumerator AskCodexInput(string question, Action<string> onAnswer)
        {
            approvalInput.value = "";
            ShowApprovalPanel("任务需要补充信息", question, true);
            approvalInput.Focus();
            while (!approvalDecision.HasValue && !CodexService.CancelRequested) yield return null;
            string answer = approvalDecision == true ? approvalInput.value : "";
            HideApprovalPanel();
            onAnswer(answer);
        }

        private void BuildVoiceSettings(VisualElement content) {
            content.Add(T.Section("04", "语音", "VOICE"));
            voiceEnabled = T.Switch("启用日语语音", voiceService.Settings.enabled); content.Add(voiceEnabled);
            voicePath = T.Field(content, "GPT-SoVITS 目录", voiceService.Settings.installPath, "留空自动查找");
            voiceEndpoint = T.Field(content, "API 地址", voiceService.Settings.endpoint);
            voiceGpt = T.Field(content, "GPT 模型", voiceService.Settings.gptModel);
            voiceSovits = T.Field(content, "SoVITS 模型", voiceService.Settings.sovitsModel);
            stopVoiceOnExit = T.Switch("退出时关闭语音服务", voiceService.Settings.stopOwnedServerOnExit);
            stopVoiceOnExit.style.marginTop = 10; content.Add(stopVoiceOnExit);
            voiceStatus = T.TextLabel(voiceService.Ready ? "语音服务已就绪" : "", 12, T.Muted); voiceStatus.style.marginTop = 6; content.Add(voiceStatus);
            var connect = T.Ghost("保存并连接", () => { SaveVoiceSettings(); voiceService.StartCoroutine(ConnectVoice()); });
            connect.style.alignSelf = Align.FlexStart; connect.style.marginTop = 10; content.Add(connect);
        }
        private void SaveVoiceSettings() {
            voiceService.Settings.enabled = voiceEnabled.value; voiceService.Settings.installPath = voicePath.value.Trim();
            voiceService.Settings.endpoint = voiceEndpoint.value.Trim(); voiceService.Settings.gptModel = voiceGpt.value.Trim(); voiceService.Settings.sovitsModel = voiceSovits.value.Trim();
            voiceService.Settings.stopOwnedServerOnExit = stopVoiceOnExit.value; voiceService.Save();
        }
        private IEnumerator ConnectVoice() { yield return voiceService.EnsureReady(); voiceStatus.text = voiceService.Ready ? "语音服务已就绪" : voiceService.LastError ?? "语音已关闭"; }

        // 语音服务不可用时的提示卡：可以去设置修复，也可以只用文字继续。
        private void ShowVoiceProblem(string message) {
            notice.text = ""; voicePopup?.RemoveFromHierarchy();
            if (companionManager != null && companionManager.Active) { companionManager.Display(message); return; }
            voicePopup = T.Card(T.Highlight); voicePopup.name = "voice-problem";
            voicePopup.style.position = Position.Absolute; voicePopup.style.left = voicePopup.style.right = 28; voicePopup.style.top = 96;
            voicePopup.style.backgroundColor = T.Surface;
            voicePopup.style.paddingTop = voicePopup.style.paddingBottom = 16; voicePopup.style.paddingLeft = voicePopup.style.paddingRight = 18;
            voicePopup.Add(T.Caps("VOICE  //  OFFLINE", T.Highlight));
            var label = T.TextLabel(message, 14, T.Text); label.style.marginTop = 8; voicePopup.Add(label);
            var buttons = T.Row(14); voicePopup.Add(buttons);
            buttons.Add(T.Ghost("只用文字", () => voicePopup.RemoveFromHierarchy()));
            buttons.Add(T.Primary("语音设置", () => { voicePopup.RemoveFromHierarchy(); OpenSettings(); }));
            document.rootVisualElement.Add(voicePopup);
        }
    }
}
