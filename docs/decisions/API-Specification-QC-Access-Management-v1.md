# API Specification — QC Organization & Access Management (v1)

**وضعیت:** Draft برای Codex
**بر اساس:** سند طراحی قابل دفاع FINAL-1.1 + ADR-0004 (RoleGroup حذف شد، PersonnelPosition دارای EffectiveFrom/EffectiveTo است)
**دامنه:** تمام صفحات Figma (Position، Personnel، Role، Access History) به‌صورت API

---

## 0. Conventions

```
Base path:      /api/v1
Auth:           Bearer JWT (ASP.NET Core Identity) — CompanyId فعال از Workspace Context گرفته می‌شود
Pagination:     ?page=1&pageSize=20&sort=field:asc|desc&q=search
Response لیست:  { items: [...], totalCount, page, pageSize }
Error format:   { code, message, details? }  — 400 Validation, 403 Forbidden (Access evaluation)، 404، 409 Conflict (rule violation)
Every write:    یک AuditLog با OperationId مشترک برای عملیات‌های چندمرحله‌ای (مثل ایجاد Personnel با ۳ مرحله) ثبت می‌شود
Permission gate: هر Endpoint دقیقاً یک Resource.Action از Permission Catalog را می‌طلبد (لیست در پیوست انتهای سند)
```

فایل‌های ضمیمه (Attachment عمومی — Position/Personnel/Role):
```
Format: PDF, XLSX, DOCX      Max: 8MB
POST /api/v1/attachments      multipart/form-data → { attachmentId, url }
GET  /api/v1/attachments/{id}
```
امضای دیجیتال Personnel قانون جدا دارد (بخش ۲.۳) — PNG/JPEG only، max 8MB، MIME header trusted-check (طبق Acceptance Checklist اصلی).

---

## 1. Position API

Permission Resource: `Position` — Actions: `Read, Create, Edit, Delete, Export`

### 1.1 لیست
```
GET /api/v1/organization/positions
    ?companyId=&status=&q=(code/title)&page=&pageSize=
→ 200 { items: [{ id, code, title, companyId, companyName, parentPositionId, parentPositionTitle,
                   personnelCount, status }], totalCount }
Permission: Position.Read
```

### 1.2 ایجاد
```
POST /api/v1/organization/positions
Body: {
  kind: "Organizational" | "NonOrganizational",
  holdingId, companyId,
  code, title,
  parentPositionId: nullable,
  status: "Active" | "Inactive",
  attachmentIds: [guid]
}
→ 201 { id }
Permission: Position.Create

Validation (از سند اصلی):
- parentPositionId باید در همان Company باشد (نمی‌تواند Parent خودش را عوض کند به شرکت دیگر)
- cycle prevention: concurrent parent changes قادر به ساخت cycle نیستند
- code باید طبق constraint یکتا در سطح Company باشد
- Company می‌تواند چند Root Position داشته باشد
```

### 1.3 ویرایش
```
PUT /api/v1/organization/positions/{id}
Body: همان فیلدهای ایجاد (partial ok)
→ 200
Permission: Position.Edit
```

### 1.4 جزئیات
```
GET /api/v1/organization/positions/{id}
→ 200 { ...همان فیلدهای لیست..., description, attachments: [{id, fileName, url}] }
Permission: Position.Read
```

### 1.5 حذف (غیرفعال‌سازی نرم)
```
DELETE /api/v1/organization/positions/{id}
→ 204
→ 409 { code: "ACTIVE_ASSIGNMENT_EXISTS", details: { activePersonnelPositionIds: [...] } }
   اگر Position دارای Assignment فعال (IsCurrentlyEffective=true) باشد و remediation صریح انجام نشده باشد
Permission: Position.Delete
```

### 1.6 نمای درختی (تصویر ۵)
```
GET /api/v1/organization/positions/tree?holdingId=
→ 200 {
    holding: { id, name },
    companies: [{
      id, name,
      positions: [{ id, code, title, parentPositionId, children: [...] }]
    }]
  }
Permission: Position.Read
```

### 1.7 جزئیات سمت انتخاب‌شده در درخت (پنل چپ تصویر ۵)
```
GET /api/v1/organization/positions/{id}/summary
→ 200 { id, code, title, status, description,
        personnel: [{ personnelId, fullName }] }
Permission: Position.Read
```

---

## 2. Personnel API

Permission Resource: `Personnel` — Actions: `Read, Create, Edit, Delete, Export`
Permission Resource: `PersonnelPosition` — Actions: `Read, Create, Edit, Delete`
Permission Resource: `PersonnelSignature` — Actions: `Read, Create`

