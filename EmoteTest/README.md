# 红莉栖 E-mote 测试窗口

双击本目录的 `AmadeusTest.exe`，或上一级的 `启动红莉栖测试.cmd`。
运行环境：Windows x64、.NET Framework 4.8、支持 DirectX 9 的显卡。
已编译，运行时无需安装 Python、开发工具或联网。

## 操作

- 顶部下拉框：白衣 / 常服，ab / c 两种姿势，共四个模型。
- 底部下拉框或「上一个 / 下一个」：切换该模型内的表情、眼神、脸红及手臂差分；循环切换。
- ab 模型各 45 项，c 模型各 31 项。使用模型原有日文名称。
- 「重播」重新应用当前差分。「重置视图」恢复默认大小和位置。
- 在立绘上滚轮缩放、按住鼠标左键拖动。
- 每次选择先应用模型的初始化状态，因此差分独立预览，不累积组合。

## 实现和限制

使用 FreeMote.NET + 原生 E-mote 驱动实时渲染，并非静态图片轮播。
原始模型使用 BC7 纹理，附带 DX9 驱动直接加载会崩溃；`cache/kurisu0.psb` 至 `kurisu3.psb` 是改用 RGBA8 纹理的兼容副本。
原始 `extracted_emote` 素材未修改。额外的动作/网格二进制资源从原模型直接提取，避免转成浮点 JSON 后损失精度。

这是兼容性测试版：当前驱动下头发、衣服的部分区域仍有细碎纹理/遮罩瑕疵，c 姿势较明显；不能把它视作与游戏内画面完全一致的最终渲染方案。
已验证四个模型来回切换、普通/其他表情和手臂差分、循环选择、退出。截图和记录在 `test-output`，运行日志在 `app.log`。

## 开发

- `Program.cs`：窗口、实时渲染、差分控制及冒烟检查。
- `build.ps1`：调用系统 .NET Framework 编译器生成 x64 可执行文件。
- `prepare-models.ps1 -Python <python.exe 路径>`：重建兼容副本，需 Python + Pillow，以及原目录的 FreeMote 工具。
- `AmadeusTest.exe --test`：自动加载四个模型和代表差分，检查非空渲染，输出 PNG 和记录，然后退出。

工具来源：<https://github.com/UlyssesWu/FreeMote>、<https://github.com/UlyssesWu/FreeMote.NET>。
附带 FreeMote.LICENSE.txt；第三方组件及游戏素材的权利不因本测试应用而改变。
