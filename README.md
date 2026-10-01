# WManager Knowledge

Windows x64 的本地知识库 Unity 插件。编辑器与发布程序共用 LLamaSharp + SQLite + RAG 核心。

当前包版本：**1.0.1**（以 `package.json` 为准）。Unity：6000.2；当前优先部署目标为 Windows x64 / Mono。LLamaSharp、CPU 和 Vulkan backend：0.27.0，支持 Qwen3.5。用户已确认当前工程 Player 运行通过；此次修复后的 Player、其他工程接入和 IL2CPP 仍需验收。

2026-10-01 修复：逐个构建场景和动态配置检查、缺失基础库报错、章节标题继承、无有效引用回答拦截。分块索引版本升级为 2：旧的非空数据库需保留原资料，改用新的 `databaseName` 后重新导入，并重新导出基础库；不会自动迁移或删除旧库。未使用基础库时清空 `seedDatabase`，已有配置不会自动修改。

构建自动检查场景中的活动/非活动 KnowledgeRuntime 和 Assets 中 Resources 预制体。Addressables、AssetBundle 或代码动态指定的配置，需通过 `Create > WManager > Knowledge Build Configuration` 创建资产并填入 `dynamicSettings`。详情见使用指南。

模型推理、检索和文档解析在本地执行，不依赖在线服务；首次部署需准备 GGUF 模型、Microsoft Visual C++ x64 运行库，以及 GPU 模式所需的显卡驱动。包依赖 `com.unity.ugui` 2.0.0，核心程序集不引用 UnityEngine。

## 文档导航

| 文档 | 内容 |
| --- | --- |
| [使用指南](Documentation~/UserGuide.md) | 编辑器、uGUI Demo、发布与常见问题 |
| [API 文档](Documentation~/API.md) | 生命周期、导入、检索、问答与部署接口 |
| [文档导入](Documentation~/DocumentImport.md) | 格式、编码、提取内容和限制 |
| [性能记录](Documentation~/Performance.md) | 本机 CPU/GPU 实测与复现条件 |
| [实施状态](Documentation~/ImplementationStatus.md) | 已实现功能、历史验证与待验收项 |
| [开发计划](Documentation~/DevelopmentPlan.md) | 设计与后续扩展 |
| [IMGUI 运行时示例](Samples~/RuntimeDemo/README.md) | 最小脚本接入、文件导入、资料刷新、检索和 RAG 问答 |
| [uGUI 示例](Samples~/UGUIDemo/README.md) | 场景接入、资料管理、RAG 与普通多轮聊天 |

## 快速开始

1. 准备模型；已有兼容 GGUF 可直接选用，无需重复下载。新配置默认使用以下模型，放到 `Assets/StreamingAssets/Knowledge/Models/`：
   - `Qwen3-4B-Q4_K_M.gguf`：https://huggingface.co/Qwen/Qwen3-4B-GGUF/blob/main/Qwen3-4B-Q4_K_M.gguf
   - `bge-small-zh-v1.5-q8_0.gguf`：https://huggingface.co/CompendiumLabs/bge-small-zh-v1.5-gguf/blob/main/bge-small-zh-v1.5-q8_0.gguf
2. 打开 `Tools > WManager > Knowledge Center`。
3. 选择 `Assets/Settings/Knowledge/KnowledgeSettings.asset`，或点击“新建配置”。新配置默认指向上述文件；已有资产可能使用其他模型，请以设置页显示的路径为准。
4. 在配置页点击“初始化知识库”。加载大型模型和计算文件哈希需要时间。
5. 在资料页点击“导入文档”，支持 TXT/Markdown、Word (.docx/.docm)、PPT (.pptx/.pptm)、Excel (.xls/.xlsx/.xlsm) 和文字型 PDF，也可填写标题和正文保存。文本支持 UTF-8、带 BOM 的 UTF-16/UTF-32，以及 Windows 中文 GBK/GB18030 编码；编辑器和发布后使用相同导入逻辑。详细范围见 `Documentation~/DocumentImport.md`。
6. 在问答页先检索，再提问。答案按 `[S1]` 格式显示引用。

没有模型时，初始化会明确提示缺少文件，不会生成模拟答案。

当前工程已准备 `Qwen3.5-2B-Q4_K_M.gguf` 和 `bge-small-zh-v1.5-q8_0.gguf`，已有文件无需重新下载。导出给其他工程时，模型不随 SDK 自动复制，需要将模型另行放到该工程的 StreamingAssets，并调整配置路径。

