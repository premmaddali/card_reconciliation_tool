# Reusable Product UI/UX Design System

## Purpose

This document describes a reusable visual and interaction system for professional, data-heavy web applications. It is intentionally independent of a particular product domain, data model, workflow, or permission scheme. It can be used as a design brief for a new application or as implementation guidance for an agent adapting an existing application.

The desired character is **calm, precise, trustworthy, and modern**: a high-quality operational tool rather than a generic admin template. The interface should feel coherent in both light and dark themes, remain highly legible with dense data, and use colour to communicate meaning rather than decoration alone.

This is a **themeable system**. The product chooses one active accent family—Blue–Grey, Red–Grey, or Orange–Grey—while retaining the same spacing, component structure, and semantic states. In the rest of this document, **accent** means the active theme colour, not a fixed brand colour.

## Design principles

1. **One visual language across every screen.** Navigation, cards, tables, forms, dialogs, dashboards, and administrative screens use the same surfaces, radius, icon treatment, spacing, and interaction states.
2. **Colour carries semantic meaning.** Green indicates primary action or success, red indicates a negative or exceptional condition, amber indicates attention, blue indicates supporting evidence or information, and violet is reserved for neutral confidence/analysis context.
3. **Icons explain, not decorate.** Every meaningful card and action row has a recognisable icon. The icon is positioned to the left of its related text or value; do not place it on the opposite edge without a strong reason.
4. **Dark mode is a first-class theme.** It is not a colour inversion. Inputs, table cells, borders, muted text, pills, and panels must all receive deliberate dark-theme values.
5. **Dense data should remain scannable.** Tables offer strong header hierarchy, readable cells, restrained row hover states, filters near the data, tabular numerals, and stable action placement.
6. **Use space intelligently.** Group a card’s icon, label, primary value, and supporting text into a compact visual unit. Avoid oversized empty card areas or detached icon blocks.

## Visual foundations

### Typography

Use a clean, modern sans-serif face with strong rendering at small sizes. Use tabular numerals for amounts, dates/times, percentages, references, and other values that users compare vertically.

| Use | Typical treatment |
| --- | --- |
| Page title | 28–32px, bold, tight tracking |
| Section/card title | 14–16px, semibold |
| Metric value | 28–32px, semibold/bold, tabular numerals |
| Body/table text | 14px, regular/medium |
| Labels/table headers | 12px, semibold, uppercase only when needed |
| Helper text | 12–14px, muted |

Avoid using low-contrast grey for entered values or important table data. In dark mode, primary content should be near-white, not dark grey.

### Spacing, corners, and elevation

- Use a 4px spacing rhythm; most component gaps should be 8, 12, 16, 20, or 24px.
- Use 8–12px radii for controls and compact panels; use 16px (`rounded-2xl`) for primary cards and analytic panels.
- Light theme may use a restrained soft shadow on elevated cards. Dark theme should use borders and tonal contrast rather than heavy shadows.
- Keep page content comfortably inset from the shell, typically 20–28px on desktop and 16px on mobile.

### Theme architecture: Blue–Grey, Red–Grey, Orange–Grey

Each theme pairs one saturated accent family with the same slate-grey neutral system. Neutrals control backgrounds, text, tables, borders, and most surfaces; the accent controls emphasis, active navigation, focus, primary calls to action, hero gradients, and selected states.

| Theme | Accent ramp | Grey foundation | Personality |
| --- | --- | --- | --- |
| Blue–Grey | blue 50 `#eff6ff` → blue 600 `#2563eb` → blue 900 `#1e3a8a` | slate 50 `#f8fafc` → slate 950 `#020617` | confident, precise, analytical |
| Red–Grey | red 50 `#fef2f2` → red 600 `#dc2626` → red 900 `#7f1d1d` | slate 50 `#f8fafc` → slate 950 `#020617` | decisive, high-attention, energetic |
| Orange–Grey | orange 50 `#fff7ed` → orange 600 `#ea580c` → orange 900 `#7c2d12` | slate 50 `#f8fafc` → slate 950 `#020617` | warm, operational, approachable |

Define each active theme through the same token contract:

