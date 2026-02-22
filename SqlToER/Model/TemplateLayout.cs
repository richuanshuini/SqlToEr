namespace SqlToER.Model
{
    /// <summary>
    /// 从 VSDX 模板中提取的布局信息
    /// </summary>
    public class TemplateLayout
    {
        public double PageWidth { get; set; }
        public double PageHeight { get; set; }

        /// <summary>
        /// 模板文件路径（导出时用作模具来源）
        /// </summary>
        public string TemplatePath { get; set; } = "";

        /// <summary>
        /// 模板中的所有形状信息
        /// </summary>
        public List<TemplateShapeInfo> Shapes { get; set; } = [];

        // ===== 从模板提取的 Master 名称（用于 Drop 形状）=====
        public string? EntityMasterName { get; set; }
        public string? AttributeMasterName { get; set; }
        public string? RelationshipMasterName { get; set; }

        // ===== 模板中各类形状的平均尺寸 =====
        public double EntityAvgWidth { get; set; }
        public double EntityAvgHeight { get; set; }
        public double AttributeAvgWidth { get; set; }
        public double AttributeAvgHeight { get; set; }
        public double RelationshipAvgWidth { get; set; }
        public double RelationshipAvgHeight { get; set; }

        /// <summary>
        /// 生成供 AI 使用的布局描述文本
        /// </summary>
        public string ToLayoutPrompt()
        {
            var entities = Shapes.Where(s => s.ShapeType == ShapeCategory.Entity).ToList();
            var attrs = Shapes.Where(s => s.ShapeType == ShapeCategory.Attribute).ToList();
            var rels = Shapes.Where(s => s.ShapeType == ShapeCategory.Relationship).ToList();

            var lines = new List<string>
            {
                $"页面大小：{PageWidth:F1} x {PageHeight:F1} 英寸。",
                $"参考模板包含 {entities.Count} 个实体、{attrs.Count} 个属性、{rels.Count} 个关系。"
            };

            if (entities.Count > 0)
            {
                lines.Add($"实体矩形尺寸：{EntityAvgWidth:F2} x {EntityAvgHeight:F2} 英寸。");

                var xs = entities.Select(e => e.X).OrderBy(x => x).ToList();
                var ys = entities.Select(e => e.Y).OrderBy(y => y).ToList();
                lines.Add($"实体 X 范围：{xs.First():F1} ~ {xs.Last():F1}，Y 范围：{ys.First():F1} ~ {ys.Last():F1}。");

                if (entities.Count > 1)
                {
                    var gapX = xs.Zip(xs.Skip(1), (a, b) => b - a).Where(g => g > 0.1).DefaultIfEmpty(3).Average();
                    lines.Add($"实体间平均水平间距约 {gapX:F1} 英寸。");
                }
            }

            if (attrs.Count > 0)
            {
                lines.Add($"属性椭圆尺寸：{AttributeAvgWidth:F2} x {AttributeAvgHeight:F2} 英寸。");

                var entityMap = entities.ToDictionary(e => e.Text, e => e, StringComparer.OrdinalIgnoreCase);
                var offsets = new List<(double dx, double dy)>();
                foreach (var attr in attrs)
                {
                    if (!string.IsNullOrEmpty(attr.ParentEntity) &&
                        entityMap.TryGetValue(attr.ParentEntity, out var entity))
                    {
                        offsets.Add((attr.X - entity.X, attr.Y - entity.Y));
                    }
                }
                if (offsets.Count > 0)
                {
                    var avgDy = offsets.Average(o => o.dy);
                    var avgDx = offsets.Average(o => Math.Abs(o.dx));
                    lines.Add($"属性通常分布在实体{(avgDy > 0 ? "上方" : "下方")}约 {Math.Abs(avgDy):F1} 英寸处，" +
                              $"水平偏移约 {avgDx:F1} 英寸，呈扇形排列。");
                }
            }

            if (rels.Count > 0)
            {
                lines.Add($"关系菱形尺寸：{RelationshipAvgWidth:F2} x {RelationshipAvgHeight:F2} 英寸。");
                lines.Add("关系菱形通常放在两个相关实体的连线中间。");
            }

            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// 模板中单个形状的信息
    /// </summary>
    public class TemplateShapeInfo
    {
        public ShapeCategory ShapeType { get; set; }
        public string Text { get; set; } = "";
        public string MasterName { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string? ParentEntity { get; set; }
    }

    public enum ShapeCategory
    {
        Entity,
        Attribute,
        Relationship,
        Connector,
        Unknown
    }
}
