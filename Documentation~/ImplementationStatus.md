# 实施状态

日期：2026-10-01。

## 已实现

- 0.1.4：uGUI Demo 场景、独立 ugui-demo.db、资料管理/文件路径导入/片段预览、限定资料检索、流式问答/取消/复制、本次运行配置。UI 位于独立 UGUI 程序集，依赖 com.unity.ugui；附带 UPM Sample 和场景生成菜单。使用文档 UserGuide.md，API 文档 API.md。

- 独立 UPM 包与四个程序集，核心无 UnityEngine 引用。
- 锁定 LLamaSharp 0.27.0 与同版本 CPU/Vulkan 后端，支持 Qwen3.5；附带托管依赖与原生升级文件。
- Windows SQLite 数据库、外键删除、事务替换、WAL、一致性 backup 导出。
- 模型哈希和索引指纹；不兼容参数阻止混用。
- Token 限制与重叠分块，中文标题、Unicode 边界处理。
- TXT/Markdown、Word (.docx/.docm)、PPT (.pptx/.pptm)、Excel (.xls/.xlsx/.xlsm)、文字型 PDF、手动文本、导入取消与进度、文档增删改。编辑器与运行时共用解析；保留表格、幻灯片备注、工作表与单元格坐标、PDF 页码标记。扫描件 OCR 和旧 .doc/.ppt 暂不支持。
- 文本编码：优先严格 UTF-8，识别 UTF-8/UTF-16/UTF-32 BOM，无 BOM 的非 UTF-8 文件在 Windows 上尝试 GB18030/GBK；编辑器和运行时共用，损坏序列报错而不替换字符。
- LLamaSharp CLS 向量适配与归一化、精确余弦检索。
- 生成模型聊天模板、上下文预算、流式输出、思考标签过滤、来源编号校验。
- 编辑器 Knowledge Center 与配置资源。
- KnowledgeRuntime、基础库校验安装、运行时持久化和 IMGUI 接入示例。
- UPM 文件夹导出、构建原生依赖复制和基础模型检查。
- 编辑器停止播放、脚本重载和场景销毁时取消任务及释放资源。
- 自定义模型文件选择、GGUF 魔数/元数据校验及架构显示。
- 配套后端版本标记、锁定时暂存原生文件、Unity 重启后自动应用升级；初始化/构建/导出拒绝混用旧后端。
- AVX2 CPU 分发、Auto/Cpu/Vulkan 配置、GPU 层数控制、GPU 初始化预热；Qwen 非思考生成后缀修复和 StatelessExecutor 复用。
- 问答检索、首字、生成、总耗时与提示词 Token 数统计。

## 验证记录

0.1.4：当前 Unity 6000.2.3f1 实际编译零警告、零错误；命令行按 Core、LLamaSharp、Runtime、UGUI、Editor 和示例程序集分别编译，零警告、零错误。在当前工程生成了完整 uGUI 场景，验证组件引用、EventSystem、页签切换、会话配置隔离、复制和 1280×900/960×540 控件边界，渲染截图后修正布局与文本留白。未创建新工程。

通过当前编辑器的临时场景实例，使用 uGUI 控制器和本地 Qwen3.5/BGE 完成真实模型初始化、手动保存、限定检索、流式引用回答、MD 文件导入和两次确认删除。实际回答“根据提供的资料，设备的保修期限为 24 个月 [S1]。”测试创建的资料和临时文件已清理。独立 UPM 场景不引用本工程字体或编辑器绝对原生目录。正式 Player 与其他工程尚待验收。

0.1.3 文档扩展：新增 23 项文档检查，覆盖真实 DOCX/DOCM、PPTX/PPTM、XLSX/XLSM、BIFF XLS 和中文双页 PDF，验证标题、表格、页眉、幻灯片关系顺序和备注、工作表、稀疏坐标、数字格式、公式缓存值、来源元数据、提取后入库检索、取消、损坏文件和空文档。完整回归使用 SDK 实际分发 DLL，共 72 项通过；本次不运行模型推理性能测试。Core、LLamaSharp、Runtime、Editor 与示例使用当前 Unity 引用分别编译，零警告、零错误。

文档检查也在当前 Unity 6000.2.3f1 自带 Mono 下通过；命令行需设置 MONO_PATH 为该安装的 MonoBleedingEdge/lib/mono/4.5/Facades，以解析 Unity 提供的 System.Memory 等门面程序集。未创建或运行其他 Unity 工程。这是 Mono 控制台兼容性检查，不等同于正式 Player 或 IL2CPP 验收。文档导入范围和限制见 DocumentImport.md。

