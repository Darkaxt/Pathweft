# Maintenance tweaks by goal with risk colours (S47a)

Authority: specification 1.17, MAINT-25..31. Plan: IMPLEMENTATION-PLAN S47 row.

Implementation (3ee640f): every tweak rule carries a goal and a risk level; the
catalog refuses a tweak rule without them and a High rule recommended as merely
Optional. 81 new rules (selected from the S47 donor inventory by a reviewed rule
pass; targets located in the cited donor lines) are typed in
MaintenanceS47Tweaks.cs; MaintenanceGuidance.json holds only their descriptions.
Seven MIT donors are vendored with licence, cited files and SHA-256 under
vendor/<donor>/source-manifest.json and checked by Verify-ReleaseSources.
RemoveWindowsAI is pinned to the KalebGreek mirror because the original repository
is gone; identity with it is unproven. Rejected candidates and their reasons are in
the task scratch rule report; MAINT-30 exclusions held (no Defender sample-consent,
no service start-type change, no key deletion, no DISM or powercfg).

Panel: goal filter (Windows tweaks only), Recommendation cell with a coloured
▲ Caution / ◆ High marker (RiskCaution/RiskHigh brushes reuse the warning/error
hues; WindowText in high contrast), risk text in the assessment, a Yes/No warning
before applying Caution or High (No default), and Restore Windows default when a
policy-only rule is set outside Pathweft. Applying Caution/High rules is never a
pending suggestion; undoing a change advised against still is.

Host scenario (gated debug build s47a-debug-1): Maintenance scenario 228 of 228
passed, including the new catalog, pending, marker, assessment, policy-only and
task-path checks; dark/light 1.0/1.5 and narrow captures inspected (the ▲ Caution
marker renders in the warning hue beside Keep enabled; goal filter beside categories).

Guest qualification: a disposable Windows Sandbox guest (build 26100, no network,
4 GiB) ran --verify-maintenance-guidance: 886 checks, none failed, result 0. Each
applicable tweak: native worker Apply, exact registry kinds/values, exact saved
originals, named scheduled tasks disabled with their states saved, Restore to the
exact prior registry and task state. Policy reset round trip on
s47.copilot-policy: set outside Pathweft recognised with an unknown neighbour shown
read-only, reset removed only the rule's values, Restore returned them. Not
applicable on 26100: s47.news-interests and s47.meet-now (Windows 10), legacy
Start search; Fax not installed. 79 new rules carry qualified 2026-11-13 / 26100.

The first guest attempt timed out after 30 minutes with no receipt; a bounded
diagnostic run showed the framework-dependent debug build waiting on the .NET
Desktop Runtime install prompt in the clean guest. The passing run staged the
host's .NET 10.0.14 runtime into the guest and set DOTNET_ROOT; Pathweft itself was
unchanged. No host setting, task or policy was changed.

This verifies configuration and Restore, not a measured benefit or every
end-to-end Windows experience. Edition limits (consumer features on Enterprise and
Education only, partial HideRecommendedSection) and undocumented values (app
permission ConsentStore Deny, search settings, HwSchMode, SmartActiveHoursState)
are stated in each rule's effect text.
