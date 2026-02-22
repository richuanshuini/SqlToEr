using SqlToER.Model;
using Visio = Microsoft.Office.Interop.Visio;

namespace SqlToER.Service
{
    /// <summary>
    /// 积木式 ER 图绘制引擎 — 纯 DBCHEN 一套标准
    ///
    /// 核心原理（Visio ShapeSheet 黑科技）：
    /// DBCHEN 的"属性"和"主键属性"是 1D 形状！
    /// - BeginX/BeginY = 椭圆所在位置（通过 Geometry 硬编码画椭圆）
    /// - EndX/EndY = 连线末端（可 GlueTo 到实体 PinX）
    /// 所以 Drop 后直接 GlueTo 就是最标准的连接方式。
    /// </summary>
    public class ErDiagramPainter
    {
        private readonly Visio.Page _page;
        private readonly Visio.Master _entityMaster;   // DBCHEN 实体（2D 矩形）
        private readonly Visio.Master _attrMaster;     // DBCHEN 属性（2D + Control Handle）
        private readonly Visio.Master _relMaster;      // DBCHEN 关系（菱形）
        private readonly Visio.Master _connMaster;     // DBCHEN 关系连接线（1D，蓝色端点）

        // 排版参数
        private const double EntityY = 5.0;
        private const double EntityStartX = 3.0;
        private const double EntitySpacing = 5.0;
        private const double AttrRadius = 2.0;

        // 默认尺寸
        private double _entityW = 1.5, _entityH = 0.6;
        private double _attrW = 1.0, _attrH = 0.5;
        private double _relW = 1.0, _relH = 0.8;

        public ErDiagramPainter(
            Visio.Page page,
            Visio.Master entityMaster,
            Visio.Master attrMaster,
            Visio.Master relMaster,
            Visio.Master connMaster)
        {
            _page = page;
            _entityMaster = entityMaster;
            _attrMaster = attrMaster;
            _relMaster = relMaster;
            _connMaster = connMaster;
        }

        public void ApplyTemplateSizes(TemplateLayout tpl)
        {
            if (tpl.EntityAvgWidth > 0) _entityW = tpl.EntityAvgWidth;
            if (tpl.EntityAvgHeight > 0) _entityH = tpl.EntityAvgHeight;
            if (tpl.AttributeAvgWidth > 0) _attrW = tpl.AttributeAvgWidth;
            if (tpl.AttributeAvgHeight > 0) _attrH = tpl.AttributeAvgHeight;
            if (tpl.RelationshipAvgWidth > 0) _relW = tpl.RelationshipAvgWidth;
            if (tpl.RelationshipAvgHeight > 0) _relH = tpl.RelationshipAvgHeight;
        }

        // ============================================================
        // 第 1 层：原子操作
        // ============================================================

        /// <summary>
        /// 画实体矩形（2D 形状，标准 Drop + SetSize）
        /// </summary>
        public Visio.Shape DrawEntity(string name, double x, double y)
        {
            var shape = _page.Drop(_entityMaster, x, y);
            shape.Text = name;
            SetSize(shape, _entityW, _entityH);
            return shape;
        }

        /// <summary>
        /// 画属性并通过控制点（Control Handle/黄色菱形）吸附到实体
        ///
        /// DBCHEN 属性的底层真相：
        /// - 它是 2D 形状，带一个 Control Handle（Visio 界面上的黄色菱形）
        /// - 椭圆通过 Geometry 硬编码在形状中心
        /// - 连线通过 Geometry 从椭圆中心画到 Controls.Row_1 的坐标
        /// - 操作 Controls.X 的 GlueTo = 手动拖动黄点到实体上
        /// </summary>
        public Visio.Shape DrawAttribute(string name, double x, double y,
            Visio.Shape targetEntity, bool isPK = false)
        {
            var shape = _page.Drop(_attrMaster, x, y);
            shape.Text = name;

            // 通过控制点 GlueTo 实体 PinX（黄色菱形吸附到实体中心）
            GlueAttributeToEntity(shape, targetEntity);

            if (isPK)
            {
                try
                {
                    shape.Characters.set_CharProps(
                        (short)Visio.VisCellIndices.visCharacterStyle, (short)4);
                }
                catch { }
            }

            return shape;
        }

