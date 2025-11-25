using UnityEngine;

namespace LocalWake.Unity
{
    public static class DtwDistance
    {
        public static float CosineDistance(float[] a, float[] b)
        {
            float dot = 0f, na = 0f, nb = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                float va = a[i];
                float vb = b[i];
                dot += va * vb;
                na += va * va;
                nb += vb * vb;
            }

            float denom = Mathf.Sqrt(na) * Mathf.Sqrt(nb) + 1e-8f;
            return 1f - dot / denom;
        }

        public static float DtwCosine(float[,] x, float[,] y)
        {
            int d = x.GetLength(0);
            int tx = x.GetLength(1);
            int ty = y.GetLength(1);

            var cost = new float[tx, ty];

            cost[0, 0] = FrameCost(x, 0, y, 0, d);
            for (int j = 1; j < ty; j++)
                cost[0, j] = cost[0, j - 1] + FrameCost(x, 0, y, j, d);
            for (int i = 1; i < tx; i++)
                cost[i, 0] = cost[i - 1, 0] + FrameCost(x, i, y, 0, d);

            for (int i = 1; i < tx; i++)
            {
                for (int j = 1; j < ty; j++)
                {
                    float c = FrameCost(x, i, y, j, d);
                    float prev = Mathf.Min(cost[i - 1, j], Mathf.Min(cost[i, j - 1], cost[i - 1, j - 1]));
                    cost[i, j] = c + prev;
                }
            }

            float totalCost = cost[tx - 1, ty - 1];
            float norm = tx + ty;
            return totalCost / norm;
        }

        static float FrameCost(float[,] x, int ix, float[,] y, int iy, int d)
        {
            var vx = new float[d];
            var vy = new float[d];
            for (int k = 0; k < d; k++)
            {
                vx[k] = x[k, ix];
                vy[k] = y[k, iy];
            }
            return CosineDistance(vx, vy);
        }
    }
}


