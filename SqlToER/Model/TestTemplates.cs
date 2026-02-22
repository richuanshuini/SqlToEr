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
            "06.5表(1:1+1:N+M:N)",
            "07.10表压力测试"
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
            7 => TenTableStress(),
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

        // ===== 07. 10表压力测试 — 属性 3~15，关系 1:1/1:N/M:N =====
        private static ErDocument TenTableStress() => new()
        {
            Entities =
            [
                new("Company"),      // 中心枢纽
                new("Department"),
                new("Employee"),
                new("Manager"),
                new("Project"),
                new("Task"),
                new("Client"),
                new("Contract"),
                new("Skill"),
                new("Office")
            ],
            Attributes =
            [
                // Company (5)
                new("Company", "company_id", true),
                new("Company", "name", false),
                new("Company", "industry", false),
                new("Company", "founded", false),
                new("Company", "revenue", false),

                // Department (4)
                new("Department", "dept_id", true),
                new("Department", "dept_name", false),
                new("Department", "budget", false),
                new("Department", "floor", false),

                // Employee (8)
                new("Employee", "emp_id", true),
                new("Employee", "first_name", false),
                new("Employee", "last_name", false),
                new("Employee", "email", false),
                new("Employee", "phone", false),
                new("Employee", "salary", false),
                new("Employee", "hire_date", false),
                new("Employee", "status", false),

                // Manager (3)
                new("Manager", "mgr_id", true),
                new("Manager", "title", false),
                new("Manager", "level", false),

                // Project (6)
                new("Project", "proj_id", true),
                new("Project", "proj_name", false),
                new("Project", "start_date", false),
                new("Project", "end_date", false),
                new("Project", "budget", false),
                new("Project", "priority", false),

                // Task (7)
                new("Task", "task_id", true),
                new("Task", "task_name", false),
                new("Task", "description", false),
                new("Task", "due_date", false),
                new("Task", "status", false),
                new("Task", "effort_hrs", false),
                new("Task", "complexity", false),

                // Client (5)
                new("Client", "client_id", true),
                new("Client", "client_name", false),
                new("Client", "contact", false),
                new("Client", "country", false),
                new("Client", "rating", false),

                // Contract (6)
                new("Contract", "contract_id", true),
                new("Contract", "title", false),
                new("Contract", "value", false),
                new("Contract", "signed_date", false),
                new("Contract", "expiry_date", false),
                new("Contract", "type", false),

                // Skill (3)
                new("Skill", "skill_id", true),
                new("Skill", "skill_name", false),
                new("Skill", "category", false),

                // Office (4)
                new("Office", "office_id", true),
                new("Office", "city", false),
                new("Office", "address", false),
                new("Office", "capacity", false)
            ],
            Relationships =
            [
                // 1:N — 公司 → 部门
                new("Has", "Company", "Department", "1:N"),
                // 1:N — 部门 → 员工
                new("WorksIn", "Employee", "Department", "1:N"),
                // 1:1 — 部门 ↔ 经理
                new("ManagedBy", "Department", "Manager", "1:1"),
                // M:N — 员工 ↔ 项目
                new("AssignedTo", "Employee", "Project", "M:N"),
                // 1:N — 项目 → 任务
                new("Contains", "Project", "Task", "1:N"),
                // M:N — 员工 ↔ 任务
                new("WorksOn", "Employee", "Task", "M:N"),
                // 1:N — 客户 → 合同
                new("Signs", "Client", "Contract", "1:N"),
                // 1:N — 项目 ↔ 合同
                new("CoveredBy", "Project", "Contract", "1:N"),
                // M:N — 员工 ↔ 技能
                new("HasSkill", "Employee", "Skill", "M:N"),
                // 1:N — 公司 → 办公室
                new("Owns", "Company", "Office", "1:N"),
                // 1:1 — 经理 ↔ 员工
                new("IsA", "Manager", "Employee", "1:1"),
                // M:N — 客户 ↔ 项目
                new("Sponsors", "Client", "Project", "M:N")
            ]
        };
    }
}