        /// <summary>
        /// 将属性的控制点（黄色菱形）吸附到实体矩形的边缘
        ///
        /// DBCHEN 属性是带 Control Handle 的 2D 形状，线从椭圆画到控制点坐标。
        /// 1. 几何交点算法：求属性→实体方向与矩形边缘的交点
        /// 2. 转换为属性的局部坐标
        /// 3. FormulaForceU 强制设置控制点 X/Y
        /// </summary>
        private static void GlueAttributeToEntity(Visio.Shape attrShape, Visio.Shape entityShape)
        {
            short secControls = (short)Visio.VisSectionIndices.visSectionControls;

            try
            {
                if (attrShape.get_RowExists(secControls, 0,
                    (short)Visio.VisExistsFlags.visExistsAnywhere) == 0)
                    return;

                // 几何交点算法
                double eX = entityShape.get_CellsU("PinX").ResultIU;
                double eY = entityShape.get_CellsU("PinY").ResultIU;
                double eW = entityShape.get_CellsU("Width").ResultIU;
                double eH = entityShape.get_CellsU("Height").ResultIU;
                double aX = attrShape.get_CellsU("PinX").ResultIU;
                double aY = attrShape.get_CellsU("PinY").ResultIU;

                double dx = aX - eX;
                double dy = aY - eY;

                double tX = double.MaxValue, tY = double.MaxValue;
                if (Math.Abs(dx) > 0.0001) tX = (eW / 2.0) / Math.Abs(dx);
                if (Math.Abs(dy) > 0.0001) tY = (eH / 2.0) / Math.Abs(dy);
                double t = Math.Min(tX, tY);

                double intersectX = eX + t * dx;
                double intersectY = eY + t * dy;

                // 边缘交点 → 属性的局部坐标
                double locPinX = attrShape.get_CellsU("LocPinX").ResultIU;
                double locPinY = attrShape.get_CellsU("LocPinY").ResultIU;
                double localX = intersectX - aX + locPinX;
                double localY = intersectY - aY + locPinY;

                // FormulaForceU 强制设置控制点坐标
                string localXStr = localX.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string localYStr = localY.ToString(System.Globalization.CultureInfo.InvariantCulture);

                attrShape.get_CellsSRC(secControls, 0,
                    (short)Visio.VisCellIndices.visCtlX).FormulaForceU = localXStr;
                attrShape.get_CellsSRC(secControls, 0,
                    (short)Visio.VisCellIndices.visCtlY).FormulaForceU = localYStr;
            }
            catch { }
        }

        /// <summary>
        /// 画关系菱形（2D 形状）+ 动态添加4角连接点
        /// </summary>
        public Visio.Shape DrawRelationship(string name, double x, double y)
        {
            var shape = _page.Drop(_relMaster, x, y);
            shape.Text = name;
            SetSize(shape, _relW, _relH);

            // 动态添加菱形4角连接点（ForeignObject 无内置连接点）
            try
            {
                short secConn = (short)Visio.VisSectionIndices.visSectionConnectionPts;
                if (shape.get_SectionExists(secConn, 0) == 0)
                    shape.AddSection(secConn);

                // Row 0: 左角
                AddConnectionPoint(shape, secConn, "Width*0", "Height*0.5");
                // Row 1: 右角
                AddConnectionPoint(shape, secConn, "Width*1", "Height*0.5");
                // Row 2: 上角
                AddConnectionPoint(shape, secConn, "Width*0.5", "Height*1");
                // Row 3: 下角
                AddConnectionPoint(shape, secConn, "Width*0.5", "Height*0");
            }
            catch { }

            return shape;
        }

        /// <summary>
        /// 在形状上添加一个连接点（辅助方法）
        /// </summary>
        private static void AddConnectionPoint(Visio.Shape shape, short secConn,
            string xFormula, string yFormula)
        {
            short row = shape.AddRow(secConn,
                (short)Visio.VisRowIndices.visRowLast,
                (short)Visio.VisRowTags.visTagCnnctPt);
            shape.get_CellsSRC(secConn, row,
                (short)Visio.VisCellIndices.visCnnctX).FormulaU = xFormula;
            shape.get_CellsSRC(secConn, row,
                (short)Visio.VisCellIndices.visCnnctY).FormulaU = yFormula;
        }

        /// <summary>
        /// 画关系连接线（DBCHEN 1D 形状，蓝色端点）
        /// 优先 GlueTo 连接点（角尖），失败退到 GlueTo PinX，再失败退到坐标
        /// </summary>
        public Visio.Shape DrawConnector(Visio.Shape from, Visio.Shape to, string label = "")
        {
            double mx = (from.get_CellsU("PinX").ResultIU + to.get_CellsU("PinX").ResultIU) / 2;
            double my = (from.get_CellsU("PinY").ResultIU + to.get_CellsU("PinY").ResultIU) / 2;
            var conn = _page.Drop(_connMaster, mx, my);

            // BeginX → from 形状
            GlueEndpoint(conn, "BeginX", "BeginY", from);
            // EndX → to 形状
            GlueEndpoint(conn, "EndX", "EndY", to);

            if (!string.IsNullOrEmpty(label))
                conn.Text = label;
            return conn;
        }

