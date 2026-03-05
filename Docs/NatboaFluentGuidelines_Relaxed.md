# Natboa Fluent Design Guidelines

> **Purpose:** A flexible guide to creating Windows desktop applications with a modern Microsoft Fluent Design look and feel. 
> These guidelines emphasize the use of native-looking Fluent UI components, materials like Mica, and clear typography, without strictly enforcing specific architectural patterns or rigid pixel values.

---

## 1. Technology Stack

To achieve the Fluent UI look in WPF, we recommend the following stack:
- **UI Framework:** [WPF-UI](https://github.com/lepoco/wpfui) (`Wpf.Ui`) - provides Fluent 2 controls and styles.
- **MVVM Elements:** `CommunityToolkit.Mvvm` is great for clean architecture but not strictly required by the UI.

---

## 2. Shell & Window Design

To get the authentic Windows 11 feel, your main window should use `ui:FluentWindow` instead of a standard `Window`. This enables custom title bars and modern backdrops.

**Key Window Features:**
- **Mica Backdrop:** Set `WindowBackdropType="Mica"` on your main window to integrate smoothly with the OS theme.
- **Integrated Title Bar:** Use `ExtendsContentIntoTitleBar="True"` and the `ui:TitleBar` control to place navigation or branding within the title area.
- **Navigation:** Use `ui:NavigationView` for modern left-pane or top-pane routing between different views.

---

## 3. Theming & Colors

Leverage WPF-UI's built-in theme manager to handle Light/Dark mode switching easily. 

- **Initialization:** Call `ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);` at application startup (or use user preferences).
- **Semantic Brushes:** Whenever possible, use WPF-UI's dynamic theme resources instead of hardcoding colors so elements update automatically during theme changes (e.g., `{ui:ThemeResource TextFillColorPrimaryBrush}`).
- **Brand Colors:** If you need specific brand colors (like Natboa Blue), define them in a central `ResourceDictionary` and reference them cleanly.

---

## 4. UI Components & Layouts

Consistent use of spacing, typography, and grouped content is key to the Fluent Design aesthetic.

### Grouping with Cards
Group related content, forms, or settings together using `ui:Card`. Cards automatically provide the correct Fluent Design rounded borders, background fills, and padding.

### Action Areas & Toolbars
For toolbars or action panels, use a standard `Border` with a rounded corner (e.g., `CornerRadius="8"`) and `Background="{ui:ThemeResource ControlFillColorDefaultBrush}"`. Place your buttons and search boxes inside to create a distinct command area.

### Buttons and Interactions
Use `ui:Button` and map its `Appearance` property based on the action's importance:
- `Primary`: Main call to action (e.g., Save, Submit).
- `Secondary` / default: Standard actions.
- `Danger`: Destructive actions (e.g., Delete).
- `Transparent`: Best for icon-only buttons in toolbars or lists to avoid visual clutter.

---

## 5. Typography & Spacing

Fluent Design uses a clear visual hierarchy. Rely on font size and boldness to distinguish importance.

- **Page Titles:** Large and SemiBold (e.g., FontSize 24 or 28).
- **Section Headers:** Medium and SemiBold (e.g., FontSize 16).
- **Subtitles/Captions:** Regular font weight but reduced emphasis (e.g., `Opacity="0.7"` or a secondary text brush).

**Spacing:**
- Keep layouts breathable. Use consistent, even margins (multiples of 4 or 8) between elements. 
- A padding/margin of about 16 to 24 pixels around the main page content keeps the app feeling spacious.
- Prefer rounded corners throughout the UI: small radiuses (4px) for controls like inputs, and larger radiuses (8px) for containers like side-panels and cards.

---

## 6. Data Presentation

When displaying tabular data, favor clean rows over harsh grid lines. You can customize the `DataGrid` to use alternating row backgrounds (`{ui:ThemeResource ControlFillColorSecondaryBrush}`) and ensure selected rows are clearly highlighted with the system accent color without feeling overly boxed in.