using SqlToER.Model;

namespace SqlToER.Service
{
    public enum TierLevel { T1, T2, T3 }

    /// <summary>
    /// 布局分层判定 + 各档参数
    /// T1(轻量): MSAGL → Arrange → Spread → Visio路由
    /// T2(中等): MSAGL全节点 → Arrange → Spread
    /// T3(重度): MSAGL全节点(宽松) → Arrange(强) → Spread
    /// </summary>
    public class LayoutTier
    {
        public TierLevel Level { get; init; }

        // ---- ArrangeLayout 参数 ----
        public double SafeGap { get; init; }
        public int SpringIter { get; init; }
        public double RepulsionFactor { get; init; }

        // ---- MSAGL 参数 ----
        public int NodeSeparation { get; init; }     // MSAGL MDS 节点间距 (pt)
        public int MdsIterations { get; init; }      // MSAGL MDS 迭代次数

        // ---- 碰撞参数 ----
        public double CollisionPadding { get; init; }   // 菱形-实体碰撞裕量 (英寸)
        public double GlobalSepPadding { get; init; }   // 全局分离最小间距 (英寸)

        // ---- 流程控制 ----
        public bool UseGraphviz { get; init; }      // T3: 用 Graphviz sfdp 引擎
        public bool UseForceAlign { get; init; }
        public bool UseArrangeLight { get; init; }
        public bool UseVisioLayout { get; init; }
        public bool SkipAttrsInMds { get; init; }

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

            // T3: 实体数 >= 25（30表级别，MDS 只放实体+菱形）
            if (E >= 25)
            {
                return new LayoutTier
                {
                    Level = TierLevel.T3,
                    SafeGap = 1.2,
                    SpringIter = 500,
                    RepulsionFactor = 0.25,
                    NodeSeparation = 100,
                    MdsIterations = 300,
                    CollisionPadding = 0.6,
                    GlobalSepPadding = 0.25,
                    UseGraphviz = true,         // T3: Graphviz sfdp 引擎
                    UseForceAlign = false,       // Graphviz 替代 MSAGL
                    UseArrangeLight = false,
                    UseVisioLayout = false,
                    SkipAttrsInMds = false,
                };
            }

            // T2: 90 < N <= 170 或 R > E
            if (N > 90 || R > E)
            {
                return new LayoutTier
                {
                    Level = TierLevel.T2,
                    SafeGap = 0.7,
                    SpringIter = 300,
                    RepulsionFactor = 0.25,
                    NodeSeparation = 80,
                    MdsIterations = 500,
                    CollisionPadding = 0.3,
                    GlobalSepPadding = 0.12,
                    UseGraphviz = false,
                    UseForceAlign = true,
                    UseArrangeLight = false,
                    UseVisioLayout = false,
                    SkipAttrsInMds = false,
                };
            }

            // T1: N <= 90 且 maxAttr <= 12 且 R <= E
            return new LayoutTier
            {
                Level = TierLevel.T1,
                SafeGap = 0.7,
                SpringIter = 300,
                RepulsionFactor = 0.25,
                NodeSeparation = 80,
                MdsIterations = 500,
                CollisionPadding = 0.3,
                GlobalSepPadding = 0.12,
                UseGraphviz = false,
                UseForceAlign = false,
                UseArrangeLight = false,
                UseVisioLayout = true,
                SkipAttrsInMds = false,
            };
        }
    }
}
