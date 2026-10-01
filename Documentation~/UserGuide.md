# 使用文档

适用：Unity 6000.2、Windows 64 位、本地 GGUF 模型、LLamaSharp 0.27.0、CPU/Vulkan 推理。解析和问答不依赖在线服务。首次部署需要准备模型与目标机器的 Visual C++ x64 运行库。

## 1. 当前工程的 uGUI Demo

打开 `Assets/KnowledgeDemo/KnowledgeUGUIDemo.unity`，进入 Play Mode。场景包含 KnowledgeRuntime、KnowledgeUGUIDemo、Canvas、uGUI 输入框/下拉框/按钮/滚动结果区和 EventSystem。

专用配置是 `Assets/KnowledgeDemo/DemoSettings.asset`，从当前知识库配置复制模型参数。Demo 使用独立数据库 `ugui-demo.db`，不会写入编辑器工作库或默认运行时工作库。初始化由按钮触发，不在场景启动时自动执行。

1. 首先关闭或释放 Knowledge Center、LLM Chat 中已加载的模型，避免重复占用内存和显存。
2. 在“配置”页确认回答模型、向量模型和推理设备。可填 StreamingAssets 相对路径或本机绝对路径，点击“应用配置”，再点击顶部“初始化”。页内修改只作用于本次运行，停止播放后不保存到配置资产。
3. 在“资料”页选择“新增资料”，填写标题与完整正文，点击“保存资料”。例如标题“设备维护”，正文“设备保修期限为24个月，滤网每30天清洁一次，维护前必须断电。”
4. 在“问答”页选择“全部资料”或单篇资料，输入“设备保修期限是多少？”。“检索”只展示相关片段；“提问”输出带来源编号的回答。答案下方列出引用资料、标题和来源路径，顶部显示首字、检索与总耗时。
5. “取消”中断当前操作；“复制内容”复制检索结果、预览片段或回答。结果区支持滚轮与拖动。

### 文件导入、更新与删除

资料页在“本地文档路径”中填完整文件路径，点击“导入文件”。支持 TXT/MD、DOCX/DOCM、PPTX/PPTM、XLS/XLSX/XLSM 和文字型 PDF。文件选择对话框由业务工程提供；本示例采用路径输入，因此无需外部对话框插件。

相同规范路径的文件保持相同 DocumentId：内容不变时跳过重建，文件更新后重新导入会事务替换旧片段。路径变更被视为另一篇资料。

选择已有资料后，可点“查看片段”进入问答页查看索引正文。索引片段包含重叠，无法准确重建原文；因此资料页的正文输入框保持空白。手动更新必须填写完整新正文，然后点击“保存资料”；原文件更新更适合直接重新导入原路径。

删除需先选中资料，点击“删除资料”，再点击“确认删除”。删除资料同时清理其片段和向量。Demo 的工作库重启后保留；需要清空时逐篇删除，或停止运行后备份并清理该专用数据库及其 WAL/SHM 文件。

### 输入与布局

场景使用 uGUI 的 StandaloneInputModule，Project Settings > Player > Active Input Handling 选择 Input Manager (Old) 或 Both。只启用新 Input System 的工程应自行换成 InputSystemUIInputModule。

CanvasScaler 按参考高度 800 缩放。已检查 1280×900、960×540 的控件边界和预览。当前工程使用 MiSans 字体资产，UPM 示例不捆绑该工程字体，运行时尝试 Microsoft YaHei/SimHei/Arial；可在 Inspector 的 Chinese Font 字段指定自己的中文字体。

## 2. 编辑器制作知识库

打开 `Tools > WManager > Knowledge Center`，指定 KnowledgeSettings。设置页选择模型，初始化后在资料页导入文档或手动输入。资料列表提供筛选、删除和正文片段预览；问答页可先检索再提问，并限定检索范围。

编辑器工作库默认为 `UserSettings/Knowledge/Databases/default.db`，实际文件名由配置的 databaseName 决定。运行时工作库位于 `Application.persistentDataPath/Knowledge/Databases`，两者分别维护。

使用“导出基础知识库”导出到 `Assets/StreamingAssets/Knowledge/Base/` 的空目录，得到数据库和 `.manifest.json`。在正式运行时配置 seedDatabase 和 seedManifest，首次启动验证哈希并复制到可写目录；已有用户工作库不覆盖。Demo 默认清空这两个配置，因此只展示可写库流程。

## 3. 模型配置与速度

generationModel 指向回答模型，embeddingModel 指向向量模型。本机已准备 Qwen3.5-2B-Q4_K_M.gguf 与 bge-small-zh-v1.5-q8_0.gguf。无需再次下载同名模型，也不要把 Word/PDF 文件放进 Models 文件夹。

