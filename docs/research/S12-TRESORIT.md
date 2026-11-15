# S12 — Tresorit capability investigation (TRES-01..04)

2026-11-15. Investigation only: no cloud content, Tresorit setting or client file was changed, no client
command or shell verb was invoked, and no credential or key material was read. Evidence is the installed
client on this PC plus Tresorit's public documentation.

## Installed client (evidence)

| Item | Observed |
| --- | --- |
| Client | `%LOCALAPPDATA%\Tresorit\v0.8\Tresorit.exe` 3.5.5737.4880, per-user install, Authenticode valid (Tresorit Kft., Budapest). .NET/WPF with WebView2. |
| Drive | `T:` "Tresorit Drive", remote `\\tresoritdrive_<id>.drive.tresorit.com\Tresorit Drive(...)`, Windows drive type Network. Implemented on **CBFS Connect** (`cbfsconnect22.dll`, `cbfsShellHelper22.dll`, core log `cbfs_drive_driver.cpp`). |
| Volume flags | Reports file system `exFAT`, max name 260. Has: case-preserved names, Unicode, reparse points, object IDs, USN journal. **Lacks**: named streams, ACLs, hard links, extended attributes, open-by-file-ID, sparse files, quotas. Free/total space is reported (here 1.08 TB total, 303 GB free: the account quota, not a disk). |
| Top level | Each tresor (main folder) is a directory with `ReadOnly, Directory`; the ACL is a synthetic `Everyone: FullControl`. Children also show `ReadOnly` on folders. Neither attributes nor ACL express the user's tresor role. |
| Shell | Context-menu handler `tresorit` for `*`, `Directory`, `Directory\Background`, `Drive`, `AllFilesystemObjects` (three `TresoritExt` CLSIDs, `TresoritExt64_5.dll`). Its verbs on T: items: **Sharing and members**, **Copy Tresorit Path**. No "show deleted", restore or version-history verb was offered on the sampled main folder and child folder. |
| URI | `tresorit:` is registered (`Tresorit.exe "%1"`). The binary only contains `tresorit://` and `tresorit://redirect#`, consistent with a sign-in redirect, not a file-operation command. |
| Other | `tresorit-core-interface.exe` takes `--container`, `--user-key-id`, fingerprints and server options: an internal crypto/core tool, not a supported contract; not examined further (credential-adjacent). |
| Client log | The CBFS driver logs `Forbidden` every 15 minutes (something on the PC periodically attempts an operation the drive refuses); slow-network and missing-file warnings also appear. |

## Public surfaces (TRES-02)

Researched by a delegated web review; support.tresorit.com refused direct fetches (HTTP 403), so rows
marked *snippet* rest on search summaries of official pages, not full reads.

| Surface | Content operations | Availability / auth | Fit for Pathweft |
| --- | --- | --- | --- |
| Tresorit API (S3-compatible gateway, github.com/tresorit/s3-api, read in full) | List/create/delete tresors (buckets); upload/download/delete/list files. No versions, restore, rename/move, empty folders or sync state; moving loses history. | Professional and business tiers (*snippet*); admin must allow it; self-hosted Docker behind your own TLS proxy; a real Tresorit user, SigV4. | Poor: each user would self-host a gateway; it lacks the restore/role data a file manager needs. |
| SCIM, AD connector, SSO | Identity only | Admin | None |
| Audit (Sentinel ingestion, Admin Center reports) | None; no audit REST API found | Admin | None |
| Linux CLI (`tresorit-cli`) | start/stop/login/status, sync, transfers; no delete/restore/leave | Linux only | None on Windows |
| Windows client switches | Installer switches only | — | None |
| Terms | No current clause on third-party clients found; a 2018 version forbade authorizing a third party to use the service on your behalf | — | Unresolved; another reason not to drive the service directly |

## Behavior and gaps on the mounted drive (TRES-01)

| Area | Provider behavior (documented, *snippet*) | Pathweft today | Gap |
| --- | --- | --- | --- |
| Main-folder delete/rename | Only the tresor **Owner** may delete or rename a main folder; deleting one is **permanent** and removes it for every member; non-owners can only leave it (app/web). | Delete goes to the shell Recycle path; on a network-type drive Windows asks for a permanent delete. The quiet permanent path first clears `ReadOnly`, which every tresor root has. | The refusal reaches the user as a generic access error, and an Owner gets no warning that the whole shared tresor goes for good. |
| Child delete / recovery | Deleting a subfolder or file is a *temporary* deletion kept in Tresorit's deleted items (no expiry, counts toward quota), restorable in the app; local copies may go to the Windows Recycle Bin. | Treated as a permanent delete (Windows' wording). | The confirmation overstates permanence and doesn't say where recovery lives. |
| Roles | Owner, Manager, Editor, Viewer (Viewer read-only). | Not visible. | No supported surface exposes the role; only the provider's refusal at operation time. |
| Streams / metadata | No named streams, ACLs, hard links. | TRANSFER-18 already copies downloads without their Zone marks and refuses other streams with a named message. | None beyond TRANSFER-18. |
| Quota | Shared tresors count toward each member's quota. | Drive space comes from the volume, which reports the account quota. | Label only ("account storage", not "disk"). |
| Cloud/sync state | No documented Windows surface. | None. | Not obtainable without internal hooks. |
| Provider commands | Sharing and members; Copy Tresorit Path (shell extension). | Pathweft's own menu adopts only Open With, 7-Zip, WinRAR and Google Drive, and skips remote drives, so these appear only under "Windows menu…". | Directly fixable through the supported shell contract. |

## Decision (TRES-04)

Do **not** integrate with Tresorit's service or internals: the only programmatic content API is a
self-hosted, admin-enabled gateway without restore or role data, the core tool is internal and
credential-adjacent, and the URI scheme carries no file commands. Integrate through the two supported
boundaries Pathweft already uses: the mounted drive and the client's own shell extension.

### Proposed stage (separate from S12; needs its own go-ahead)

| ID | Requirement and acceptance |
| --- | --- |
| TRES-05 | On Tresorit Drive, Pathweft's own context menu shows the Tresorit shell extension's commands (Sharing and members, Copy Tresorit Path) as Explorer does, by adding the `tresorit` handler to the adopted extensions and allowing it on drives `CloudVolumes.Provider` identifies as Tresorit (other remote drives keep today's exclusion). Verify the verbs are listed (not invoked) for a main folder and a child on the real drive. |
| TRES-06 | Deleting a Tresorit **main folder** asks first with provider-specific text: only the tresor's Owner can delete it, it is deleted for every member and can't be restored, and others can leave it in the Tresorit app. Never clear its `ReadOnly` attribute to force the delete. A refusal from the drive is reported as "Tresorit only lets the tresor's Owner delete or rename it" instead of a bare access error. Verify on a disposable tresor created by the owner for the test, never on existing content. |
| TRES-07 | Deleting children on Tresorit Drive says the items go to Tresorit's deleted files and can be restored from the Tresorit app (Show deleted), instead of Windows' "permanently delete" wording. Verify with disposable files in a disposable tresor; confirm restore in the client by the owner. |
| TRES-08 | The drive's space reads as the Tresorit account's storage, not a disk. |

Acceptance for the stage as a whole: no use of the S3 gateway, `tresorit-core-interface.exe`, logs or
credentials; mutation tests only on disposable fixtures the owner creates; normal release and installed
verification. Open questions: whether the client also adds verbs on files (version history), and the
error code the drive returns for a refused main-folder delete; both to be observed on the disposable
fixture, not on real tresors.
