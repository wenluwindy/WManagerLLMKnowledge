# WManager Knowledge 开发方案

更新时间：2026-10-01。首版平台：Windows 10/11 x64，Unity 6000.2，完全离线。

## 1. 目标

制作可独立导出的 Unity UPM 插件。编辑器和发布程序共用同一套知识库核心，支持文本资料导入、分块、向量生成、检索、带来源问答、更新、删除和持久化。扩展知识库通过更新文档索引实现，不需要训练模型。

核心不依赖 WManager 单例、UI、Addressables 或 UniTask。Unity 接入层使用 Task 和 CancellationToken，使用方可以自行适配 UniTask。

## 2. 架构

```text
编辑器 Knowledge Center / 发布后 KnowledgeRuntime
                       ↓
                IKnowledgeService
                       ↓
文本解析 → Token 分块 → IEmbeddingProvider → SQLite
                       ↓
问题向量 → 余弦检索 → 上下文预算 → IAnswerGenerator
                       ↓
                流式回答 + 来源引用
```

程序集：WManager.Knowledge.Core、WManager.Knowledge.LLamaSharp、WManager.Knowledge.Runtime、WManager.Knowledge.Editor。LLamaSharp 后端与核心分别封装，模型文件独立配置。

## 3. 技术基线

| 模块 | 首版方案 |
| --- | --- |
| 生成引擎 | LLamaSharp 0.27.0 + 同版本 CPU/Vulkan backend，支持 Qwen3/Qwen3.5 |
| 生成模型 | Qwen3-4B GGUF Q4_K_M；低配置可选择 Qwen3-1.7B，需实际评估 |
| 向量模型 | BGE-small-zh-v1.5 的兼容 GGUF；默认 CLS 池化，L2 归一化 |
| 数据库 | Windows 自带 winsqlite3，通过参数化 SQLite API 存储 |
| 搜索 | 内存精确余弦检索；规模增长后引入向量索引实现 |
| 文档 | TXT、Markdown、Word (.docx/.docm)、PPT (.pptx/.pptm)、Excel (.xls/.xlsx/.xlsm)、文字型 PDF 和手动文本；文本支持 UTF-8、带 BOM 的 UTF-16/UTF-32、Windows GBK/GB18030 |
| 发布 | Windows x64，CPU AVX2 / Vulkan GPU；老机器可安装 noavx，IL2CPP 必须独立验收 |

LLamaSharp 的托管依赖与原生后端版本必须保持一致。GGUF 扩展名不能保证模型架构、池化、分词兼容。BGE 输入上限默认 512 Token，分块默认 320 Token、重叠 48 Token。

SQLite 初版采用 Windows 系统库，不引入服务器，也不依赖全局安装 .NET SDK。其他平台需要替换 SQLite 原生库和模型后端，并重新验证。

Windows 原生 CPU 后端需要 Microsoft Visual C++ x64 运行库；发布安装包需要处理此依赖。

## 4. 数据和更新规则

- 文档保存 ID、标题、来源、正文哈希、更新时间和分块数量。
- 分块保存正文、章节、序号、文档 ID、向量、向量维度。
- 数据库记录 schema version 和索引指纹。
- 索引指纹涵盖模型文件 SHA-256、向量维度、查询指令、池化和分块参数。
- 模型或索引规则变更时阻止混用已有向量；允许显式重建到新库。
- 导入先完成全部分块与向量，再通过事务替换文档；失败或取消不修改原文档。
- 同一文档 ID 更新替换旧分块；删除在一个事务内同步删除文档与分块。
- 更换生成模型通常不需要重建索引。

## 5. 问答规则

向量检索取 Top K，按生成模型实际 Token 数筛选上下文；保留系统提示、问题和输出预算。生成模型使用模型适配的聊天模板，Qwen3/Qwen3.5 非思考模式在生成前补齐关闭思考的模板后缀。证据按 [S1]、[S2] 编号。程序校验回答中的引用 ID，只返回实际存在的引用。

相似度不是正确率。阈值需要真实问题集调优。检索无结果时直接返回缺少依据。首版只接收当前问题，不默认持久保存聊天；多轮历史作为后续扩展，并必须受 Token 预算约束。

导入文本作为证据，不能覆盖系统规则。模型权重可复用，推理上下文和向量上下文分别串行访问，后台执行，Unity UI 回主线程更新。取消及资源释放需要处理退出、停止播放和脚本重载。

## 6. 文件部署

基础数据库和模型可以随包放到 StreamingAssets/Knowledge；第一次运行复制基础数据库到 persistentDataPath/Knowledge。用户工作库只写 persistentDataPath。复制使用临时文件和原子替换，已有数据库不覆盖。模型保留真实磁盘文件路径，避免加载为 TextAsset。

应用更新保留用户工作库。首版不会自动合并新版基础库，后续增加稳定文档 ID、迁移清单和冲突记录。知识库导出使用 SQLite backup API 生成一致性快照，并写出清单与 SHA-256。

## 7. 编辑器和运行时

编辑器窗口：创建配置、选择模型、初始化、导入文本/Office/PDF、手动文本、文档列表、删除、检索预览、流式问答、取消、导出数据库、导出 UPM。

运行时组件：使用同一配置初始化知识库，公开导入、搜索、问答和删除接口，退出时取消任务并释放资源。提供可运行 IMGUI 示例作为接入参考，不侵入项目业务 UI。

构建检查：Windows x64、生成/向量模型、匹配 backend/bundle 标记、CPU/Vulkan 原生依赖、基础数据库与索引指纹。禁止将编辑器代码编入 Player。

## 8. 阶段和验收

| 阶段 | 交付 | 验收 |
| --- | --- | --- |
| 1 | 独立包、方案、核心、SQLite、模型适配、依赖安装工具 | 编译，事务导入、持久化、引用和取消测试 |
| 2 | 编辑器窗口、运行时组件、示例、导出 | 可从编辑器制作基础库，Player 继续扩展 |
| 3 | 实际 GGUF、Windows Player 验证 | 原生加载、向量、中文问答、离线冷启动 |
| 4 | 发布质量与性能 | 真实问题集评估、内存/速度、异常恢复、全新工程接入 |
| 5 | 扩展 | OCR、旧版 .doc/.ppt、关键词融合、大规模向量索引、多轮 RAG 对话；Office/文字型 PDF 与 GPU 加速已接入 |

实际模型文件需由用户选择或提供。没有模型时，不以模拟向量或固定答案冒充完整 RAG。核心逻辑测试使用明确标注的测试提供器；--model-smoke 使用 SDK 分发的实际 DLL 与本地 GGUF 检查加载、中文检索与问答。发布兼容性和业务问题集答案质量仍需使用实际模型确认。

## 9. 交付结构

SDK 是 Packages/com.wmanager.knowledge，可直接复制为 embedded package，或通过编辑器导出到外部目录。模型包单独交付，携带来源、许可证和哈希。知识库包包含 database 与 manifest。第三方 notices 和安装依赖锁定文件一起交付。

## 10. 参考

- https://github.com/SciSharp/LLamaSharp
- https://github.com/eublefar/LLAMASharpUnityDemo
- https://huggingface.co/Qwen/Qwen3-4B
- https://huggingface.co/BAAI/bge-small-zh-v1.5