        /// <summary>
        /// 将 1D 连接线的端点吸附到目标形状
        /// 优先: GlueTo 最近的连接点 → GlueTo PinX → 坐标设置
        /// </summary>
        private static void GlueEndpoint(Visio.Shape conn, string cellX, string cellY, Visio.Shape target)
        {
            // 尝试 GlueTo 连接点（如果目标有连接点，比如菱形的角尖）
            short secConn = (short)Visio.VisSectionIndices.visSectionConnectionPts;
            try
            {
                if (target.get_SectionExists(secConn, 0) != 0)
                {
                    // 找最近的连接点
                    double connX = conn.get_CellsU(cellX).ResultIU;
                    double connY = conn.get_CellsU(cellY).ResultIU;
                    short bestRow = FindNearestConnectionPoint(target, secConn, connX, connY);

                    if (bestRow >= 0)
                    {
                        var connPtCell = target.get_CellsSRC(secConn, bestRow,
                            (short)Visio.VisCellIndices.visCnnctX);
                        conn.get_CellsU(cellX).GlueTo(connPtCell);
                        return;
                    }
                }
            }
            catch { }

            // 退到 GlueTo PinX（实体矩形等无连接点的形状）
            try
            {
                conn.get_CellsU(cellX).GlueTo(target.get_CellsU("PinX"));
                return;
            }
            catch { }

            // 最终兜底：坐标
            conn.get_CellsU(cellX).ResultIU = target.get_CellsU("PinX").ResultIU;
            conn.get_CellsU(cellY).ResultIU = target.get_CellsU("PinY").ResultIU;
        }

        /// <summary>
        /// 在目标形状的连接点中找最近的一个
        /// </summary>
        private static short FindNearestConnectionPoint(Visio.Shape shape, short secConn,
            double refX, double refY)
        {
            short rowCount = shape.get_RowCount(secConn);
            if (rowCount == 0) return -1;

            double pinX = shape.get_CellsU("PinX").ResultIU;
            double pinY = shape.get_CellsU("PinY").ResultIU;
            double locPinX = shape.get_CellsU("LocPinX").ResultIU;
            double locPinY = shape.get_CellsU("LocPinY").ResultIU;

            short bestRow = 0;
            double bestDist = double.MaxValue;

            for (short r = 0; r < rowCount; r++)
            {
                // 连接点局部坐标 → 页面坐标
                double localX = shape.get_CellsSRC(secConn, r,
                    (short)Visio.VisCellIndices.visCnnctX).ResultIU;
                double localY = shape.get_CellsSRC(secConn, r,
                    (short)Visio.VisCellIndices.visCnnctY).ResultIU;
                double pageX = pinX - locPinX + localX;
                double pageY = pinY - locPinY + localY;

                double dist = (pageX - refX) * (pageX - refX) + (pageY - refY) * (pageY - refY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestRow = r;
                }
            }
            return bestRow;
        }

        // ============================================================
        // 第 2 层：组件
        // ============================================================

        /// <summary>
        /// 画实体 + 伞形属性
        /// centerAngle: 属性扇面的中心方向（弧度），默认朝上 PI/2
        /// </summary>
        public Visio.Shape DrawEntityWithAttrs(
            string entityName, List<ErAttribute> attrs, double x, double y,
            double centerAngle = Math.PI / 2)
        {
            var entity = DrawEntity(entityName, x, y);

            int n = attrs.Count;
            if (n == 0) return entity;

            // 扇面张角：基于椭圆宽度和半径动态计算，确保不重叠
            // 相邻属性的弧距 >= 椭圆宽度 * 1.2（留 20% 间隙）
            double minStep = (_attrW * 1.2) / AttrRadius;
            double fanSpan = Math.Min(Math.PI, (n + 1) * minStep);
            double startAngle = centerAngle - fanSpan / 2;
            double angleStep = fanSpan / (n + 1);

            for (int i = 0; i < n; i++)
            {
                double angle = startAngle + (i + 1) * angleStep;
                double ax = x + AttrRadius * Math.Cos(angle);
                double ay = y + AttrRadius * Math.Sin(angle);

                DrawAttribute(attrs[i].Name, ax, ay, entity, attrs[i].IsPrimaryKey);
            }

            return entity;
        }

