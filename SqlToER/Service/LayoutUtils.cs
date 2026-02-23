namespace SqlToER.Service
{
    /// <summary>
    /// 布局工具函数 — 移植自 sql_to_ER/js/layout/utils.js
    /// </summary>
    public static class LayoutUtils
    {
        /// <summary>
        /// 将角度归一化到 [0, 2π)
        /// </summary>
        public static double NormalizeAngle(double a)
        {
            double ang = a % (Math.PI * 2);
            if (ang < 0) ang += Math.PI * 2;
            return ang;
        }

        /// <summary>
        /// 确定性哈希 — 相同输入永远产生相同输出
        /// </summary>
        public static int DeterministicHash(string str, int extraSeed = 0)
        {
            int hash = extraSeed;
            foreach (char c in str)
            {
                hash = ((hash << 5) - hash) + c;
                hash &= hash;
            }
            return Math.Abs(hash);
        }

        /// <summary>
        /// 确定性随机 [-0.5, 0.5)
        /// </summary>
        public static double DeterministicRandom(int seed, int extraSeed = 0)
        {
            double x = Math.Sin(seed + extraSeed * 1000) * 10000;
            return (x - Math.Floor(x)) - 0.5;
        }

        /// <summary>
        /// 节点半径（对角线 / 2）
        /// </summary>
        public static double NodeRadius(double w, double h)
        {
            return Math.Sqrt(w * w + h * h) / 2;
        }
    }
}
