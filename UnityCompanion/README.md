# 牧濑红莉栖 AI 陪伴程序 · Unity 初版

项目使用本机 Unity 6000.6.2f1。场景为 `Assets/Scenes/Companion.unity`；静态立绘来自现有 E-mote 测试渲染，角色设定放在 `Assets/StreamingAssets/persona.md`。立绘没有口型、眨眼或动作动画。

## 运行

- 打开 `UnityCompanion` 项目并运行 `Companion` 场景；或双击上一级的 `启动红莉栖陪伴程序.cmd`，启动已构建的 Windows 独立窗口。
- 在设置中填写自己的 DeepSeek API Key，然后在立绘下方的输入区输入。Enter 发送，Shift+Enter 换行。输入区可以收起；角色对白显示在覆盖立绘下半身的半透明文本框中。
- 点击输入框左侧的「＋」选择 PNG 或 JPEG，包括游戏保存的截图。选图功能不需要先配置 API Key；发送消息仍需要密钥。程序会把图片缩放到最长边 1280 像素并压缩为 JPEG；附件名称显示在输入区内，可用「×」移除。图片不会自动发送；按「发送」后才随本轮文本提交。如果 Windows 文件选择器启动失败，输入区下方会显示错误码。

日常对话、图片理解、游戏截图分析全部调用 `deepseek-flash`。`deepseek-v4-pro` 尚未接入自动任务路由。没有 API Key 时聊天禁用，不产生离线假回复。

## Codex 复杂任务

复杂编程、项目修改、调试，以及需要多步骤执行并生成产物的任务会通过 `delegate_codex` 工具交给本机 Codex App Server。普通聊天和简单电脑操作仍使用 DeepSeek 和原有工具，不需要切换模式。明确的编程执行指令会强制选择委托工具；其他复杂任务由模型根据说明选择。

- 设置 → CODEX：路径留空会从 PATH 和本机 Codex 安装目录查找 `codex.exe`；模型留空沿用 Codex 的默认配置，也可以填写该账号可用的模型 ID。点击「检查连接」查看认证状态；未登录时点击「登录 Codex」在浏览器完成 ChatGPT 登录。
- ChatGPT 登录消耗该账号的 Codex 额度；已有 API 登录按 API 用量计费。DeepSeek 仍承担任务判断和角色回复。程序通过 Codex 管理认证，不复制或显示 Codex 凭据。
- 例如：「帮我写一个 Python 小工具并运行验证」「修复 D:\\Projects\\demo 中的编译报错」「继续上个 Codex 任务，增加导出功能」。新任务默认使用 Agent 工作目录下独立的 `Codex/<任务编号>` 文件夹；修改现有项目时请在指令中明确目录。
- 执行期间显示进度，并提供「停止当前任务」。工作目录之外的目录授权、Codex 请求的命令/文件/权限审批，会在程序内显示。采用 `workspace-write` 和 `on-request`，不会自动开启完全访问。Codex 需要补充信息时会显示输入框。
- 「记录 → 任务」可查看任务要求、工作目录、状态、Codex 任务 ID 和执行报告。输入「继续上个 Codex 任务……」会恢复最近一个已有 ID 的任务。任务保存在 `Application.persistentDataPath/codex-tasks.json`，配置在 `codex-settings.json`，原始会话由 Codex 自行保存。不保证出现在 Codex 桌面应用侧栏。
- 每条消息最多委托一次，单次执行等待上限 30 分钟。取消或断线不会回滚已经产生的文件；未收到停止确认时会明确标为待确认。Codex 未登录或报错时不会暗中改用 DeepSeek 执行同一个复杂任务。若执行后角色回复生成失败，会保留执行器报告，避免重复提交。
- 任务上下文包含当前指令、必要的任务说明及本轮附件，不发送整份人格设定或所有相处记忆。桌面自动化能力仍取决于 Codex 自身安装的工具；接入不等于增加任意鼠标键盘操作能力。