## Odin 窗口与大模型聊天

安装 Odin Inspector 时，知识中心和聊天窗口使用 Odin 参数绘制、中文参数名称与分类页签；没有 Odin 的独立项目使用 Unity 默认参数面板。

知识中心支持按标题/来源筛选资料、指定单篇资料检索、查看引用正文和复制回答。“最大证据数”用于控制问答引用范围。模型加载后先释放模型，再修改参数并重新初始化。配置保存到所选 KnowledgeSettings 资产。

打开 `Tools > WManager > LLM Chat`，或点击知识中心的“大模型聊天”。选择 LlamaChatSettings 资产，在“模型与参数”页配置 GGUF 模型、上下文、输出长度、采样、线程、GPU 和系统提示词。当前项目的 `Assets/Settings/Knowledge/LlamaChatSettings.asset` 指向已有 Qwen3.5-2B 模型。聊天仅加载回答模型，无需向量模型或知识库。

对话支持流式输出、取消、复制消息、新会话和 JSON 导入/导出。取消或失败保留输入，完整问答才提交到历史。模型重新加载后恢复窗口历史；超出上下文预算时按完整问答轮次裁剪最早历史。导出用于长期保存会话。两个窗口独立加载模型，同时使用会占用额外内存和显存。

## 自定义模型

配置中的 generationModel 和 embeddingModel 可以自由指定 StreamingAssets 相对路径或本机绝对路径。设置页的“选择回答模型…”和“选择向量模型…”会校验 GGUF 文件并保存路径，页面显示文件的实际架构，不按文件名猜测。

已使用本地 `Qwen3.5-2B-Q4_K_M.gguf`（qwen35）与 `bge-small-zh-v1.5-q8_0.gguf`（bert）验证权重加载、中文检索和带引用回答。模型无需改名，选择自己的文件即可。其他模型仍需满足当前 llama.cpp 的架构与聊天模板支持。当前向量适配采用 CLS 池化；自定义向量模型需要与此池化及查询指令相匹配。

原生后端升级时，旧 DLL 会被当前 Unity 进程占用。新版 CPU/Vulkan 原生文件放在包内 `NativeUpdate~`，完全退出并重新打开 Unity 后自动安装。仅重载脚本无法替换。若自动安装失败，退出 Unity 后运行项目中的 `Tools/Knowledge/InstallDependencies.ps1`。

SDK 会检查 backend.version.txt 和 backend.bundle.txt，阻止托管 DLL 与原生库混用。升级完成前不能初始化、构建或导出知识库 SDK。向量后端版本进入索引指纹；从旧 LLamaSharp 版本升级需要重建旧索引。CPU 指令集与回答 GPU 模式的调整不改变索引指纹；此次分块版本变更需要重新导入旧索引资料。

## 回答速度

配置中的 acceleration 可选择 Auto、Cpu 或 Vulkan。Auto 在驱动可用时使用 GPU，否则使用 CPU 并记录原因。gpuLayers 默认 99，使当前 2B 模型全部层上显卡；大模型显存不足时可降低此值。向量模型保持 CPU，以保留当前索引行为。Vulkan 使用显卡驱动提供的运行库，无需安装 CUDA Toolkit。

disableThinking 默认开启；Qwen3/Qwen3.5 的回答模板会在生成前关闭思考段，而非只把生成的思考文字隐藏。模型权重和执行器复用；单轮问答仍隔离推理上下文。GPU 初始化包含一次短预热，将首次着色器编译移到初始化阶段。第一次初始化可能比后续慢。

CPU 模式优先使用 AVX2 后端，本机 Ryzen 9600X 支持。threads=0 自动使用逻辑线程数的一半；更多线程不一定更快。目标电脑必须支持所分发的 CPU 指令集，老机器可重新安装 noavx 后端。backend.cpu.txt 记录实际安装的版本。

CPU 模式首次初始化只加载 CPU 原生库；随后切到 GPU 需要完全重启 Unity/Player，因为原生库在进程内保持加载。已使用 Auto/Vulkan 加载 GPU 后端的进程可以释放模型后切到 CPU 计算。更换原生库或指令集也必须重启。

