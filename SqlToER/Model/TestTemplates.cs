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
            "07.10表压力测试",
            "08.20表极限压测"
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
            8 => TwentyTableStress(),
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

        // ===== 08. 20表极限压力测试 — 属性 3~30，1:1/1:N/M:N =====
        private static ErDocument TwentyTableStress() => new()
        {
            Entities =
            [
                new("Corporation"),   // 30属性
                new("Branch"),        // 5属性
                new("Department"),    // 8属性
                new("Team"),          // 4属性
                new("Employee"),      // 20属性
                new("Manager"),       // 6属性
                new("Intern"),        // 3属性(最少)
                new("Project"),       // 12属性
                new("Task"),          // 9属性
                new("Milestone"),     // 5属性
                new("Client"),        // 10属性
                new("Contract"),      // 7属性
                new("Invoice"),       // 6属性
                new("Product"),       // 15属性
                new("Warehouse"),     // 4属性
                new("Supplier"),      // 8属性
                new("Skill"),         // 3属性(最少)
                new("Certificate"),   // 5属性
                new("Vehicle"),       // 7属性
                new("Office")         // 6属性
            ],
            Attributes =
            [
                // Corporation (30属性 — 最多)
                new("Corporation", "corp_id", true),
                new("Corporation", "corp_name", false),
                new("Corporation", "industry", false),
                new("Corporation", "founded_year", false),
                new("Corporation", "ceo_name", false),
                new("Corporation", "revenue", false),
                new("Corporation", "profit", false),
                new("Corporation", "market_cap", false),
                new("Corporation", "stock_ticker", false),
                new("Corporation", "hq_country", false),
                new("Corporation", "hq_city", false),
                new("Corporation", "hq_address", false),
                new("Corporation", "phone", false),
                new("Corporation", "fax", false),
                new("Corporation", "email", false),
                new("Corporation", "website", false),
                new("Corporation", "employee_count", false),
                new("Corporation", "branch_count", false),
                new("Corporation", "tax_id", false),
                new("Corporation", "reg_number", false),
                new("Corporation", "fiscal_year_end", false),
                new("Corporation", "auditor", false),
                new("Corporation", "board_size", false),
                new("Corporation", "public_listed", false),
                new("Corporation", "exchange", false),
                new("Corporation", "sector", false),
                new("Corporation", "subsector", false),
                new("Corporation", "rating", false),
                new("Corporation", "risk_level", false),
                new("Corporation", "status", false),

                // Branch (5)
                new("Branch", "branch_id", true),
                new("Branch", "branch_name", false),
                new("Branch", "city", false),
                new("Branch", "country", false),
                new("Branch", "capacity", false),

                // Department (8)
                new("Department", "dept_id", true),
                new("Department", "dept_name", false),
                new("Department", "budget", false),
                new("Department", "floor", false),
                new("Department", "head_count", false),
                new("Department", "cost_center", false),
                new("Department", "created_date", false),
                new("Department", "status", false),

                // Team (4)
                new("Team", "team_id", true),
                new("Team", "team_name", false),
                new("Team", "focus_area", false),
                new("Team", "size", false),

                // Employee (20属性)
                new("Employee", "emp_id", true),
                new("Employee", "first_name", false),
                new("Employee", "last_name", false),
                new("Employee", "middle_name", false),
                new("Employee", "email", false),
                new("Employee", "phone", false),
                new("Employee", "mobile", false),
                new("Employee", "salary", false),
                new("Employee", "bonus", false),
                new("Employee", "hire_date", false),
                new("Employee", "birth_date", false),
                new("Employee", "gender", false),
                new("Employee", "address", false),
                new("Employee", "city", false),
                new("Employee", "zip_code", false),
                new("Employee", "country", false),
                new("Employee", "title", false),
                new("Employee", "grade", false),
                new("Employee", "status", false),
                new("Employee", "ssn", false),

                // Manager (6)
                new("Manager", "mgr_id", true),
                new("Manager", "title", false),
                new("Manager", "level", false),
                new("Manager", "office_no", false),
                new("Manager", "direct_reports", false),
                new("Manager", "bonus_pct", false),

                // Intern (3属性 — 最少)
                new("Intern", "intern_id", true),
                new("Intern", "school", false),
                new("Intern", "end_date", false),

                // Project (12)
                new("Project", "proj_id", true),
                new("Project", "proj_name", false),
                new("Project", "start_date", false),
                new("Project", "end_date", false),
                new("Project", "budget", false),
                new("Project", "priority", false),
                new("Project", "status", false),
                new("Project", "risk_level", false),
                new("Project", "sponsor", false),
                new("Project", "methodology", false),
                new("Project", "completion_pct", false),
                new("Project", "phase", false),

                // Task (9)
                new("Task", "task_id", true),
                new("Task", "task_name", false),
                new("Task", "description", false),
                new("Task", "due_date", false),
                new("Task", "status", false),
                new("Task", "effort_hrs", false),
                new("Task", "complexity", false),
                new("Task", "assignee", false),
                new("Task", "priority", false),

                // Milestone (5)
                new("Milestone", "ms_id", true),
                new("Milestone", "ms_name", false),
                new("Milestone", "target_date", false),
                new("Milestone", "achieved", false),
                new("Milestone", "deliverable", false),

                // Client (10)
                new("Client", "client_id", true),
                new("Client", "client_name", false),
                new("Client", "contact_person", false),
                new("Client", "email", false),
                new("Client", "phone", false),
                new("Client", "country", false),
                new("Client", "industry", false),
                new("Client", "rating", false),
                new("Client", "since_year", false),
                new("Client", "credit_limit", false),

                // Contract (7)
                new("Contract", "contract_id", true),
                new("Contract", "title", false),
                new("Contract", "value", false),
                new("Contract", "signed_date", false),
                new("Contract", "expiry_date", false),
                new("Contract", "type", false),
                new("Contract", "status", false),

                // Invoice (6)
                new("Invoice", "invoice_id", true),
                new("Invoice", "amount", false),
                new("Invoice", "issue_date", false),
                new("Invoice", "due_date", false),
                new("Invoice", "paid", false),
                new("Invoice", "currency", false),

                // Product (15)
                new("Product", "product_id", true),
                new("Product", "product_name", false),
                new("Product", "category", false),
                new("Product", "price", false),
                new("Product", "cost", false),
                new("Product", "weight", false),
                new("Product", "dimensions", false),
                new("Product", "color", false),
                new("Product", "sku", false),
                new("Product", "barcode", false),
                new("Product", "stock_qty", false),
                new("Product", "min_stock", false),
                new("Product", "manufacturer", false),
                new("Product", "warranty", false),
                new("Product", "launch_date", false),

                // Warehouse (4)
                new("Warehouse", "wh_id", true),
                new("Warehouse", "location", false),
                new("Warehouse", "area_sqm", false),
                new("Warehouse", "manager", false),

                // Supplier (8)
                new("Supplier", "supplier_id", true),
                new("Supplier", "supplier_name", false),
                new("Supplier", "contact", false),
                new("Supplier", "country", false),
                new("Supplier", "lead_time", false),
                new("Supplier", "rating", false),
                new("Supplier", "payment_terms", false),
                new("Supplier", "min_order", false),

                // Skill (3属性 — 最少)
                new("Skill", "skill_id", true),
                new("Skill", "skill_name", false),
                new("Skill", "category", false),

                // Certificate (5)
                new("Certificate", "cert_id", true),
                new("Certificate", "cert_name", false),
                new("Certificate", "issuer", false),
                new("Certificate", "valid_until", false),
                new("Certificate", "level", false),

                // Vehicle (7)
                new("Vehicle", "vehicle_id", true),
                new("Vehicle", "plate_no", false),
                new("Vehicle", "make", false),
                new("Vehicle", "model", false),
                new("Vehicle", "year", false),
                new("Vehicle", "mileage", false),
                new("Vehicle", "fuel_type", false),

                // Office (6)
                new("Office", "office_id", true),
                new("Office", "city", false),
                new("Office", "address", false),
                new("Office", "capacity", false),
                new("Office", "rent", false),
                new("Office", "floors", false)
            ],
            Relationships =
            [
                // ---- 1:N 关系 ----
                new("HasBranch", "Corporation", "Branch", "1:N"),
                new("HasDept", "Branch", "Department", "1:N"),
                new("HasTeam", "Department", "Team", "1:N"),
                new("WorksIn", "Employee", "Department", "1:N"),
                new("BelongsTo", "Employee", "Team", "1:N"),
                new("Contains", "Project", "Task", "1:N"),
                new("HasMilestone", "Project", "Milestone", "1:N"),
                new("Signs", "Client", "Contract", "1:N"),
                new("BillsTo", "Contract", "Invoice", "1:N"),
                new("Stores", "Warehouse", "Product", "1:N"),
                new("Mentors", "Employee", "Intern", "1:N"),
                new("HasOffice", "Branch", "Office", "1:N"),
                // ---- 1:1 关系 ----
                new("ManagedBy", "Department", "Manager", "1:1"),
                new("IsA", "Manager", "Employee", "1:1"),
                new("AssignedVehicle", "Manager", "Vehicle", "1:1"),
                // ---- M:N 关系 ----
                new("AssignedTo", "Employee", "Project", "M:N"),
                new("WorksOn", "Employee", "Task", "M:N"),
                new("HasSkill", "Employee", "Skill", "M:N"),
                new("EarnedCert", "Employee", "Certificate", "M:N"),
                new("Sponsors", "Client", "Project", "M:N"),
                new("Supplies", "Supplier", "Product", "M:N"),
                new("CoveredBy", "Project", "Contract", "1:N"),
                new("UsesProduct", "Project", "Product", "M:N")
            ]
        };
    }
}