| Token | Use |
| --- | --- |
| `accent-50/100` | selected/soft background, subtle information panels |
| `accent-200` | borders, low-emphasis icon tiles |
| `accent-400` | focus rings, secondary icon treatment |
| `accent-500` | links, active navigation, standard emphasis |
| `accent-600` | primary buttons and strong interactive elements |
| `accent-700/800/900` | hover states, hero depth, dark text on pale accent surfaces |
| `grey-50…950` | all structural surfaces, text, tables, borders, and dark mode |

Semantic colours remain independent of the active theme:

| Token | Intended use |
| --- | --- |
| Success | emerald/green: accepted, completed, healthy |
| Attention | amber: warning, pending review |
| Negative | rose/red: mismatch, error, loss, critical exception |
| Evidence | blue: evidence, references, supporting information |
| Analysis | violet: confidence, analytical context, neutral comparison |

The Red–Grey theme must not make ordinary error or exception states ambiguous: use rose/critical red values with a luminance or hue distinct from the chosen red accent, and always pair status colour with text and/or an icon.

## App shell and navigation

### Shell

Use a persistent left navigation and a compact top bar on desktop.

- The sidebar has a brand block at the top, grouped navigation sections, clear active state, and a user/profile area at the bottom.
- The top bar contains the current section title/breadcrumb context, search or utility controls, notifications, and theme switching.
- Use a thin border between shell regions rather than excessive shadows.
- In dark mode, use deep navy/slate surfaces instead of pure black for persistent chrome; reserve a near-black page background for contrast behind content panels.

### Navigation states

- Active item: soft accent surface, accent-coloured icon and text.
- Inactive item: muted neutral text and outline icon.
- Hover: a subtle neutral/primary wash, never a saturated block.
- Navigation icons should use the same stroke weight and approximate 18–20px size.

### Top-right utility icon cluster

The top-right area is a compact global-utility cluster. It should be visually quiet at rest, consistent across pages, and easy to operate without reading long labels.

Recommended order:

```text
[theme switcher] [supporting utility] [lock/security] | [avatar] [user name / menu]
```

- Use 36–40px square icon buttons with a 20px outline icon, 8px radius, and a clear accessible label/tooltip.
- At rest, icons use muted grey (`grey-500` light / `grey-300` dark), not the active accent.
- Hover uses a soft `accent-50` background in light mode and an accent-tinted translucent background in dark mode; the icon changes to `accent-600` or a light accent tone.
- Active/toggled controls use a filled or softly filled accent surface with high-contrast icon colour. Do not rely on colour alone; update the tooltip and `aria-pressed` state.
- Use a 1px vertical divider before the profile menu to separate global utilities from identity/account controls.
- Notification counters are small, solid, high-contrast circular badges at the icon’s top-right. They may use semantic red for unread/urgent content even in a Red–Grey accent theme.
- The avatar is a 32–36px circular chip. It may use the active accent background with white initials; place the user name beside it on desktop and hide the name on compact widths.
- Keep utilities product-generic: theme, notifications, help, lock/security, sign-out, profile. Do not add page-specific actions to this cluster.

### Login and authentication pages

Authentication screens belong to the design system, not a separate visual product. They use the selected theme but a simplified layout with fewer competing controls.

#### Desktop layout

Use a two-panel layout at large breakpoints:

```text
+--------------------------------+--------------------------------+
| Brand / value panel            | Sign-in form                   |
|                                |                                |
| [brand mark] Product name      | Sign in                        |
| Supporting descriptor          | Supporting instruction         |
|                                |                                |
| Headline                       | Username/input                 |
| Short value proposition        | Password/input                 |
| Three concise benefit points   | Provider/select (if needed)    |
|                                | Primary submit                 |
| Legal/internal-use note        | Account-help text              |
+--------------------------------+--------------------------------+
```

