# Amadeus

Windows 上的 Unity AI 陪伴程序原型，包含聊天、图片输入、记忆以及本机 Codex 任务委托。当前使用静态立绘。

## 项目目录

- `UnityCompanion/`：主要应用。包含源码、场景、Unity 设置和包依赖清单。
- `EmoteTest/`：独立的 E-mote 渲染实验，仅保存源码、构建脚本和第三方许可证说明。
- `.codex-integration/mock_server.py`：Unity 编辑器 Codex 检查使用的模拟服务；运行结果不提交。
- `牧濑红莉栖_陪伴型AI人格设定.md`：角色设定参考。主程序实际读取 `UnityCompanion/Assets/StreamingAssets/persona.md`。

## 获取并运行主程序

1. 克隆本仓库，安装 Unity **6000.6.2f1** 及 Windows 构建支持。
2. 在 Unity Hub 中添加并打开 `UnityCompanion` 文件夹，等待依赖导入完成。
3. 如需显示立绘，将你有权使用的 PNG 放到 `UnityCompanion/Assets/Resources/kurisu.png`。该路径及其 `.meta` 被忽略，不会上传。仓库不附带游戏立绘，未配置时角色图片区域为空。
4. 打开 `Assets/Scenes/Companion.unity`，进入 Play 模式。在应用设置中填写自己的 DeepSeek API Key。
5. 如需使用 Codex 委托，另行安装 Codex 并在本机完成登录；可在程序设置中指定可执行文件路径并检查连接。

平台为 Windows；密钥加密、文件选择、截图和电脑操作包含 Windows 专用实现。

详细操作、数据存储和功能边界见 [Unity 项目说明](UnityCompanion/README.md)。

## Windows 构建

现有编辑器入口为 `ProjectSetup.BuildWindows`。也可以在仓库根目录使用 PowerShell 调用 Unity 批处理构建，将下列 Unity 路径替换为实际安装位置：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe' -batchmode -quit -projectPath "$PWD\UnityCompanion" -executeMethod ProjectSetup.BuildWindows -logFile "$PWD\unity-build.log"
```

构建输出为 `KurisuCompanion_Windows/`，成功后可运行根目录的 `启动红莉栖陪伴程序.cmd`。首次克隆没有编译成品，必须先构建。构建目录和日志不提交。

## 可选 E-mote 实验

主程序无需先构建 `EmoteTest`。实验代码依赖未随仓库分发的 FreeMote 工具、原生驱动和游戏模型，因此不能仅凭本仓库直接构建运行该实验。

- `build.ps1` 预期在 `extracted_emote/tools/FreeMote/lib/` 找到 `FreeMote.NET.dll` 和 `emotedriver.dll`，并在其父目录找到 `FreeMote.LICENSE.txt`。
- 模型准备脚本依赖 `extracted_emote/decompiled_characters/`、`extracted_emote/motion/motion/`、FreeMote 的 `PsBuild.exe` 以及 Python + Pillow。
- 只在本地配置你有权使用的资源。操作说明见 [E-mote 实验说明](EmoteTest/README.md)，第三方许可见 [FreeMote 声明](EmoteTest/FreeMote.LICENSE.txt)。

## 版本管理范围

根目录 `.gitignore` 使用允许列表，保留维护中的项目文件，排除提取素材、剧情文本集、语音补丁、模型缓存、预览截图、Unity Library、构建产物和本机运行记录。新增根目录文件或项目时，需要有意更新允许列表。

API Key、聊天记录、自动记忆和 Codex 认证属于本机用户数据，不应复制到仓库。应用的加密密钥文件位于 Unity `Application.persistentDataPath`，也不应提交。

现有 `CodexChecks` 自动检查依赖当前用户目录下的 Codex 捆绑 Python 路径；换电脑后需要具备相同运行环境或调整检查中的 Python 路径。涉及真实 API 的检查需要个人凭据并可能产生费用。

## 素材与许可证

本仓库当前不声明统一的开源许可证。第三方组件及游戏素材各自的权利和许可证保持不变；仓库设为私有不代表获得素材分发授权。提取的游戏资源、游戏立绘及语音补丁不随源码提交。公开项目或发布带素材的安装包之前，应分别核实源码依赖和素材的分发条件。
