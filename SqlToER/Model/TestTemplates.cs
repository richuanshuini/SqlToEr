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
            "08.20表极限压测",
            "09.30表极限压测"
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
            9 => ThirtyTableStress(),
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
        // ===== 09. 30表极限压力测试 — 属性 2~30，关系复杂度 1~10/实体 =====
        private static ErDocument ThirtyTableStress() => new()
        {
            Entities =
            [
                new("Organization"),  // 30属性
                new("Division"),      // 8属性
                new("Department"),    // 12属性
                new("Team"),          // 5属性
                new("Employee"),      // 25属性
                new("Manager"),       // 7属性
                new("Intern"),        // 3属性
                new("Contractor"),    // 6属性
                new("Project"),       // 18属性
                new("Task"),          // 10属性
                new("Milestone"),     // 5属性
                new("Deliverable"),   // 4属性
                new("Client"),        // 15属性
                new("Contract"),      // 9属性
                new("Invoice"),       // 7属性
                new("Payment"),       // 6属性
                new("Product"),       // 20属性
                new("Warehouse"),     // 5属性
                new("Supplier"),      // 10属性
                new("Shipment"),      // 8属性
                new("Skill"),         // 3属性
                new("Certificate"),   // 5属性
                new("Training"),      // 4属性
                new("Vehicle"),       // 9属性
                new("Office"),        // 6属性
                new("Meeting"),       // 2属性
                new("Document"),      // 7属性
                new("Budget"),        // 11属性
                new("Risk"),          // 4属性
                new("Audit")          // 3属性
            ],
            Attributes =
            [
                // Organization (30属性)
                new("Organization", "org_id", true),
                new("Organization", "org_name", false),
                new("Organization", "industry", false),
                new("Organization", "founded", false),
                new("Organization", "ceo", false),
                new("Organization", "revenue", false),
                new("Organization", "profit", false),
                new("Organization", "market_cap", false),
                new("Organization", "ticker", false),
                new("Organization", "country", false),
                new("Organization", "city", false),
                new("Organization", "address", false),
                new("Organization", "zip_code", false),
                new("Organization", "phone", false),
                new("Organization", "fax", false),
                new("Organization", "website", false),
                new("Organization", "email", false),
                new("Organization", "tax_id", false),
                new("Organization", "reg_number", false),
                new("Organization", "legal_form", false),
                new("Organization", "employees_cnt", false),
                new("Organization", "sector", false),
                new("Organization", "rating", false),
                new("Organization", "stock_exchange", false),
                new("Organization", "fiscal_year", false),
                new("Organization", "currency", false),
                new("Organization", "language", false),
                new("Organization", "timezone", false),
                new("Organization", "logo_url", false),
                new("Organization", "description", false),
                // Division (8属性)
                new("Division", "div_id", true),
                new("Division", "div_name", false),
                new("Division", "head_count", false),
                new("Division", "budget", false),
                new("Division", "location", false),
                new("Division", "created_date", false),
                new("Division", "status", false),
                new("Division", "floor", false),
                // Department (12属性)
                new("Department", "dept_id", true),
                new("Department", "dept_name", false),
                new("Department", "dept_code", false),
                new("Department", "head_count", false),
                new("Department", "budget", false),
                new("Department", "cost_center", false),
                new("Department", "location", false),
                new("Department", "phone_ext", false),
                new("Department", "email_alias", false),
                new("Department", "floor", false),
                new("Department", "active", false),
                new("Department", "created_date", false),
                // Team (5属性)
                new("Team", "team_id", true),
                new("Team", "team_name", false),
                new("Team", "size", false),
                new("Team", "focus_area", false),
                new("Team", "sprint_cycle", false),
                // Employee (25属性)
                new("Employee", "emp_id", true),
                new("Employee", "first_name", false),
                new("Employee", "last_name", false),
                new("Employee", "email", false),
                new("Employee", "phone", false),
                new("Employee", "hire_date", false),
                new("Employee", "salary", false),
                new("Employee", "bonus", false),
                new("Employee", "job_title", false),
                new("Employee", "grade", false),
                new("Employee", "ssn", false),
                new("Employee", "dob", false),
                new("Employee", "gender", false),
                new("Employee", "address", false),
                new("Employee", "city", false),
                new("Employee", "state", false),
                new("Employee", "zip", false),
                new("Employee", "country", false),
                new("Employee", "emergency_contact", false),
                new("Employee", "blood_type", false),
                new("Employee", "photo_url", false),
                new("Employee", "badge_id", false),
                new("Employee", "parking_spot", false),
                new("Employee", "shift", false),
                new("Employee", "active", false),
                // Manager (7属性)
                new("Manager", "mgr_id", true),
                new("Manager", "mgr_level", false),
                new("Manager", "reports_count", false),
                new("Manager", "bonus_pct", false),
                new("Manager", "authority", false),
                new("Manager", "office_num", false),
                new("Manager", "assistant", false),
                // Intern (3属性)
                new("Intern", "intern_id", true),
                new("Intern", "school", false),
                new("Intern", "end_date", false),
                // Contractor (6属性)
                new("Contractor", "ctr_id", true),
                new("Contractor", "company", false),
                new("Contractor", "hourly_rate", false),
                new("Contractor", "contract_end", false),
                new("Contractor", "clearance", false),
                new("Contractor", "agency", false),
                // Project (18属性)
                new("Project", "proj_id", true),
                new("Project", "proj_name", false),
                new("Project", "start_date", false),
                new("Project", "end_date", false),
                new("Project", "budget", false),
                new("Project", "status", false),
                new("Project", "priority", false),
                new("Project", "category", false),
                new("Project", "description", false),
                new("Project", "methodology", false),
                new("Project", "risk_level", false),
                new("Project", "completion_pct", false),
                new("Project", "sponsor", false),
                new("Project", "roi", false),
                new("Project", "phase", false),
                new("Project", "gate_status", false),
                new("Project", "code_repo", false),
                new("Project", "wiki_url", false),
                // Task (10属性)
                new("Task", "task_id", true),
                new("Task", "task_name", false),
                new("Task", "assignee", false),
                new("Task", "due_date", false),
                new("Task", "status", false),
                new("Task", "priority", false),
                new("Task", "story_points", false),
                new("Task", "sprint", false),
                new("Task", "label", false),
                new("Task", "created_at", false),
                // Milestone (5属性)
                new("Milestone", "ms_id", true),
                new("Milestone", "ms_name", false),
                new("Milestone", "target_date", false),
                new("Milestone", "achieved", false),
                new("Milestone", "deliverable", false),
                // Deliverable (4属性)
                new("Deliverable", "dlv_id", true),
                new("Deliverable", "dlv_name", false),
                new("Deliverable", "format", false),
                new("Deliverable", "sign_off", false),
                // Client (15属性)
                new("Client", "client_id", true),
                new("Client", "client_name", false),
                new("Client", "industry", false),
                new("Client", "contact_person", false),
                new("Client", "email", false),
                new("Client", "phone", false),
                new("Client", "address", false),
                new("Client", "city", false),
                new("Client", "country", false),
                new("Client", "tier", false),
                new("Client", "account_mgr", false),
                new("Client", "since_date", false),
                new("Client", "credit_limit", false),
                new("Client", "payment_terms", false),
                new("Client", "nda_signed", false),
                // Contract (9属性)
                new("Contract", "contract_id", true),
                new("Contract", "contract_type", false),
                new("Contract", "value", false),
                new("Contract", "start_date", false),
                new("Contract", "end_date", false),
                new("Contract", "status", false),
                new("Contract", "penalty_clause", false),
                new("Contract", "auto_renew", false),
                new("Contract", "signed_by", false),
                // Invoice (7属性)
                new("Invoice", "inv_id", true),
                new("Invoice", "inv_date", false),
                new("Invoice", "amount", false),
                new("Invoice", "tax", false),
                new("Invoice", "due_date", false),
                new("Invoice", "status", false),
                new("Invoice", "currency", false),
                // Payment (6属性)
                new("Payment", "pay_id", true),
                new("Payment", "pay_date", false),
                new("Payment", "amount", false),
                new("Payment", "method", false),
                new("Payment", "reference", false),
                new("Payment", "confirmed", false),
                // Product (20属性)
                new("Product", "prod_id", true),
                new("Product", "prod_name", false),
                new("Product", "sku", false),
                new("Product", "category", false),
                new("Product", "price", false),
                new("Product", "cost", false),
                new("Product", "weight", false),
                new("Product", "dimensions", false),
                new("Product", "color", false),
                new("Product", "material", false),
                new("Product", "brand", false),
                new("Product", "model", false),
                new("Product", "warranty", false),
                new("Product", "origin", false),
                new("Product", "hs_code", false),
                new("Product", "stock_qty", false),
                new("Product", "reorder_level", false),
                new("Product", "lead_time", false),
                new("Product", "discontinued", false),
                new("Product", "image_url", false),
                // Warehouse (5属性)
                new("Warehouse", "wh_id", true),
                new("Warehouse", "wh_name", false),
                new("Warehouse", "location", false),
                new("Warehouse", "capacity", false),
                new("Warehouse", "manager", false),
                // Supplier (10属性)
                new("Supplier", "supp_id", true),
                new("Supplier", "supp_name", false),
                new("Supplier", "country", false),
                new("Supplier", "contact", false),
                new("Supplier", "email", false),
                new("Supplier", "phone", false),
                new("Supplier", "rating", false),
                new("Supplier", "lead_time", false),
                new("Supplier", "min_order", false),
                new("Supplier", "payment_terms", false),
                // Shipment (8属性)
                new("Shipment", "ship_id", true),
                new("Shipment", "ship_date", false),
                new("Shipment", "carrier", false),
                new("Shipment", "tracking", false),
                new("Shipment", "weight", false),
                new("Shipment", "cost", false),
                new("Shipment", "eta", false),
                new("Shipment", "status", false),
                // Skill (3属性)
                new("Skill", "skill_id", true),
                new("Skill", "skill_name", false),
                new("Skill", "category", false),
                // Certificate (5属性)
                new("Certificate", "cert_id", true),
                new("Certificate", "cert_name", false),
                new("Certificate", "issuer", false),
                new("Certificate", "valid_until", false),
                new("Certificate", "level", false),
                // Training (4属性)
                new("Training", "train_id", true),
                new("Training", "course_name", false),
                new("Training", "duration_hrs", false),
                new("Training", "provider", false),
                // Vehicle (9属性)
                new("Vehicle", "vehicle_id", true),
                new("Vehicle", "plate", false),
                new("Vehicle", "make", false),
                new("Vehicle", "model", false),
                new("Vehicle", "year", false),
                new("Vehicle", "mileage", false),
                new("Vehicle", "fuel_type", false),
                new("Vehicle", "insurance_exp", false),
                new("Vehicle", "assigned_to", false),
                // Office (6属性)
                new("Office", "office_id", true),
                new("Office", "office_name", false),
                new("Office", "city", false),
                new("Office", "floor_count", false),
                new("Office", "capacity", false),
                new("Office", "rent_cost", false),
                // Meeting (2属性)
                new("Meeting", "mtg_id", true),
                new("Meeting", "mtg_title", false),
                // Document (7属性)
                new("Document", "doc_id", true),
                new("Document", "doc_title", false),
                new("Document", "version", false),
                new("Document", "author", false),
                new("Document", "created_at", false),
                new("Document", "file_type", false),
                new("Document", "file_size", false),
                // Budget (11属性)
                new("Budget", "budget_id", true),
                new("Budget", "fiscal_year", false),
                new("Budget", "quarter", false),
                new("Budget", "amount", false),
                new("Budget", "spent", false),
                new("Budget", "remaining", false),
                new("Budget", "category", false),
                new("Budget", "approved_by", false),
                new("Budget", "status", false),
                new("Budget", "variance", false),
                new("Budget", "notes", false),
                // Risk (4属性)
                new("Risk", "risk_id", true),
                new("Risk", "description", false),
                new("Risk", "severity", false),
                new("Risk", "mitigation", false),
                // Audit (3属性)
                new("Audit", "audit_id", true),
                new("Audit", "audit_date", false),
                new("Audit", "findings", false)
            ],
            Relationships =
            [
                // 层级结构 (1:N)
                new("HasDivision", "Organization", "Division", "1:N"),
                new("HasDept", "Division", "Department", "1:N"),
                new("HasTeam", "Department", "Team", "1:N"),
                new("BelongsTo", "Employee", "Team", "M:N"),
                new("ManagedBy", "Team", "Manager", "1:1"),
                // 人员关系
                new("Manages", "Manager", "Employee", "1:N"),
                new("SupervisedBy", "Intern", "Employee", "1:N"),
                new("ContractedBy", "Contractor", "Department", "M:N"),
                new("HasSkill", "Employee", "Skill", "M:N"),
                new("EarnedCert", "Employee", "Certificate", "M:N"),
                new("AttendsTrain", "Employee", "Training", "M:N"),
                // 项目关系
                new("AssignedTo", "Employee", "Project", "M:N"),
                new("WorksOn", "Employee", "Task", "M:N"),
                new("HasTask", "Project", "Task", "1:N"),
                new("HasMilestone", "Project", "Milestone", "1:N"),
                new("HasDeliverable", "Milestone", "Deliverable", "1:N"),
                new("RequiresSkill", "Project", "Skill", "M:N"),
                // 客户-合同-财务
                new("Sponsors", "Client", "Project", "M:N"),
                new("SignsContract", "Client", "Contract", "1:N"),
                new("CoveredBy", "Project", "Contract", "1:N"),
                new("HasInvoice", "Contract", "Invoice", "1:N"),
                new("PaidBy", "Invoice", "Payment", "1:N"),
                // 供应链
                new("Supplies", "Supplier", "Product", "M:N"),
                new("StoredIn", "Product", "Warehouse", "M:N"),
                new("ShipsFrom", "Warehouse", "Shipment", "1:N"),
                new("Contains", "Shipment", "Product", "M:N"),
                new("UsesProduct", "Project", "Product", "M:N"),
                // 办公-车辆
                new("LocatedIn", "Department", "Office", "1:N"),
                new("AssignedVehicle", "Employee", "Vehicle", "1:1"),
                new("FleetAt", "Office", "Vehicle", "1:N"),
                // 会议-文档
                new("AttendsM", "Employee", "Meeting", "M:N"),
                new("Discusses", "Meeting", "Project", "M:N"),
                new("HasDoc", "Project", "Document", "1:N"),
                new("AuthoredBy", "Document", "Employee", "1:N"),
                // 预算-风险-审计
                new("AllocBudget", "Department", "Budget", "1:N"),
                new("ProjBudget", "Project", "Budget", "1:N"),
                new("HasRisk", "Project", "Risk", "1:N"),
                new("MitigatedBy", "Risk", "Employee", "M:N"),
                new("AuditsDept", "Audit", "Department", "1:N"),
                new("AuditsProj", "Audit", "Project", "1:N"),
                // 额外跨域关系
                new("SupplierAudit", "Audit", "Supplier", "1:N"),
                new("TrainingCert", "Training", "Certificate", "M:N"),
                new("OfficeAt", "Organization", "Office", "1:N"),
                new("OrgClient", "Organization", "Client", "M:N"),
                new("InternProject", "Intern", "Project", "M:N")
            ]
        };
    }
}
