using System.Collections.Generic;
using UnityEngine;

namespace WManager.Knowledge.Editor
{
    [CreateAssetMenu(fileName = "KnowledgeBuildConfiguration", menuName = "WManager/Knowledge Build Configuration")]
    public sealed class KnowledgeBuildConfiguration : ScriptableObject
    {
        public List<KnowledgeSettings> dynamicSettings = new List<KnowledgeSettings>();
    }
}
