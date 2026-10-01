# Runtime Knowledge Demo

基于 Unity IMGUI 的最小运行时 RAG 接入示例，使用真实本地模型。编辑器 Play Mode 与 Windows x64 Player 共用 KnowledgeRuntime API；不依赖 Odin、TMP 或其他业务框架。需要完整 WManager Knowledge 包及其原生/托管依赖。

## 接入

1. 在 Package Manager 中选择 WManager Knowledge，导入 `Runtime Knowledge Demo`。
2. 场景中新建 GameObject，添加 KnowledgeDemo，Unity 自动添加 KnowledgeRuntime。
3. 在 KnowledgeRuntime 的 Settings 中指定 KnowledgeSettings。建议复制配置并设置独立的 databaseName，例如 runtime-demo-v2.db，避免与业务工作库混用。
4. 配置 generationModel 与 embeddingModel。使用 StreamingAssets 相对 GGUF 路径，将实际文件放入 `Assets/StreamingAssets/Knowledge/Models/`。当前包支持本地 Qwen3.5 与 BGE 模型，示例不会下载模型。
5. 不使用基础库时清空 seedDatabase；使用时需同时提供相匹配的数据库与 seedManifest。发布配置中的模型路径应为 StreamingAssets 相对路径。
6. 关闭 KnowledgeRuntime 的 Initialize On Start，进入 Play Mode 后点击“初始化”；也可保留自动初始化，但其启动异常由 KnowledgeRuntime 报告。

通过 Package Manager 缓存安装时，使用 KnowledgeRuntime Inspector 的“设置当前包的编辑器原生库路径”。Windows Player 使用发布后的 Plugins 目录。目标电脑需安装 Visual C++ x64 运行库，Vulkan 模式需支持的显卡驱动。

## 操作

- 初始化后显示资料列表，空库提示“知识库已就绪，暂无资料”。“刷新资料”只读取已初始化服务；未初始化时提示先初始化，不自动加载模型或创建数据库。
- 填标题和完整正文，点击“保存资料”。首次保存创建 DocumentId；后续保存使用相同 ID，事务替换该资料。点击“新建资料”清空此 ID 后创建另一篇资料。示例不提供已有资料选择、编辑或删除界面。
- 在标题/正文下方的文件路径输入框填写本地完整路径，点击“导入文件”。支持 TXT/MD、DOCX/DOCM、PPTX/PPTM、XLS/XLSX/XLSM、文字型 PDF。相同文件路径再次导入用于更新；文件导入不改变手动输入资料的 DocumentId。
- 问题输入框下，“检索”展示相关正文、章节、分数与来源；“提问”使用配置中的 topK、minimumScore、maximumEvidence 生成流式 RAG 回答。检索范围为全部资料，问答为单轮。
- “复制结果”复制检索或问答内容。“取消”取消当前操作；失败或取消的导入不会提交部分资料更新。已输出的回答草稿可能保留，取消后不得将它当作完整答案。

## 返回状态与生命周期

InsufficientEvidence 表示没有符合条件的资料，未调用生成模型。MissingCitations 表示有证据但模型没有有效来源编号，最终显示引用失败提示。流式文本是草稿，完成后使用 AnswerResult.Text 替换并附加有效来源。页面同时显示首字、检索和总耗时；相似度和有效编号不等于事实正确率。

KnowledgeIndexMismatchException 表示已有非空数据库的索引与当前模型、分块参数或分块版本不兼容，示例显示中文处理提醒，不输出异常堆栈。此时不是“暂无资料”：保留原资料，恢复原配置，或换新的 databaseName 后重新导入；当前分块版本为 2，旧索引需重建，基础库也需重新导出。不会自动删除或迁移旧库。

Progress 回调在 Unity 主线程创建；示例用操作编号、取消状态和销毁标记丢弃过期回调。组件销毁会取消当前任务，KnowledgeRuntime 负责释放服务和模型，业务代码不要重复 Dispose runtime.Service。Shutdown 后同一 Runtime 不能再次初始化。

工作数据库位于 `Application.persistentDataPath/Knowledge/Databases/`，停止/重启后保留。手动编辑的当前 DocumentId 和输入框内容仅在本次运行保留，不作为原文归档。

需要完整资料管理和普通多轮聊天时，导入 `uGUI Knowledge Demo`。普通聊天使用 LlamaChatService，与本示例的单轮 RAG 分开。详细格式限制见包内 `Documentation~/DocumentImport.md`，接口见 `Documentation~/API.md`，发布与升级见 `Documentation~/UserGuide.md`。
