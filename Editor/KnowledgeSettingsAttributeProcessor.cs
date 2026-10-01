#if ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEngine;

namespace WManager.Knowledge.Editor
{
    // Apply editor presentation without adding Odin dependencies to runtime assets.
    internal sealed class KnowledgeSettingsAttributeProcessor : OdinAttributeProcessor<LlamaModelSettings>
    {
        private static readonly Dictionary<string, string[]> Fields = new Dictionary<string, string[]>
        {
            { "generationModel", new[] { "模型", "回答模型路径" } },
            { "embeddingModel", new[] { "模型", "向量模型路径" } },
            { "contextTokens", new[] { "生成", "上下文 Token 数" } },
            { "maximumOutputTokens", new[] { "生成", "最大输出 Token 数" } },
            { "temperature", new[] { "生成", "温度" } },
            { "topP", new[] { "生成", "Top P" } },
            { "samplingTopK", new[] { "生成", "采样 Top K" } },
            { "repeatPenalty", new[] { "生成", "重复惩罚" } },
            { "disableThinking", new[] { "生成", "关闭思考输出" } },
            { "acceleration", new[] { "加速", "推理设备" } },
            { "gpuLayers", new[] { "加速", "GPU 层数" } },
            { "threads", new[] { "加速", "推理线程数（0 自动）" } },
            { "batchThreads", new[] { "加速", "批处理线程数（0 自动）" } },
            { "batchTokens", new[] { "加速", "批处理 Token 数" } },
            { "microBatchTokens", new[] { "加速", "微批处理 Token 数" } },
            { "warmupGpu", new[] { "加速", "加载时预热" } },
            { "databaseName", new[] { "知识库", "数据库文件名" } },
            { "seedDatabase", new[] { "知识库", "基础数据库路径" } },
            { "seedManifest", new[] { "知识库", "基础清单路径" } },
            { "chunkTokens", new[] { "知识库", "分块 Token 数" } },
            { "overlapTokens", new[] { "知识库", "重叠 Token 数" } },
            { "embeddingContextTokens", new[] { "知识库", "向量上下文 Token 数" } },
            { "queryInstruction", new[] { "知识库", "检索指令" } },
            { "topK", new[] { "检索", "召回片段数" } },
            { "minimumScore", new[] { "检索", "最低相似度" } },
            { "maximumEvidence", new[] { "检索", "最大证据数" } },
            { "answerSystemPrompt", new[] { "提示词", "知识问答系统提示词" } },
            { "systemPrompt", new[] { "提示词", "聊天系统提示词" } }
        };

        public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member, List<Attribute> attributes)
        {
            if (!Fields.TryGetValue(member.Name, out var presentation)) return;
            attributes.RemoveAll(attribute => attribute is HeaderAttribute);
            attributes.Add(new TabGroupAttribute("参数", presentation[0]));
            attributes.Add(new LabelTextAttribute(presentation[1]));
        }
    }
}
#endif
