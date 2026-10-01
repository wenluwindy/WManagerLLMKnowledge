# API 文档

命名空间：`WManager.Knowledge`；示例 UI 位于 `WManager.Knowledge.Samples`。程序集为 Core、LLamaSharp、Runtime、UGUI 和仅编辑器使用的 Editor。Core 不引用 UnityEngine，uGUI 组件单独放在 UGUI 程序集中。

## 1. KnowledgeRuntime

Unity MonoBehaviour，添加到活动 GameObject，指定 KnowledgeSettings。默认 Start 自动初始化；Demo 场景关闭了 initializeOnStart，由按钮调用。

| 成员 | 说明 |
| --- | --- |
| `KnowledgeSettings Settings { get; set; }` | 初始化前指定配置；已加载服务不会因赋值自动重建 |
| `bool IsReady { get; }` | 服务已创建 |
| `IKnowledgeService Service { get; }` | 未初始化访问会抛 InvalidOperationException |
| `string LastError { get; }` | 最近初始化失败的消息；导入/问答错误通过任务异常返回 |
| `string EditorNativeLibraryDirectory { set; }` | 编辑器原生目录；Player 忽略此配置 |
| `string ResolveNativeLibraryDirectory()` | 解析当前编辑器/Player 的原生库目录，可供独立聊天服务复用 |
| `Task InitializeAsync(CancellationToken ct = default)` | 初始化、可选基础库安装与模型加载；正在初始化时复用该任务 |
| `Task<ImportResult> ImportFileAsync(string path, IProgress<ImportProgress> progress = null, CancellationToken ct = default)` | 自动初始化、后台解析文件，然后入库 |
| `void Shutdown()` | 取消生命周期任务并释放服务；终止后不能复用该组件 |

InitializeAsync 并发调用共用第一个初始化任务，后续调用者的 ct 不独立控制等待。OnDestroy 调用 Shutdown。Service 的所有方法仍需由调用方传入自己的取消令牌，不能假设外部操作会自动使用组件的生命周期令牌。

```csharp
await runtime.InitializeAsync(ct);
var imported = await runtime.ImportFileAsync(path,
    new Progress<ImportProgress>(p => Debug.Log($"{p.Stage}: {p.Completed}/{p.Total}")), ct);
```

## 2. IKnowledgeService

实现 IDisposable；由 KnowledgeRuntime 创建的服务归该组件管理，业务代码不要提前 Dispose。直接使用工厂时由调用方负责释放。

| 方法 | 返回与行为 |
| --- | --- |
| `InitializeAsync(ct)` | `Task`，检查数据库/索引兼容性 |
| `ImportAsync(ImportRequest request, IProgress<ImportProgress> progress = null, CancellationToken ct = default)` | `Task<ImportResult>`，分块、向量化、事务更新 |
| `ListDocumentsAsync(ct)` | `Task<IReadOnlyList<KnowledgeDocument>>`，资料元数据 |
| `ReadDocumentChunksAsync(string documentId, ct)` | `Task<IReadOnlyList<SearchHit>>`，按序读取索引片段，不执行语义搜索 |
| `DeleteDocumentAsync(string documentId, ct)` | `Task`，删除资料及所属片段/向量 |
| `SearchAsync(SearchRequest request, ct)` | `Task<SearchResult>`，精确余弦相似度检索 |
| `AskAsync(AskRequest request, IProgress<AnswerDelta> stream = null, CancellationToken ct = default)` | `Task<AnswerResult>`，检索后流式生成并校验引用 |
| `ExportAsync(string destinationPath, ct)` | `Task<KnowledgeExport>`，数据库一致性快照与清单元数据；目标文件必须不存在 |

表中的 `ct` 均为 `CancellationToken ct = default`。服务内部串行执行操作；排队与执行都可能被取消。导入失败或取消不提交部分更新，保留旧版本；服务 Dispose 后不能继续调用。片段含重叠，不是原文归档，不能直接拼接用于覆盖原资料。

## 3. 导入契约

`ImportRequest`：DocumentId、Title、Source、Text 均为 string 字段。DocumentId 为空时服务生成 ID；相同 ID 的内容更新替换旧片段。保持 ID 不变才能更新同一资料。

`ImportRequest.FromFile(string path)` 或 `FromFile(string path, CancellationToken ct)` 同步提取文件，生成由规范绝对路径（转大写）哈希得到的 ID；Title 是不含扩展名的文件名，Source 是绝对路径。只负责提取，不计算向量。请用 Task.Run 避免解析阻塞 Unity 主线程。

支持格式与 200 万字符上限见 DocumentImport.md。InvalidDataException 表示格式损坏、空 Office 文档、无文字 PDF 或提取超限；旧 DOC/PPT 和未知格式抛 NotSupportedException；文件不存在等 I/O 异常向上传递。

