namespace SqlToER.Model
{
    /// <summary>
    /// 预制测试模板 — 用于逐级验证绘图引擎的每个积木块
    /// </summary>
    public static class TestTemplates
    {
        public static readonly string[] Names =
        [
            "（无）",
            "01.单实体",
            "02.实体+3属性",
            "03.实体+主键",
            "04.双实体+1:N关系",
            "05.完整示例(3表)"
        ];

        /// <summary>
        /// 根据索引生成对应的测试 ErDocument
        /// </summary>
        public static ErDocument? Create(int index) => index switch
        {
            1 => SingleEntity(),
            2 => EntityWith3Attrs(),
            3 => EntityWithPK(),
            4 => TwoEntities1N(),
            5 => FullExample(),
            _ => null
        };

        // ===== 01. 单实体 =====
        private static ErDocument SingleEntity() => new()
        {
            Entities = [new("Student")]
        };

        // ===== 02. 实体+3属性 =====
        private static ErDocument EntityWith3Attrs() => new()
        {
            Entities = [new("Student")],
            Attributes =
            [
                new("Student", "name", false),
                new("Student", "age", false),
                new("Student", "class_id", false)
            ]
        };

        // ===== 03. 实体+主键+普通属性 =====
        private static ErDocument EntityWithPK() => new()
        {
            Entities = [new("Student")],
            Attributes =
            [
                new("Student", "student_id", true),
                new("Student", "name", false),
                new("Student", "age", false)
            ]
        };

        // ===== 04. 双实体+1:N关系 =====
        private static ErDocument TwoEntities1N() => new()
        {
            Entities = [new("ClassRoom"), new("Student")],
            Attributes =
            [
                new("ClassRoom", "class_id", true),
                new("ClassRoom", "name", false),
                new("Student", "student_id", true),
                new("Student", "name", false)
            ],
            Relationships =
            [
                new("BelongsTo", "Student", "ClassRoom", "1:N")
            ]
        };

        // ===== 05. 完整示例(3表) =====
        private static ErDocument FullExample() => new()
        {
            Entities =
            [
                new("ClassRoom"),
                new("Student"),
                new("Course")
            ],
            Attributes =
            [
                new("ClassRoom", "class_id", true),
                new("ClassRoom", "class_name", false),
                new("ClassRoom", "floor", false),
                new("Student", "student_id", true),
                new("Student", "name", false),
                new("Student", "age", false),
                new("Student", "class_id", false),
                new("Course", "course_id", true),
                new("Course", "course_name", false),
                new("Course", "credits", false)
            ],
            Relationships =
            [
                new("BelongsTo", "Student", "ClassRoom", "1:N"),
                new("Enrolls", "Student", "Course", "M:N")
            ]
        };
    }
}