- Left brand panel: deep accent background with two or three oversized, low-opacity abstract circles. Use no photography unless a product has a deliberate image strategy.
- Use `accent-900` as the base; use `accent-700/800` translucent circles to create depth. The panel’s text is white with pale accent supporting text.
- Brand mark: compact 40px rounded square, white or pale neutral fill, accent-coloured initials/mark.
- Value proposition: 28–32px semibold headline, concise supporting copy, and up to three benefit statements with pale accent check icons.
- Right form panel: neutral white (`grey-0`) in light mode and a deep grey/slate surface in dark mode. Keep the form to approximately 360–400px wide.
- The form uses the shared input system. Filled text must remain near-black in light mode and near-white in dark mode. Placeholder text is visibly lighter than entered text.
- Use an accent primary submit button with a left-aligned sign-in/arrow icon. Error messages use a restrained semantic red bordered panel with an alert icon.
- Keep secondary help text compact and centered below the form.

#### Compact/mobile layout

- Hide the decorative brand panel below the large breakpoint.
- Retain a small logo/brand block above the sign-in form.
- Use full-width form controls and a full-width primary action.
- Preserve generous page padding (at least 24px), keyboard focus visibility, and accessible labels.

#### Authentication state rules

- Login, reset-password, change-password, and account-recovery pages use the same structure, card radius, inputs, buttons, alerts, and top-right utility rules where applicable.
- Never place dark text on a dark input or leave the browser’s default autofill colour uncontrolled.
- Use text links sparingly and style them with the active accent; do not add multiple competing secondary calls to action.

## Page hierarchy

### Hero/header

Use a reusable hero for principal pages:

- Rounded 16px container.
- Accent gradient from the theme’s deep accent through its mid accent to a lighter accent.
- Breadcrumbs at the top in a subdued white/green tone.
- Main icon, eyebrow, title, and subtitle grouped on the left.
- Optional concise KPI or status block aligned on the right.
- White icon tile with translucent background; do not leave the hero without an icon when equivalent pages use one.

For utility pages, use a smaller page header with eyebrow, title, subtitle, and breadcrumb rather than a hero. Keep the structure identical across pages.

### Section layout

- Place key KPIs immediately below a hero or page header.
- Group related controls with their table or panel, not in a detached unrelated toolbar.
- Use responsive grids: 5 compact metric cards at wide desktop, 2 cards on tablet, 1 card on narrow screens.
- Preserve hierarchy when wrapping: titles and actions should remain visually tied to their panel.

## Cards

### Shared card structure

All cards should use one core treatment:

- Neutral surface with a subtle border.
- 16px radius for primary cards.
- Compact padding (16–20px for metrics, 20–24px for larger content panels).
- A 3px top border communicates the card’s semantic category.
- In dark theme, use a translucent navy/slate surface with a quiet slate border.

Semantic top-border examples:

| Card category | Border | Icon tile |
| --- | --- | --- |
| Informational/analysis | active accent or violet | accent/violet treatment |
| Processed/success | emerald | emerald gradient |
| Warning/review | amber | amber gradient |
| Duplicate/review detail | amber | amber gradient |
| Exception/negative | rose | rose/red gradient |

### Metric cards

The metric-card pattern is mandatory for summary values.

```text
+-----------------------------------+
| [icon]  Label                     |
|         90,411                    |
|         Supporting context         |
+-----------------------------------+
```

- Put the icon tile at the upper-left beside the text, not isolated on the right.
- Icon tile: 40px-ish rounded square, white icon, semantic gradient fill.
- Label: small, medium-weight, muted.
- Value: large, tabular, semantic colour only when its meaning is success, warning, or error.
- Hint: small muted text below the value.
- Use a slight upward movement and soft shadow on hover in light theme only.

### Scheme/option cards

When representing a selection of schemes, products, or work areas:

- Use a semantic top border that matches the identity icon colour.
- Place the primary icon and item title together in a horizontal header.
- Put the item name next to—not below or far from—the icon.
- Action rows are full-width, softly raised rows inside the card.
- Each action row has: small left icon tile, action label, and a small accent-coloured circular arrow affordance on the right.
- The arrow stays accent-coloured in normal state, not only on hover.
- Use varied identity colours (green, red, blue, amber) so neighbouring cards are easier to distinguish.

```text
+-------------------------------------+
| [identity icon]  Item name           |
|                  Supporting label     |
|                                     |
| [small icon]  Action name       (→) |
| [small icon]  Action name       (→) |
+-------------------------------------+
```

