using SqlToER.Model;

namespace SqlToER.Helper
{
    /// <summary>
    /// ER 文档结构验证器
    /// </summary>
    public static class ErDocumentValidator
    {
        /// <summary>
        /// 验证 ErDocument 的结构合理性，返回错误列表（空 = 通过）
        /// </summary>
        public static List<string> Validate(ErDocument doc)
        {
            var errors = new List<string>();
            var entityNames = new HashSet<string>(doc.Entities.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);

            // 1. 实体非空
            if (entityNames.Count == 0)
                errors.Add("Entities 为空，未识别到任何实体/表");

            // 2. 属性归属检查
            foreach (var attr in doc.Attributes)
            {
                if (!entityNames.Contains(attr.EntityName))
                    errors.Add($"Attribute '{attr.Name}' 的 EntityName '{attr.EntityName}' 不在 Entities 列表中");
            }

            // 3. 每个实体至少有一个主键
            var entitiesWithPk = new HashSet<string>(
                doc.Attributes.Where(a => a.IsPrimaryKey).Select(a => a.EntityName),
                StringComparer.OrdinalIgnoreCase);

            foreach (var entity in doc.Entities)
            {
                if (!entitiesWithPk.Contains(entity.Name))
                    errors.Add($"实体 '{entity.Name}' 没有识别到主键字段");
            }

            // 4. 关系端点检查 + 基数合法性
            var validCardinalities = new HashSet<string> { "1:1", "1:N", "M:N" };
            foreach (var rel in doc.Relationships)
            {
                if (!entityNames.Contains(rel.Entity1))
                    errors.Add($"Relationship '{rel.Name}' 的 Entity1 '{rel.Entity1}' 不在 Entities 列表中");
                if (!entityNames.Contains(rel.Entity2))
                    errors.Add($"Relationship '{rel.Name}' 的 Entity2 '{rel.Entity2}' 不在 Entities 列表中");
                if (!validCardinalities.Contains(rel.Cardinality))
                    errors.Add($"Relationship '{rel.Name}' 的 Cardinality '{rel.Cardinality}' 非法，应为 1:1/1:N/M:N");
            }

            return errors;
        }
    }
}