问答底部显示检索、首字、生成和总耗时；AnswerResult 同时提供这些统计与 PromptTokens。总耗时从问答实际执行开始计，不包含模型初始化及排队。性能对比需使用相同 GGUF、提示词 Token 数、输出长度和预热条件，不能用无资料的短问句直接对比 RAG 长上下文。实测数据和复现方法见 `Documentation~/Performance.md`。

## 编辑器与运行时数据库

编辑器工作库位于 `UserSettings/Knowledge/Databases/default.db`。

发布后工作库位于 `Application.persistentDataPath/Knowledge/Databases/default.db`。

在编辑器资料页“导出基础知识库”，选择 `Assets/StreamingAssets/Knowledge/Base/`，生成数据库与同名 `.manifest.json`（默认 `default.db`、`default.manifest.json`）。确认运行时配置的 `seedDatabase`、`seedManifest` 指向对应的 StreamingAssets 相对路径。运行时首次启动检查文件名、SHA-256、数据库索引元数据和当前模型/分块指纹后复制到可写目录，已有用户库不覆盖，不自动升级或合并。导出目标存在时请另选空目录，避免意外覆盖。仅使用可写库时，将 `seedDatabase` 留空；新配置和 uGUI Demo 默认不安装基础库。配置了基础库却缺少文件或清单时，构建及首次安装会明确报错。

修改 embedding 模型、查询指令、向量模型上下文上限、分块大小、重叠或分块版本会改变索引指纹。已有资料的数据库不允许混用新规则，会抛出 `KnowledgeIndexMismatchException`。这不是空库：保留原资料，恢复兼容配置，或使用新的 databaseName 并重新导入。分块版本已为 2；重建后还需重新导出基础库并更新 seedDatabase/seedManifest。修改回答模型的上下文上限不改变向量索引指纹。

## 发布程序接入

### 示例选择

| 示例 | 用途 | 使用方式 |
| --- | --- | --- |
| Runtime Knowledge Demo（IMGUI） | 最小运行时 API 接入，手动资料、文件导入、刷新、检索、单轮 RAG、取消和复制 | 导入 Sample，在 GameObject 上添加 KnowledgeDemo 并指定 KnowledgeRuntime.Settings；见 [README](Samples~/RuntimeDemo/README.md) |
| uGUI Knowledge Demo | 完整资料管理、限定检索、单轮 RAG、普通多轮聊天与会话配置 | 导入 Sample 并打开场景；见 [README](Samples~/UGUIDemo/README.md) |

两个示例的“刷新”都只读取已初始化的资料列表：未初始化提示先初始化，空库提示“暂无资料”，有资料显示篇数。刷新不会自动加载模型。索引不兼容显示中文处理提醒，不会将旧库误当成空库或删除资料。

### uGUI 场景

当前工程已有 uGUI 示例场景：`Assets/KnowledgeDemo/KnowledgeUGUIDemo.unity`，专用配置为同目录 `DemoSettings.asset`，使用独立 `ugui-demo.db`，不会写入编辑器默认库。进入 Play Mode 后由按钮触发初始化；“应用配置”只修改本次运行，不保存到配置资产，模型加载后不能应用新配置。

在 Package Manager 中也可导入 `uGUI Knowledge Demo`；示例包括资料管理、文件路径导入、片段预览、限定资料检索、流式问答、取消、复制和本次运行模型配置。也可使用 `Tools > WManager > Create uGUI Knowledge Demo` 生成场景，已有同名场景只定位、不覆盖。

示例新增“普通聊天”页，直接发送即可加载回答模型，支持多轮会话、流式输出、取消、新会话、复制与释放聊天模型，不需要先初始化知识库。会话在本次运行期间保留，普通聊天与 RAG 各自加载模型。

普通聊天的取消或失败保留输入与完整历史；“释放聊天模型”保留本次会话，下次发送重新加载后恢复；“新会话”清空历史。停止运行后不保存。系统提示词可在 KnowledgeUGUIDemo Inspector 的 Chat System Prompt 中设置。只测试普通聊天时无需加载向量模型；同时初始化 RAG 会额外占用内存和显存。

示例使用 `StandaloneInputModule`，Active Input Handling 需为 Input Manager (Old) 或 Both；仅启用新 Input System 的工程需替换为 `InputSystemUIInputModule`。UPM 示例不依赖本工程字体，可在 Inspector 的 Chinese Font 字段指定自己的中文字体。完整操作见 [使用指南](Documentation~/UserGuide.md)，开发接口见 [API 文档](Documentation~/API.md)。