        /// <summary>
        /// 画关系菱形 + 两条连线（带基数标注）
        /// diamondX/Y: 可指定菱形位置（null = 自动计算中点）
        /// </summary>
        public Visio.Shape DrawRelBetween(
            string relName, string cardinality,
            Visio.Shape entity1, Visio.Shape entity2,
            double? diamondX = null, double? diamondY = null)
        {
            double x1 = entity1.get_CellsU("PinX").ResultIU;
            double x2 = entity2.get_CellsU("PinX").ResultIU;
            double y1 = entity1.get_CellsU("PinY").ResultIU;
            double y2 = entity2.get_CellsU("PinY").ResultIU;

            double dx = diamondX ?? (x1 + x2) / 2.0;
            double dy = diamondY ?? (y1 + y2) / 2.0;

            var diamond = DrawRelationship(relName, dx, dy);

            var parts = cardinality.Split(':');
            string cardL = parts.Length == 2 ? parts[0] : cardinality;
            string cardR = parts.Length == 2 ? parts[1] : "";

            DrawConnector(entity1, diamond, cardL);
            DrawConnector(diamond, entity2, cardR);

            return diamond;
        }

        // ============================================================
        // 第 3 层：组合器 — ER 星形布局算法
        // ============================================================

        /// <summary>
        /// 布局结果：每个实体的位置 + 属性扇面方向
        /// </summary>
        private record EntityPlacement(double X, double Y, double AttrAngle);

        public void DrawErDiagram(ErDocument erDoc, Action<string>? onStatus = null)
        {
            var attrsByEntity = erDoc.Attributes
                .GroupBy(a => a.EntityName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // ---- 步骤1: 计算布局 ----
            onStatus?.Invoke("正在计算布局...");
            var layout = CalculateLayout(erDoc);

            // ---- 步骤2: 画实体+属性 ----
            onStatus?.Invoke("正在绘制实体...");
            var entityShapes = new Dictionary<string, Visio.Shape>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in erDoc.Entities)
            {
                if (!layout.TryGetValue(entity.Name, out var place)) continue;
                var attrs = attrsByEntity.GetValueOrDefault(entity.Name, []);
                var shape = DrawEntityWithAttrs(entity.Name, attrs,
                    place.X, place.Y, place.AttrAngle);
                entityShapes[entity.Name] = shape;
            }

            // ---- 步骤3: 画关系菱形+连线 ----
            onStatus?.Invoke("正在绘制关系...");
            var diamondPositions = CalculateDiamondPositions(erDoc, layout);
            for (int i = 0; i < erDoc.Relationships.Count; i++)
            {
                var rel = erDoc.Relationships[i];
                if (entityShapes.TryGetValue(rel.Entity1, out var s1) &&
                    entityShapes.TryGetValue(rel.Entity2, out var s2))
                {
                    var (dx, dy) = diamondPositions[i];
                    DrawRelBetween(rel.Name, rel.Cardinality, s1, s2, dx, dy);
                }
            }

            _page.AutoSizeDrawing();
        }

