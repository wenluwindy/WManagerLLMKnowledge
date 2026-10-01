# WManager Knowledge

Windows x64 的本地知识库 Unity 插件。编辑器与发布程序共用 LLamaSharp + SQLite + RAG 核心。

当前版本：0.1.4。Unity：6000.2。LLamaSharp、CPU 和 Vulkan backend：0.27.0，支持 Qwen3.5。

## 快速开始

1. 下载模型，放到 `Assets/StreamingAssets/Knowledge/Models/`：
   - `Qwen3-4B-Q4_K_M.gguf`：https://huggingface.co/Qwen/Qwen3-4B-GGUF/blob/main/Qwen3-4B-Q4_K_M.gguf
   - `bge-small-zh-v1.5-q8_0.gguf`：https://huggingface.co/CompendiumLabs/bge-small-zh-v1.5-gguf/blob/main/bge-small-zh-v1.5-q8_0.gguf
2. 打开 `Tools > WManager > Knowledge Center`。
3. 选择 `Assets/Settings/Knowledge/KnowledgeSettings.asset`，或点击“新建配置”。默认路径已配置为上述文件。
4. 在配置页点击“初始化知识库”。加载大型模型和计算文件哈希需要时间。
5. 在资料页点击“导入文档”，支持 TXT/Markdown、Word (.docx/.docm)、PPT (.pptx/.pptm)、Excel (.xls/.xlsx/.xlsm) 和文字型 PDF，也可填写标题和正文保存。文本支持 UTF-8、带 BOM 的 UTF-16/UTF-32，以及 Windows 中文 GBK/GB18030 编码；编辑器和发布后使用相同导入逻辑。详细范围见 `Documentation~/DocumentImport.md`。
6. 在问答页先检索，再提问。答案按 `[S1]` 格式显示引用。

没有模型时，初始化会明确提示缺少文件，不会生成模拟答案。

## Odin 窗口与大模型聊天

安装 Odin Inspector 时，知识中心和聊天窗口使用 Odin 参数绘制、中文参数名称与分类页签；没有 Odin 的独立项目使用 Unity 默认参数面板。

知识中心支持按标题/来源筛选资料、指定单篇资料检索、查看引用正文和复制回答。“最大证据数”用于控制问答引用范围。模型加载后先释放模型，再修改参数并重新初始化。配置保存到所选 KnowledgeSettings 资产。

打开 `Tools > WManager > LLM Chat`，或点击知识中心的“大模型聊天”。选择 LlamaChatSettings 资产，在“模型与参数”页配置 GGUF 模型、上下文、输出长度、采样、线程、GPU 和系统提示词。当前项目的 `Assets/Settings/Knowledge/LlamaChatSettings.asset` 指向已有 Qwen3.5-2B 模型。聊天仅加载回答模型，无需向量模型或知识库。

对话支持流式输出、取消、复制消息、新会话和 JSON 导入/导出。取消或失败保留输入，完整问答才提交到历史。模型重新加载后恢复窗口历史；超出上下文预算时按完整问答轮次裁剪最早历史。导出用于长期保存会话。两个窗口独立加载模型，同时使用会占用额外内存和显存。

## 自定义模型

配置中的 generationModel 和 embeddingModel 可以自由指定 StreamingAssets 相对路径或本机绝对路径。设置页的“选择回答模型…”和“选择向量模型…”会校验 GGUF 文件并保存路径，页面显示文件的实际架构，不按文件名猜测。

已使用本地 `Qwen3.5-2B-Q4_K_M.gguf`（qwen35）与 `bge-small-zh-v1.5-q8_0.gguf`（bert）验证权重加载、中文检索和带引用回答。模型无需改名，选择自己的文件即可。其他模型仍需满足当前 llama.cpp 的架构与聊天模板支持。当前向量适配采用 CLS 池化；自定义向量模型需要与此池化及查询指令相匹配。

原生后端升级时，旧 DLL 会被当前 Unity 进程占用。新版 CPU/Vulkan 原生文件放在包内 `NativeUpdate~`，完全退出并重新打开 Unity 后自动安装。仅重载脚本无法替换。若自动安装失败，退出 Unity 后运行项目中的 `Tools/Knowledge/InstallDependencies.ps1`。