### 2.1 لیست
```
GET /api/v1/organization/personnel
    ?companyId=&status=&q=(nationalCode/name)&page=&pageSize=
→ 200 { items: [{ id, primaryPersonnelCode, fullName, nationalCode, companyId, companyName,
                   primaryPositionTitle, status, signatureStatus: "Registered"|"NotRegistered" }],
        totalCount }
Permission: Personnel.Read
```

### 2.2 ایجاد — مرحله ۱: اطلاعات پایه
```
POST /api/v1/organization/personnel
Body: {
  firstName, lastName, nationalCode, gender, phoneNumber,
  companyId, status: "Active"|"Inactive",
  attachmentIds: [guid]
}
→ 201 { id }   -- Personnel در حالت DRAFT ساخته می‌شود، هنوز EMPLOYED نیست
Permission: Personnel.Create

Validation:
- nationalCode duplicate → 409 { code: "DUPLICATE_NATIONAL_CODE" }
- ثبت نهایی (EMPLOYED) بدون حداقل یک Position فعال ممکن نیست (بند ۲.۳ را ببین)
```

### 2.3 ایجاد — مرحله ۲: سمت‌ها و نقش‌های کاری
```
POST /api/v1/organization/personnel/{id}/positions
Body: {
  positionId,
  isPrimary: bool,
  effectiveFrom: datetime,          -- می‌تواند در آینده باشد (ADR-0004)
  effectiveTo: datetime | null,     -- می‌تواند از قبل برنامه‌ریزی شود (ADR-0004)
  status: "Active" | "Inactive"
}
→ 201 { personnelPositionId }
Permission: PersonnelPosition.Create

Validation (سند اصلی + ADR-0004):
- Overlap check: بازهٔ [effectiveFrom, effectiveTo) با هیچ رکورد دیگر همان (Personnel,Position) هم‌پوشانی ندارد
- اگر isPrimary=true: هیچ Primary مؤثر دیگری (بازهٔ هم‌پوشان) برای این Personnel در همان Company نباید وجود داشته باشد →
  409 { code: "PRIMARY_OVERLAP_CONFLICT" }
- این Endpoint هرگز Role/AccessRule نمی‌سازد (Position != Role — invariant قفل‌شده)
```

```
PUT /api/v1/organization/personnel/{id}/positions/{personnelPositionId}
Body: { isPrimary?, effectiveFrom?, effectiveTo?, status? }
→ 200
→ 409 { code: "SEALED_RECORD" }   -- اگر now > effectiveTo فعلی (بخش ۲.۷ ADR-0004)؛
                                      باید از طریق Endpoint اصلاحی زیر انجام شود
Permission: PersonnelPosition.Edit
```

```
POST /api/v1/organization/personnel/{id}/positions/{personnelPositionId}/correct
Body: { newEffectiveFrom?, newEffectiveTo?, reason }
→ 201 { newPersonnelPositionId }   -- رکورد جدید با Origin=CORRECTION، رکورد قبلی sealed می‌ماند
Permission: PersonnelPosition.Edit
```

```
DELETE /api/v1/organization/personnel/{id}/positions/{personnelPositionId}
→ 204   -- soft-deactivate (Status=Inactive)، رکورد historical حذف فیزیکی نمی‌شود
Permission: PersonnelPosition.Delete
```

مودال «ویرایش کد شغلی» (تصویر ۹) دقیقاً همین `PUT .../positions/{personnelPositionId}` است؛ فیلدهای قابل ویرایش در مودال: کد شغلی (PositionId نمایشی)، عنوان، وضعیت، IsPrimary.

### 2.4 ایجاد — مرحله ۳: امضای دیجیتال
```
POST /api/v1/organization/personnel/{id}/signature
Body (multipart/form-data): { file: jpg|jpeg (max 8MB), description }
→ 201 { signatureId, version }
Permission: PersonnelSignature.Create

Validation:
- فقط PNG/JPEG پذیرفته می‌شود؛ MIME header باید trusted باشد (بررسی سطح بایت، نه فقط پسوند)
- فایل بیش از ۸MB رد می‌شود
- هر Upload یک Version جدید می‌سازد؛ Version قبلی IsCurrent=false می‌شود ولی حذف نمی‌شود
- محتوای Signature در AuditLog ذخیره نمی‌شود (فقط متادیتا: Version, Hash, تغییر IsCurrent)
```
```
GET /api/v1/organization/personnel/{id}/signature/current
GET /api/v1/organization/personnel/{id}/signature/history
→ 200 [{ version, uploadedAt, isCurrent, approvedAt? }]
Permission: PersonnelSignature.Read
```