### 业务 API

把 KnowledgeRuntime 添加到 GameObject，指定 KnowledgeSettings。默认 Start 时初始化。服务准备好后通过 `runtime.Service` 调用导入、删除、搜索和问答。使用示例按钮手动初始化时，建议关闭 Initialize On Start。

以下示例使用 `System` 和 `WManager.Knowledge` 命名空间；`runtime`、`text`、`progress` 和 `ct` 由业务代码提供。请在 Unity 主线程通过 `await` 调用，不要使用 `.Wait()` 或 `.Result`。

```csharp
await runtime.InitializeAsync(ct);

await runtime.Service.ImportAsync(new ImportRequest
{
    DocumentId = "product-manual",
    Title = "产品说明",
    Source = "用户输入",
    Text = "这里是正文"
}, progress, ct);

var settings = runtime.Settings;
text.text = "";
var answer = await runtime.Service.AskAsync(
    new AskRequest
    {
        Question = "产品如何维护？",
        TopK = settings.topK,
        MinimumScore = settings.minimumScore,
        MaximumEvidence = settings.maximumEvidence
    },
    new Progress<AnswerDelta>(delta => text.text += delta.Text),
    ct);

text.text = answer.Text;
```

`SearchRequest` 和 `AskRequest` 的默认值不会自动继承配置资产；需要时显式传入配置字段，`DocumentId` 可限定单篇资料。`InsufficientEvidence` 表示检索缺少依据，不调用生成模型；`MissingCitations` 表示有证据但模型没有有效引用，最终 Text 返回校验失败提示。流式输出属于草稿，必须以最终 Text 替换。RAG 问答当前为单轮，不保留多轮历史；LLM Chat 的多轮对话不等于多轮 RAG。引用编号会校验，但不代表每个事实均已验证，相似度也不是答案置信度。

更新资料复用 DocumentId，删除调用 DeleteDocumentAsync。只有导入全部分块和向量成功后才提交数据库，失败或取消保留旧资料。相同规范路径的文件保持相同 DocumentId，路径变更视为另一篇资料。索引片段带重叠，不能直接拼接作为原文覆盖。

对 UI 使用在主线程创建的 Progress；直接实现 IProgress 时回调可能在后台线程。取消或 UI 销毁后已排队的回调仍可能到达，业务 UI 应丢弃过期片段。调用方应为 Service 操作显式传入取消令牌并处理异常。KnowledgeRuntime 销毁时释放资源；`Shutdown()` 是终止操作，同一组件不能再次初始化。

导入文件可以调用 `await runtime.ImportFileAsync(path, progress, ct)`，支持与编辑器相同的 Office/PDF 格式；文件选择界面由业务工程提供。SDK 不依赖原生文件对话框，也无需安装 Office。

普通聊天通过 `LlamaChatService.CreateAsync` 创建，使用 `runtime.Settings.CreateBackendOptions(runtime.ResolveNativeLibraryDirectory())` 提供回答模型参数，无需调用 runtime.InitializeAsync。`SendAsync` 自动维护多轮历史，`RestoreHistoryAsync` 用于恢复或清空完整轮次；直接创建的聊天服务由调用方 Dispose。完整示例见 [API 文档](Documentation~/API.md)。

IMGUI 示例保存资料后复用同一 DocumentId 更新；“新建资料”开始另一篇资料。文件导入不会改变手动资料的 DocumentId。它不提供已有资料选择、编辑或删除界面，完整管理使用 uGUI 示例。示例已加入过期回调保护，取消或销毁后不会让上一轮流式回调覆盖下一轮结果。不要同时加载编辑器和运行时模型，除非内存足够。

## 导出给其他工程

配置页点击“导出独立 UPM 包”，选择一个外部父目录。得到 `com.wmanager.knowledge` 文件夹，包含代码、程序集、托管 DLL、Windows CPU/Vulkan 原生依赖、示例和文档。

使用方将整个目录放入自己的 Packages 目录，或使用 Package Manager 的 Add package from disk 选择 package.json。模型与基础知识库另外复制到使用方的 StreamingAssets/Knowledge。

本插件不强制依赖 WManager、UniTask、Odin、Addressables 或现有 UI 框架。

## 依赖与构建

