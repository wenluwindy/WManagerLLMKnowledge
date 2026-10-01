# uGUI Knowledge Demo

在 Package Manager 中选择 WManager Knowledge，导入 `uGUI Knowledge Demo`，打开 `KnowledgeUGUIDemo.unity`。场景使用 Unity uGUI，不依赖 Odin、TMP 或 WManager 业务 UI。

确认 `DemoSettings.asset` 中的回答模型和向量模型对应本地 GGUF 文件，模型放在使用方 `Assets/StreamingAssets/Knowledge/Models`。示例配置默认 Qwen3.5-2B 与 BGE 中文向量模型。场景启动不会自动加载模型；进入 Play Mode 后点击“初始化”。

示例独立使用 `ugui-demo.db`，位于 `Application.persistentDataPath/Knowledge/Databases`，不安装基础库。资料页支持手动新增/完整正文更新、两次确认删除、文件路径导入和索引片段预览；问答页支持范围选择、检索、流式回答、取消及复制。

Input Handling 请选择 `Input Manager (Old)` 或 `Both`。如果业务工程只启用新 Input System，将场景 EventSystem 的 StandaloneInputModule 替换为 InputSystemUIInputModule。示例默认在 Windows 上使用系统中文字体；可在 KnowledgeUGUIDemo 的 Chinese Font 字段指定自己有授权的字体资产。

通过 Package Manager 缓存安装包时，选中 KnowledgeRuntime，使用 Inspector 的“设置当前包的编辑器原生库路径”。发布后自动改用 Player Plugins 目录。

完整操作与 API 见包内 `Documentation~/UserGuide.md` 和 `Documentation~/API.md`。