SDK 会检查 backend.version.txt 和 backend.bundle.txt，阻止托管 DLL 与原生库混用。升级完成前不能初始化、构建或导出知识库 SDK。向量后端版本进入索引指纹；从旧 LLamaSharp 版本升级需要重建旧索引。本次仍使用 0.27.0，CPU 指令集与回答 GPU 模式的调整不改变索引指纹，无需重新导入资料。

## 回答速度

配置中的 acceleration 可选择 Auto、Cpu 或 Vulkan。Auto 在驱动可用时使用 GPU，否则使用 CPU 并记录原因。gpuLayers 默认 99，使当前 2B 模型全部层上显卡；大模型显存不足时可降低此值。向量模型保持 CPU，以保留当前索引行为。Vulkan 使用显卡驱动提供的运行库，无需安装 CUDA Toolkit。

disableThinking 默认开启；Qwen3/Qwen3.5 的回答模板会在生成前关闭思考段，而非只把生成的思考文字隐藏。模型权重和执行器复用；单轮问答仍隔离推理上下文。GPU 初始化包含一次短预热，将首次着色器编译移到初始化阶段。第一次初始化可能比后续慢。

CPU 模式优先使用 AVX2 后端，本机 Ryzen 9600X 支持。threads=0 自动使用逻辑线程数的一半；更多线程不一定更快。目标电脑必须支持所分发的 CPU 指令集，老机器可重新安装 noavx 后端。backend.cpu.txt 记录实际安装的版本。

CPU 模式首次初始化只加载 CPU 原生库；随后切到 GPU 需要完全重启 Unity/Player，因为原生库在进程内保持加载。已使用 Auto/Vulkan 加载 GPU 后端的进程可以释放模型后切到 CPU 计算。更换原生库或指令集也必须重启。

问答底部显示检索、首字、生成和总耗时；AnswerResult 同时提供这些统计与 PromptTokens。总耗时从问答实际执行开始计，不包含模型初始化及排队。性能对比需使用相同 GGUF、提示词 Token 数、输出长度和预热条件，不能用无资料的短问句直接对比 RAG 长上下文。实测数据和复现方法见 `Documentation~/Performance.md`。

## 编辑器与运行时数据库

编辑器工作库位于 `UserSettings/Knowledge/Databases/default.db`。

发布后工作库位于 `Application.persistentDataPath/Knowledge/Databases/default.db`。

在编辑器资料页“导出基础知识库”，选择 `Assets/StreamingAssets/Knowledge/Base/`，生成 `default.db` 与 `default.manifest.json`。运行时首次启动校验 SHA-256 后复制到可写目录，已有用户库不覆盖。导出目标存在时请另选空目录，避免意外覆盖。

修改 embedding 模型、查询指令、上下文上限、分块大小或重叠会改变索引指纹。已有资料的数据库不允许混用新规则。使用新的 databaseName 并重新导入。

## 发布程序接入

当前工程已有 uGUI 示例场景：`Assets/KnowledgeDemo/KnowledgeUGUIDemo.unity`，专用配置为同目录 DemoSettings.asset，使用独立 ugui-demo.db。在 Package Manager 中也可导入 `uGUI Knowledge Demo`；示例包括资料管理、文件路径导入、片段预览、限定资料检索、流式问答、取消、复制和本次运行模型配置。完整操作见 `Documentation~/UserGuide.md`，开发接口见 `Documentation~/API.md`。

把 KnowledgeRuntime 添加到 GameObject，指定 KnowledgeSettings。默认 Start 时初始化。服务准备好后通过 `runtime.Service` 调用导入、删除、搜索和问答。

```csharp
await runtime.InitializeAsync(ct);

await runtime.Service.ImportAsync(new ImportRequest
{
    DocumentId = "product-manual",
    Title = "产品说明",
    Source = "用户输入",
    Text = "这里是正文"
}, progress, ct);

var answer = await runtime.Service.AskAsync(
    new AskRequest { Question = "产品如何维护？" },
    new Progress<AnswerDelta>(delta => text.text += delta.Text),
    ct);

text.text = answer.Text;
```