当前包已附带依赖 DLL，使用方无需安装 .NET SDK。开发者重新安装或切换 CPU 指令集时使用项目根目录的 `Tools/Knowledge/InstallDependencies.ps1`。默认 avx2；老机器可用 `-CpuVariant noavx`。安装工具同时准备匹配的 Vulkan 库。切换后必须重启 Unity，且发布目标 CPU 必须兼容。

原生目录：`Plugins/Windows/x86_64/`。Vulkan 的整套匹配 DLL 放在其 `Vulkan~` 子目录，避免 Unity 自动导入同名 DLL；加载器在第一次初始化时选择一个完整库族。发布处理器把 CPU 依赖与 Vulkan~ 子目录复制到 `<Application>_Data/Plugins/x86_64/`。

CPU 后端依赖 Microsoft Visual C++ x64 运行库（MSVCP140、VCRUNTIME140、VCRUNTIME140_1、VCOMP140）。目标电脑需安装兼容的 Visual C++ Redistributable，安装包应包含官方运行库安装步骤。官方下载：https://aka.ms/vs/17/release/vc_redist.x64.exe 。当前开发电脑已完成原生库加载检查。

运行时默认从 embedded package 路径找到编辑器原生库。如果通过 Package Manager 缓存安装，在 KnowledgeRuntime Inspector 中点击“设置当前包的编辑器原生库路径”。Player 始终使用发布目录。

## 文档导入限制

- TXT/Markdown 支持 Unicode 与 Windows GBK/GB18030；单份文件最多提取 200 万字符，超限需拆分。
- Word/PPT 宏格式只读取内容，不执行宏；旧版 `.doc/.ppt` 需先转换为 `.docx/.pptx`。
- Excel 读取公式缓存值，不重新计算公式；缺少缓存时请先在表格软件中计算并保存。
- PDF 仅提取文字层，不做 OCR；混合 PDF 只读取有文字的部分，多栏顺序与表格结构可能失真。
- 图片、图表图像和嵌入附件不做识别；加密文档需先解除密码。文件解析取消可能在解析器返回或下一段、页、行时才生效。

详细提取范围、依赖版本和来源标记见 [文档导入](Documentation~/DocumentImport.md)。

## 当前验收范围

此次修复完整回归 77 项、部署专项 12 项、实际 Unity 检查 10 项通过。Unity 检查使用真实 Qwen3.5/BGE，验证引用回答、基础库安装及构建指纹一致性。Unity 编译无错误，插件分程序集编译无警告、无错误；工程另有文件对话框示例 WWW 废弃警告。以下为修复前的历史记录，完整时间线见 [实施状态](Documentation~/ImplementationStatus.md)：

后续更新：uGUI 普通聊天通过真实模型的 9 项交互检查及两种窗口尺寸渲染检查；资料刷新通过实际 Unity 的 6 项检查。更新后的 IMGUI 示例按独立程序集编译零警告、零错误，尚未另做运行时交互或 Player 验收。上述检查数量分别对应不同测试入口，不代表一次统一的 Player 测试。

- 核心自动化验证涵盖真实 SQLite 持久化、事务更新、取消、导出快照、指纹不兼容、向量异常、来源校验和 Unicode 分块。
- 文档扩展完整回归使用 SDK 实际分发 DLL，记录为 72 项通过，覆盖 Office/PDF 解析与提取后入库检索；另有编码专项回归。
- 当前 Unity 6000.2.3f1 的 Core、LLamaSharp、Runtime、UGUI、Editor 与示例分别编译通过，记录为零警告、零错误。
- 已用当前工程的临时 uGUI 场景实例与真实 Qwen3.5/BGE 验证初始化、保存、限定检索、流式引用回答、MD 文件导入和确认删除。不是模拟推理，也不等同于正式 Player 验收。
- 本机 CPU/GPU 的 RAG 问答速度见 [性能记录](Documentation~/Performance.md)；真实业务问题集质量、峰值内存和正式发布性能尚未评估。
- 用户已确认当前工程 Player 运行通过；此次修复尚未重新构建 Player，不能据此确认全部首次安装、CPU/GPU 和异常场景。其他工程接入与 IL2CPP 尚未验收。
- 扫描文档 OCR、旧版 `.doc/.ppt`、关键词融合、多轮 RAG 历史、大规模 ANN 索引、基础库迁移与冲突合并尚未实现。

后续设计见 [开发计划](Documentation~/DevelopmentPlan.md)。