`ImportProgress`：Stage（Splitting、Embedding、Committed）、Completed、Total、只读 Fraction。总数未知时 Fraction=0；文件解析阶段不提供分块进度。`ImportResult`：DocumentId、ChunkCount、Unchanged。

`KnowledgeDocument`：Id、Title、Source、ContentHash、UpdatedUtc、ChunkCount。UpdatedUtc 是 UTC 字符串。

```csharp
var result = await runtime.Service.ImportAsync(new ImportRequest {
    DocumentId = "manual-v1", Title = "设备维护", Source = "业务系统",
    Text = "保修期限为24个月。"
}, progress, ct);
// 更新仍使用 manual-v1；完整替换而非追加。
await runtime.Service.DeleteDocumentAsync(result.DocumentId, ct);
```

## 4. 检索与问答契约

| 字段 | SearchRequest | AskRequest |
| --- | --- | --- |
| Question | string，必填问题 | string，必填问题 |
| TopK | int，默认 8 | int，默认 8 |
| MinimumScore | float，默认 0.35 | float，默认 0.35 |
| DocumentId | string，空为全部资料 | string，空为全部资料 |
| MaximumEvidence | 无 | int，默认 5；生成上下文的证据上限 |

请求默认值不会自动继承 KnowledgeSettings。调用方应按需将 settings.topK、minimumScore、maximumEvidence 显式传入。Score 是余弦相似度，不是概率或答案置信度。

`SearchResult.Hits` 为 IReadOnlyList&lt;SearchHit&gt;。SearchHit 字段：ChunkId、DocumentId、Title、Source、Heading、Text、Ordinal（从 0 开始）、Score。ReadDocumentChunksAsync 返回的 Score 不表示检索相似度。

`AnswerDelta.Text` 为增量文本。`AnswerResult` 字段：

| 字段 | 含义 |
| --- | --- |
| Text | 最终答案，以此替换流式累积结果 |
| InsufficientEvidence | 无符合条件证据时直接返回，不调用生成模型 |
| MissingCitations | 有检索证据但生成结果没有有效引用；Text 返回校验失败提示，保留 RetrievedEvidence 与耗时 |
| Citations | IReadOnlyList&lt;KnowledgeCitation&gt;，最终有效引用 |
| RetrievedEvidence | 同类型列表，送入提示词的候选证据；不保证都被最终引用 |
| ElapsedMilliseconds | 实际问答总耗时，不含模型初始化或排队 |
| RetrievalMilliseconds | 检索耗时 |
| GenerationMilliseconds | 模型生成耗时 |
| FirstTokenMilliseconds | 首个可见片段的时间；未记录时为 -1 |
| PromptTokens | 最终提示词 token 数 |

KnowledgeCitation 包含 Id（如 S1，不含方括号）和 Hit。来源由程序检索结果提供；模型引用编号会校验，但这不等于每个事实都已验证。省略引用或只使用虚构编号会设置 MissingCitations=true，不自动重试。流式片段属于待校验草稿，最终必须用 AnswerResult.Text 替换。问答目前是单轮 RAG，不保存多轮历史。

```csharp
var settings = runtime.Settings;
var request = new AskRequest {
    Question = "保修期限是多少？", TopK = settings.topK,
    MinimumScore = settings.minimumScore, MaximumEvidence = settings.maximumEvidence
};
// 在 Unity 主线程创建 Progress，回调才会回到主线程。
var stream = new Progress<AnswerDelta>(delta => answerText.text += delta.Text);
answerText.text = "";
var result = await runtime.Service.AskAsync(request, stream, ct);
answerText.text = result.Text;
foreach (var citation in result.Citations)
    Debug.Log($"[{citation.Id}] {citation.Hit.Title}: {citation.Hit.Source}");
```

UI 销毁、取消或下一轮开始后，已排队的 Progress 回调仍可能到达。业务 UI 应用生命周期标记或操作编号丢弃过期片段。Demo 使用 revision 处理这一情况。

## 5. 配置

KnowledgeSettings 继承 LlamaModelSettings，是可序列化 ScriptableObject。Inspector 属性名称与 C# 字段一致。

| 类别 | 字段与主要默认值 |
| --- | --- |
| 模型 | generationModel、embeddingModel；StreamingAssets 相对路径或绝对 GGUF 路径 |
| 工作库/基础库 | databaseName=default.db；seedDatabase、seedManifest；空 seedDatabase 表示不安装基础库 |
| 分块 | chunkTokens=320、overlapTokens=48、embeddingContextTokens=512、queryInstruction |
| 检索 | topK=8、minimumScore=0.35、maximumEvidence=5 |
| 生成 | contextTokens=4096、maximumOutputTokens=512、temperature=0.2、topP=0.9、samplingTopK=40、repeatPenalty=1.1、disableThinking=true、answerSystemPrompt |
| 加速 | acceleration=Auto、gpuLayers=99、threads=0、batchThreads=0、batchTokens=512、microBatchTokens=512、warmupGpu=true |

