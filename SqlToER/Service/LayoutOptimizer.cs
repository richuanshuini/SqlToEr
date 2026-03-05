using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// 迭代布局优化器 —— 每轮渐进放大参数，重新布局并覆盖保存
    /// </summary>
    public static class LayoutOptimizer
    {
        /// <summary>
        /// 按优化轮次渐进放大 LayoutTier 参数
        /// round=1 → ×1.3, round=2 → ×1.6, round=3 → ×1.9 ...
        /// </summary>
        public static LayoutTier Escalate(LayoutTier baseTier, int round)
        {
            double scale = 1.0 + round * 0.3;
            return new LayoutTier
            {
                Level = baseTier.Level,
                SafeGap = baseTier.SafeGap * scale,
                SpringIter = (int)(baseTier.SpringIter * scale),
                RepulsionFactor = baseTier.RepulsionFactor, // 不变
                NodeSeparation = (int)(baseTier.NodeSeparation * scale),
                MdsIterations = (int)(baseTier.MdsIterations * scale),
                CollisionPadding = baseTier.CollisionPadding * scale,
                GlobalSepPadding = baseTier.GlobalSepPadding * scale,
                UseGraphviz = baseTier.UseGraphviz,
                UseForceAlign = baseTier.UseForceAlign,
                UseArrangeLight = baseTier.UseArrangeLight,
                UseVisioLayout = baseTier.UseVisioLayout,
                SkipAttrsInMds = baseTier.SkipAttrsInMds,
            };
        }

        /// <summary>
        /// 用更强参数重新布局并覆盖保存 VSDX
        /// </summary>
        /// <param name="seedCoords">上轮坐标（首轮为 null 或首次导出返回的坐标）</param>
        /// <returns>本轮最终坐标（供下轮使用）</returns>
        public static Dictionary<string, (double X, double Y)> OptimizeVsdx(
            string vsdxPath,
            ErDocument erDoc,
            TemplateLayout? tpl,
            int round,
            Action<string>? onStatus = null,
            Dictionary<string, (double X, double Y)>? seedCoords = null)
        {
            var baseTier = LayoutTier.Detect(erDoc);
            var escalatedTier = Escalate(baseTier, round);

            onStatus?.Invoke($"🔄 第 {round} 轮优化（参数 ×{1.0 + round * 0.3:F1}）...");

            var service = new VisioExportService();
            return service.ExportToVsdx(erDoc, vsdxPath, tpl, onStatus, escalatedTier, seedCoords);
        }
    }
}
