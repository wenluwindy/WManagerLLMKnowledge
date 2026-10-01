# WManager Knowledge

Windows x64 的本地知识库 Unity 插件。编辑器与发布程序共用 LLamaSharp + SQLite + RAG 核心。

当前版本：0.1.0。Unity：6000.2。LLamaSharp 和 CPU backend：0.24.0。

## 快速开始

1. 下载模型，放到 `Assets/StreamingAssets/Knowledge/Models/`：
   - `Qwen3-4B-Q4_K_M.gguf`：https://huggingface.co/Qwen/Qwen3-4B-GGUF/blob/main/Qwen3-4B-Q4_K_M.gguf
   - `bge-small-zh-v1.5-q8_0.gguf`：https://huggingface.co/CompendiumLabs/bge-small-zh-v1.5-gguf/blob/main/bge-small-zh-v1.5-q8_0.gguf
2. 打开 `Tools > WManager > Knowledge Center`。
3. 选择 `Assets/Settings/Knowledge/KnowledgeSettings.asset`，或点击“新建配置”。默认路径已配置为上述文件。
4. 在配置页点击“初始化知识库”。加载大型模型和计算文件哈希需要时间。
5. 在资料页导入 UTF-8 TXT/Markdown，或填写标题和正文保存。
6. 在问答页先检索，再提问。答案按 `[S1]` 格式显示引用。

没有模型时，初始化会明确提示缺少文件，不会生成模拟答案。

## 编辑器与运行时数据库

编辑器工作库位于 `UserSettings/Knowledge/Databases/default.db`。

发布后工作库位于 `Application.persistentDataPath/Knowledge/Databases/default.db`。

在编辑器资料页“导出基础知识库”，选择 `Assets/StreamingAssets/Knowledge/Base/`，生成 `default.db` 与 `default.manifest.json`。运行时首次启动校验 SHA-256 后复制到可写目录，已有用户库不覆盖。导出目标存在时请另选空目录，避免意外覆盖。

修改 embedding 模型、查询指令、上下文上限、分块大小或重叠会改变索引指纹。已有资料的数据库不允许混用新规则。使用新的 databaseName 并重新导入。

## 发布程序接入

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

导入文件可以调用 runtime.ImportFileAsync；文件选择界面由业务工程提供。SDK 不依赖原生文件对话框。

从 Package Manager 的 Samples 导入 Runtime Knowledge Demo，场景中添加 KnowledgeRuntime 与 KnowledgeDemo、指定配置，即可测试运行时手动录入和流式问答。不要同时加载编辑器和运行时模型，除非内存足够。

## 导出给其他工程

配置页点击“导出独立 UPM 包”，选择一个外部父目录。得到 `com.wmanager.knowledge` 文件夹，包含代码、程序集、托管 DLL、Windows CPU 原生依赖、示例和文档。

使用方将整个目录放入自己的 Packages 目录，或使用 Package Manager 的 Add package from disk 选择 package.json。模型与基础知识库另外复制到使用方的 StreamingAssets/Knowledge。

本插件不强制依赖 WManager、UniTask、Odin、Addressables 或现有 UI 框架。

## 依赖与构建

当前包已附带依赖 DLL，使用方无需安装 .NET SDK。开发者重新安装或切换 CPU 指令集时使用项目根目录的 `Tools/Knowledge/InstallDependencies.ps1`。默认 noavx 兼容方案，可选 avx2 加快支持该指令集的电脑。切换后必须重启 Unity，且发布目标 CPU 必须兼容。

原生目录：`Plugins/Windows/x86_64/`。发布处理器把 CPU 依赖复制到 `<Application>_Data/Plugins/x86_64/`。

CPU 后端依赖 Microsoft Visual C++ x64 运行库（MSVCP140、VCRUNTIME140、VCRUNTIME140_1、VCOMP140）。目标电脑需安装兼容的 Visual C++ Redistributable，安装包应包含官方运行库安装步骤。官方下载：https://aka.ms/vs/17/release/vc_redist.x64.exe 。当前开发电脑已完成原生库加载检查。

运行时默认从 embedded package 路径找到编辑器原生库。如果通过 Package Manager 缓存安装，在 KnowledgeRuntime Inspector 中点击“设置当前包的编辑器原生库路径”。Player 始终使用发布目录。

## 当前验收范围

- 核心自动化验证涵盖真实 SQLite 持久化、事务更新、取消、导出快照、指纹不兼容、向量异常、来源校验和 Unicode 分块。
- LLamaSharp 后端完成原生库加载检查。
- 使用 Unity 6000.2.3f1 编译了完整 Runtime、Editor 与示例源代码。
- 真实 GGUF 模型未提供，尚未验证生成质量、BGE 向量正确性、模型加载峰值内存和推理速度。
- 正式 Windows 发布与其他工程接入请在实际模型配置后验收；IL2CPP 兼容性尚未确认。
- 目前只支持 TXT/Markdown、单轮问答、CPU、精确向量检索。PDF/Word、GPU、关键词融合、多轮历史与大型索引尚未实现。

详见 `Documentation~/DevelopmentPlan.md` 与 `Documentation~/ImplementationStatus.md`。