修复跨程序集 CS0200：BackendDescription 改为在 KnowledgeService 的公开构造函数中初始化，保持只读，保留原三参数构造函数。此前 UnityCompile 将全部源码合并为单一程序集，未覆盖 internal 的跨程序集访问限制；现按 Core、LLamaSharp、Runtime、Editor 四个程序集与实际分发的托管 DLL 分别编译，示例单独编译，零警告、零错误。修复后核心验证 42 项通过；本次未重跑模型性能测试。

当前 0.1.2：47 项检查通过，包含真实 Qwen3.5/BGE 模型的检索与引用回答、首片段后取消以及取消后的下一次提问。完整 Runtime/Editor/示例源码使用当前 Unity 引用编译，零警告、零错误。验证控制台加载 SDK 分发的 .NET Standard DLL 和暂存的匹配原生库。本机长资料样例纯 CPU 从约 35 秒降至约 2.4 秒；短资料纯 CPU 首字约 0.85 秒、完成约 1.4 秒；Vulkan GPU 预热后完成约 0.31–0.34 秒。见 Performance.md，不能视为所有业务问题的速度保证。

AVX2 + Vulkan 新库已准备在 NativeUpdate~。当前 Unity 仍占用原 CPU noavx DLL，需完全退出并重新打开 Unity 后自动安装，才会在窗口里使用新版；不需要重新下载模型或重建 0.27.0 索引。当前配置保留用户的 Qwen3.5，acceleration=Auto，gpuLayers=99。正式 Player 的 GPU 加速与业务问题集尚待验收。

本次修复文档导入 DecoderFallbackException：新增 14 项编码与入库回归检查，包含 GBK 中文、GB18030 四字节字符、六种 Unicode 编码/BOM 组合、空文件及非法/截断字节。现有验证工具无模型推理模式共 38 项检查通过，完整插件源码使用当前 Unity 引用编译，零警告、零错误。GBK/GB18030 解码使用 Windows 系统 API，无需新增 I18N 或编码包依赖。未取得用户本次报错的原文档，原文件需在 Unity 内重试导入；本次未重跑模型推理或正式 Player 验收。

27 项检查通过，包括核心事务/取消/引用逻辑、GGUF 校验、真实权重加载、中文向量检索和实际回答。完整插件源码使用当前 Unity 6000.2.3f1 引用编译，零警告、零错误。

本地 Qwen3.5-2B-Q4_K_M.gguf 的架构为 qwen35；旧 0.24.0 报 unknown model architecture。升级 0.27.0 后，通过命令行加载 SDK 分发的 .NET Standard DLL 与新版原生 DLL，使用用户本地的 Qwen3.5 与 BGE 文件验证成功。示例资料规定保修 24 个月，实际输出“根据提供的资料，设备保修期限为 24 个月 [S1]。”并通过引用校验。

用户已重启 Unity，编辑器日志确认“已安装 LLamaSharp 0.27.0 CPU 后端”，backend.version.txt 为 0.27.0。默认配置保留用户选择的 Qwen3.5-2B。SDK 层真实模型检查已通过，Unity 窗口问答由用户继续测试。未创建新 Unity 工程。

曾完成一次隔离 Unity 工程的 Windows Mono 原生库烟雾验证，SQLite 和 llama.cpp 均加载成功。该检查不包括实际 GGUF 推理，也不代表正式 Player 验收。用户后续要求暂不使用新工程，已停止继续独立工程验证。

临时独立验证工程已停止使用；清理删除被系统策略拦截，因此 Tools/Knowledge/UnityValidation 目录仍保留，不再继续运行。当前工程保留插件、源码编译工具和核心测试工具。Windows CPU 后端的 PE 依赖已检查，需要 Microsoft Visual C++ x64 运行库。

核心逻辑验证的生成与向量提供器是明确标注的测试替身。--model-smoke 单独使用真实模型。一次中文烟雾验证不能代替业务问题集质量评估。

## 后续验收

1. 当前工程导入包与默认配置，确认 Unity 无编译错误。
2. 完全重启 Unity，在设置页确认模型与 Auto/Cpu/Vulkan 模式，初始化并确认升级后的 Unity 内推理和耗时。
3. 导入中文说明资料，验证检索、带引用回答与无依据拒答。
4. 更新、删除、取消导入、重启编辑器，验证持久化与旧内容清理。
5. 导出基础库，当前工程 Windows x64 Player 验证首次安装、运行时扩展、重启恢复。
6. 评估速度、内存和真实问题集；之后再安排其他工程接入与 IL2CPP。

## 后续扩展

PDF/Word/OCR、中文关键词融合、大规模 ANN 索引、向量缓存、CUDA 可选后端、多轮会话、基础库版本迁移与冲突合并。