### 2.5 جزئیات کامل (تصویر ۸)
```
GET /api/v1/organization/personnel/{id}
→ 200 {
    firstName, lastName, nationalCode, gender, phoneNumber, companyId, status,
    positions: [{ personnelPositionId, positionCode, positionTitle, isPrimary,
                  effectiveFrom, effectiveTo, status, accessGroupHint }],
                  -- accessGroupHint فقط نمایشی است (مثلاً "گروه بازرس QC")؛
                     از Role مرتبط با UserCompany خوانده می‌شود، نه از PersonnelPosition
    attachments: [{ id, fileName, url }],
    signature: { version, url, isCurrent }
  }
Permission: Personnel.Read
```

### 2.6 ویرایش اطلاعات پایه / حذف
```
PUT /api/v1/organization/personnel/{id}
DELETE /api/v1/organization/personnel/{id}
→ DELETE اگر Personnel وضعیت EMPLOYED با Position فعال دارد → 409 { code: "EMPLOYED_HAS_ACTIVE_POSITION" }
Permission: Personnel.Edit / Personnel.Delete
```

---

## 3. Role & Permission API

Permission Resource: `Role` — Actions: `Read, Create, Edit, Delete`
Permission Resource: `AccessRule` — Actions: `Read, Create, Edit, Delete`
Permission Resource: `UserRole` — Actions: `Create, Delete`

> طبق ADR-0004: RoleGroup حذف شده؛ هیچ فیلد «انتخاب گروه‌های عضو» در این API وجود ندارد و ستون «گروه‌های عضو» از لیست حذف شده.

### 3.1 لیست
```
GET /api/v1/access-control/roles
    ?companyId=&applicationId=&status=&q=&page=&pageSize=
→ 200 { items: [{ id, code, title, userCount, status }], totalCount }
Permission: Role.Read
```

### 3.2 ایجاد — مرحله ۱: اطلاعات نقش
```
POST /api/v1/access-control/roles
Body: {
  companyId, applicationId, name, code,
  parentRoleId: nullable,
  validFrom, validUntil: nullable,
  status, description,
  attachmentIds: [guid]
}
→ 201 { id }
Permission: Role.Create
```

### 3.3 ایجاد — مرحله ۲: مجوزهای عملیاتی
```
GET /api/v1/access-control/resources/tree?applicationId=
→ 200 [{ id, code, name, children: [{ id, code, name, actions: ["Read","Edit","Delete","Create","Export","Print"] }] }]
   -- درخت ماژول/زیرماژول برای انتخاب سمت راست فرم (تصویر ۳ و ۴)
Permission: Resource.Read
```
```
PUT /api/v1/access-control/roles/{id}/permissions
Body: {
  entries: [{
    resourceId,                       -- مثلاً «ایستگاه‌کاری» زیر «ساختار تولید»
    actions: [{
      actionCode: "Read"|"Edit"|"Delete"|"Create"|"Export"|"Print",
      effect: "ALLOW" | "DENY",
      scope: { scopeType: "Workshop"|"Company"|"Global", scopeKeys: [guid] } | null
             -- null یعنی بدون Scope اضافی؛ طبق ScopeMode fail-closed،
                اگر Resource نیازمند Scope باشد و اینجا null بماند، Rule مؤثر نمی‌شود
    }]
  }]
}
→ 200 { createdAccessRuleIds: [...] }
Permission: AccessRule.Create

Validation:
- DENY prerequisite gates (بخش ۲۷ سند اصلی) در همین لحظه اعمال می‌شود: اگر ALLOW روی Action‌ای
  ثبت شود که Q (prerequisite) آن در همان branch/scope با DENY مسدود است، آن ALLOW به‌صورت
  ineffective ثبت می‌شود (نه رد کامل request) و در Response با warning علامت می‌خورد.
```

### 3.4 مودال «کارگاه» (تصویر ۸ سند سوم)
```
GET /api/v1/access-control/scopes/workshops?companyId=
→ 200 [{ id, name, code, relatedSite }]
Permission: RuleScope.Read
   -- خروجی این Endpoint دقیقاً همان scopeKeys ورودی PUT .../permissions در بند بالاست
```