更新资料复用 DocumentId，删除调用 DeleteDocumentAsync。只有导入全部分块和向量成功后才提交数据库。对 UI 使用在主线程创建的 Progress；直接实现 IProgress 时回调可能在后台线程。

导入文件可以调用 `await runtime.ImportFileAsync(path, progress, ct)`，支持与编辑器相同的 Office/PDF 格式；文件选择界面由业务工程提供。SDK 不依赖原生文件对话框，也无需安装 Office。

从 Package Manager 的 Samples 导入 Runtime Knowledge Demo，场景中添加 KnowledgeRuntime 与 KnowledgeDemo、指定配置，即可测试运行时手动录入和流式问答。不要同时加载编辑器和运行时模型，除非内存足够。

## 导出给其他工程

配置页点击“导出独立 UPM 包”，选择一个外部父目录。得到 `com.wmanager.knowledge` 文件夹，包含代码、程序集、托管 DLL、Windows CPU/Vulkan 原生依赖、示例和文档。

使用方将整个目录放入自己的 Packages 目录，或使用 Package Manager 的 Add package from disk 选择 package.json。模型与基础知识库另外复制到使用方的 StreamingAssets/Knowledge。

本插件不强制依赖 WManager、UniTask、Odin、Addressables 或现有 UI 框架。

## 依赖与构建

当前包已附带依赖 DLL，使用方无需安装 .NET SDK。开发者重新安装或切换 CPU 指令集时使用项目根目录的 `Tools/Knowledge/InstallDependencies.ps1`。默认 avx2；老机器可用 `-CpuVariant noavx`。安装工具同时准备匹配的 Vulkan 库。切换后必须重启 Unity，且发布目标 CPU 必须兼容。

原生目录：`Plugins/Windows/x86_64/`。Vulkan 的整套匹配 DLL 放在其 `Vulkan~` 子目录，避免 Unity 自动导入同名 DLL；加载器在第一次初始化时选择一个完整库族。发布处理器把 CPU 依赖与 Vulkan~ 子目录复制到 `<Application>_Data/Plugins/x86_64/`。

CPU 后端依赖 Microsoft Visual C++ x64 运行库（MSVCP140、VCRUNTIME140、VCRUNTIME140_1、VCOMP140）。目标电脑需安装兼容的 Visual C++ Redistributable，安装包应包含官方运行库安装步骤。官方下载：https://aka.ms/vs/17/release/vc_redist.x64.exe 。当前开发电脑已完成原生库加载检查。

运行时默认从 embedded package 路径找到编辑器原生库。如果通过 Package Manager 缓存安装，在 KnowledgeRuntime Inspector 中点击“设置当前包的编辑器原生库路径”。Player 始终使用发布目录。

## 当前验收范围

- 核心自动化验证涵盖真实 SQLite 持久化、事务更新、取消、导出快照、指纹不兼容、向量异常、来源校验和 Unicode 分块。
- LLamaSharp 后端完成原生库加载检查。
- 使用 Unity 6000.2.3f1 编译了完整 Runtime、Editor 与示例源代码。
- 已在命令行中使用 SDK 实际分发的 .NET Standard 托管 DLL 和本地 Qwen3.5/BGE 完成模型加载、中文检索、带引用回答验证，共 27 项检查通过。当前工程的 Unity 重启日志已确认新版原生后端安装成功。
- 已在本机使用 SDK 的 .NET Standard DLL 测量 CPU/GPU 的 RAG 问答速度，详见性能记录；真实业务问题集质量和峰值内存尚未评估。
- 正式 Windows 发布与其他工程接入请在实际模型配置后验收；IL2CPP 兼容性尚未确认。
- 目前支持 TXT/Markdown、Word/PPT/Excel/文字型 PDF、单轮 RAG 问答、CPU/Vulkan GPU、精确向量检索。扫描文档 OCR、旧版 .doc/.ppt、关键词融合、多轮 RAG 历史与大型索引尚未实现。

详见 `Documentation~/DevelopmentPlan.md` 与 `Documentation~/ImplementationStatus.md`。