        /// <summary>
        /// ER 专用星形布局算法
        /// 
        /// 1. 构建邻接图，计算每个实体的度数（连接的关系数量）
        /// 2. 度数最高的实体做枢纽（中心）
        /// 3. 与枢纽直接相连的实体放在内圈
        /// 4. 不与枢纽相连的实体放在外圈
        /// 5. 属性扇面朝外（远离中心方向）
        /// </summary>
        private Dictionary<string, EntityPlacement> CalculateLayout(ErDocument erDoc)
        {
            var result = new Dictionary<string, EntityPlacement>(StringComparer.OrdinalIgnoreCase);
            var entities = erDoc.Entities;
            var rels = erDoc.Relationships;

            if (entities.Count == 0) return result;

            // === 边缘情况：只有1个实体 ===
            if (entities.Count == 1)
            {
                result[entities[0].Name] = new(EntityStartX, EntityY, Math.PI / 2);
                return result;
            }

            // === 边缘情况：无关系 → 水平排列 ===
            if (rels.Count == 0)
            {
                double x = EntityStartX;
                foreach (var e in entities)
                {
                    result[e.Name] = new(x, EntityY, Math.PI / 2);
                    x += EntitySpacing;
                }
                return result;
            }

            // === 边缘情况：只有2个实体 → 左右排列 ===
            if (entities.Count == 2)
            {
                result[entities[0].Name] = new(EntityStartX, EntityY, Math.PI * 3 / 4);
                result[entities[1].Name] = new(EntityStartX + EntitySpacing, EntityY, Math.PI / 4);
                return result;
            }

            // === 正常情况：3+ 个实体 ===

            // 1. 构建邻接图，计算度数
            var degree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entities)
            {
                degree[e.Name] = 0;
                neighbors[e.Name] = new(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var rel in rels)
            {
                if (degree.ContainsKey(rel.Entity1))
                {
                    degree[rel.Entity1]++;
                    neighbors[rel.Entity1].Add(rel.Entity2);
                }
                if (degree.ContainsKey(rel.Entity2))
                {
                    degree[rel.Entity2]++;
                    neighbors[rel.Entity2].Add(rel.Entity1);
                }
            }

            // 2. 找枢纽（度数最高的实体）
            string hub = entities[0].Name;
            int maxDeg = 0;
            foreach (var kv in degree)
            {
                if (kv.Value > maxDeg)
                {
                    maxDeg = kv.Value;
                    hub = kv.Key;
                }
            }

            // 3. 分类：与枢纽直接相连 vs 不相连
            var connectedToHub = new List<string>();
            var notConnected = new List<string>();
            foreach (var e in entities)
            {
                if (e.Name.Equals(hub, StringComparison.OrdinalIgnoreCase)) continue;
                if (neighbors[hub].Contains(e.Name))
                    connectedToHub.Add(e.Name);
                else
                    notConnected.Add(e.Name);
            }

            // 4. 枢纽在中心
            double centerX = EntityStartX + (entities.Count - 1) * EntitySpacing / 2.0;
            double centerY = EntityY;
            result[hub] = new(centerX, centerY, Math.PI / 2);

            // 5. 与枢纽相连的实体放在圆周上（等角分布）
            var allPeripheral = new List<string>();
            allPeripheral.AddRange(connectedToHub);
            allPeripheral.AddRange(notConnected);

            double radius = EntitySpacing * 1.2;
            int totalPeripheral = allPeripheral.Count;

            for (int i = 0; i < totalPeripheral; i++)
            {
                // 从正上方开始，顺时针均匀分布
                // 偏移 PI/2 使第一个在正上方
                double angle = Math.PI / 2.0 + 2.0 * Math.PI * i / totalPeripheral;
                double ex = centerX + radius * Math.Cos(angle);
                double ey = centerY + radius * Math.Sin(angle);

                // 属性扇面朝外（从实体指向远离中心的方向）
                double attrAngle = angle;

                result[allPeripheral[i]] = new(ex, ey, attrAngle);
            }

            return result;
        }

        /// <summary>
        /// 计算每个关系菱形的位置
        /// 放在两个连接实体的中线上，垂直偏移避免重叠
        /// </summary>
        private List<(double X, double Y)> CalculateDiamondPositions(
            ErDocument erDoc, Dictionary<string, EntityPlacement> layout)
        {
            var positions = new List<(double X, double Y)>();

            // 统计每对实体之间的关系数量（用于同对多关系的偏移）
            var pairCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var rel in erDoc.Relationships)
            {
                if (!layout.TryGetValue(rel.Entity1, out var p1) ||
                    !layout.TryGetValue(rel.Entity2, out var p2))
                {
                    positions.Add((0, 0));
                    continue;
                }

                // 中点
                double mx = (p1.X + p2.X) / 2.0;
                double my = (p1.Y + p2.Y) / 2.0;

                // 同对实体间多个关系时，垂直偏移
                string pairKey = string.Compare(rel.Entity1, rel.Entity2,
                    StringComparison.OrdinalIgnoreCase) < 0
                    ? $"{rel.Entity1}|{rel.Entity2}" : $"{rel.Entity2}|{rel.Entity1}";

                if (!pairCount.TryGetValue(pairKey, out int idx))
                    idx = 0;
                pairCount[pairKey] = idx + 1;

                // 垂直偏移（两实体连线的法向偏移）
                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001) len = 1;

                // 法向量（垂直于连线方向）
                double nx = -dy / len;
                double ny = dx / len;

                double offset = (idx - 0) * 1.5; // 每个额外关系偏移 1.5
                mx += nx * offset;
                my += ny * offset;

                positions.Add((mx, my));
            }

            return positions;
        }

        // ============================================================
        // 工具
        // ============================================================

        private static void SetSize(Visio.Shape shape, double w, double h)
        {
            try
            {
                try { shape.get_CellsU("LockWidth").ResultIU = 0; } catch { }
                try { shape.get_CellsU("LockHeight").ResultIU = 0; } catch { }
                shape.get_CellsU("Width").ResultIU = w;
                shape.get_CellsU("Height").ResultIU = h;
            }
            catch { }
        }
    }
}