### 3.5 کپی مجوزها از نقش دیگر (تصویر ۷ سند سوم)
```
POST /api/v1/access-control/roles/{id}/permissions/copy-from
Body: { sourceRoleId, mode: "APPEND" | "REPLACE" }
→ 200 { copiedAccessRuleCount }
Permission: AccessRule.Create

قاعده: مجوزهای کپی‌شده دقیقاً همان DENY/prerequisite gate بند ۳.۳ را می‌گذرانند —
کپی هیچ raceای برای دور زدن DENY محسوب نمی‌شود (سند اصلی: «RoleGroup ALLOW نیز اگر از همان
Role branch مؤثر می‌شود، تابع همین gates است» — این اصل برای Copy هم به همین ترتیب برقرار است).
mode=REPLACE پیش از کپی، تمام AccessRuleهای موجود با Origin=DIRECT این Role را غیرفعال می‌کند.
```

### 3.6 جزئیات / نمای درختی
```
GET /api/v1/access-control/roles/{id}
→ 200 { code, title, description, status, users: [{ userId, fullName }],
        permissions: [{ resourceId, resourceName, actionCode, effect, scope }] }
Permission: Role.Read

GET /api/v1/access-control/roles/tree?holdingId=
→ همان ساختار درختی Position (بند ۱.۶) ولی روی Role
Permission: Role.Read
```

### 3.7 تخصیص گروهی (جایگزین RoleGroup — ADR-0004 §1.3)
```
POST /api/v1/access-control/roles/{id}/bulk-assign
Body: { userCompanyIds: [guid], validFrom, validUntil: nullable }
→ 200 { createdUserRoleCount }
Permission: UserRole.Create
   -- برای هر userCompanyId یک UserRole مستقل ایجاد می‌کند؛ هیچ Entity گروه پایداری ساخته نمی‌شود
```

---

## 4. Access History (Audit) API

Permission Resource: `AuditLog` — Actions: `Read, Export`

### 4.1 لیست/جستجو
```
GET /api/v1/audit/access-history
    ?actorUserId=&companyId=&dateFrom=&dateTo=
    &changeType=Create|Edit|Delete&resourceId=&source=&page=&pageSize=
→ 200 { items: [{ operationId, actorUserId, actorFullName, actorRoleTitle,
                   occurredAt, changeType, source, resourceSummary }],
        totalCount }
Permission: AuditLog.Read
```

### 4.2 جزئیات یک عملیات (تصویر ۴ سند چهارم)
```
GET /api/v1/audit/access-history/{operationId}
→ 200 {
    actor: { userId, fullName, roleTitle },
    occurredAt, changeType, source,
    changedModules: [{
      resourceId, resourceName, subResourceName,
      actions: [{ actionCode, before: bool, after: bool }]
    }]
  }
Permission: AuditLog.Read
```

### 4.3 خروجی/چاپ
```
GET /api/v1/audit/access-history/export?format=pdf|xlsx&(همان فیلترهای بند ۴.۱)
→ 200 (file stream)
Permission: AuditLog.Export
   -- چاپ در UI از همین Export(pdf) با render مستقیم مرورگر انجام می‌شود؛ Endpoint جدا ندارد.
```

---

## پیوست — Permission Catalog لازم برای این API (اضافه به Catalog موجود)

```
Resource: Position          Actions: Read, Create, Edit, Delete, Export
Resource: Personnel         Actions: Read, Create, Edit, Delete, Export
Resource: PersonnelPosition Actions: Read, Create, Edit, Delete
Resource: PersonnelSignature Actions: Read, Create
Resource: Role              Actions: Read, Create, Edit, Delete
Resource: AccessRule        Actions: Read, Create, Edit, Delete
Resource: UserRole          Actions: Create, Delete
Resource: RuleScope         Actions: Read
Resource: AuditLog          Actions: Read, Export
Resource: Attachment        Actions: Create, Read
```

هر Endpoint بالا دقیقاً به یکی از این Resource.Action نگاشت می‌شود و از همان موتور DENY/Scope/prerequisite سند اصلی عبور می‌کند — هیچ مسیر جداگانه یا bypass برای این ماژول‌ها تعریف نشده است.

## سؤالات باز برای تأیید

1. `accessGroupHint` در جزئیات Personnel (بند ۲.۵) صرفاً نمایشی است یا باید یک Endpoint مجزا برای واکشی Roleهای فعال آن Personnel هم اضافه شود؟
2. برای `PUT .../roles/{id}/permissions`، آیا ارسال یک Resource بدون هیچ Action (یعنی حذف کامل دسترسی‌های قبلی آن Resource) باید معنای «همه را DENY کن» داشته باشد یا «هیچ تغییری نده»؟ این روی رفتار Partial Update اثر مستقیم دارد.
