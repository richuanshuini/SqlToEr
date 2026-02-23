using SqlToER.Model;

namespace SqlToER.Service
{
    public enum TierLevel { T1, T2, T3 }

    /// <summary>
    /// 布局分层判定 + 各档参数
    /// T1(轻量): MSAGL → Arrange → Spread → Visio路由
    /// T2(中等): ForceAlign → Arrange → Spread
    /// T3(重度): ForceAlign → Arrange(强) → Spread → Arrange-light
    /// </summary>
    public class LayoutTier
    {
        public TierLevel Level { get; init; }

        // ---- ArrangeLayout 参数 ----
        public double SafeGap { get; init; }
        public int SpringIter { get; init; }
        public double RepulsionFactor { get; init; }

        // ---- 流程控制 ----
        public bool UseForceAlign { get; init; }
        public bool UseArrangeLight { get; init; }
        public bool UseVisioLayout { get; init; }

        /// <summary>
        /// 按复杂度指标自动判定档位
        /// </summary>
        public static LayoutTier Detect(ErDocument erDoc)
        {
            int E = erDoc.Entities.Count;
            int A = erDoc.Attributes.Count;
            int R = erDoc.Relationships.Count;
            int N = E + A + R;

            int maxAttr = 0;
            var attrCounts = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Count());
            if (attrCounts.Any())
                maxAttr = attrCounts.Max();

            // T3: N > 170 或 maxAttr > 20
            if (N > 170 || maxAttr > 20)
            {
                return new LayoutTier
                {
                    Level = TierLevel.T3,
                    SafeGap = 1.3,
                    SpringIter = 900,
                    RepulsionFactor = 0.35,
                    UseForceAlign = true,
                    UseArrangeLight = true,
                    UseVisioLayout = false,
                };
            }

            // T2: 90 < N <= 170 或 R > E
            if (N > 90 || R > E)
            {
                return new LayoutTier
                {
                    Level = TierLevel.T2,
                    SafeGap = 1.0,
                    SpringIter = 600,
                    RepulsionFactor = 0.30,
                    UseForceAlign = true,
                    UseArrangeLight = true,
                    UseVisioLayout = false,
                };
            }

            // T1: N <= 90 且 maxAttr <= 12 且 R <= E
            return new LayoutTier
            {
                Level = TierLevel.T1,
                SafeGap = 0.7,
                SpringIter = 300,
                RepulsionFactor = 0.25,
                UseForceAlign = false,
                UseArrangeLight = false,
                UseVisioLayout = true,
            };
        }
    }
}
