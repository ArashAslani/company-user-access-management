# ADR-0004 — حذف RoleGroup + Effective Dating برای PersonnelPosition

**وضعیت:** Accepted
**نسخه سند پایه:** FINAL-1.1 (سند طراحی قابل دفاع Identity/Organization/Authorization)
**دامنه تأثیر:** Authorization (بخش A، سکشن‌های ۲۲/۲۴/۲۷/۵۳)، Organization (بخش A، سکشن ۱۴)، Workflow/Cartable

---

## Decision 1 — حذف RoleGroup

### 1.1 Schema Diff

**حذف کامل:**
```
DROP: RoleGroup            (Id, CompanyId, ApplicationId, PrincipalId)
DROP: RoleGroupRole         (RoleGroupId FK, RoleId FK)
```

**تغییر:**
```
AuthPrincipal.Kind: { User, Role, RoleGroup }  →  { User, Role }
```

`AccessRule.PrincipalId` از این پس فقط می‌تواند به یک principal از نوع `User` (از طریق `UserCompany`) یا `Role` اشاره کند. هر `AccessRule` موجود با `PrincipalId` از نوع `RoleGroup` باید قبل از drop شدن جدول migrate شود (نگاه کن به ۱.۳).

### 1.2 Cascading impacts روی متن قفل‌شده

| سکشن سند اصلی | اثر |
|---|---|
| ۲۲. Role Group | کل سکشن Deprecated می‌شود؛ باید از سند حذف یا با یادداشت "Removed in ADR-0004" علامت‌گذاری شود. |
| ۲۷.0 (خط "RoleGroup ALLOW نیز اگر از همان Role branch مؤثر می‌شود، تابع همین prerequisite/DENY gates است...") | این جمله حذف می‌شود؛ دیگر موضوعیت ندارد چون RoleGroup منبع ALLOW نیست. |
| ۵۳. Effective Access فرمول `UNION RoleGroup contribution` | از فرمول حذف می‌شود: <br>`EffectiveAccess = Direct User source UNION Role Branch(es) UNION valid Delegations` |
| ERD (شکل ۱) | باکس‌های `ROLE_GROUP` و `ROLE_GROUP_ROLE` و یال‌های مرتبط حذف می‌شوند؛ `AUTH_PRINCIPAL` فقط دو یال ورودی (از `USER_COMPANY` و `ROLE`) باقی می‌ماند. |
| Acceptance Checklist بخش Authorization | آیتم‌های مرتبط با RoleGroup (ساخت، assignment، copy) حذف می‌شوند. |

### 1.3 جایگزین قابلیت «تخصیص گروهی» (Bulk Assignment)

هدف اصلی RoleGroup — طبق طراحی قبلی — «container خالص برای bulk assignment» بود، نه یک واحد امنیتی مستقل. این قابلیت با حذف Entity از بین نمی‌رود؛ به یک **Application Command** بدون Persistent Entity منتقل می‌شود:

```
BulkAssignRoleCommand
--------------------------------
RoleId
UserCompanyIds[]          (یا یک Filter: Position/Department/...)
ValidFrom / ValidUntil

=> برای هر UserCompanyId یک ردیف مستقل UserRole ایجاد می‌کند.
هیچ Entity واسطی (Group) پایدار نمی‌ماند.
```

این یعنی UI می‌تواند همان تجربه «انتخاب چند کاربر و اساین یک نقش» را بدهد (مطابق طرح Figma «انتخاب گروه‌های عضو» در فرم ایجاد نقش)، بدون این‌که دیتامدل یک Entity گروه نگه دارد. اگر بعداً نیاز به «گروه پایدار قابل استفاده مجدد» احساس شد (نه یک‌بار مصرف)، این یک تصمیم جداست و باید دوباره باز شود — فعلاً به‌صراحت خارج از دامنه است.

### 1.4 Migration (اگر داده موجود دارید)

```
FOR EACH RoleGroup g:
    FOR EACH (u, r) IN effective memberships of g:
        اگر AccessRule با PrincipalId=g.PrincipalId, RoleId=r وجود دارد:
            برای هر User عضو g → یک AccessRule/UserRole مستقیم با PrincipalId=User بساز
    سپس g و RoleGroupRole مرتبط را حذف کن
```
این یک عملیات یک‌باره seed/migration است؛ باید در audit با `Origin=SYSTEM` ثبت شود.

### 1.5 سؤال باز برای تأیید

Delegation (سکشن ۳۳) طبق قفل قبلی «strict subset rules» دارد. در سند فعلی هیچ اشاره‌ای نیست که RoleGroup هدف مستقیم Delegation بوده باشد، پس فرض می‌کنم Delegation بدون تغییر باقی می‌ماند. اگر جایی RoleGroup به‌عنوان delegate-target استفاده می‌شده، باید صریحاً بگی تا اصلاح شود.

