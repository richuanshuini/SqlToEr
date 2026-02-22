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
            "05.完整示例(3表)",
            "06.5表(1:1+1:N+M:N)"
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
            6 => FiveTablesAllRelations(),
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

        // ===== 06. 5表 — 同时包含 1:1、1:N、M:N =====
        private static ErDocument FiveTablesAllRelations() => new()
        {
            Entities =
            [
                new("Department"),
                new("Employee"),
                new("Passport"),
                new("Project"),
                new("Skill")
            ],
            Attributes =
            [
                // Department
                new("Department", "dept_id", true),
                new("Department", "dept_name", false),
                new("Department", "location", false),
                // Employee
                new("Employee", "emp_id", true),
                new("Employee", "name", false),
                new("Employee", "salary", false),
                new("Employee", "hire_date", false),
                // Passport
                new("Passport", "passport_no", true),
                new("Passport", "issue_date", false),
                new("Passport", "expire_date", false),
                // Project
                new("Project", "proj_id", true),
                new("Project", "proj_name", false),
                new("Project", "budget", false),
                // Skill
                new("Skill", "skill_id", true),
                new("Skill", "skill_name", false),
                new("Skill", "level", false)
            ],
            Relationships =
            [
                // 1:N — 部门 → 员工
                new("WorksIn", "Employee", "Department", "1:N"),
                // 1:1 — 员工 ↔ 护照
                new("Holds", "Employee", "Passport", "1:1"),
                // M:N — 员工 ↔ 项目
                new("AssignedTo", "Employee", "Project", "M:N"),
                // M:N — 员工 ↔ 技能
                new("HasSkill", "Employee", "Skill", "M:N")
            ]
        };
    }
}