`ResolveModelPath(value)` 解析模型相对/绝对路径；`ResolveStreamingPath(relative)` 限制路径在 StreamingAssets 内。`CreateKnowledgeOptions(databaseDirectory)` 校验数据库文件名并创建核心参数；`CreateBackendOptions(nativeDirectory)` 创建模型/后端参数。配置字段修改不会自动重载模型或重建索引。

KnowledgeOptions 字段：DatabasePath、ChunkTokens=320、OverlapTokens=48、MaximumDocumentCharacters=2000000。FromFile 提取入口另外有固定 200 万字符上限；调整服务选项不改变提取上限。

## 6. 工厂与扩展接口

`LlamaKnowledgeFactory.CreateAsync(KnowledgeOptions knowledge, LlamaBackendOptions backend, CancellationToken ct = default)` 返回 Task&lt;KnowledgeService&gt;，加载真实模型并初始化服务。backend 必须提供 NativeLibraryDirectory、GenerationModelPath、EmbeddingModelPath。其余生成、分块模型、线程与加速参数详见 LlamaBackendOptions 源码。

```csharp
using (var service = await LlamaKnowledgeFactory.CreateAsync(
    new KnowledgeOptions { DatabasePath = databasePath },
    new LlamaBackendOptions {
        NativeLibraryDirectory = nativeDirectory,
        GenerationModelPath = generationPath,
        EmbeddingModelPath = embeddingPath,
        Acceleration = LlamaAcceleration.Auto
    }, ct))
{
    var result = await service.SearchAsync(new SearchRequest { Question = "保修" }, ct);
}
```

LlamaAcceleration 枚举：Auto、Cpu、Vulkan。`IsBackendInstalled(directory)` 检查配套版本；BackendVersion、BackendBundleVersion 为分发版本常量。KnowledgeService.BackendDescription 是只读后端说明，不属于 IKnowledgeService 接口。

`LlamaKnowledgeFactory.CreateIndexFingerprint(KnowledgeOptions options, LlamaBackendOptions backend)` 从 GGUF 元数据和文件哈希计算索引指纹，不加载模型权重；需要模型提供 embedding_length 元数据。`CreateEmbeddingFingerprint(string modelHash, string queryInstruction, int contextTokens)` 生成与实际向量提供器一致的指纹，modelHash 为模型文件 SHA-256。模型文件哈希仍有磁盘读取开销。

`KnowledgeIndex.SplitterVersion` 当前为 2；`CreateFingerprint(KnowledgeOptions options, string embeddingFingerprint, int dimensions)` 生成完整索引指纹。旧版本非空库不能混用，需新数据库名和原资料重新导入。

注入自定义实现可使用 `new KnowledgeService(options, embeddings, generator)`，或四参数构造函数附加 backendDescription。服务拥有并释放 providers；不要将同一个 provider 交给多个同时销毁的服务。

| 扩展接口 | 必须实现 |
| --- | --- |
| IEmbeddingProvider : IDisposable | Fingerprint、Dimensions、MaxInputTokens；CountTokens(text)；EmbedAsync(text, isQuery, ct) 返回 Task&lt;float[]&gt; |
| IAnswerGenerator : IDisposable | ContextTokens、MaxOutputTokens；CountTokens(prompt)；BuildPrompt(question, evidence)；GenerateAsync(prompt, stream, ct) 返回 Task&lt;string&gt; |

Fingerprint 必须随向量模型或嵌入规则变化而变化；向量必须具有固定维度、合法数值。isQuery 区分查询与资料。生成器须遵守 token 预算及取消约定，提供真实输出。

### 普通聊天接口

uGUI Demo 的普通聊天使用 `LlamaChatService`，不依赖知识库服务。`CreateAsync(LlamaBackendOptions options, string systemPrompt, CancellationToken ct = default)` 只加载回答模型；`SendAsync(string question, IProgress<AnswerDelta> stream = null, CancellationToken ct = default)` 返回 ChatReply，完整回答后提交本轮历史。取消或失败不提交半轮历史。`RestoreHistoryAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)` 恢复完整用户/助手对，空列表清空会话。服务归调用方管理，使用完需 Dispose。