---

## Decision 2 — Effective Dating برای PersonnelPosition (Start/End)

### 2.1 مسئله

فیلدهای فعلی (`AssignedAt`/`EndedAt` در ERD) صرفاً audit-timestamp هستند (چه زمانی رکورد در سیستم ثبت/بسته شد)، نه «چه زمانی این انتساب در دنیای واقعی معتبر است». نیاز جدید: پرسنل ممکن است شروع کارش در یک سمت از آینده باشد، یا برکناری‌اش از قبل برای یک تاریخ مشخص در آینده برنامه‌ریزی شده باشد. این مستقیماً روی **کارتابل/Workflow** اثر می‌گذارد، چون ارجاع باید به «دارندهٔ مؤثرِ فعلیِ سمت» برود، نه هرکسی که صرفاً Status=Active دارد.

### 2.2 Schema Diff

```
PersonnelPosition
--------------------------------
Id                  PK
PersonnelId         FK
PositionId          FK
IsPrimary           bool
Status              ACTIVE / INACTIVE          -- کلید دستی روشن/خاموش، مستقل از تاریخ
EffectiveFrom        datetime  NOT NULL          -- شروع واقعی/برنامه‌ریزی‌شده (می‌تواند در آینده باشد)
EffectiveTo           datetime  NULL              -- پایان واقعی/برنامه‌ریزی‌شده (می‌تواند در آینده باشد)
CreatedAt            datetime  NOT NULL          -- audit: چه زمانی رکورد ثبت شد
DeactivatedAt        datetime  NULL              -- audit: چه زمانی Status به INACTIVE تغییر کرد (اگر زودتر از EffectiveTo برنامه‌ریزی‌شده باشد)
```

**نکتهٔ طراحی مهم:** `Status` و `Effective window` دو محور مستقل‌اند:
- `Status=INACTIVE` یعنی یک ادمین صریحاً این رکورد را خاموش کرده (صرف‌نظر از تاریخ).
- `EffectiveFrom/EffectiveTo` یعنی بازهٔ زمانی که این انتساب — اگر Status اجازه بدهد — معتبر است.

### 2.3 قاعدهٔ «دارندهٔ مؤثر فعلی» (برای Workflow/Cartable و OrgChart)

این تابع باید در یک نقطهٔ واحد (نه در هر query جداگانه) پیاده‌سازی شود:

```
IsCurrentlyEffective(pp, now) =
    pp.Status == ACTIVE
    AND pp.EffectiveFrom <= now
    AND (pp.EffectiveTo IS NULL OR pp.EffectiveTo > now)
```

**توصیهٔ صریح:** یک وضعیت ذخیره‌شدهٔ جداگانه مثل `SCHEDULED` یا `ENDED` اضافه نکنید. این وضعیت‌ها باید **derived** باشند (محاسبه‌شده در لحظهٔ خواندن)، نه ستون دیتابیس — چون نگه‌داشتن آن‌ها به‌صورت stored نیاز به یک background job برای flip کردن دقیقاً سر ساعت `EffectiveFrom`/`EffectiveTo` دارد که یک منبع کلاسیک باگ (race با کارتابل، timezone، job قطع‌شده) است. `IsCurrentlyEffective` همیشه at-read-time محاسبه شود.

### 2.4 اثر روی Cartable/Workflow

```
GetCurrentHolder(positionId, now) =
    SELECT Personnel
    FROM PersonnelPosition
    WHERE PositionId = positionId
      AND IsCurrentlyEffective(*, now) = true
```

اگر بیش از یک نفر هم‌زمان `IsCurrentlyEffective=true` روی همان Position باشند (چند نفر هم‌زمان یک سمت را دارند)، ارجاع کارتابل باید به **همهٔ** آن‌ها برود یا به Primary — این یک تصمیم Workflow جداست که باید صریح مشخص شود (پیشنهاد: به همه، با قابلیت Claim توسط اولین نفر؛ رفتار «فقط Primary» ریسک این را دارد که اگر Primary هنوز فعال نشده و نفر قبلی هنوز در بازهٔ Effective باشد، هیچ‌کس ارجاع را نگیرد).

این تصمیم فقط بخشی از gap قبلاً پرچم‌گذاری‌شده («Workshop-based task routing») را می‌بندد — بعد زمانی آن را حل می‌کند، ولی بعد Personnel/Position→Workshop همچنان باز است و تصمیم جداگانهٔ خودش را می‌خواهد.