接口依据：[Codex App Server](https://learn.chatgpt.com/docs/app-server)。本次按本机 `codex-cli 0.155.0-alpha.16.4` 生成的协议 Schema 验证，通信使用后台进程的 UTF-8 JSONL 标准输入/输出，不开放网络监听端口。

## Agent 电脑操作

直接输入“打开桌面上的某个 PDF”“搜索某主题的论文”等简单指令。无需切换模式：每条消息都能按需要调用电脑工具；明确的电脑操作请求会要求模型先调用工具，避免直接回复“没有连接设备”。Agent 通过 `deepseek-flash` 的工具调用，反复选择并组合一个通用的 `computer_action` 工具，当前可搜索网页和 Crossref 论文元数据、打开网页或本机文件/目录、列目录、读取和写入文本文件，以及运行 PowerShell 命令。复杂任务采用上文的 Codex 委托。

打开网页 URL 后，程序会尝试抓取 HTML 并提取正文供红莉栖阅读；也可让她“读一下刚才打开的页面”，程序会使用保存的上次 URL。需要登录、依赖 JavaScript 渲染或拒绝直接抓取的网站可能读不到正文，程序会说明失败。说“看看我的电脑屏幕里面有什么”时，她会抓取**当前主屏幕**，把压缩后的截图随本轮请求发送给 `deepseek-flash`，依据图片回复。截图只在请求时获取，不持续监视，也不保存到对话文件。其他显示器、不可见窗口以及任意点击和键盘操作仍不属于此截图能力。

相对路径指向 Agent 工作目录，可在设置里点击「打开 Agent 工作目录」查看。读取或列出工作目录以外的文件、打开本机路径、覆盖文件、写入工作目录以外的位置，以及运行命令时，程序会显示具体路径或命令并等待允许。拒绝后 Agent 会收到拒绝结果。一次请求最多执行 12 个电脑操作、进行 9 轮模型调用；涉及多个步骤的任务可能产生多次 API 费用。网页搜索返回标题和摘要，论文检索返回元数据，不代表已经阅读全文。

角色持续性由设定文件和程序共同维持。默认相处语气偏亲近、自然熟悉，没有手动关系阶段；记忆不会改变她的核心性格。用户要求“不要扮演红莉栖”时，程序仍让模型以她的口吻回应；明显的模型自我介绍式回复会被拦下。明确询问程序是否为 AI 时，角色会简短诚实回答，同时保持对话语气。短语检查无法保证识别所有形式的脱离角色回复。

API Key 使用 Windows 当前用户的 DPAPI 加密，存储在 Unity 的 `Application.persistentDataPath/deepseek-key.bin`。对话记录和自动记忆以普通 JSON 文本保存在同目录的 `conversation.json`，旁边的 `.bak` 是上一份可恢复备份；点击「记录」可回看。程序最多保存最近 250 轮问答，每次请求发送最近十轮和最多 24 条自动记忆。每轮成功对话后，后台再调用一次 `deepseek-flash`，只从用户原话提取值得长期保存的事实或偏好；一次性任务、屏幕内容和网页正文不进入自动记忆。低价值记忆随时间由细节压缩成摘要、核心，再删除；高价值记忆保留。旧版手动填写的称呼与备注会迁移成高价值记忆，旧关系阶段不再使用。图片只随当前请求发送，后续轮次只留文字标记。请求错误时保留输入与附件，原对白不被错误覆盖。角色设定文件在构建后位于 `KurisuCompanion_Data/StreamingAssets/persona.md`，编辑后重启生效。

## 构建与边界

通过 Unity 编辑器运行 `ProjectSetup.BuildWindows`，输出到同级 `KurisuCompanion_Windows`。功能检查包括本机截图、网页正文、记忆压缩/遗忘，并用已保存的 API Key 验证过真实记忆提取和屏幕图片理解调用。自动记忆会增加每轮对话的 API 调用次数；提取失败时保留待处理消息，后续重试。

实现依据：[DeepSeek 图像理解](https://api-docs.deepseek.com/guides/vision/)、[DeepSeek 工具调用](https://api-docs.deepseek.com/guides/tool_calls/)、[DeepSeek JSON 输出](https://api-docs.deepseek.com/guides/json_mode/)、[Crossref API](https://api.crossref.org/)、[Unity UI Toolkit 运行时 UI](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-render-runtime-ui.html)。
