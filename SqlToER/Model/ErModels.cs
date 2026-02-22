namespace SqlToER.Model
{
    /// <summary>
    /// 陈氏 ER 图 — 实体（支持可选坐标）
    /// </summary>
    public record ErEntity(string Name, double? X = null, double? Y = null);

    /// <summary>
    /// 陈氏 ER 图 — 属性（支持可选坐标）
    /// </summary>
    public record ErAttribute(
        string EntityName,
        string Name,
        bool IsPrimaryKey,
        double? X = null,
        double? Y = null
    );

    /// <summary>
    /// 陈氏 ER 图 — 关系（支持可选坐标）
    /// </summary>
    public record ErRelationship(
        string Name,
        string Entity1,
        string Entity2,
        string Cardinality,   // "1:1" | "1:N" | "M:N"
        double? X = null,
        double? Y = null
    );

    /// <summary>
    /// 陈氏 ER 图 — 文档根对象
    /// </summary>
    public class ErDocument
    {
        public List<ErEntity> Entities { get; set; } = [];
        public List<ErAttribute> Attributes { get; set; } = [];
        public List<ErRelationship> Relationships { get; set; } = [];
    }
}