## Buttons and actions

### Primary actions

- Solid `accent-600` background with white, semibold text.
- Rounded 8px corners, 12–14px horizontal padding, 8–10px vertical padding.
- Use an icon to the left for action buttons when it improves recognition.
- Hover changes to a darker accent tone, not an unrelated hue.
- Disabled controls lower opacity and show a non-interactive cursor.

### Secondary actions

- Neutral/slate fill or a clearly bordered neutral button.
- Keep hierarchy lower than the primary action.

### Evidence/information actions

- Use a solid blue button, not a green outline button.
- This distinguishes supporting evidence from state-changing actions.

### Destructive or exceptional actions

- Use a red/rose treatment only for irreversible or negative actions.
- Amber is for reversible warning/review actions.

## Forms and inputs

Forms must feel intentional in both themes and cannot inherit unreadable browser defaults.

### Control treatment

- Inputs, selects, textareas, and date pickers have 8px radius, 1px neutral border, 12px horizontal padding, and 14px text.
- Light: white or very light grey fill; dark text.
- Dark: deep slate fill (not transparent), slate border, near-white entered text, and muted-light placeholder text.
- Focus: accent border plus a soft accent focus ring.
- Labels sit directly above controls, using a 12px semibold muted treatment.
- Required fields use a simple `*` in the label; errors appear below the field in red.
- Do not rely on placeholder text as a label.

### File inputs

Use a custom visible `Choose file` button and a separate filename display.

- Button uses the primary action treatment.
- Empty filename state is muted.
- Selected filename uses success green and truncates cleanly.
- Pair labels with the real source type in generic terms (for example, “CMS Transaction File” and “Bank Ledger File”), avoiding organisation-specific language.

### Form grouping

- Use a lightly tinted or bordered fieldset for related run/configuration parameters.
- Keep explanatory helper text inside the fieldset below its setting.
- In side drawers or modals, show the selected record context in a small neutral summary panel before editable fields.

## Status pills

Status must be a solid, readable rounded pill—not transparent text with a separate dot.

- Pill shape: fully rounded, minimum width around 128px for table alignment, centered text.
- Padding: approximately 12px horizontal and 4px vertical.
- Use the normal table-cell text colour for label text where contrast allows; the pill background provides the state cue.
- Do not add a leading dot when using a filled pill.

Recommended status fills:

| State | Light fill | Dark fill |
| --- | --- | --- |
| Matched | emerald 200 | emerald 800 |
| Matched with tolerance | lighter emerald 100 | distinct emerald 700 |
| Completed | violet 200 | violet 700/800 |
| Attention/review | amber 200 | amber 800 |
| Error/exception | red 200 | red 800 |
| Informational | primary/blue 200 | primary/blue 800 |

The tolerance-success shade must be visibly distinct from an exact match while still reading as positive.

## Tables and dense data

### Native tables

- Use a dedicated bordered container with a white/light surface in light theme and deep slate surface in dark theme.
- Header: light slate in light mode; dark slate in dark mode; 12px semibold uppercase labels when appropriate.
- Body cells: 14px readable neutral text; in dark mode use light slate/near-white (`#e2e8f0` range), never black text.
- Use a subtle divider between rows.
- Add a low-saturation accent hover wash. Zebra rows in dark mode can use a very subtle tonal difference.
- Right-align amounts and numeric metrics; apply tabular numerals.
- Use `white-space: nowrap` only where it assists scanning; give long text a max width with truncation and a title/expanded detail mechanism.
- Keep row actions in a stable rightmost column. Use a sticky action column for very wide horizontal tables.

### Filters and pagination

- Put high-level filters in the table toolbar.
- Put column-specific filters immediately under their corresponding headers.
- Filters should share the same themed input/select system.
- Provide one explicit **Clear** action.
- Put row count and pagination in the footer; use compact Previous/Next controls.

### Third-party data grids

Apply the same header, cell, border, zebra, and hover tokens to any component-library grid. Do not allow a third-party grid to retain its default white-only styling in dark mode.

## Panels, drawers, and modal dialogs

