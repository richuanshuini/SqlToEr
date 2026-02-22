using System.Runtime.InteropServices;
using SqlToER.Model;
using Visio = Microsoft.Office.Interop.Visio;

namespace SqlToER.Service
{
    /// <summary>
    /// 解析 VSDX 参考模板，提取布局和模具信息
    /// </summary>
    public class TemplateParserService
    {
        private static readonly string[] EntityKeywords = ["Entity", "实体", "Rectangle", "Box", "Process"];
        private static readonly string[] AttributeKeywords = ["Attribute", "属性", "Ellipse", "Circle", "Oval"];
        private static readonly string[] RelationshipKeywords = ["Relationship", "关系", "Diamond", "Decision"];

        public TemplateLayout ParseTemplate(string vsdxPath, Action<string>? onStatus = null)
        {
            Visio.Application? app = null;
            Visio.Document? doc = null;

            try
            {
                onStatus?.Invoke("正在打开参考模板...");
                app = new Visio.Application { Visible = false };
                doc = app.Documents.Open(vsdxPath);
                var page = doc.Pages[1];

                double pageW = 20, pageH = 15;
                try
                {
                    pageW = page.PageSheet.get_CellsU("PageWidth").ResultIU;
                    pageH = page.PageSheet.get_CellsU("PageHeight").ResultIU;
                }
                catch { }

                var layout = new TemplateLayout
                {
                    PageWidth = pageW,
                    PageHeight = pageH,
                    TemplatePath = vsdxPath
                };

                onStatus?.Invoke("正在分析模板形状...");

                var shapeInfos = new List<(Visio.Shape shape, TemplateShapeInfo info)>();
                CollectShapes(page.Shapes, shapeInfos, layout);

                // 推断属性所属实体
                onStatus?.Invoke("正在分析连接关系...");
                InferAttributeParentsByConnects(page, shapeInfos);

                // ===== 统计各类形状的 Master 名称和平均尺寸 =====
                ComputeTemplateStats(layout);

                var ec = layout.Shapes.Count(s => s.ShapeType == ShapeCategory.Entity);
                var ac = layout.Shapes.Count(s => s.ShapeType == ShapeCategory.Attribute);
                var rc = layout.Shapes.Count(s => s.ShapeType == ShapeCategory.Relationship);
                onStatus?.Invoke($"模板分析完成：{ec} 实体，{ac} 属性，{rc} 关系 | " +
                                 $"模具：{layout.EntityMasterName ?? "?"} / {layout.AttributeMasterName ?? "?"} / {layout.RelationshipMasterName ?? "?"}");

                return layout;
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80040154))
            {
                throw new InvalidOperationException("未检测到 Microsoft Visio，无法解析模板。", ex);
            }
            finally
            {
                try { doc?.Close(); } catch { }
                try { app?.Quit(); } catch { }
                ReleaseComObject(doc);
                ReleaseComObject(app);
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>
        /// 统计各类形状的 Master 名称和平均尺寸
        /// </summary>
        private static void ComputeTemplateStats(TemplateLayout layout)
        {
            var entities = layout.Shapes.Where(s => s.ShapeType == ShapeCategory.Entity).ToList();
            var attrs = layout.Shapes.Where(s => s.ShapeType == ShapeCategory.Attribute).ToList();
            var rels = layout.Shapes.Where(s => s.ShapeType == ShapeCategory.Relationship).ToList();

            // 找出最常用的 Master 名称（投票法）
            if (entities.Count > 0)
            {
                layout.EntityMasterName = entities
                    .Where(e => !string.IsNullOrEmpty(e.MasterName))
                    .GroupBy(e => e.MasterName)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;
                layout.EntityAvgWidth = entities.Average(e => e.Width);
                layout.EntityAvgHeight = entities.Average(e => e.Height);
            }

            if (attrs.Count > 0)
            {
                layout.AttributeMasterName = attrs
                    .Where(a => !string.IsNullOrEmpty(a.MasterName))
                    .GroupBy(a => a.MasterName)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;
                layout.AttributeAvgWidth = attrs.Average(a => a.Width);
                layout.AttributeAvgHeight = attrs.Average(a => a.Height);
            }

            if (rels.Count > 0)
            {
                layout.RelationshipMasterName = rels
                    .Where(r => !string.IsNullOrEmpty(r.MasterName))
                    .GroupBy(r => r.MasterName)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;
                layout.RelationshipAvgWidth = rels.Average(r => r.Width);
                layout.RelationshipAvgHeight = rels.Average(r => r.Height);
            }
        }

        // ============ 形状收集 ============

        private static void CollectShapes(
            Visio.Shapes shapes,
            List<(Visio.Shape shape, TemplateShapeInfo info)> shapeInfos,
            TemplateLayout layout)
        {
            foreach (Visio.Shape shape in shapes)
            {
                try
                {
                    var info = ExtractShapeInfo(shape);
                    if (info != null)
                    {
                        shapeInfos.Add((shape, info));
                        layout.Shapes.Add(info);
                    }

                    if (shape.Shapes != null && shape.Shapes.Count > 0)
                        CollectShapes(shape.Shapes, shapeInfos, layout);
                }
                catch { }
            }
        }

        private static TemplateShapeInfo? ExtractShapeInfo(Visio.Shape shape)
        {
            try
            {
                string text = "";
                try { text = shape.Text?.Trim() ?? ""; } catch { }

                // 获取 Master 名称
                string masterName = "";
                try
                {
                    if (shape.Master != null)
                        masterName = shape.Master.NameU ?? shape.Master.Name ?? "";
                }
                catch { }

                double x = 0, y = 0, w = 0, h = 0;
                try
                {
                    x = shape.get_CellsU("PinX").ResultIU;
                    y = shape.get_CellsU("PinY").ResultIU;
                    w = shape.get_CellsU("Width").ResultIU;
                    h = shape.get_CellsU("Height").ResultIU;
                }
                catch { return null; }

                return new TemplateShapeInfo
                {
                    Text = text,
                    MasterName = masterName,
                    X = x,
                    Y = y,
                    Width = w,
                    Height = h,
                    ShapeType = ClassifyShape(shape, masterName, w, h)
                };
            }
            catch { return null; }
        }

        private static ShapeCategory ClassifyShape(Visio.Shape shape, string masterName, double w, double h)
        {
            try { if (shape.OneD != 0) return ShapeCategory.Connector; } catch { }

            if (!string.IsNullOrEmpty(masterName))
            {
                if (EntityKeywords.Any(k => masterName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return ShapeCategory.Entity;
                if (AttributeKeywords.Any(k => masterName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return ShapeCategory.Attribute;
                if (RelationshipKeywords.Any(k => masterName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return ShapeCategory.Relationship;
            }

            double ratio = w / Math.Max(h, 0.01);
            if (ratio > 0.6 && ratio < 2.5 && w < 1.5 && h < 1.0)
                return ShapeCategory.Attribute;
            if (w > 1.0 && h > 0.3)
                return ShapeCategory.Entity;

            return ShapeCategory.Unknown;
        }

        // ============ 连接关系推断 ============

        private static void InferAttributeParentsByConnects(
            Visio.Page page,
            List<(Visio.Shape shape, TemplateShapeInfo info)> shapeInfos)
        {
            var shapeIdMap = shapeInfos.ToDictionary(s => s.shape.ID, s => s.info);

            try
            {
                foreach (Visio.Connect conn in page.Connects)
                {
                    try
                    {
                        var connectorShape = conn.FromSheet;
                        var targetShape = conn.ToSheet;
                        if (connectorShape == null || targetShape == null) continue;

                        var connectedIds = new HashSet<int>();
                        foreach (Visio.Connect c in page.Connects)
                        {
                            if (c.FromSheet?.ID == connectorShape.ID && c.ToSheet != null)
                                connectedIds.Add(c.ToSheet.ID);
                        }

                        var items = connectedIds
                            .Where(id => shapeIdMap.ContainsKey(id))
                            .Select(id => (id, info: shapeIdMap[id]))
                            .ToList();

                        var attrItems = items.Where(c => c.info.ShapeType == ShapeCategory.Attribute).ToList();
                        var entityItems = items.Where(c => c.info.ShapeType == ShapeCategory.Entity).ToList();

                        foreach (var a in attrItems)
                        {
                            if (string.IsNullOrEmpty(a.info.ParentEntity) && entityItems.Count > 0)
                                a.info.ParentEntity = entityItems[0].info.Text;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 距离兜底
            foreach (var (_, info) in shapeInfos)
            {
                if (info.ShapeType != ShapeCategory.Attribute || !string.IsNullOrEmpty(info.ParentEntity))
                    continue;

                var nearest = shapeInfos
                    .Where(s => s.info.ShapeType == ShapeCategory.Entity)
                    .OrderBy(s => Math.Pow(s.info.X - info.X, 2) + Math.Pow(s.info.Y - info.Y, 2))
                    .FirstOrDefault();

                if (nearest.info != null)
                    info.ParentEntity = nearest.info.Text;
            }
        }

        private static void ReleaseComObject(object? obj)
        {
            if (obj == null) return;
            try { Marshal.ReleaseComObject(obj); } catch { }
        }
    }
}
