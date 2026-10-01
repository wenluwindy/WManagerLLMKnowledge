# 回答性能记录

日期：2026-10-01。使用当前工程的验证控制台，未创建新 Unity 工程。

## 原因和调整

原先分发的 CPU 原生库为 noavx：原生系统信息没有 AVX/AVX2 优化标记。本机 Ryzen 5 9600X 支持 AVX2，换用 0.27.0 的 avx2 库。旧实现固定 CPU，不使用本机 RTX 5070 Ti。

Qwen3.5 官方模板的非思考生成后缀是 `<think>\n\n</think>\n\n`。LLamaSharp 所调用的内置 ChatML 模板不处理 enable_thinking 参数，仅在用户消息末尾添加 /no_think 不等同于设置完整后缀。现按实际 GGUF 架构为 Qwen3/Qwen3.5 补齐后缀，其他模型保留原模板；开启思考时，流过滤器也能识别提示词已打开的思考段。

原先每次回答新建 StatelessExecutor，其构造器和 InferAsync 各分配一次上下文。现执行器复用，减少一次无用的上下文构建；单轮问题仍使用独立上下文，不会串入上一次回答。GPU 预热在服务初始化完成前执行，首轮问答无需再承担首次着色器编译。

新增检索、生成、首个可见非空白片段和总耗时，以及提示词 Token 数统计。问答计时不包含模型初始化和服务队列等待。

## 本机实测

- CPU：AMD Ryzen 5 9600X，6 核 12 线程；本次 CPU 推理使用 6 线程。
- GPU：NVIDIA GeForce RTX 5070 Ti，约 16GB 显存，驱动 616.64。
- 内存：约 32GB。
- 模型：用户本地 Qwen3.5-2B-Q4_K_M.gguf 与 bge-small-zh-v1.5-q8_0.gguf。
- 使用 SDK 分发的 .NET Standard 托管 DLL 与实际原生 DLL，非模拟推理。

问题为“设备保修期限是多少？”，回答约 27 个字符，含实际来源引用。构造 5 份相同资料，服务去重后送入 1 个证据片段；不是 5 个不同证据的压力测试。优化前后长资料内容一致，模板后缀按修复调整。

| 配置 | 首字 | 完成 | 输入 |
| --- | --- | --- | --- |
| 原实现，CPU noavx | 31.26–31.99 秒 | 34.82–35.75 秒 | 相同长资料，旧模板 |
| 优化后，CPU AVX2 | 1.79–1.87 秒 | 2.37–2.42 秒 | 262 Token |
| 优化后，CPU AVX2，短资料 | 0.84–0.90 秒 | 1.39–1.43 秒 | 127 Token |
| 优化后，Vulkan GPU，预热后 | 约 0.22 秒 | 0.31–0.34 秒 | 262 Token |

CPU 测试首次原生加载选择纯 CPU 库，无 GPU 参与生成。GPU 测试日志确认使用 RTX 5070 Ti、25/25 层 offload，向量模型仍为 CPU。答案事实与引用均通过检查。

首次未预热 GPU 测试为首字 15.49 秒、完成 16.19 秒；第二次完成 0.31 秒。加入初始化预热后，问答第一次完成约 0.34 秒。首次驱动着色器编译转移到初始化，驱动缓存与系统负载会影响初始化用时。

以上是短答案样例，未使用用户的业务文档，不是所有问题的速度保证。输入更多证据、生成长答案、开启思考都会增加耗时。CPU 加速与模板修复同时应用，这不是各项优化单独贡献的消融实验。

## 与 LLMUnity 的比较

读取的公开源码显示，LLMUnity 使用 LlamaLib 常驻服务，按可用硬件架构选择原生库，默认关闭 reasoning。其 batchSize 默认也是 512；不能仅因使用 C# 或 LLamaSharp 就判定会慢。此前固定 noavx 的分发选择是本插件实现的问题。

用户提供的“1 秒回答”尚未在本机用完全相同的 GGUF、提示词、输出长度和缓存条件重测。当前短资料 CPU 样例首字约 0.85 秒、完成约 1.4 秒；较长资料样例需要处理 262 Token，约 2.4 秒。首字与完整答案需分开比较，缓存命中与冷提示词处理也需区分。

参考源码：

- https://github.com/undreamai/LLMUnity/blob/main/Runtime/LlamaLib/LlamaLib.cs
- https://github.com/undreamai/LLMUnity/blob/main/Runtime/LLM.cs
- https://github.com/SciSharp/LLamaSharp/blob/v0.27.0/LLama/LLamaStatelessExecutor.cs
- https://huggingface.co/Qwen/Qwen3.5-2B/blob/main/tokenizer_config.json

## 复现

先完全退出 Unity，运行安装工具，重新打开 Unity。默认安装 AVX2 + Vulkan：

```powershell
./Tools/Knowledge/InstallDependencies.ps1 -CpuVariant avx2
```

使用已有的验证工具分别运行，不要同时运行多个性能测试：

```powershell
dotnet run --project Tools/Knowledge/Validation/Knowledge.Validation.csproj -c Release -p:UseUnityAssemblies=true -- --benchmark --cpu
dotnet run --project Tools/Knowledge/Validation/Knowledge.Validation.csproj -c Release -p:UseUnityAssemblies=true -- --benchmark --cpu --short
dotnet run --project Tools/Knowledge/Validation/Knowledge.Validation.csproj -c Release -p:UseUnityAssemblies=true -- --benchmark
```

原生库仍被 Unity 占用、安装文件处于暂存状态时，在验证命令末尾增加 `--pending`；验证控制台可以独立加载暂存库，但这不表示当前 Unity 已切换 DLL。`--no-warmup` 可观察未预热的 GPU 首轮表现。

要支持较旧 CPU，可使用 `-CpuVariant noavx` 重新分发。AVX2 包要求目标 CPU 支持该指令集。GPU 使用驱动提供的 Vulkan；大模型显存不足时降低 gpuLayers。正式 Windows Player 和 IL2CPP 性能需要在实际发布后另行验收。
