using UnityEngine;

namespace WManager.Knowledge
{
    [CreateAssetMenu(fileName = "LlamaChatSettings", menuName = "WManager/LLM Chat Settings")]
    public sealed class LlamaChatSettings : LlamaModelSettings
    {
        [Header("Conversation")]
        [TextArea(4, 10)] public string systemPrompt = "你是一个可靠的中文助手。回答清晰、简洁，不确定时说明不确定。";
    }
}
