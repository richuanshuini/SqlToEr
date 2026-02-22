using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Layout.MDS;
using Microsoft.Msagl.Miscellaneous;
using MsaglDrawing = Microsoft.Msagl.Drawing;
using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// 使用 MSAGL 在 Headless 模式下计算 ER 图布局。
    /// 内置阈值策略：
    ///   ≤10 实体 → MDS（多维缩放，紧凑自然）
    ///   >10 实体 → Sugiyama 分层（内置交叉最小化）
    /// </summary>
    public static class MsaglLayoutService
    {
        private const double INCH_TO_PT = 72.0;
        private const int MDS_THRESHOLD = 10;

        /// <summary>
        /// 计算 ER 图布局，返回实体的中心坐标（Visio 英寸单位）。
        /// </summary>
        public static Dictionary<string, (double X, double Y)> CalculateLayout(
            ErDocument erDoc,
            double entityW, double entityH,
            double attrW,
            double relW, double relH)
        {
            var result = new Dictionary<string, (double X, double Y)>(
                StringComparer.OrdinalIgnoreCase);

            if (erDoc.Entities.Count == 0) return result;

            // ---- 预计算属性数 ----
            var attrCounts = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // ---- 1. 建图 ----
            var drawingGraph = new MsaglDrawing.Graph("ER");
            var nodeSizes = new Dictionary<string, (double W, double H)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var entity in erDoc.Entities)
            {
                int nAttrs = attrCounts.GetValueOrDefault(entity.Name, 0);
                // BoundaryCurve = 实体矩形 + 属性缓冲区（不含完整扇面直径）
                // 属性多的实体需要更大的缓冲区，但不能照搬扇面直径（否则 MSAGL 把节点推太远）
                double bufferInch = Math.Min(nAttrs * 0.3, 3.0); // 每个属性增加 0.3 英寸，上限 3
                double boxW = (entityW + attrW * 2 + bufferInch) * INCH_TO_PT;
                double boxH = (entityH + attrW * 2 + bufferInch) * INCH_TO_PT;

                var node = drawingGraph.AddNode(entity.Name);
                node.Attr.Shape = MsaglDrawing.Shape.Box;
                nodeSizes[entity.Name] = (boxW, boxH);
            }

            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                string dId = $"◇{rel.Name}_{i}";

                var node = drawingGraph.AddNode(dId);
                node.Attr.Shape = MsaglDrawing.Shape.Diamond;
                nodeSizes[dId] = (relW * 2 * INCH_TO_PT, relH * 2 * INCH_TO_PT);

                drawingGraph.AddEdge(rel.Entity1, dId);
                drawingGraph.AddEdge(dId, rel.Entity2);
            }

            // ---- 2. 设置 BoundaryCurve ----
            drawingGraph.CreateGeometryGraph();

            foreach (var drawingNode in drawingGraph.Nodes)
            {
                var geoNode = drawingNode.GeometryNode;
                if (geoNode == null) continue;

                if (nodeSizes.TryGetValue(drawingNode.Id, out var size))
                    geoNode.BoundaryCurve = CurveFactory.CreateRectangle(
                        size.W, size.H, new Point(0, 0));
                else
                    geoNode.BoundaryCurve = CurveFactory.CreateRectangle(
                        entityW * INCH_TO_PT, entityH * INCH_TO_PT, new Point(0, 0));
            }

            // ---- 3. 阈值选择算法 ----
            LayoutAlgorithmSettings settings;

            if (erDoc.Entities.Count <= MDS_THRESHOLD)
            {
                // 小图：MDS — 紧凑自然的分布
                settings = new MdsLayoutSettings
                {
                    ScaleX = 1.0,
                    ScaleY = 1.0,
                    IterationsWithMajorization = 50,
                    NodeSeparation = 20,
                };
            }
            else
            {
                // 大图：Sugiyama — 内置交叉最小化
                var sugiyama = new SugiyamaLayoutSettings
                {
                    NodeSeparation = 80,
                    LayerSeparation = 120,
                };
                sugiyama.EdgeRoutingSettings.EdgeRoutingMode = EdgeRoutingMode.StraightLine;
                settings = sugiyama;
            }

            var geometryGraph = drawingGraph.GeometryGraph;
            LayoutHelpers.CalculateLayout(geometryGraph, settings, null);

            // ---- 4. 提取坐标（点数 → 英寸）----
            foreach (var drawingNode in drawingGraph.Nodes)
            {
                var geoNode = drawingNode.GeometryNode;
                if (geoNode == null) continue;
                result[drawingNode.Id] = (
                    geoNode.Center.X / INCH_TO_PT,
                    geoNode.Center.Y / INCH_TO_PT
                );
            }

            return result;
        }
    }
}
