# Plan: Group Images — Bundling & Linking

## Goal

Bundle a set of images (one per group) inside the app and link each image to its group
at runtime — with no DB changes and no manual mapping file to maintain.

---

## Where the Images Live

```
RadioV2/
└── Assets/
    └── Groups/
        ├── jazz.png
        ├── rock.png
        ├── classical.png
        ├── pop.png
        └── ...
```

All images go in `Assets/Groups/`. You drop files there; the app picks them up automatically.

---

## Naming Convention (the link between image and group)

The link is **purely by filename**. The rule:

> Take the group's `Name` from the DB, convert it to lowercase, replace spaces and special
> characters with underscores, strip anything else. That becomes the filename (without extension).

Examples:

| Group name in DB | Image filename |
|---|---|
| `Jazz` | `jazz.png` |
| `Classic Rock` | `classic_rock.png` |
| `Hip-Hop` | `hip_hop.png` |
| `R&B` | `r_b.png` |
| `Top 40` | `top_40.png` |

You rename your image files to follow this pattern before dropping them into `Assets/Groups/`.

---

## Supported Formats

Accept `.png`, `.jpg`, `.jpeg`, `.webp` — the helper tries each extension in that order.
Use `.png` where possible (best quality for logos/illustrations).

---

## How Images Are Compiled into the App

In `RadioV2.csproj`, add a wildcard `<Resource>` entry so every file you drop into
`Assets/Groups/` is automatically compiled into the assembly — no `.csproj` edit needed
each time you add a new image:

```xml
<ItemGroup>
  <Resource Include="Assets\Groups\**\*" />
</ItemGroup>
```

This embeds the images as WPF resources. They are referenced at runtime via **pack URIs**:

```
pack://application:,,,/Assets/Groups/jazz.png
```

No loose files, no deployment headache — images travel inside the `.exe`.

---

## The Helper: `Helpers/GroupImageHelper.cs`

A single static method that takes a group name and returns a usable `ImageSource`
(or `null` if no image exists for that group).

```csharp
public static class GroupImageHelper
{
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp" };

    public static ImageSource? GetImage(string groupName)
    {
        string key = Sanitize(groupName);

        foreach (string ext in Extensions)
        {
            string uri = $"pack://application:,,,/Assets/Groups/{key}{ext}";
            try
            {
                // ResourceManager checks whether the resource exists before decoding
                var info = Application.GetResourceStream(new Uri(uri));
                if (info is null) continue;
                info.Stream.Dispose();

                return new BitmapImage(new Uri(uri));
            }
            catch (IOException)
            {
                // resource not found — try next extension
            }
        }

        return null; // caller shows placeholder
    }

    private static string Sanitize(string name)
    {
        // lowercase, replace anything that isn't a letter or digit with underscore,
        // collapse multiple underscores, trim edges
        var sb = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }
        // Collapse consecutive underscores
        return System.Text.RegularExpressions.Regex
            .Replace(sb.ToString(), "_+", "_")
            .Trim('_');
    }
}
```

---

## How the Card Uses the Image

The `GroupCarouselCard` UserControl calls the helper in its code-behind when the
`DataContext` (a `Group`) is set:

```csharp
// GroupCarouselCard.xaml.cs
protected override void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
{
    if (DataContext is Group group)
    {
        GroupImage.Source = GroupImageHelper.GetImage(group.Name);
        PlaceholderIcon.Visibility = GroupImage.Source is null
            ? Visibility.Visible : Visibility.Collapsed;
        GroupImage.Visibility = GroupImage.Source is null
            ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

In XAML, the card has two overlapping elements inside the image area — only one is visible:

```xml
<!-- Image (shown when a file exists) -->
<Image x:Name="GroupImage"
       Width="160" Height="160"
       Stretch="UniformToFill"
       Visibility="Collapsed" />

<!-- Placeholder (shown when no image file exists) -->
<Border x:Name="PlaceholderIcon"
        Background="{ui:ThemeResource ControlFillColorSecondaryBrush}">
    <ui:SymbolIcon Symbol="MusicNote24"
                   FontSize="48"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" />
</Border>
```

---

## Fallback Behaviour

If no image file matches a group name, the card shows the placeholder tile automatically.
No crash, no broken image icon — the group card still works and looks clean.

This means you don't need to provide images for every group — only the ones that will
appear in carousel rows (i.e., groups assigned to a category).

---

## Files to Create / Modify

| File | Change |
|---|---|
| `Assets/Groups/` | New folder — you drop `.png`/`.jpg` files here |
| `RadioV2.csproj` | Add `<Resource Include="Assets\Groups\**\*" />` |
| `Helpers/GroupImageHelper.cs` | New helper — name sanitiser + pack URI resolver |
| `Controls/GroupCarouselCard.xaml/.cs` | Use helper on `DataContextChanged` to set image |

---

## Step-by-Step: Adding a New Group Image

1. Take the group's name exactly as it appears in the DB (e.g. `Classic Rock`).
2. Apply the sanitisation rule → `classic_rock`.
3. Save/rename your image file to `classic_rock.png`.
4. Drop it into `Assets/Groups/`.
5. Rebuild the app (`dotnet build`).
6. Done — the card for that group now shows the image.

No code changes, no config changes, no DB changes.
