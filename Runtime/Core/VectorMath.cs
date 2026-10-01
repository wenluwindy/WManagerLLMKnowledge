using System;

namespace WManager.Knowledge
{
    internal static class VectorMath
    {
        public static float[] Normalize(float[] vector, int dimensions)
        {
            if (vector == null || vector.Length != dimensions) throw new InvalidOperationException("Embedding dimension mismatch.");
            double norm = 0;
            foreach (float value in vector)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Embedding contains a non-finite value.");
                norm += (double)value * value;
            }
            if (norm < 1e-20) throw new InvalidOperationException("Embedding is a zero vector.");
            double scale = 1.0 / Math.Sqrt(norm);
            var normalized = new float[vector.Length];
            for (int i = 0; i < vector.Length; i++) normalized[i] = (float)(vector[i] * scale);
            return normalized;
        }

        public static float Dot(float[] left, float[] right)
        {
            if (left.Length != right.Length) throw new InvalidOperationException("Stored embedding dimension mismatch. Rebuild this knowledge base.");
            double sum = 0;
            for (int i = 0; i < left.Length; i++) sum += (double)left[i] * right[i];
            return (float)sum;
        }
    }
}