### 2.5 اثر روی Primary Position Uniqueness (بخش ۱۴.۱.۱ سند اصلی)

قاعدهٔ فعلی: «در هر Company بیش از یک Primary فعال برای یک Personnel ایجاد نمی‌شود.» این باید به effective-window گسترش پیدا کند:

```
Constraint (application-level, نه صرفاً DB unique index ساده):
FOR a given Personnel, Company:
    at most ONE row WHERE IsPrimary=true AND IsCurrentlyEffective(*, now)=true
```

چون دو ردیف Primary می‌توانند هم‌زمان در DB باشند (یکی رو به پایان، یکی تازه‌شروع) بدون این‌که تناقض واقعی باشد — تا وقتی بازه‌هایشان هم‌پوشانی نداشته باشند. پس Constraint سطح DB (unique index ساده روی `IsPrimary`) کافی نیست؛ باید overlap-check در زمان write انجام شود (همان الگوی «حفاظت تراکنشی در برابر Race Condition» که در سند اصلی برای SetPrimaryPosition قفل شده، باید overlap زمانی را هم چک کند، نه فقط Status).

### 2.6 Overlap Validation

```
Rule: برای یک (Personnel, Position) واحد، دو ردیف با EffectiveFrom/EffectiveTo هم‌پوشان مجاز نیست.

Check at write-time:
    NOT EXISTS (
        SELECT 1 FROM PersonnelPosition existing
        WHERE existing.PersonnelId = new.PersonnelId
          AND existing.PositionId = new.PositionId
          AND existing.Id != new.Id
          AND existing.EffectiveFrom < COALESCE(new.EffectiveTo, MAX_DATE)
          AND COALESCE(existing.EffectiveTo, MAX_DATE) > new.EffectiveFrom
    )
```

### 2.7 Immutability بعد از عبور از زمان (سازگار با اصل «Position change lifecycle preserves historical records»)

```
اگر now > pp.EffectiveTo  (بازه به‌طور کامل در گذشته sealed شده):
    ویرایش EffectiveFrom/EffectiveTo مستقیم مجاز نیست.
    هر اصلاح باید از طریق یک رکورد جدید اصلاحی (Origin=CORRECTION) با AuditLog کامل (Before/After) انجام شود.

اگر now <= pp.EffectiveTo یا EffectiveTo هنوز NULL است:
    ویرایش EffectiveFrom (فقط اگر now < EffectiveFrom فعلی، یعنی هنوز شروع نشده) و EffectiveTo آزاد است.
```

### 2.8 Acceptance Checklist — Delta (اضافه به بخش ۶۱ سند اصلی)

```
[ ] EffectiveFrom می‌تواند در آینده باشد؛ رکورد قبل از EffectiveFrom در GetCurrentHolder ظاهر نمی‌شود.
[ ] EffectiveTo می‌تواند در آینده برنامه‌ریزی شود؛ رکورد بعد از عبور EffectiveTo دیگر Current نیست ولی از دیتابیس حذف نمی‌شود.
[ ] Status=INACTIVE صرف‌نظر از Effective window همیشه رکورد را از Current حذف می‌کند.
[ ] دو ردیف با بازهٔ زمانی هم‌پوشان برای یک (Personnel,Position) واحد رد می‌شود.
[ ] دو Primary هم‌زمان مؤثر (overlap در EffectiveFrom/EffectiveTo) برای یک Personnel در یک Company رد می‌شود؛ Primaryهای غیرهم‌پوشان مجازند.
[ ] بعد از عبور EffectiveTo، رکورد immutable می‌شود؛ اصلاح فقط با رکورد CORRECTION جدید و AuditLog امکان‌پذیر است.
[ ] هیچ Status محاسبه‌شده‌ای (SCHEDULED/ENDED) در DB ذخیره نمی‌شود؛ همه از EffectiveFrom/EffectiveTo/Status در لحظهٔ خواندن derive می‌شوند.
[ ] GetCurrentHolder(Position) برای کارتابل از IsCurrentlyEffective استفاده می‌کند، نه صرفاً Status=ACTIVE.
[ ] این تغییر روی Authorization اثر مستقیم ندارد (Position != Role همچنان برقرار است)؛ EffectiveFrom/EffectiveTo فقط روی Organization/Workflow اثر دارد.
```

### 2.9 سؤال باز برای تأیید

وقتی چند نفر هم‌زمان `IsCurrentlyEffective=true` روی یک Position باشند، ارجاع کارتابل به «همه» برود یا فقط «Primary»؟ (پیشنهاد داده شد: همه با Claim، به دلیل ریسک gap در لحظهٔ جابه‌جایی Primary — نیاز به تأیید صریح تو دارد.)