ChatReply 字段：Text、History、PromptTokens、DroppedTurns、FirstTokenMilliseconds、ElapsedMilliseconds。ChatMessage 字段：Role（ChatRole.User/Assistant）、Text、CreatedUtc。历史超出上下文预算时裁剪最早完整轮次，不检索资料或要求来源编号。

```csharp
using (var chat = await LlamaChatService.CreateAsync(
    runtime.Settings.CreateBackendOptions(runtime.ResolveNativeLibraryDirectory()),
    "你是一个可靠的中文助手。", ct))
{
    await chat.SendAsync("请记住我的编号是731。", stream, ct);
    var reply = await chat.SendAsync("我的编号是什么？", stream, ct);
    answerText.text = reply.Text;
}
```

以上 runtime 必须已指定 Settings，stream 是主线程创建的 Progress<AnswerDelta>；无需调用 runtime.InitializeAsync。普通聊天与 RAG 各自加载权重，同时使用会占用额外内存。

## 7. 数据库导出与部署

`ExportAsync(destinationPath, ct)` 仅导出数据库并返回 KnowledgeExport，不自动写 JSON。Unity 中调用 `KnowledgeDeployment.WriteManifest(databasePath, manifest)` 写同名 `.manifest.json`。

```csharp
var manifest = await runtime.Service.ExportAsync(destinationDatabase, ct);
KnowledgeDeployment.WriteManifest(destinationDatabase, manifest);
```

KnowledgeExport 字段：schemaVersion、databaseFile、databaseSha256、indexFingerprint、embeddingFingerprint、dimensions、chunkTokens、overlapTokens、documentCount、createdUtc。

`KnowledgeDeployment.InstallSeedAsync(sourceDatabase, sourceManifest, destinationDatabase, ct, string expectedFingerprint = null)` 校验并复制基础库，已有目标库直接保留，不承担数据库升级或合并。第五参数可选，原四参数调用兼容；自行调用时应传入当前索引指纹以检查模型兼容性。KnowledgeRuntime 首次安装已传入此参数，普通接入无需重复调用。源数据库或清单不存在会抛 FileNotFoundException；校验失败抛 InvalidDataException。

`KnowledgeDeployment.ReadSeedManifest(string sourceDatabase, string sourceManifest)` 检查源文件并通过 Unity JsonUtility 读取清单，请在主线程调用。`KnowledgeIndex.ValidateSeed(string databasePath, KnowledgeExport manifest, string expectedFingerprint = null)` 检查清单字段、文件名、SHA-256 和实际数据库索引元数据；提供 expectedFingerprint 时进一步检查兼容性，以只读方式打开源库。

构建处理器逐个检查构建场景和 Assets 中 Resources 预制体内的 KnowledgeRuntime（含非活动对象）。动态指定配置的项目，通过 `Create > WManager > Knowledge Build Configuration` 创建 `WManager.Knowledge.Editor.KnowledgeBuildConfiguration` 资产，将 Addressables、AssetBundle 或代码动态使用的 KnowledgeSettings 填入 `dynamicSettings`。构建校验所有此类注册资产；该类型仅存在于 Editor 程序集，运行时代码不要引用。配置了基础库的构建会校验实际数据库、清单及当前模型/分块指纹。

编辑器专用：`KnowledgePackageExporter.Export(parentDirectory)` 返回导出包目录，不覆盖已有目标；`KnowledgeDemoSceneBuilder.CreateScene()` 创建 `Assets/KnowledgeDemo/KnowledgeUGUIDemo.unity`，已有场景只定位。运行时不要引用 Editor 程序集。

## 8. 生命周期与错误

所有异步接口使用任务异常报告失败；OperationCanceledException 表示取消。参数错误、索引指纹不兼容、数据库 I/O、文件解析、原生加载与模型加载错误应在 UI 边界捕获并显示。主线程避免 `.Wait()`/`.Result`，使用 await；模型推理和数据库工作由 SDK 后台执行。

`KnowledgeIndexMismatchException : InvalidOperationException` 表示已有非空工作库的索引不兼容，提供中文处理提示。uGUI Demo 将其显示为状态提醒；调用方可单独捕获该类型。不要将它当作空库或自动删除原数据库。

KnowledgeUGUIDemo.BuildUI() 可在空示例对象上创建 uGUI 层级，已有绑定时跳过。Demo 保留所有 UI 引用供 Inspector 检查，在 Awake 注册按钮事件；场景文件中的事件列表为空是正常的。更换或删除绑定组件后应同时维护控制器逻辑。

Windows x64 Mono 为当前优先路径。用户已确认当前工程 Player 运行通过。此次修复通过核心/部署回归、实际 Unity 编译与真实 Qwen3.5/BGE 检查，尚未重新构建 Player；其他工程和 IL2CPP 仍需验收。
