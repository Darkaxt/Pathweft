# Auxiliary-window design contract

2026-11-05. S14 / WINDOW-01..03 / UPDATE-02. Preserve Pathweft's existing
Settings direction and the user's reference screenshots. This inventory and
contract are implementation targets, not claims of completed visual verification.

## Shared language

- Use existing Native light/dark and Original palette/accent resources; do not
  change Windows settings or recolor document/video/model pixels.
- Segoe UI Variable Text/Segoe UI,12-DIP body;14-DIP semibold section headings;
  muted12/11-DIP supporting text. Keep monospaced shortcut/code text for its role.
-16-DIP outer content margin;16x12-DIP panel padding;12-DIP content-panel corners;
  quiet1-DIP panel border. Fields/actions use6-DIP corners. Navigation has an
 180-DIP column,16-DIP gap,12x10-DIP item padding and the same accent-soft selected
  state, semibold text and visible keyboard outline.
- Shared auxiliary action buttons use12x6-DIP padding and30-DIP minimum height.
  Their templates honor Padding; the inherited Tab template's fixed9x3 padding
  is not a substitute. Main file-manager command icons retain their compact size.
- At narrow widths, wrap command groups and prose, scroll content rather than
  covering actions, and keep minimum sizes bounded by the work area. Qualify
  actual100/150% content scaling and representative viewport sizes, not only
  static resource values. WPF owns physical DPI; text-scale changes are separate.
- Keep native captions/ownership/taskbar/reuse/close semantics. Transient search
  and renderer canvases are intentional variants of this language, not forced
  copies of the Settings form. Native OS dialogs/Shell menus are outside ownership.

## Complete owned-surface inventory

| Surface | Applied contract / preserved behavior |
| --- | --- |
| Settings | Shared navigation item/card/heading resources and body spacing; existing categories, card scrolling, controls and live text scaling retained. |
| Help | Same navigation selection/card/field/spacing; reading-section bold/highlight and search retained; shortcut/explanation columns reflow within the content region. |
| What's new | Same navigation/card/heading/actions; release/month navigation retained; banner and notes share the scrollable content panel so shorter windows retain reading space. |
| Manual update checker / offer | One themed surface immediately shows Checking, then installed-version/current, available-version/notes/action or failure/explanation/Retry. Sidebar and Settings join the same in-flight HTTP task; closing the surface does not launch another request or resurrect it on response. |
| Download/deployment | Existing measured bytes/hash/cancel and transfer/editor protection retained in the same updater surface. Normal shutdown cancels/observes an in-flight check. Installer's native deployment popup retains the passed Pathweft palette and actual native progress; do not replace its deployment owner. |
| Search widget | Same typography, margin and field radius/padding; intentional captionless search surface and results, shortcuts, containing-folder/Properties, focus/hide/reuse/DPI constraints retained. |
| Preview sidebar / PiP / detached / quick view | Existing Pathweft preview container and renderer/session retained; shared window typography/frame for floating hosts, renderer-owned content sizing/DPI. Closing detached still restores the same preview. |
| Transfers | Shared margins/window typography; native status/card grouping and per-job colors/speed hierarchy retained. Close hides active transfers rather than cancelling. Successful completion automatically dismisses its card and hides an empty window; failed/cancelled/partial outcomes remain for review. Undo and arrival refresh precede dismissal. |
| New file | Shared field/actions/outer spacing/window scaling; existing file templates and create/cancel behavior. |
| Rename/new-folder prompt | Shared field/actions/spacing/scaling; existing default selection, Enter/Escape and actual operation owner. |
| Question/error/conflict choices | Shared question/detail/action hierarchy and spacing/scaling; existing outcomes/defaults preserved. No duplicate destructive action or timeout-driven dismissal. |
| Multiple rename | Shared preview field/actions/spacing/scaling; existing patterns/live preview/commit owner preserved. |
| Duplicate finder | Shared panel/actions/spacing/scaling and wrapping command groups; existing matching, progress, selections and mutation owners preserved. |
| Recycle Bin | Shared panel/actions/spacing/scaling and minimum sizes; existing enumeration, selection/restore/delete/confirmation owners retained. |
| Editor | Shared native auxiliary frame typography; Monaco's existing full-canvas UI, editor theme, file/dirty-state lifecycle and rendering scale retained. No added form/card padding around an editor canvas. |
| Taskbar search entry | Intentional compact taskbar host using existing field/theme/focus behavior; no navigation/card or changed placement inside the taskbar. Its full result popup uses the search-widget contract above. |

## Required acceptance evidence

Inspect actual rendered windows under Native light/dark and Original, narrow and
normal bounds,100/150% content scale. Verify navigation/card/field/action geometry,
clipping/scroll reachability, reading selection, keyboard/accessibility and actual
owner/reuse/close behavior. Use focused screenshots plus structured bounds/state.
Recheck the existing download/hash/cancel/deployment handoff and preview/editor/
transfer lifecycle where affected. HTTP checks must cover held-response immediate
feedback, concurrent callers, up-to-date, available, failure/explicit retry,
unchanged automatic silence, dismissal and actual shutdown cancellation.

All source/integration acceptance must pass before S14 closes. S15 owns the
combined actual package, normal release, independent retrieval and installed
verification; do not publish individual window changes.