CPU 模式使用分发的 AVX2 原生库；Vulkan 使用显卡驱动，Auto 会尝试 GPU 并在不可用时记录原因后使用 CPU。threads=0 自动选择线程，gpuLayers=99 默认尝试将回答模型全部层放入显卡。向量模型在 CPU 上执行。disableThinking 建议开启，避免小问题仍产生很长思考过程。

模型初始化包含权重读取、哈希计算和可选 GPU 预热，与每轮问答耗时分开。大文档首次导入还需要为每个分块计算向量。回答速度取决于相同模型、提示词长度和输出长度，详细实测见 Performance.md。

已加载模型时不能在 Demo 内应用新配置，请停止播放后修改资产，再重新运行。进程首次加载 CPU 原生库后切换 GPU，以及替换原生 DLL，都可能需要完全重启 Unity/Player。

修改向量模型、查询指令、嵌入上下文或分块参数会改变索引指纹。换一个 databaseName 并重新导入资料，不要把不同向量规则写入同一旧库。

## 4. 发布与业务接入

目标先选择 Windows x64、Mono。将 Demo 场景加入 Build Profiles 的场景列表，或者在自己的场景添加 KnowledgeRuntime 并指定配置。发布模型路径应使用 StreamingAssets 相对路径，构建前确认实际 GGUF 文件存在。

模型放在 `Assets/StreamingAssets/Knowledge/Models/`。构建处理器检查平台、模型和原生版本，并复制 CPU/Vulkan 后端。目标机器安装 Microsoft Visual C++ x64 Redistributable，并为 Vulkan 模式安装显卡驱动。

业务代码通过 `await runtime.InitializeAsync(ct)` 初始化，之后调用 `runtime.Service` 的导入、搜索和问答 API。文件导入使用 `runtime.ImportFileAsync`；文件选择、用户权限、删除确认及 UI 由业务工程管理。主线程创建 `Progress<T>` 才能让进度和流式回调回到 Unity UI 线程。

模型已经加载后重复初始化复用服务；对象销毁时 KnowledgeRuntime 取消任务并释放资源。Shutdown 是终止操作，同一组件不能再次初始化；需要重新启动时创建新的运行时组件。

已在当前 Unity 编辑器中通过 uGUI 控制器和实际本地 Qwen3.5/BGE 完成初始化、保存、限定检索、流式引用回答、文件导入与确认删除检查，测试资料已清理。该检查通过临时场景实例执行，不代表正式 Player 验收；Windows 发布后的持久化与其他工程接入仍需业务验收。IL2CPP 尚未完成兼容性验收。

## 5. 导出其他工程

Knowledge Center 配置页点击“导出独立 UPM 包”，选择外部父目录。将导出的 `com.wmanager.knowledge` 目录放到使用方 Packages，或使用 Add package from disk 选择 package.json。代码、Office/PDF 解析 DLL、CPU/Vulkan 依赖、uGUI 组件、Samples 和文档一同导出。

使用方在 Package Manager 导入 `uGUI Knowledge Demo`，打开导入目录的 KnowledgeUGUIDemo.unity。示例附带 DemoSettings，但模型与基础知识库需另行复制到使用方 StreamingAssets。UPM 示例使用独立字体回退，不依赖当前工程 Assets/Fonts。

如果包安装在 Package Manager 缓存中，选中 KnowledgeRuntime，点击 Inspector 的“设置当前包的编辑器原生库路径”。Player 使用发布目录，不使用该编辑器路径。也可以通过 `Tools > WManager > Create uGUI Knowledge Demo` 在使用方生成场景；存在同名场景时只定位，不覆盖。

## 6. 常见问题

| 现象 | 处理 |
| --- | --- |
| 初始化提示缺少模型或权重失败 | 检查配置路径、GGUF 文件完整性、架构支持和配套原生版本；重新选择文件 |
| 文档为空或 PDF 提示 OCR | 图片没有可读取文字，先做 OCR；旧 DOC/PPT 需另存为新格式 |
| 没有检索结果 | 先确认资料入库、检索范围与向量模型；调整 minimumScore 前先看检索结果 |
| 参数改变后提示索引不兼容 | 使用新 databaseName 并重新导入 |
| 运行时按钮没有响应 | 检查 EventSystem、GraphicRaycaster、Active Input Handling；操作中除取消外主要按钮会锁定 |
| 保存旧资料提示正文为空 | 请输入完整替换正文，或者重新导入原文件 |
| 字体乱码或方框 | 指定覆盖中文字符的字体资产 |
| GPU 切换仍使用 CPU | 先看后端日志；进程已加载 CPU 原生库时完全重启 |

文档格式的具体限制见 DocumentImport.md；完整接口见 API.md。
