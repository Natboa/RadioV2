# Reordering Discover Page Categories

## Overview

The Discover page shows category rows (carousels) in a specific order. The order is stored in the database via a `DisplayOrder` integer on each category row. Because the DB is pre-seeded and the seeder only runs once, **you must update two files** to change the order.

---

## Step 1 — Update `CategorySeeder.cs`

**File:** `Services/CategorySeeder.cs`

The `SeedData` array controls the order for **fresh databases** (first-time installs or after a wipe). Reorder the tuples so they appear in the sequence you want:

```csharp
private static readonly (string CategoryName, string[] GroupKeys)[] SeedData =
[
    ("Rock & Metal",        [ ... ]),   // ← will get DisplayOrder = 0
    ("Electronic & Dance",  [ ... ]),   // ← DisplayOrder = 1
    // ... etc
    ("Europe",              [ ... ]),   // ← last = highest DisplayOrder
];
```

The `DisplayOrder` value is assigned automatically as `0, 1, 2 ...` based on position in this array — no manual numbering needed.

---

## Step 2 — Update `DatabaseInitService.cs`

**File:** `Services/DatabaseInitService.cs`

Step 7 at the bottom of `InitialiseAsync` runs `UPDATE` SQL on every startup to fix the `DisplayOrder` on **existing databases**. Update the `orderMap` array to match your new order exactly:

```csharp
var orderMap = new (string Name, int Order)[]
{
    ("Rock & Metal",               0),
    ("Electronic & Dance",         1),
    ("Pop, Charts & Decades",      2),
    ("Urban & Latin",              3),
    ("Jazz, Chill & Instrumental", 4),
    ("News, Talk & Sports",        5),
    ("Specialty & Mood",           6),
    ("Global & Cultural",          7),
    ("Europe",                     8),
    ("Americas",                   9),
    ("Asia, Pacific & Africa",    10),
};
```

The numbers here must match the position order in `SeedData`. The category **names must match exactly** (case-sensitive) — these are matched against the `Name` column in the Categories table.

---

## Adding a New Category

1. Add a new tuple to `SeedData` in `CategorySeeder.cs` at the desired position.
2. Add the corresponding entry to `orderMap` in `DatabaseInitService.cs`.
3. List the group keys to assign to it (use `GroupImageHelper.Sanitize` logic — lowercase, underscores for spaces).

## Removing a Category

1. Remove the tuple from `SeedData`.
2. Remove the entry from `orderMap`.
3. Groups that were assigned to it will have `CategoryId = NULL` and won't appear on the Discover page (they remain in the Browse page).

---

## Why Two Places?

| File | Purpose |
|------|---------|
| `CategorySeeder.cs` — `SeedData` | Sets order for **new/fresh databases** only (runs once when Categories table is empty) |
| `DatabaseInitService.cs` — `orderMap` | Fixes order on **existing databases** every startup via SQL UPDATE |

Both must be kept in sync, otherwise fresh installs and existing installs will show a different order.