- Use a dark translucent full-screen scrim (`black` at roughly 40% opacity).
- Drawers enter from the right and use a white/slate surface, header border, scrollable body, and fixed footer actions.
- Modal panels use a 12–16px radius and a clear title bar with a close icon.
- The primary confirmation action belongs at the footer’s right edge; Cancel is secondary.
- Long forms use a scrollable body while keeping the action footer visible.

## Workflow/information panels

Use a distinct reusable information-panel treatment for verification, workflow, and controlled status information.

- Light: subtle `accent-50` to white gradient, `accent-200` border, pale-accent icon tile, deep-accent text.
- Dark: dark accent-tinted gradient, translucent accent border, pale-accent icon tile and near-white primary text.
- Never retain a bright white information panel in dark mode.
- Layout: icon, eyebrow, main status, optional helper/role text, and a right-aligned primary action.

## Analytics and visualisations

Use charts sparingly to clarify distribution and ranking rather than to decorate dashboards.

### Chart card

- Use the same 16px card surface and border as other content cards.
- Title at top left; no heavy chart chrome.
- Empty state says “No data is available yet.” in muted text.

### Donut chart

- Use a compact native CSS/SVG donut rather than a visually heavy chart library where possible.
- Center label displays total and a small descriptor.
- Place a compact legend beside or below the donut with colour dot, label, and tabular value.
- Use semantic but differentiated segment colours: active accent, violet, red, teal, amber, blue.

### Horizontal bar chart

- Use labels and values above/alongside each bar.
- Bars sit on muted tracks with rounded ends.
- Sort descending and use a small number of items (typically 5–8).
- Align the bars with the same card, typography, and dark-theme tokens as the donut chart.

## Light and dark theme specification

| Element | Light theme | Dark theme |
| --- | --- | --- |
| App background | soft off-white/light slate | near-black/deep navy |
| Primary content surface | white | dark slate/navy |
| Card border | pale slate | translucent slate |
| Main text | slate 900 | near-white |
| Muted text | slate 500/600 | slate 300/400 |
| Input fill | white or slate 50 | slate 700/800 |
| Table header | slate 50 | slate 800 |
| Row hover | pale accent wash | low-opacity accent wash |
| Focus ring | accent with low opacity | lighter accent with low opacity |

Every component introduced into the product must be reviewed in both themes before acceptance. The minimum review checklist is: text contrast, placeholder visibility, border visibility, hover state, focus state, selected/active state, disabled state, and semantic pill/icon visibility.

## Responsive behavior

- Desktop: persistent side navigation; multi-column card grids; toolbar actions can sit inline.
- Tablet: reduce card columns, allow toolbars to wrap, retain readable control widths.
- Mobile: collapse side navigation to a drawer; stack page-header actions; use single-column cards; preserve minimum 44px touch targets.
- Wide tables remain horizontally scrollable inside their panel; do not compress all columns to unreadable widths.
- Sticky right action columns may be retained on touch devices only if they do not obscure essential data.

## Accessibility and interaction quality

- Maintain visible keyboard focus on all interactive controls.
- Use accessible labels for icon-only buttons and compact filters.
- Meet colour contrast requirements without relying solely on colour; pair status colour with descriptive text and, where useful, icons.
- Keep animation restrained: 160–200ms hover/focus transitions, minimal movement, no continuous decorative animation.
- Use consistent icons for the same concept throughout the product.

## Implementation checklist for another project

1. Select Blue–Grey, Red–Grey, or Orange–Grey, then define its accent-token contract and the common grey foundation before styling pages.
2. Build theme-aware base styles for inputs, selects, textareas, tables, buttons, and cards.
3. Implement reusable `PageHeader`, `StatCard`, `StatusBadge`, chart-card, and drawer/modal components.
4. Apply icon-left card composition everywhere: dashboard KPIs, summary cards, option cards, and action rows.
5. Apply the same table treatment to native tables and third-party grids.
6. Add the dark-theme review checklist to the implementation process.
7. Use the dashboard to demonstrate the full system: hero/header, semantic KPIs, charts, rich cards, and dense tables.
8. Keep all wording product-generic; do not encode a specific client, bank, vendor, or workflow name into shared UI components.
